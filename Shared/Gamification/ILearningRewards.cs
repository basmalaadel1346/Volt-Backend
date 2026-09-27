namespace Shared.Gamification;

/// <summary>
/// The one thing the learning modules say to the Gamification module: "this
/// child just did something that counts". Gamification decides what it is worth
/// — Sparks, a longer streak, a freeze spent — and the caller only passes the
/// outcome on to the child.
///
/// Lives in Shared for the same reason ILessonAvailability does: Assessment and
/// Content must be able to report an activity without referencing GamificationBL.
///
/// Every call is IDEMPOTENT on <see cref="LearningActivity.ReferenceKey"/>: the
/// same finished attempt or lesson reported twice (a replayed submit, a retried
/// request) earns its reward once. Implementations must never throw: a reward
/// that could not be granted must not fail the lesson or the quiz that earned it.
/// </summary>
public interface ILearningRewards
{
    Task<RewardOutcome> RecordActivityAsync(LearningActivity activity, CancellationToken cancellationToken = default);
}

/// <summary>What a child just finished, and what makes it worth more.</summary>
/// <param name="UserId">The learner.</param>
/// <param name="Kind">Which reward rule applies.</param>
/// <param name="ReferenceKey">
/// Stable, unique identity of the thing that was finished — "attempt:42",
/// "lesson:5". The idempotency key: the same key never pays twice.
/// </param>
/// <param name="PerfectScore">No wrong answers, which earns a bonus.</param>
/// <param name="EarnedXp">XP the activity itself was worth, for the double-XP boost to multiply.</param>
public sealed record LearningActivity(
    Guid UserId,
    LearningActivityKind Kind,
    string ReferenceKey,
    bool PerfectScore = false,
    int EarnedXp = 0);

public enum LearningActivityKind
{
    /// <summary>A lesson marked complete.</summary>
    LessonCompleted,

    /// <summary>Any quiz attempt submitted — a lesson quiz, a level assessment, a review.</summary>
    QuizCompleted,

    /// <summary>The first-run placement test finished.</summary>
    PlacementCompleted,

    /// <summary>A level-skip challenge passed.</summary>
    LevelSkipPassed
}

/// <summary>
/// What the activity was worth. Also what the app needs to redraw the Sparks
/// counter and the streak flame without a second call.
/// </summary>
public sealed record RewardOutcome(
    int SparksEarned,
    int SparksBalance,
    int CurrentStreakDays,
    int LongestStreakDays,
    bool StreakExtendedToday,
    int FreezesSpent,
    int FreezesAvailable,
    IReadOnlyList<RewardLine> Lines)
{
    /// <summary>Nothing was granted — no gamification module, or the activity was already paid for.</summary>
    public static RewardOutcome None { get; } =
        new(0, 0, 0, 0, false, 0, 0, Array.Empty<RewardLine>());
}

/// <summary>One line of the "you earned…" breakdown, ready to show.</summary>
/// <param name="Reason">A stable code the app may switch on, e.g. "QuizCompleted".</param>
/// <param name="Sparks">Sparks this line granted.</param>
/// <param name="Message">The same line as a sentence, in the learner's language.</param>
public sealed record RewardLine(string Reason, int Sparks, string Message);

/// <summary>
/// Used when the Gamification module is not registered. Every activity is
/// accepted and worth nothing, so the learning modules run unchanged.
/// </summary>
public sealed class NullLearningRewards : ILearningRewards
{
    public static readonly NullLearningRewards Instance = new();

    public Task<RewardOutcome> RecordActivityAsync(
        LearningActivity activity, CancellationToken cancellationToken = default) =>
        Task.FromResult(RewardOutcome.None);
}
