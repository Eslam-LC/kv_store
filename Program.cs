using System.CommandLine;
using System.Text;
using kv_store.EnumsAndConstants;
using kv_store.Implementations;
using static kv_store.EnumsAndConstants.ErrorCode;

namespace kv_store
{
    class Program
    {
        static void Main(string[] args)
        {
            ErrorCode errCode;

            var dataDirOption = new Option<DirectoryInfo>("--data-dir", "-d")
            {
                Description = "specifies directory for wal.log and snapshot.dat.",
                DefaultValueFactory = parseResult => new DirectoryInfo("./data"),
            };

            var hexOption = new Option<bool>("--hex", "-x")
            {
                Description = "use to enter raw hex values.",
                DefaultValueFactory = parseResult => false,
            };

            var keyArgument = new Argument<string>("key") { Description = "the key of the entry" };
            var valueArgument = new Argument<string[]>("value") { Description = "value to insert" };
            var pathArgument = new Argument<string?>("path")
            {
                Description = "snapshot file's path",
                Arity = ArgumentArity.ZeroOrOne,
            };

            var putCommand = new Command("put", "inserts a key value pair into store.")
            {
                Arguments = { keyArgument, valueArgument },
                Options = { hexOption },
            };
            var getCommand = new Command("get", "view the value as utf8 string.")
            {
                Arguments = { keyArgument },
                Options = { hexOption },
            };
            var deleteCommand = new Command("delete", "deletes a key along with it's value.")
            {
                Arguments = { keyArgument },
            };
            var snapshotSaveCommand = new Command("save", "save a snapshot")
            {
                Arguments = { pathArgument },
            };
            var snapshotLoadCommand = new Command("load", "load a snapshot")
            {
                Arguments = { pathArgument },
            };
            var snapshotCommand = new Command("snapshot", "save/load a snapshot.")
            {
                Subcommands = { snapshotSaveCommand, snapshotLoadCommand },
            };
            var replayCommand = new Command(
                "replay",
                "appends entries in the write ahead log file"
            );
            var exitCommand = new Command("exit", "closes the program.");

            var rootCommand = new RootCommand("A write ahead logger with snapshot feature.")
            {
                Subcommands =
                {
                    putCommand,
                    getCommand,
                    deleteCommand,
                    snapshotCommand,
                    replayCommand,
                    exitCommand,
                },
                Options = { dataDirOption },
            };

            DirectoryInfo? dir = rootCommand.Parse(args).GetValue(dataDirOption);

            dir ??= new(@"./data");

            string? ErrorMessage = null,
                SuccessMessage = null;

            if (!dir.Exists)
                dir.Create();

            var Engine = new WAEngine(dir.FullName); // later on the configs may include file names.
            errCode = Engine.Init(out var errors);
            if (errCode != None)
            {
                if (errCode == SstablesFailedToLoad)
                {
                    foreach (var (e, f) in errors)
                    {
                        Console.WriteLine($"Error {errCode} \nWhen loading file {f}");
                    }
                }
                else
                {
                    Console.WriteLine($"Engine initialization error: {errCode}");
                }
            }

            putCommand.SetAction(parseResult =>
            {
                var key = parseResult.GetValue(keyArgument);
                var value = parseResult.GetValue(valueArgument);
                var hex = parseResult.GetValue(hexOption);

                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    ErrorMessage = $"Error: Invalid key or value entered.";
                    return;
                }

                byte[][] ABytes = new byte[value.Length][];

                for (int i = 0; i < value.Length; i++)
                {
                    string str = value[i];
                    if (hex)
                    {
                        var errorCode = ConvertHexStringToBytes(str[2..], out ABytes[i]);
                        if (errorCode != None || ABytes[i] == null)
                        {
                            ErrorMessage = $"Error: {errorCode}";
                            return;
                        }
                    }
                    else
                    {
                        var TotalLength = Encoding.UTF8.GetByteCount(str);
                        ABytes[i] = new byte[TotalLength];
                        var _ = Encoding.UTF8.GetBytes(str, 0, str.Length, ABytes[i], 0);
                    }
                }

                byte[] bytes = [.. ABytes.SelectMany(s => s)];

                var errCode = Engine.Put(key, [.. bytes]);
                if (errCode != None)
                {
                    ErrorMessage = $"Error: {errCode}.";
                    return;
                }
                SuccessMessage = $"key: {key} was inserted.";
            });

            getCommand.SetAction(parseResult =>
            {
                var key = parseResult.GetValue(keyArgument);
                var hex = parseResult.GetValue(hexOption);

                if (key == null)
                {
                    ErrorMessage = $"Error: {KeyIsInvalid}.";
                    return;
                }
                var errCode = Engine.TryGet(key, out var value);
                if (errCode != None)
                {
                    ErrorMessage = $"Error: {errCode}.";
                    return;
                }
                if (hex)
                    SuccessMessage = $"{key} : {PrintByteArray(value!)}";
                else
                    SuccessMessage = $"{key} : {PrintByteArrayAsString(value!)}";
            });

            deleteCommand.SetAction(parseResult =>
            {
                var key = parseResult.GetValue(keyArgument);

                if (key == null)
                {
                    ErrorMessage = $"Error: {KeyIsInvalid}.";
                    return;
                }
                var errCode = Engine.Delete(key);
                if (errCode != None)
                {
                    ErrorMessage = $"Error: {errCode}.";
                    return;
                }
                SuccessMessage = $"key: {key} was deleted.";
            });

            snapshotSaveCommand.SetAction(parseResult =>
            {
                ErrorCode errCode;
                var path = parseResult.GetValue(pathArgument);
                if (!string.IsNullOrWhiteSpace(path))
                    Engine.SnapshotFile = path;

                errCode = Engine.SaveSnapshot();

                if (errCode != None)
                {
                    ErrorMessage = $"Error: {errCode}.";
                    return;
                }
                SuccessMessage = $"snapshot saved.";
            });

            snapshotLoadCommand.SetAction(parseResult =>
            {
                ErrorCode errCode;
                var path = parseResult.GetValue(pathArgument);
                if (!string.IsNullOrWhiteSpace(path))
                    Engine.SnapshotFile = path;

                errCode = Engine.LoadSnapshot();

                if (errCode != None)
                {
                    ErrorMessage = $"Error: {errCode}.";
                    return;
                }
                Console.Write($"do you want to append WAL operations? (y/n)");
                var key = Console.ReadKey();
                Console.WriteLine();
                if (key.KeyChar == 'y' || key.Key == ConsoleKey.Enter)
                {
                    errCode = Engine.ReplayRecords();
                    if (errCode != None)
                    {
                        Console.WriteLine($"Error: {errCode}");
                    }
                    else
                    {
                        Console.WriteLine($"WAL appended from: {Engine.WALFile}.");
                    }
                }
                SuccessMessage = $"snapshot loaded.";
            });

            replayCommand.SetAction(parseResult =>
            {
                ErrorCode errCode;
                errCode = Engine.ReplayRecords();
                if (errCode != None)
                {
                    ErrorMessage = $"Error: {errCode}.";
                    return;
                }
                SuccessMessage = $"WAL restored.";
            });

            exitCommand.SetAction(parseResult =>
            {
                ExecuteExitRoutine();
            });

            while (true)
            {
                Console.Write($"> ");
                if (!string.IsNullOrWhiteSpace(ErrorMessage))
                {
                    Console.Write($"{ErrorMessage}\n");
                    ErrorMessage = null;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(SuccessMessage))
                {
                    Console.Write($"{SuccessMessage}\n");
                    SuccessMessage = null;
                    continue;
                }

                string? input = Console.ReadLine();

                if (input is null)
                {
                    Console.WriteLine();
                    break;
                }
                else if (string.IsNullOrWhiteSpace(input))
                    continue;

                var parseResult = rootCommand.Parse(input);

                if (parseResult.Errors.Any())
                {
                    foreach (var error in parseResult.Errors)
                    {
                        Console.WriteLine($"Error: {error.Message}");
                    }
                    continue;
                }

                var err = parseResult.Invoke();
                if (err != 0)
                {
                    Console.WriteLine($"Invokation code: {err}");
                }
            }
        }

        static string? PrintByteArray(byte[] ba)
        {
            if (ba == null || ba.Length == 0)
                return null;

            var hex = Convert.ToHexString(ba);
            var sb = new StringBuilder(hex.Length + ba.Length + 1);
            for (int i = 0; i < hex.Length; i += 4)
            {
                int count = Math.Min(4, hex.Length - i);
                sb.Append(hex, i, count);
                sb.Append(' ');
            }
            return sb.ToString().TrimEnd();
        }

        static string? PrintByteArrayAsString(byte[] ba)
        {
            if (ba == null || ba.Length == 0)
                return null;

            return Encoding.UTF8.GetString(ba);
        }

        static void ExecuteExitRoutine()
        {
            string appName = AppDomain.CurrentDomain.FriendlyName ?? "the application";
            Console.WriteLine($"Thank you for using {appName}");
            Environment.Exit(0);
        }

        static ErrorCode ConvertHexStringToBytes(in string str, out byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                bytes = [];
                return ArgumentsAreInvalid;
            }

            byte[] tempbytes;
            try
            {
                tempbytes = Convert.FromHexString(str.AsSpan());
            }
            catch (FormatException)
            {
                bytes = [];
                return ValueIsInvalid;
            }

            bytes = tempbytes;

            return None;
        }
    }
}
