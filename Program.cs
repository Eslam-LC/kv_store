namespace kv_store;

class Program
{
    static void Main(string[] args)
    {
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
