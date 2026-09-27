namespace GamificationDA.Entities;

/// <summary>
/// A running XP boost. Separate from the inventory because it is a window in
/// time, not a possession: it starts when the child activates it and ends on its
/// own. Rows are kept after they expire so "you earned double XP here" stays
/// explainable.
/// </summary>
public class LearnerBoost
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public int ShopItemId { get; set; }

    /// <summary>What XP is multiplied by while this runs.</summary>
    public byte Multiplier { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public virtual LearnerWallet Wallet { get; set; } = null!;

    public virtual ShopItem ShopItem { get; set; } = null!;
}
