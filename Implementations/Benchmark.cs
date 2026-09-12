using System.Diagnostics;

namespace kv_store.Implementations;

public static class Benchmark
{
    public static void Run(string dataDir, int ops, string? reportPath = null)
    {
        var engine = new WAEngine(dataDir);
        engine.Init(out var errors);

        var report = new BenchReport { Dir = dataDir, TotalOps = ops };

        report
            .Phase("warmup", 2_000, _ => Put(engine, 2_000))
            .Phase("put", ops, _ => Put(engine, ops))
            .Phase(
                "flush",
                ops,
                row =>
                {
                    var sw = Stopwatch.StartNew();
                    engine.FlushToSSTable();
                    row.Ms = sw.Elapsed.TotalMilliseconds; // override wall-clock
                    row.Notes.Add($"tables: {TableSizeMb(engine):F1} MB");
                }
            )
            .Phase("table-get", ops, _ => TableGet(engine, ops))
            .Phase("scan", 500, _ => Scan(engine, 500, ops))
            .Phase("delete", ops / 2, _ => Delete(engine, ops))
            .Phase("get", ops, _ => Get(engine, ops));

        if (reportPath is null)
            report.Print();
        else
            report.Save(reportPath);
    }

    public static void Put(WAEngine engine, int ops)
    {
        for (int i = 0; i < ops; i++)
        {
            string key = $"k{i:D8}";
            var value = new byte[100];
            Random.Shared.NextBytes(value);

            engine.Put(key, value);
        }
    }

    public static void Delete(WAEngine engine, int ops)
    {
        for (int i = 0; i < ops; i += 2)
        {
            string key = $"k{i:D8}";
            engine.Delete(key);
        }
    }

    public static void Get(WAEngine engine, int ops)
    {
        for (int i = 0; i < ops; i++)
        {
            string key = $"k{i:D8}";
            engine.TryGet(key, out _);
        }
    }

    public static void TableGet(WAEngine engine, int ops)
    {
        for (int i = 0; i < ops; i++)
        {
            string key = $"k{i:D8}";
            engine.TryGet(key, out _);
        }
    }

    public static void Scan(WAEngine engine, int scans, int ops)
    {
        int windows = Math.Max(1, ops / 1000);
        for (int i = 0; i < scans; i++)
        {
            int window = (i % windows) * 1000;
            string start = $"k{window:D8}";
            string end = $"k{window + 999:D8}";
            engine.Scan(start, end, out var results);
            _ = results.Count(); // force the merge to materialize
        }
    }

    static double TableSizeMb(WAEngine engine)
    {
        string dir = Path.GetDirectoryName(engine.WALFile)!;
        long bytes = Directory.GetFiles(dir, "SSTable-*").Sum(f => new FileInfo(f).Length);
        return bytes / (1024.0 * 1024.0);
    }
}

/// <summary>
/// Collects benchmark phases and renders them as an aligned table.
///
/// Two modes, one type:
///   • Basic    — phase / ops / ms / ops-per-sec
///   • Extended — same table + free-form Notes per row (memory, file sizes, …)
///
/// Output goes to any TextWriter: Console.Out (default) or a file via Save().
/// </summary>
public sealed class BenchReport
{
    public string Title { get; init; } = "kv-store benchmark";
    public string Dir { get; init; } = "";
    public long TotalOps { get; init; }
    public List<BenchRow> Rows { get; } = new();

    /// <summary>
    /// Times <paramref name="body"/> and appends a row.
    /// The body may set <see cref="BenchRow.Ms"/> itself to override the wall-clock
    /// measurement (e.g. flush = table-write time only), and may add Notes for
    /// the extended report mode.
    /// </summary>
    public BenchReport Phase(string name, long ops, Action<BenchRow>? body = null)
    {
        var row = new BenchRow { Phase = name, Ops = ops };
        var sw = Stopwatch.StartNew();
        body?.Invoke(row);
        sw.Stop();

        if (row.Ms == 0)
            row.Ms = sw.Elapsed.TotalMilliseconds;
        Rows.Add(row);
        return this;
    }

    public void Print(TextWriter? output = null) => BenchPrinter.Print(this, output ?? Console.Out);

    public void Save(string path)
    {
        using var w = new StreamWriter(path);
        Print(w);
    }
}

public sealed class BenchRow
{
    public required string Phase { get; init; }
    public long Ops { get; init; }
    public double Ms { get; set; }

    public double? OpsPerSec => Ms > 0 && Ops > 0 ? Ops / (Ms / 1000.0) : null;

    /// <summary>Extra info appended after the standard columns, e.g. "table: 11.2 MB".</summary>
    public List<string> Notes { get; } = new();
}

public static class BenchPrinter
{
    // Fixed widths → stable columns → easy to diff two reports.
    const int WPhase = 15;
    const int WOps = 9;
    const int WMs = 9;
    const int WOpsSec = 10;

    public static void Print(BenchReport report, TextWriter w)
    {
        w.WriteLine($"{report.Title}    ops={report.TotalOps}  dir={report.Dir}");
        w.WriteLine(Header());
        foreach (var row in report.Rows)
            w.WriteLine(FormatRow(row));
    }

    static string Header() =>
        Pad("phase", WPhase, left: true)
        + Pad("ops", WOps)
        + Pad("ms", WMs)
        + Pad("ops/sec", WOpsSec);

    static string FormatRow(BenchRow row)
    {
        string tail = row.OpsPerSec?.ToString("F1") ?? "";
        string line =
            Pad(row.Phase, WPhase, left: true)
            + Pad(row.Ops.ToString(), WOps)
            + Pad(row.Ms.ToString("F1"), WMs)
            + Pad(tail, WOpsSec);

        if (row.Notes.Count > 0)
            line += "  " + string.Join(", ", row.Notes);

        return line;
    }

    static string Pad(string s, int width, bool left = false) =>
        left ? s.PadRight(width) : s.PadLeft(width);
}
