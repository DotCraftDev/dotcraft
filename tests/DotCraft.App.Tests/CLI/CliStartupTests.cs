using DotCraft.CLI;

namespace DotCraft.Tests.CLI;

public sealed class CliStartupTests
{
    [Fact]
    public void DecideWorkspaceStartup_NoArgsWithoutWorkspace_InitializesInteractively()
    {
        var decision = CliStartup.DecideWorkspaceStartup(
            CommandLineArgs.RunMode.None,
            workspaceExists: false);

        Assert.Equal(WorkspaceStartupDecision.InitializeInteractively, decision);
    }

    [Fact]
    public void DecideWorkspaceStartup_NoArgsWithWorkspace_ShowsUsage()
    {
        var decision = CliStartup.DecideWorkspaceStartup(
            CommandLineArgs.RunMode.None,
            workspaceExists: true);

        Assert.Equal(WorkspaceStartupDecision.ShowUsage, decision);
    }

    [Fact]
    public void DecideWorkspaceStartup_ExecWithoutWorkspace_FailsAsHeadless()
    {
        var decision = CliStartup.DecideWorkspaceStartup(
            CommandLineArgs.RunMode.Exec,
            workspaceExists: false);

        Assert.Equal(WorkspaceStartupDecision.MissingWorkspace, decision);
    }

    [Fact]
    public async Task WriteUsageAsync_IncludesExecAndSubcommands()
    {
        using var writer = new StringWriter();

        await CliStartup.WriteUsageAsync(writer);

        var usage = writer.ToString();
        Assert.Contains("dotcraft exec <prompt>", usage, StringComparison.Ordinal);
        Assert.Contains("dotcraft exec -", usage, StringComparison.Ordinal);
        Assert.Contains("dotcraft app-server | gateway | hub | acp | setup | skill", usage, StringComparison.Ordinal);
    }
}
