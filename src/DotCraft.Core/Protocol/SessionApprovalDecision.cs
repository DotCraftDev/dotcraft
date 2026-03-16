namespace DotCraft.Protocol;

/// <summary>
/// Rich approval decision captured by Session Core and the wire protocol.
/// </summary>
public enum SessionApprovalDecision
{
    AcceptOnce,
    AcceptForSession,
    Reject,
    CancelTurn
}

/// <summary>
/// Helpers for working with <see cref="SessionApprovalDecision"/>.
/// </summary>
public static class SessionApprovalDecisionExtensions
{
    /// <summary>
    /// Returns true when the decision allows the requested operation to continue.
    /// </summary>
    public static bool IsApproved(this SessionApprovalDecision decision) =>
        decision is SessionApprovalDecision.AcceptOnce or SessionApprovalDecision.AcceptForSession;

    /// <summary>
    /// Returns true when the decision should be cached for the rest of the thread.
    /// </summary>
    public static bool AppliesToSession(this SessionApprovalDecision decision) =>
        decision == SessionApprovalDecision.AcceptForSession;
}
