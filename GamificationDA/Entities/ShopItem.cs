namespace GamificationDA.Entities;

/// <summary>
/// Something a child can spend Sparks on. Seeded and maintained by admins; the
/// catalogue is small enough that its two languages live in columns rather than
/// in a translation table.
/// </summary>
public class ShopItem
{
    public int Id { get; set; }

    /// <summary>Stable machine name ("streak_freeze", "double_xp_15"), so code never depends on an id.</summary>
    public string Code { get; set; } = null!;

    /// <summary>StreakFreeze | Avatar | Boost — what buying it actually does.</summary>
    public string Kind { get; set; } = null!;

    public string NameAr { get; set; } = null!;

    public string NameEn { get; set; } = null!;

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public int PriceSparks { get; set; }

    /// <summary>
    /// How many of this item a child may hold at once, or null for no limit. Two
    /// freezes is the usual cap: enough to cover a bad week, not enough to make
    /// the streak meaningless.
    /// </summary>
    public byte? MaxOwned { get; set; }

    /// <summary>Boost items only: what XP is multiplied by while it runs.</summary>
    public byte? BoostMultiplier { get; set; }

    /// <summary>Boost items only: how long it runs once activated.</summary>
    public int? BoostMinutes { get; set; }

    /// <summary>Optional artwork for the shop tile.</summary>
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; }

    public short SortOrder { get; set; }

    public virtual ICollection<LearnerItem> LearnerItems { get; set; } = new List<LearnerItem>();
}
