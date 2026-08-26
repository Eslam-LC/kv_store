using System.CommandLine;
using System.Text;
using kv_store;
using kv_store.Enums;
using kv_store.Implementations;

#region Day1
// Result res;
// byte[] value;
// KeyValueStore kvs = new();
// kvs.Put("key1", [51, 64, 128, 2, 32, 255]);
// kvs.Put("key2", [23, 48, 62, 12]);
// kvs.Put("key3", [45, 78, 91, 69]);
// res = kvs.Put("key3", []);
// System.Console.WriteLine($"{res.Error}");

// kvs.TryGet("key2", out value);
// PrintByteArray(value);

// kvs.Delete("key1");
// res = kvs.TryGet("key1", out _);
// System.Console.WriteLine($"{res.Error}");

// res = kvs.Delete("key4");
// System.Console.WriteLine($"{res.Error}");

// res = kvs.TryGet("key3", out value);
// System.Console.WriteLine($"{res.Error}");

#endregion

System.Console.WriteLine();

#region Day2
// File.Delete(@"./data/wal_log");

Result errCode;
var writer = new WAWriter();
errCode = writer.Initialize();

// System.Console.WriteLine($"writer initialization error: {errCode.Error}");
var reader = new WAReader();
errCode = reader.Initialize();

// System.Console.WriteLine($"reader initialization error: {errCode.Error}");
var store = new KeyValueStore();
var snapper = new Snapshot();

var Engine = new WAEngine();
errCode = Engine.Initialize(writer, store, reader, snapper);
System.Console.WriteLine($"engine initialization error: {errCode.Error}");

// Engine.Put("key1", [0x51, 0x64, 0x12, 0x2, 0x32, 0x55]);
// Engine.Put("key2", [0x23, 0x48, 0x62, 0x12]);
// Engine.Put("key3", [0x45, 0x78, 0x91, 0x69]);
// errCode = Engine.Put("key3", []);
// System.Console.WriteLine($"repeat put on key3: {errCode.Error}");

// byte[] value1;
// Engine.TryGet("key2", out value1);
// Console.Write($"values at key2: ");
// PrintByteArray(value1);

// Engine.Delete("key1");
// errCode = Engine.TryGet("key1", out _);
// System.Console.WriteLine($"get key1 after delete: {errCode.Error}");

// errCode = Engine.Delete("key4");
// System.Console.WriteLine($"delete key4: {errCode.Error}");

// errCode = Engine.TryGet("key3", out _);
// System.Console.WriteLine($"get key3 while empty: {errCode.Error}");

#endregion

System.Console.WriteLine();

#region Day3

// Engine.ReplayRecords();
// Engine.TryGet("key2", out var bytes);
// Console.Write($"values at key2: ");
// PrintByteArray(bytes);

#endregion

#region snapshot

// snapper.SaveSnapshot(store);
// Engine.LoadSnapshot(store);

// Engine.TryGet("key2", out var value1);
// Console.Write($"values at key2: ");
// PrintByteArray(value1);

#endregion

#region CLI


// put get delete snapshot exit
// key value

var keyArgument = new Argument<string>("key") { Description = "the key of the entry" };
var valueArgument = new Argument<string[]>("value") { Description = "value to insert" };
var pathArgument = new Argument<string?>("path")
{
    Description = "snapshot file's path",
    Arity = ArgumentArity.ZeroOrOne,
};

// var saveOption = new Option<bool>("--save");
// var loadOption = new Option<bool>("--load");

var putCommand = new Command("put", "inserts a key value pair to the store.")
{
    Arguments = { keyArgument, valueArgument },
};
var getCommand = new Command("get", "gets the value of a key.") { Arguments = { keyArgument } };
var deleteCommand = new Command("delete", "deletes a key along with it's value.")
{
    Arguments = { keyArgument },
};
var snapshotSaveCommand = new Command("save", "save a snapshot") { Arguments = { pathArgument } };
var snapshotLoadCommand = new Command("load", "load a snapshot") { Arguments = { pathArgument } };
var snapshotCommand = new Command("snapshot", "save/load a snapshot.")
{
    Subcommands = { snapshotSaveCommand, snapshotLoadCommand },
    // Options = { loadOption },
};
var exitCommand = new Command("exit", "closes the program.");

var rootCommand = new RootCommand("A write ahead logger with snapshot feature.")
{
    Subcommands = { putCommand, getCommand, deleteCommand, snapshotCommand, exitCommand },
};

string? ErrorMessage = null,
    SuccessMessage = null;

StringBuilder sb = new();

bool valid;
_ = IsFileExist(@"./data/snapshot.dat", out valid);

if (valid)
{
    errCode = Engine.LoadSnapshot();
    if (errCode.Error != ErrorCode.None)
        sb.Append($"Error: {errCode.Error}");
}

_ = IsFileExist(@"./data/wal_log", out valid);

if (valid)
{
    errCode = Engine.ReplayRecords();
    if (errCode.Error != ErrorCode.None)
        sb.Append($"Error: {errCode.Error}");
}

if (sb.Length > 0)
    ErrorMessage = sb.ToString();

putCommand.SetAction(parseResult =>
{
    var key = parseResult.GetValue(keyArgument);
    var value = parseResult.GetValue(valueArgument);
    if (key == null || value == null)
    {
        ErrorMessage = $"invalid key or value entered.";
        return;
    }

    var TotalLength = value.Sum(s => Encoding.UTF8.GetByteCount(s));
    var bytes = new byte[TotalLength];
    var offset = 0;
    foreach (var str in value)
    {
        var count = Encoding.UTF8.GetBytes(str, 0, str.Length, bytes, offset);
        offset += count;
    }

    var errCode = Engine.Put(key, [.. bytes]);
    if (errCode.Error != ErrorCode.None)
    {
        ErrorMessage = $"Error: {errCode.Error}.";
        return;
    }
    SuccessMessage = $"key: {key} was inserted.";
});

getCommand.SetAction(parseResult =>
{
    var key = parseResult.GetValue(keyArgument);

    if (key == null)
    {
        ErrorMessage = $"invalid key entered.";
        return;
    }
    var errCode = Engine.TryGet(key, out var value);
    if (errCode.Error != ErrorCode.None)
    {
        ErrorMessage = $"Error: {errCode.Error}.";
        return;
    }
    SuccessMessage = $"{key} : {PrintByteArray(value)}";
});

deleteCommand.SetAction(parseResult =>
{
    var key = parseResult.GetValue(keyArgument);

    if (key == null)
    {
        ErrorMessage = $"invalid key entered.";
        return;
    }
    var errCode = Engine.Delete(key);
    if (errCode.Error != ErrorCode.None)
    {
        ErrorMessage = $"[{errCode.Error}] error occured.";
        return;
    }
    SuccessMessage = $"key: {key} was deleted.";
});

snapshotSaveCommand.SetAction(parseResult =>
{
    Result errCode;
    var path = parseResult.GetValue(pathArgument);
    if (string.IsNullOrWhiteSpace(path))
    {
        errCode = Engine.SaveSnapshot();
    }
    else
    {
        errCode = Engine.SaveSnapshot(path);
    }
    if (errCode.Error != ErrorCode.None)
    {
        ErrorMessage = $"Error: {errCode.Error}.";
        return;
    }
    SuccessMessage = $"snapshot saved.";
});

snapshotLoadCommand.SetAction(parseResult =>
{
    Result errCode;
    var path = parseResult.GetValue(pathArgument);
    if (string.IsNullOrWhiteSpace(path))
    {
        errCode = Engine.LoadSnapshot();
    }
    else
    {
        errCode = Engine.LoadSnapshot(path);
    }
    if (errCode.Error != ErrorCode.None)
    {
        ErrorMessage = $"Error: {errCode.Error}.";
        return;
    }
    SuccessMessage = $"snapshot loaded.";
});

exitCommand.SetAction(parseResult =>
{
    ExcuteExitRoutine();
});

while (true)
{
    Console.Write($"> ");
    // display error or output
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

    if (string.IsNullOrWhiteSpace(input))
        continue;

    var arguments = input.Split(" ", StringSplitOptions.RemoveEmptyEntries);
    var parseResult = rootCommand.Parse(arguments);

    if (parseResult.Errors.Any())
    {
        foreach (var error in parseResult.Errors)
        {
            System.Console.WriteLine($"Error: {error.Message}");
        }
        continue;
    }

    // if (parseResult.CommandResult.Command == rootCommand)
    // {
    //     // System.Console.WriteLine();
    // }

    var err = parseResult.Invoke();
    if (err != 0)
    {
        System.Console.WriteLine($"Invokation code: {err}");
    }
}

static string? PrintByteArray(byte[] ba)
{
    if (ba == null)
        return null;

    var hex = Convert.ToHexString(ba);
    var sb = new StringBuilder(hex.Length + ba.Length + 1);
    for (int i = 0; i < hex.Length; i += 4)
    {
        sb.Append(hex, i, 4);
        sb.Append(' ');
    }
    // sb.Append('\n');
    return sb.ToString();
}

void ExcuteExitRoutine()
{
    Environment.Exit(0);
}

static Result IsFileExist(string _path, out bool valid)
{
    valid = false;
    bool pathInvalid = string.IsNullOrWhiteSpace(_path);
    if (pathInvalid)
        return new Result(ErrorCode.InvalidPath);

    bool fileExists = File.Exists(_path);
    if (!fileExists)
        return new Result(ErrorCode.InvalidPath);

    valid = true;
    return new Result(ErrorCode.None);
}
// void PrintRecordsCollection(ICollection<WARecord> records)
// {
//     if (records == null)
//         return;
//     foreach (var record in records)
//     {
//         Console.WriteLine($"Record Length: {record.RecordLength}");
//         Console.WriteLine($"Operation: {record.Op}");
//         Console.WriteLine($"Key Length: {record.KeyLength}");
//         Console.WriteLine($"Key: {Encoding.UTF8.GetString(record.Key)}");
//         Console.WriteLine($"Value Length: {record.ValueLength}");
//         if (record.ValueLength > 0)
//         {
//             Console.Write($"Value: ");
//             foreach (var item in record.Value ?? [])
//             {
//                 Console.Write($" {item}");
//             }
//         }
//     }
//     Console.WriteLine();
// }

#endregion
