using DotCraft.Localization;
using DotCraft.Protocol;

namespace DotCraft.AppServer;

internal sealed record HubTurnNotificationSpec(
    string Kind,
    string TitleKey,
    string BodyKey,
    string Severity);

internal sealed record HubTurnNotificationDecision(
    bool ShouldNotify,
    string DisplayName);

internal static class HubTurnNotificationPolicy
{
    public static HubTurnNotificationSpec? GetSpec(SessionThreadRuntimeSignal signal) =>
        signal switch
        {
            SessionThreadRuntimeSignal.TurnCompleted => new HubTurnNotificationSpec(
                "turnCompleted",
                "hub.notification.turn_completed.title",
                "hub.notification.turn_completed.body",
                "success"),
            SessionThreadRuntimeSignal.TurnFailed => new HubTurnNotificationSpec(
                "turnFailed",
                "hub.notification.turn_failed.title",
                "hub.notification.turn_failed.body",
                "error"),
            _ => null
        };

    public static async Task<HubTurnNotificationDecision> ResolveDecisionAsync(
        ISessionService sessionService,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var thread = await sessionService.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            if (ThreadVisibility.IsInternal(thread))
                return new HubTurnNotificationDecision(false, string.Empty);

            if (!string.IsNullOrWhiteSpace(thread.DisplayName))
                return new HubTurnNotificationDecision(true, thread.DisplayName.Trim());
        }
        catch
        {
            // Notifications are best-effort; falling back keeps turn completion isolated.
        }

        return new HubTurnNotificationDecision(
            true,
            LanguageService.Current.T("hub.notification.thread.default"));
    }
}
