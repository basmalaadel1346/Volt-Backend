namespace Shared.Common.Realtime;

/// <summary>
/// Pushes to one learner's open app, so it can update a screen the moment
/// background work finishes instead of asking "are you done yet?" every couple
/// of minutes. Implemented over SignalR in the API; every module talks to this
/// contract, not to SignalR.
///
/// Every method is best-effort: a push that cannot be delivered (the app is
/// closed, the hub is down) is not an error, because every payload here is also
/// readable from a normal endpoint. Implementations must therefore never throw.
/// </summary>
public interface ILearnerNotifier
{
    /// <summary>The AI finished writing the hints for an attempt's wrong answers.</summary>
    Task HintsReadyAsync(
        Guid userId, long attemptId, string hintsStatus, CancellationToken cancellationToken = default);

    /// <summary>The AI finished grading some or all of an attempt's essay answers.</summary>
    Task EssaysGradedAsync(
        Guid userId, long attemptId, int gradedCount, int pendingCount, CancellationToken cancellationToken = default);

    /// <summary>Sparks, streak or inventory changed — the app should refresh the gamification bar.</summary>
    Task RewardsChangedAsync(
        Guid userId, RewardsChangedNotification notification, CancellationToken cancellationToken = default);
}

/// <summary>What changed about a learner's rewards, as the app would show it.</summary>
public sealed record RewardsChangedNotification(
    int SparksBalance,
    int SparksEarned,
    int CurrentStreakDays,
    int FreezesAvailable,
    bool StreakExtendedToday,
    int FreezesSpent,
    string? Message);

/// <summary>
/// Used when no real-time transport is registered — a test host, or a job
/// running outside the API. Every push is simply dropped: the same information
/// is always available from an endpoint.
/// </summary>
public sealed class NullLearnerNotifier : ILearnerNotifier
{
    public static readonly NullLearnerNotifier Instance = new();

    public Task HintsReadyAsync(Guid userId, long attemptId, string hintsStatus, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task EssaysGradedAsync(Guid userId, long attemptId, int gradedCount, int pendingCount, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RewardsChangedAsync(Guid userId, RewardsChangedNotification notification, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
