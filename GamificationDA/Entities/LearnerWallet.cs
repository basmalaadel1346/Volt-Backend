using System.ComponentModel.DataAnnotations;

namespace GamificationDA.Entities;

/// <summary>
/// One learner's gamification state: the Sparks they hold, the streak they are
/// protecting, and the freezes standing between the two.
///
/// One row per learner, created the first time they earn anything. UserId is a
/// plain column with no foreign key — the same cross-module convention
/// Assessment uses for QuizAttempts.UserId.
/// </summary>
public class LearnerWallet
{
    public Guid UserId { get; set; }

    /// <summary>Sparks available to spend right now.</summary>
    public int SparksBalance { get; set; }

    /// <summary>Every Spark ever earned. Never goes down, so spending cannot erase a record.</summary>
    public int LifetimeSparks { get; set; }

    /// <summary>Consecutive days with at least one activity, freezes included.</summary>
    public int CurrentStreakDays { get; set; }

    /// <summary>The longest streak this learner has ever reached.</summary>
    public int LongestStreakDays { get; set; }

    /// <summary>
    /// The DAY of the last counted activity, in UTC. A date, not a timestamp:
    /// a streak is counted in days, and storing the instant invited comparisons
    /// that made "yesterday" depend on the time of day.
    /// </summary>
    public DateOnly? LastActivityOn { get; set; }

    /// <summary>
    /// Freezes in hand. Each one covers one missed day, automatically, the next
    /// time the child comes back. Capped by GamificationSettings.MaxStreakFreezes
    /// so buying ten is not a way to stay away for ten days.
    /// </summary>
    public byte StreakFreezes { get; set; }

    /// <summary>Freezes this learner has ever spent — for the "you were saved" message.</summary>
    public int StreakFreezesUsed { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Makes the read-modify-write of a reward safe: two activities finishing at
    /// the same instant no longer lose one set of Sparks — the second gets a
    /// concurrency conflict and is retried.
    /// </summary>
    [Timestamp] public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<SparkTransaction> SparkTransactions { get; set; } = new List<SparkTransaction>();

    public virtual ICollection<LearnerItem> Items { get; set; } = new List<LearnerItem>();

    public virtual ICollection<LearnerBoost> Boosts { get; set; } = new List<LearnerBoost>();
}
