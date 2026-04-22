using DotCraft.Context.Compaction;
using DotCraft.Localization;

namespace DotCraft.Tests.Context.Compaction;

public sealed class CompactionPromptsTests
{
    [Fact]
    public void FormatCompactSummary_StripsAnalysisBlock()
    {
        var raw = "<analysis>internal thoughts</analysis><summary>the important part</summary>";
        var formatted = CompactionPrompts.FormatCompactSummary(raw);

        Assert.DoesNotContain("<analysis>", formatted);
        Assert.DoesNotContain("internal thoughts", formatted);
        Assert.Contains("the important part", formatted);
    }

    [Fact]
    public void FormatCompactSummary_EmptyReturnsEmpty()
    {
        Assert.Equal(string.Empty, CompactionPrompts.FormatCompactSummary(""));
    }

    [Fact]
    public void GetCompactPrompt_IncludesNoToolsReminder()
    {
        var prompt = CompactionPrompts.GetCompactPrompt(Language.English);
        Assert.Contains("Do NOT call any tools", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<analysis>", prompt);
        Assert.Contains("<summary>", prompt);
    }

    [Fact]
    public void GetPartialCompactPrompt_DifferentFromFull()
    {
        var full = CompactionPrompts.GetCompactPrompt(Language.English);
        var partial = CompactionPrompts.GetPartialCompactPrompt(Language.English);
        Assert.NotEqual(full, partial);
    }

    [Fact]
    public void GetCompactPrompt_Chinese()
    {
        var prompt = CompactionPrompts.GetCompactPrompt(Language.Chinese);
        Assert.Contains("不要调用任何工具", prompt);
    }

    [Fact]
    public void GetCompactUserSummaryMessage_AttachesRecentPreservedNote()
    {
        var msg = CompactionPrompts.GetCompactUserSummaryMessage(
            "<summary>X</summary>",
            transcriptPath: null,
            recentMessagesPreserved: true,
            language: Language.English);
        Assert.Contains("This session is being continued", msg);
        Assert.Contains("Recent messages are preserved", msg);
    }

    [Fact]
    public void GetCompactUserSummaryMessage_OmitsTranscriptHintWhenNotProvided()
    {
        var msg = CompactionPrompts.GetCompactUserSummaryMessage(
            "<summary>X</summary>",
            transcriptPath: null,
            recentMessagesPreserved: false,
            language: Language.English);
        Assert.DoesNotContain("read the full transcript", msg, StringComparison.OrdinalIgnoreCase);
    }
}
