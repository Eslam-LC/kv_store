using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;
using kv_store;
using kv_store.Enums;
using kv_store.Extensions;
using kv_store.Implementations;

namespace kv_store
{
    class Program
    {
        static void Main(string[] args)
        {
            ErrorCode errCode;

            var dataDirOption = new Option<DirectoryInfo>("--data-dir", "-d")
            {
                Description = "directory for wal_log and snapshot.dat.",
                DefaultValueFactory = parseResult => new DirectoryInfo("./data"),
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
            };
            var putHexCommand = new Command("puthex", "inserts a hex value for a key into store")
            {
                Arguments = { keyArgument, valueArgument },
            };
            var getCommand = new Command("get", "view the value as utf8 string.")
            {
                Arguments = { keyArgument },
            };
            var getHexCommand = new Command(
                "gethex",
                "gets the value of a key and view it in hex format."
            )
            {
                Arguments = { keyArgument },
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
                    putHexCommand,
                    getCommand,
                    getHexCommand,
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

            var SnapshotPath = Path.Combine(dir.FullName, "snapshot.dat");
            var WALPath = Path.Combine(dir.FullName, "wal_log");

            var writer = new WAWriter();
            errCode = writer.Initialize(WALPath);

            var reader = new WAReader();
            errCode = reader.Initialize(WALPath);

            var store = new KeyValueStore();
            var snapper = new Snapshot();

            var Engine = new WAEngine();
            errCode = Engine.Initialize(writer, store, reader, snapper);
            if (errCode != ErrorCode.None)
                Console.WriteLine($"engine initialization error: {errCode.GetDescription()}");

            bool valid = File.Exists(SnapshotPath);

            if (valid)
            {
                errCode = Engine.LoadSnapshot(SnapshotPath);
                if (errCode != ErrorCode.None)
                {
                    valid = false;
                    Console.WriteLine($"Error: {errCode.GetDescription()}");
                }
                else
                {
                    Console.WriteLine($"snapshot loaded from: {SnapshotPath}.");
                    valid = File.Exists(WALPath);

                    if (valid)
                    {
                        errCode = Engine.ReplayRecords();
                        if (errCode != ErrorCode.None)
                        {
                            Console.WriteLine($"Error: {errCode.GetDescription()}");
                        }
                        else
                        {
                            Console.WriteLine($"WAL appended from: {WALPath}.");
                        }
                    }
                }
            }

            if (!valid)
            {
                if (File.Exists(WALPath))
                {
                    Console.Write(
                        $"Snapshot file do not exist or failed to load. do you want to append WAL operations anyway (y/n)?"
                    );
                    var key = Console.ReadKey();
                    Console.WriteLine();
                    if (key.KeyChar == 'y')
                    {
                        errCode = Engine.ReplayRecords();
                        if (errCode != ErrorCode.None)
                        {
                            Console.WriteLine($"Error: {errCode.GetDescription()}");
                        }
                        else
                        {
                            Console.WriteLine($"WAL appended from: {WALPath}.");
                        }
                    }
                }
            }

            putCommand.SetAction(parseResult =>
            {
                var key = parseResult.GetValue(keyArgument);
                var value = parseResult.GetValue(valueArgument);
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    ErrorMessage = $"Error: Invalid key or value entered.";
                    return;
                }

                byte[][] ABytes = new byte[value.Length][];

                for (int i = 0; i < value.Length; i++)
                {
                    string str = value[i];
                    if (str.StartsWith($"0x", StringComparison.OrdinalIgnoreCase))
                    {
                        var errorCode = ConvertHexStringToBytes(str[2..], out ABytes[i]);
                        if (errorCode != ErrorCode.None || ABytes[i] == null)
                        {
                            ErrorMessage = $"Error: {errorCode.GetDescription()}";
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
                if (errCode != ErrorCode.None)
                {
                    ErrorMessage = $"Error: {errCode.GetDescription()}.";
                    return;
                }
                SuccessMessage = $"key: {key} was inserted.";
            });

            putHexCommand.SetAction(parseResult =>
            {
                var key = parseResult.GetValue(keyArgument);
                var value = parseResult.GetValue(valueArgument);
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    ErrorMessage = $"Error: Invalid key or value entered.";
                    return;
                }

                byte[][] ABytes = new byte[value.Length][];

                for (int i = 0; i < value.Length; i++)
                {
                    string str = value[i];
                    var errorCode = ConvertHexStringToBytes(str, out ABytes[i]);

                    if (errorCode != ErrorCode.None || ABytes[i] == null)
                    {
                        ErrorMessage = $"Error: {errorCode.GetDescription()}";
                        return;
                    }
                }

                byte[] bytes = [.. ABytes.SelectMany(s => s)];

                // Console.WriteLine(PrintByteArray(bytes)); // debug line

                var errCode = Engine.Put(key, [.. bytes]);
                if (errCode != ErrorCode.None)
                {
                    ErrorMessage = $"Error: {errCode.GetDescription()}.";
                    return;
                }
                SuccessMessage = $"key: {key} was inserted.";
            });

            getCommand.SetAction(parseResult =>
            {
                var key = parseResult.GetValue(keyArgument);

                if (key == null)
                {
                    ErrorMessage = $"Error: {ErrorCode.KeyNotValid}.";
                    return;
                }
                var errCode = Engine.TryGet(key, out var value);
                if (errCode != ErrorCode.None)
                {
                    ErrorMessage = $"Error: {errCode.GetDescription()}.";
                    return;
                }
                SuccessMessage = $"{key} : {PrintByteArrayAsString(value)}";
            });

            getHexCommand.SetAction(parseResult =>
            {
                var key = parseResult.GetValue(keyArgument);

                if (key == null)
                {
                    ErrorMessage = $"Error: {ErrorCode.KeyNotValid}.";
                    return;
                }
                var errCode = Engine.TryGet(key, out var value);
                if (errCode != ErrorCode.None)
                {
                    ErrorMessage = $"Error: {errCode.GetDescription()}.";
                    return;
                }
                SuccessMessage = $"{key} : {PrintByteArray(value)}";
            });

            deleteCommand.SetAction(parseResult =>
            {
                var key = parseResult.GetValue(keyArgument);

                if (key == null)
                {
                    ErrorMessage = $"Error: {ErrorCode.KeyNotValid}.";
                    return;
                }
                var errCode = Engine.Delete(key);
                if (errCode != ErrorCode.None)
                {
                    ErrorMessage = $"Error: {errCode.GetDescription()}.";
                    return;
                }
                SuccessMessage = $"key: {key} was deleted.";
            });

            snapshotSaveCommand.SetAction(parseResult =>
            {
                ErrorCode errCode;
                var path = parseResult.GetValue(pathArgument);
                if (string.IsNullOrWhiteSpace(path))
                {
                    errCode = Engine.SaveSnapshot();
                }
                else
                {
                    errCode = Engine.SaveSnapshot(path);
                }
                if (errCode != ErrorCode.None)
                {
                    ErrorMessage = $"Error: {errCode.GetDescription()}.";
                    return;
                }
                SuccessMessage = $"snapshot saved.";
            });

            snapshotLoadCommand.SetAction(parseResult =>
            {
                ErrorCode errCode;
                var path = parseResult.GetValue(pathArgument);
                if (string.IsNullOrWhiteSpace(path))
                {
                    errCode = Engine.LoadSnapshot(SnapshotPath);
                }
                else
                {
                    errCode = Engine.LoadSnapshot(path);
                }
                if (errCode != ErrorCode.None)
                {
                    ErrorMessage = $"Error: {errCode.GetDescription()}.";
                    return;
                }
                SuccessMessage = $"snapshot loaded.";
            });

            replayCommand.SetAction(parseResult =>
            {
                ErrorCode errCode;
                errCode = Engine.ReplayRecords();
                if (errCode != ErrorCode.None)
                {
                    ErrorMessage = $"Error: {errCode.GetDescription()}.";
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
                return ErrorCode.InvalidArguments;
            }

            byte[] tempbytes;
            try
            {
                tempbytes = Convert.FromHexString(str.AsSpan());
            }
            catch (FormatException)
            {
                bytes = [];
                return ErrorCode.ValueNotValid;
            }

            bytes = tempbytes;

            return ErrorCode.None;
        }
    }
}
