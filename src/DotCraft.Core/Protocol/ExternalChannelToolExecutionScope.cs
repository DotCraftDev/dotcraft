using DotCraft.Security;

namespace DotCraft.Protocol;

/// <summary>
/// Captures the active turn context required by runtime external channel tool wrappers.
/// </summary>
public sealed class ExternalChannelToolExecutionContext
{
    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public required string OriginChannel { get; init; }

    public string? ChannelContext { get; init; }

    public string? SenderId { get; init; }

    public string? GroupId { get; init; }

    public required string WorkspacePath { get; init; }

    public required bool RequireApprovalOutsideWorkspace { get; init; }

    public required IApprovalService ApprovalService { get; init; }

    public PathBlacklist? PathBlacklist { get; init; }

    public required SessionTurn Turn { get; init; }

    public required Func<int> NextItemSequence { get; init; }

    public required Action<SessionItem> EmitItemStarted { get; init; }

    public required Action<SessionItem> EmitItemCompleted { get; init; }
}

public static class ExternalChannelToolExecutionScope
{
    private static readonly AsyncLocal<ExternalChannelToolExecutionContext?> CurrentContext = new();

    public static ExternalChannelToolExecutionContext? Current => CurrentContext.Value;

    public static IDisposable Set(ExternalChannelToolExecutionContext context)
    {
        CurrentContext.Value = context;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        public void Dispose() => CurrentContext.Value = null;
    }
}
