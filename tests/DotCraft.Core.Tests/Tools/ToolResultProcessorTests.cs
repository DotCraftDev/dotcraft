using DotCraft.Tools;

namespace DotCraft.Core.Tests.Tools;

public class ToolResultProcessorTests
{
    [Fact]
    public void Process_Null_ReturnsEmptyMessage()
    {
        var r = ToolResultProcessor.Process("Exec", null, 1000, "C:\\w", "s1", 40);
        Assert.Equal("(Exec completed with no output)", r);
    }

    [Fact]
    public void Process_WhitespaceString_ReturnsEmptyMessage()
    {
        var r = ToolResultProcessor.Process("Exec", "   \n\t  ", 1000, "C:\\w", "s1", 40);
        Assert.Equal("(Exec completed with no output)", r);
    }

    [Fact]
    public void Process_DescribeNoOutput_ReturnsEmptyMessage()
    {
        var r = ToolResultProcessor.Process("Exec", "(no output)", 1000, "C:\\w", "s1", 40);
        Assert.Equal("(Exec completed with no output)", r);
    }

    [Fact]
    public void Process_UnderLimit_ReturnsOriginalString()
    {
        var s = new string('a', 100);
        var r = ToolResultProcessor.Process("Exec", s, 200, "C:\\w", "s1", 40);
        Assert.Same(s, r);
    }

    [Fact]
    public void Process_MaxZero_Unlimited_PassesThroughNonEmpty()
    {
        var s = new string('x', 500_000);
        var r = ToolResultProcessor.Process("Exec", s, 0, "C:\\w", "s1", 40);
        Assert.Same(s, r);
    }

    [Fact]
    public void Process_OverLimit_WritesSpillAndReturnsPreview()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-trp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            // Many lines so BuildPreview uses head/tail (not the short single-block form).
            var text = string.Join("\n", Enumerable.Range(0, 200).Select(_ => new string('z', 24)));
            var r = ToolResultProcessor.Process("GrepFiles", text, 1000, workspace, "thread-1", 2) as string;
            Assert.NotNull(r);
            Assert.Contains(ToolResultProcessor.SpillPreviewMarker, r, StringComparison.Ordinal);

            var spillDir = Path.Combine(workspace, ".craft", "tool-results", "thread-1");
            Assert.True(Directory.Exists(spillDir));
            var files = Directory.GetFiles(spillDir, "GrepFiles_*.txt");
            Assert.Single(files);
            Assert.Equal(text, File.ReadAllText(files[0]));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Process_OverLimit_FewLines_TruncatesPreviewToMaxResultChars()
    {
        const int limit = 1000;
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-trp-short-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            // Two lines total: stays in BuildPreview short-line branch; body would exceed limit without truncation.
            var text = new string('a', 5000) + "\n" + new string('b', 10);
            var r = ToolResultProcessor.Process("Exec", text, limit, workspace, "s1", 40) as string;
            Assert.NotNull(r);
            Assert.Contains("full output at:", r, StringComparison.OrdinalIgnoreCase);
            Assert.True(r.Length <= limit + 200, $"Preview length {r.Length} should not far exceed limit + footer.");

            var spillDir = Path.Combine(workspace, ".craft", "tool-results", "s1");
            var files = Directory.GetFiles(spillDir, "Exec_*.txt");
            Assert.Single(files);
            Assert.Equal(text, File.ReadAllText(files[0]));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void BuildPreview_ShortText_IncludesFullOutputReference()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 5).Select(i => $"L{i}"));
        var p = ToolResultProcessor.BuildPreview(lines, 40, ".craft/tool-results/t/x.txt");
        Assert.Contains("full output at:", p, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveMaxResultChars_UsesGlobalWhenNoPerTool()
    {
        var n = ToolResultProcessor.ResolveMaxResultChars($"Tool_{Guid.NewGuid():N}", 42_000);
        Assert.Equal(42_000, n);
    }
}
