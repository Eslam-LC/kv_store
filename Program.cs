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
            ErrorCode errorCode;

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
            var startKeyArgument = new Argument<string>("startKey")
            {
                Description = "the start key (inclusive)",
            };
            var endKeyArgument = new Argument<string>("endKey")
            {
                Description = "the end key (inclusive)",
            };

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
            var scanCommand = new Command("scan", "gets all entries between two keys")
            {
                Arguments = { startKeyArgument, endKeyArgument },
                Options = { hexOption },
            };
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
            errorCode = Engine.Init(out var errors);
            if (errorCode != None)
            {
                if (errorCode == SstablesFailedToLoad)
                {
                    foreach (var (e, f) in errors)
                    {
                        Console.WriteLine($"Error {errorCode} \nWhen loading file {f}");
                    }
                }
                else
                {
                    Console.WriteLine($"Engine initialization error: {errorCode}");
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
                SuccessMessage =
                    $"Retrieved '{key}' ({(hex ? PrintByteArray(value!) : PrintByteArrayAsString(value!))?.Length ?? 0} chars).";
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
                errorCode = Engine.ReplayRecords();
                if (errorCode != None)
                {
                    ErrorMessage = $"Error: {errorCode}.";
                    return;
                }
                SuccessMessage = $"WAL restored.";
            });

            scanCommand.SetAction(parseResult =>
            {
                var startKey = parseResult.GetValue(startKeyArgument);
                var endKey = parseResult.GetValue(endKeyArgument);
                var hex = parseResult.GetValue(hexOption);
                if (string.IsNullOrWhiteSpace(startKey) || string.IsNullOrWhiteSpace(endKey))
                {
                    ErrorMessage = $"Error: {KeyIsInvalid}.";
                    return;
                }
                errorCode = Engine.Scan(startKey, endKey, out var results);
                if (errorCode != None)
                {
                    ErrorMessage = $"Error: {errorCode}.";
                    return;
                }
                var resultsArray = results.ToArray();
                if (hex)
                    BrettyPrintHex(resultsArray);
                else
                    BrettyPrintRaw(resultsArray);
                SuccessMessage =
                    $"Scanned '{startKey}'..'{endKey}': {resultsArray.Length} pair(s).";
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

        static void BrettyPrintRaw(KeyValuePair<string, byte[]>[] pairs)
        {
            if (pairs == null || pairs.Length == 0)
                return;

            Console.WriteLine($"{"Key", -20}{"Value", -50}");
            Console.WriteLine(new string('-', 70));

            foreach (var item in pairs)
            {
                var value = PrintByteArrayAsString(item.Value) ?? string.Empty;
                Console.WriteLine($"{item.Key, -20}{value, -50}");
            }
        }

        static void BrettyPrintHex(KeyValuePair<string, byte[]>[] pairs)
        {
            if (pairs == null || pairs.Length == 0)
                return;

            Console.WriteLine($"{"Key", -20}{"Value", -50}");
            Console.WriteLine(new string('-', 70));

            foreach (var item in pairs)
            {
                var value = PrintByteArray(item.Value) ?? string.Empty;
                Console.WriteLine($"{item.Key, -20}{value, -50}");
            }
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
