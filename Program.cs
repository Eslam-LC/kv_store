using kv_store.Implementations;

namespace kv_store;

class Program
{
    static void Main(string[] args)
    {
        #region Day1
        Result res;
        byte[] value;
        KeyValueStore kvs = new();
        kvs.Put("key1", [51, 64, 128, 2, 32, 255]);
        kvs.Put("key2", [23, 48, 62, 12]);
        kvs.Put("key3", [45, 78, 91, 69]);
        res = kvs.Put("key3", []);
        System.Console.WriteLine($"{res.Error}");

        kvs.TryGet("key2", out value);
        PrintByteArray(value);

        kvs.Delete("key1");
        res = kvs.TryGet("key1", out _);
        System.Console.WriteLine($"{res.Error}");

        res = kvs.Delete("key4");
        System.Console.WriteLine($"{res.Error}");

        res = kvs.TryGet("key3", out value);
        System.Console.WriteLine($"{res.Error}");

        #endregion

        System.Console.WriteLine();

        #region Day2
        Result errCode;
        var writer = new WAWriter();
        errCode = writer.Initialize();
        System.Console.WriteLine($"writer initialization error: {errCode.Error}");
        var Engine = new WAEngine();
        errCode = Engine.Initialize(writer, new KeyValueStore());
        System.Console.WriteLine($"engine initialization error: {errCode.Error}");

        Engine.Put("key1", [51, 64, 128, 2, 32, 255]);
        Engine.Put("key2", [23, 48, 62, 12]);
        Engine.Put("key3", [45, 78, 91, 69]);
        errCode = Engine.Put("key3", []);
        System.Console.WriteLine($"repeat put on key3: {errCode.Error}");

        Engine.TryGet("key2", out value);
        Console.Write($"values at key2: ");
        PrintByteArray(value);

        Engine.Delete("key1");
        errCode = Engine.TryGet("key1", out _);
        System.Console.WriteLine($"get key1 after delete: {errCode.Error}");

        errCode = Engine.Delete("key4");
        System.Console.WriteLine($"delete key4: {errCode.Error}");

        errCode = Engine.TryGet("key3", out value);
        System.Console.WriteLine($"get key3 while empty: {errCode.Error}");

        #endregion
    }

    static void PrintByteArray(byte[] ba)
    {
        if (ba == null)
            return;
        foreach (var elem in ba)
        {
            Console.Write($"{elem}\t");
        }
        System.Console.WriteLine();
    }
}
