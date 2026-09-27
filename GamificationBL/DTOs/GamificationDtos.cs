namespace GamificationBL.DTOs;

/// <summary>GET /api/gamification/me — everything the reward bar shows.</summary>
public class LearnerGamificationDto
{
    public string Language { get; set; } = null!;

    public int SparksBalance { get; set; }

    public int LifetimeSparks { get; set; }

    public int CurrentStreakDays { get; set; }

    public int LongestStreakDays { get; set; }

    /// <summary>The day of the last counted activity (UTC), or null before the first one.</summary>
    public DateOnly? LastActivityOn { get; set; }

    /// <summary>True once today's activity has been counted: the flame is lit.</summary>
    public bool ActiveToday { get; set; }

    /// <summary>
    /// Days the streak survives without another activity, freezes included. 0
    /// means it breaks unless the child does something today.
    /// </summary>
    public int StreakSafeForDays { get; set; }

    public byte StreakFreezes { get; set; }

    public byte MaxStreakFreezes { get; set; }

    public int StreakFreezesUsed { get; set; }

    /// <summary>Days still to go for the next streak reward box.</summary>
    public int DaysToNextMilestone { get; set; }

    public int MilestoneSparks { get; set; }

    /// <summary>A sentence for the child, in <see cref="Language"/>.</summary>
    public string Message { get; set; } = null!;

    public List<OwnedItemDto> Items { get; set; } = new();

    /// <summary>Boosts running right now. Empty when none is.</summary>
    public List<ActiveBoostDto> ActiveBoosts { get; set; } = new();
}

public class OwnedItemDto
{
    public int ItemId { get; set; }

    public string Code { get; set; } = null!;

    public string Kind { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? ImageUrl { get; set; }

    public int Quantity { get; set; }

    /// <summary>Cosmetics only: the child is wearing this one.</summary>
    public bool IsEquipped { get; set; }
}

public class ActiveBoostDto
{
    public int ItemId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public byte Multiplier { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int SecondsRemaining { get; set; }
}

/// <summary>GET /api/gamification/shop — one buyable item, priced for this child.</summary>
public class ShopItemDto
{
    public int ItemId { get; set; }

    public string Code { get; set; } = null!;

    /// <summary>StreakFreeze | Avatar | Boost.</summary>
    public string Kind { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? ImageUrl { get; set; }

    public int PriceSparks { get; set; }

    /// <summary>How many the child already has.</summary>
    public int Owned { get; set; }

    /// <summary>The ceiling on holding this item, or null when there is none.</summary>
    public byte? MaxOwned { get; set; }

    /// <summary>Boosts only.</summary>
    public byte? BoostMultiplier { get; set; }

    /// <summary>Boosts only.</summary>
    public int? BoostMinutes { get; set; }

    /// <summary>The child can buy it right now: enough Sparks, and not at the cap.</summary>
    public bool CanAfford { get; set; }

    public bool AtMaxOwned { get; set; }
}

/// <summary>The result of a purchase: what was bought and what the wallet looks like now.</summary>
public class PurchaseResultDto
{
    public int ItemId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int PricePaid { get; set; }

    public int SparksBalance { get; set; }

    public int Owned { get; set; }

    /// <summary>Set when the purchase started a boost.</summary>
    public ActiveBoostDto? ActivatedBoost { get; set; }

    public string Message { get; set; } = null!;
}
