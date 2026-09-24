namespace GamificationDA.Entities;

/// <summary>
/// Every movement of Sparks, earned or spent. The wallet's balance is the
/// running total; this is the ledger that explains it, and the ledger is also
/// what makes rewards IDEMPOTENT: the unique (UserId, Reason, ReferenceKey)
/// index is what stops a replayed submit paying for the same attempt twice.
/// </summary>
public class SparkTransaction
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Positive when earned, negative when spent. Never zero.</summary>
    public int Amount { get; set; }

    /// <summary>Why: LessonCompleted, QuizCompleted, PerfectScore, StreakMilestone, ShopPurchase…</summary>
    public string Reason { get; set; } = null!;

    /// <summary>
    /// What it was for — "attempt:42", "lesson:5", "purchase:7". Together with
    /// Reason it identifies the event, so the same event can only ever pay once.
    /// Null for a movement with no natural identity (an admin adjustment).
    /// </summary>
    public string? ReferenceKey { get; set; }

    /// <summary>The balance immediately after this movement, so the ledger reads without replaying it.</summary>
    public int BalanceAfter { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual LearnerWallet Wallet { get; set; } = null!;
}
