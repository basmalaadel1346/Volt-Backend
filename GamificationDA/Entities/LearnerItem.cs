namespace GamificationDA.Entities;

/// <summary>What a learner owns, one row per (learner, item).</summary>
public class LearnerItem
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public int ShopItemId { get; set; }

    public int Quantity { get; set; }

    /// <summary>
    /// Cosmetics only: the avatar piece the child is currently wearing. Several
    /// owned, at most one equipped per kind.
    /// </summary>
    public bool IsEquipped { get; set; }

    public DateTime AcquiredAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual LearnerWallet Wallet { get; set; } = null!;

    public virtual ShopItem ShopItem { get; set; } = null!;
}
