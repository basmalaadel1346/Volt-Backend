namespace GamificationBL;

/// <summary>
/// What each thing is worth, bound from the "Gamification" configuration
/// section. Every value has a safe default, so the section is optional — and
/// every one of them is a tuning knob a designer will want to turn without a
/// deploy.
/// </summary>
public sealed class GamificationSettings
{
    public const string SectionName = "Gamification";

    /// <summary>Sparks for finishing a lesson.</summary>
    public int LessonCompletedSparks { get; set; } = 5;

    /// <summary>Sparks for submitting any quiz attempt.</summary>
    public int QuizCompletedSparks { get; set; } = 5;

    /// <summary>Extra Sparks for a quiz answered with no mistakes at all.</summary>
    public int PerfectScoreBonusSparks { get; set; } = 10;

    /// <summary>Sparks for finishing the first-run placement test.</summary>
    public int PlacementCompletedSparks { get; set; } = 20;

    /// <summary>Sparks for passing a level-skip challenge.</summary>
    public int LevelSkipPassedSparks { get; set; } = 25;

    /// <summary>The reward box for keeping a streak this many days.</summary>
    public int StreakMilestoneDays { get; set; } = 7;

    public int StreakMilestoneSparks { get; set; } = 50;

    /// <summary>
    /// Freezes a learner may hold at once. Two is the deliberate ceiling: enough
    /// to survive a bad week, not enough to buy ten and disappear for ten days,
    /// which would turn the streak into something that no longer measures a habit.
    /// </summary>
    public int MaxStreakFreezes { get; set; } = 2;

    public int EffectiveLessonCompletedSparks => Math.Clamp(LessonCompletedSparks, 0, 10_000);

    public int EffectiveQuizCompletedSparks => Math.Clamp(QuizCompletedSparks, 0, 10_000);

    public int EffectivePerfectScoreBonusSparks => Math.Clamp(PerfectScoreBonusSparks, 0, 10_000);

    public int EffectivePlacementCompletedSparks => Math.Clamp(PlacementCompletedSparks, 0, 10_000);

    public int EffectiveLevelSkipPassedSparks => Math.Clamp(LevelSkipPassedSparks, 0, 10_000);

    public int EffectiveStreakMilestoneDays => Math.Clamp(StreakMilestoneDays, 2, 365);

    public int EffectiveStreakMilestoneSparks => Math.Clamp(StreakMilestoneSparks, 0, 10_000);

    public byte EffectiveMaxStreakFreezes => (byte)Math.Clamp(MaxStreakFreezes, 0, 10);
}
