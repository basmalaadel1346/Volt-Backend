using Microsoft.AspNetCore.SignalR;
using Shared.Common.Realtime;

namespace ElectroWorld.Realtime;

/// <summary>
/// Delivers <see cref="ILearnerNotifier"/> over SignalR.
///
/// Every push is best-effort and NEVER throws: the callers are background jobs
/// finishing work that is already saved, and everything sent here is also
/// readable from an ordinary endpoint. A failed push must cost a notification,
/// never the work it was announcing.
/// </summary>
public sealed class SignalRLearnerNotifier : ILearnerNotifier
{
    private readonly IHubContext<LearnerHub> _hub;
    private readonly ILogger<SignalRLearnerNotifier> _logger;

    public SignalRLearnerNotifier(IHubContext<LearnerHub> hub, ILogger<SignalRLearnerNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task HintsReadyAsync(
        Guid userId, long attemptId, string hintsStatus, CancellationToken cancellationToken = default) =>
        SendAsync(userId, LearnerHubEvents.HintsReady, new
        {
            attemptId,
            hintsStatus,
            // Named so a client that missed the push knows where to look instead.
            endpoint = $"/api/quiz-attempts/{attemptId}/retry-questions"
        }, cancellationToken);

    public Task EssaysGradedAsync(
        Guid userId, long attemptId, int gradedCount, int pendingCount, CancellationToken cancellationToken = default) =>
        SendAsync(userId, LearnerHubEvents.EssaysGraded, new
        {
            attemptId,
            gradedCount,
            pendingCount,
            endpoint = $"/api/quiz-attempts/{attemptId}/result"
        }, cancellationToken);

    public Task RewardsChangedAsync(
        Guid userId, RewardsChangedNotification notification, CancellationToken cancellationToken = default) =>
        SendAsync(userId, LearnerHubEvents.RewardsChanged, new
        {
            notification.SparksBalance,
            notification.SparksEarned,
            notification.CurrentStreakDays,
            notification.FreezesAvailable,
            notification.StreakExtendedToday,
            notification.FreezesSpent,
            notification.Message,
            endpoint = "/api/gamification/me"
        }, cancellationToken);

    private async Task SendAsync(Guid userId, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients
                .Group(LearnerHub.GroupFor(userId))
                .SendAsync(method, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "The {Method} push to user {UserId} was not delivered; the same data is available from its endpoint.",
                method, userId);
        }
    }
}
