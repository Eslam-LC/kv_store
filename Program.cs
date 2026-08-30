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
            var writer = new WAWriter();
            errCode = writer.Initialize();

            var reader = new WAReader();
            errCode = reader.Initialize();

            var store = new KeyValueStore();
            var snapper = new Snapshot();

            var Engine = new WAEngine();
            errCode = Engine.Initialize(writer, store, reader, snapper);
            if (errCode != ErrorCode.None)
                Console.WriteLine($"engine initialization error: {errCode.GetDescription()}");

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
            };

            string? ErrorMessage = null,
                SuccessMessage = null;

            StringBuilder sb = new();

            var SnapshotPath = @"./data/snapshot.dat";

            bool valid = File.Exists(SnapshotPath);

            if (valid)
            {
                errCode = Engine.LoadSnapshot();
                if (errCode != ErrorCode.None)
                    sb.Append($"Error: {errCode.GetDescription()}");
                else
                    sb.Append($"snapshot loaded from: {SnapshotPath}.");
            }

            // if snapshot fails loading may be prompt the user first to append wal log or not.
            var WALLogPath = @"./data/wal_log";

            valid = File.Exists(WALLogPath);

            if (valid)
            {
                errCode = Engine.ReplayRecords();
                if (errCode != ErrorCode.None)
                {
                    if (sb.Length > 0)
                        sb.Append('\n');
                    sb.Append($"Error: {errCode.GetDescription()}");
                }
                else
                {
                    if (sb.Length > 0)
                        sb.Append('\n');
                    sb.Append($"WAL appended from: {WALLogPath}.");
                }
            }

            // sb is used for error and success here may be just print is better.

            if (sb.Length > 0)
                ErrorMessage = sb.ToString();

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
                    errCode = Engine.LoadSnapshot();
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
                ExecuteExitRoutine(args);
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

        static void ExecuteExitRoutine(string[] args)
        {
            string appName = args?.Length > 0 ? args[0] : "the application";
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
