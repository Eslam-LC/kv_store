using System.CommandLine;
using kv_store;
using kv_store.EnumsAndConstants;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class ProgramTests : IDisposable
{
    readonly string tempDir;

    public ProgramTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "kv-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        { /* best effort */
        }
    }

    static RootCommand BuildRoot(out Program.ReplContext ctx)
    {
        ctx = new Program.ReplContext();
        return Program.BuildRootCommand(ctx);
    }

    [Fact]
    public void BuildRootCommand_AssignsDataDirOptionToContext()
    {
        BuildRoot(out var ctx);
        Assert.NotNull(ctx.DataDirOption);
        Assert.Equal("--data-dir", ctx.DataDirOption.Name);
    }

    [Fact]
    public void Scan_MissingArguments_ReportsParseErrors()
    {
        var root = BuildRoot(out _);
        Assert.NotEmpty(root.Parse("scan").Errors);
        Assert.NotEmpty(root.Parse("scan a").Errors);
        Assert.Empty(root.Parse("scan a b").Errors);
    }

    [Fact]
    public void Scan_Arguments_BindStartAndEndKeys()
    {
        var root = BuildRoot(out var ctx);
        var result = root.Parse("scan alpha omega");
        Assert.Empty(result.Errors);
        Assert.Equal("alpha", result.GetValue(ctx.StartKeyArgument));
        Assert.Equal("omega", result.GetValue(ctx.EndKeyArgument));
    }

    [Fact]
    public void Scan_Action_ReportsRowCount_ThroughContext()
    {
        var root = BuildRoot(out var ctx);
        ctx.Engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, ctx.Engine.Init(out _));
        Assert.Equal(ErrorCode.None, ctx.Engine.Put("a", [1]));
        Assert.Equal(ErrorCode.None, ctx.Engine.Put("c", [3]));

        Assert.Equal(0, root.Parse("scan a b").Invoke());
        Assert.Equal("Scanned 'a'..'b': 1 pair(s).", ctx.SuccessMessage);
        Assert.Null(ctx.ErrorMessage);
    }

    [Fact]
    public void Put_Action_ReportsInsertion_ThroughContext()
    {
        var root = BuildRoot(out var ctx);
        ctx.Engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, ctx.Engine.Init(out _));

        Assert.Equal(0, root.Parse("put k s").Invoke());
        Assert.Equal("key: k was inserted.", ctx.SuccessMessage);
        Assert.Null(ctx.ErrorMessage);
    }

    [Fact]
    public void Get_Action_ReportsRetrieval_ThroughContext()
    {
        var root = BuildRoot(out var ctx);
        ctx.Engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, ctx.Engine.Init(out _));
        Assert.Equal(ErrorCode.None, ctx.Engine.Put("k", "v"u8.ToArray()));

        Assert.Equal(0, root.Parse("get k").Invoke());
        Assert.Equal("Retrieved 'k' (1 chars).", ctx.SuccessMessage);
        Assert.Null(ctx.ErrorMessage);
    }

    [Fact]
    public void Get_MissingKey_ReportsParseError()
    {
        var root = BuildRoot(out _);
        Assert.NotEmpty(root.Parse("get").Errors);
    }
}