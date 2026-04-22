namespace DotCraft.Protocol;

/// <summary>
/// Internal runtime lifecycle signals that hosts can aggregate into workspace-level thread runtime snapshots.
/// </summary>
public enum SessionThreadRuntimeSignal
{
    TurnStarted,
    TurnCompleted,
    TurnCompletedAwaitingPlanConfirmation,
    TurnFailed,
    TurnCancelled,
    ApprovalRequested,
    ApprovalResolved,
    /// <summary>
    /// A successful context compaction just completed. UI layers use this to
    /// clear any "context almost full" warning indicator.
    /// </summary>
    ContextCompacted,
}
