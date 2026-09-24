namespace GamificationBL.Services.Constants;

/// <summary>Mirrors CK_ShopItems_Kind: what buying an item actually does.</summary>
public static class ShopItemKinds
{
    /// <summary>Goes into the inventory as an extra life for the streak.</summary>
    public const string StreakFreeze = "StreakFreeze";

    /// <summary>A cosmetic the child can wear. Several owned, one equipped.</summary>
    public const string Avatar = "Avatar";

    /// <summary>A timed XP multiplier; buying it starts the clock.</summary>
    public const string Boost = "Boost";

    public static readonly IReadOnlyList<string> All = [StreakFreeze, Avatar, Boost];
}

/// <summary>
/// Why Sparks moved. Stored in SparkTransactions.Reason and, together with the
/// reference key, is what makes a reward payable exactly once.
/// </summary>
public static class SparkReasons
{
    public const string LessonCompleted = "LessonCompleted";
    public const string QuizCompleted = "QuizCompleted";
    public const string PerfectScore = "PerfectScore";
    public const string PlacementCompleted = "PlacementCompleted";
    public const string LevelSkipPassed = "LevelSkipPassed";
    public const string StreakMilestone = "StreakMilestone";
    public const string ShopPurchase = "ShopPurchase";
}

/// <summary>The item every shop is expected to carry, referenced by code rather than id.</summary>
public static class ShopItemCodes
{
    public const string StreakFreeze = "streak_freeze";
}
