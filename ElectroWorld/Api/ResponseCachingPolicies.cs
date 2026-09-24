namespace ElectroWorld.Api;

/// <summary>
/// Named output-cache policies, so an endpoint says WHAT it is rather than
/// repeating durations.
///
/// Only content that is the same for everyone is cached: the level and lesson
/// catalogue, the quiz metadata behind it, the shop. Nothing that depends on WHO
/// is asking is ever cached — a child's progress, their attempts, their wallet —
/// because a cache keyed on the URL alone would serve one child another child's
/// answer. That is why these policies are opt-in per endpoint rather than a
/// blanket default.
/// </summary>
public static class ResponseCachingPolicies
{
    /// <summary>
    /// Shared reference content: levels, lessons, quiz metadata, the shop
    /// catalogue. Short enough that an admin's edit shows up within a minute,
    /// long enough to absorb the burst of a class opening the app together.
    /// </summary>
    public const string PublicContent = "PublicContent";

    /// <summary>
    /// Per-learner content that is still worth caching for a few seconds: it
    /// varies by the caller's token, so it must never be shared between users.
    /// </summary>
    public const string PerLearner = "PerLearner";

    public static TimeSpan PublicContentDuration => TimeSpan.FromSeconds(60);

    public static TimeSpan PerLearnerDuration => TimeSpan.FromSeconds(15);
}
