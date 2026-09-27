using GamificationBL;
using GamificationBL.Services;

namespace Gamification.Tests;

/// <summary>
/// The streak rule. Everything that is easy to get subtly wrong about a streak —
/// where the day boundary falls, what counts as "missed", what a gap costs in
/// freezes — is decided by StreakRules and pinned here.
/// </summary>
public class StreakRuleTests
{
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly DateOnly Thursday = Monday.AddDays(3);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);

    [Fact]
    public void TheFirstActivityEver_StartsAStreakOfOne()
    {
        var change = StreakRules.Apply(null, Monday, currentStreak: 0, freezesAvailable: 0);

        Assert.Equal(1, change.StreakDays);
        Assert.True(change.Extended);
        Assert.False(change.Broken);
        Assert.Equal((byte)0, change.FreezesSpent);
    }

    [Fact]
    public void ComingBackTheNextDay_ExtendsTheStreak()
    {
        var change = StreakRules.Apply(Monday, Tuesday, currentStreak: 4, freezesAvailable: 0);

        Assert.Equal(5, change.StreakDays);
        Assert.True(change.Extended);
    }

    [Fact]
    public void ASecondActivityTheSameDay_CountsOnce()
    {
        // A streak counts days, not sessions — otherwise an afternoon of quizzes
        // would "build" a habit the child does not have.
        var change = StreakRules.Apply(Monday, Monday, currentStreak: 4, freezesAvailable: 2);

        Assert.Equal(4, change.StreakDays);
        Assert.False(change.Extended);
        Assert.Equal((byte)0, change.FreezesSpent);
    }

    [Fact]
    public void MissingADay_WithAFreeze_KeepsTheStreakAndSpendsOne()
    {
        // Away Tuesday, back Wednesday: one missed day, one freeze.
        var change = StreakRules.Apply(Monday, Wednesday, currentStreak: 15, freezesAvailable: 1);

        Assert.Equal(16, change.StreakDays);
        Assert.Equal((byte)1, change.FreezesSpent);
        Assert.False(change.Broken);
    }

    [Fact]
    public void MissingTwoDays_CostsTwoFreezes()
    {
        var change = StreakRules.Apply(Monday, Thursday, currentStreak: 15, freezesAvailable: 2);

        Assert.Equal(16, change.StreakDays);
        Assert.Equal((byte)2, change.FreezesSpent);
        Assert.False(change.Broken);
    }

    [Fact]
    public void MissingADay_WithNoFreeze_BreaksTheStreakBackToOne()
    {
        var change = StreakRules.Apply(Monday, Wednesday, currentStreak: 15, freezesAvailable: 0);

        Assert.Equal(1, change.StreakDays);
        Assert.True(change.Broken);
        Assert.True(change.Extended);
    }

    [Fact]
    public void AGapTooLongToBridge_DoesNotSpendTheFreezesAnyway()
    {
        // Six days missed, two freezes held. Taking them for a rescue that did not
        // happen would be the one thing more discouraging than losing the streak.
        var change = StreakRules.Apply(Monday, NextMonday, currentStreak: 30, freezesAvailable: 2);

        Assert.Equal(1, change.StreakDays);
        Assert.True(change.Broken);
        Assert.Equal((byte)0, change.FreezesSpent);
    }

    [Fact]
    public void AClockSkewedBackwards_NeverInflatesTheStreak()
    {
        // "Yesterday" arriving after "today" is a clock or timezone artefact, not a
        // day of learning.
        var change = StreakRules.Apply(Wednesday, Monday, currentStreak: 7, freezesAvailable: 2);

        Assert.Equal(7, change.StreakDays);
        Assert.False(change.Extended);
        Assert.Equal((byte)0, change.FreezesSpent);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(14, true)]
    [InlineData(21, true)]
    [InlineData(6, false)]
    [InlineData(8, false)]
    [InlineData(0, false)]
    public void MilestonesLandEverySeventhDay(int streakDays, bool expected)
        => Assert.Equal(expected, StreakRules.IsMilestone(streakDays, 7));
}

/// <summary>What each activity is worth, and that misconfiguration cannot break it.</summary>
public class GamificationSettingTests
{
    [Fact]
    public void Defaults_MatchTheDesignedEconomy()
    {
        var settings = new GamificationSettings();

        Assert.Equal(5, settings.EffectiveLessonCompletedSparks);
        Assert.Equal(5, settings.EffectiveQuizCompletedSparks);
        Assert.Equal(10, settings.EffectivePerfectScoreBonusSparks);
        Assert.Equal(20, settings.EffectivePlacementCompletedSparks);
        Assert.Equal(7, settings.EffectiveStreakMilestoneDays);
        Assert.Equal(50, settings.EffectiveStreakMilestoneSparks);
        Assert.Equal((byte)2, settings.EffectiveMaxStreakFreezes);
    }

    [Fact]
    public void MisconfiguredValues_AreClamped()
    {
        var settings = new GamificationSettings
        {
            LessonCompletedSparks = -5,
            StreakMilestoneDays = 0,
            MaxStreakFreezes = 500
        };

        Assert.Equal(0, settings.EffectiveLessonCompletedSparks);
        // Never 0: a milestone "every 0 days" would divide by zero on the progress
        // screen and fire on every single activity.
        Assert.Equal(2, settings.EffectiveStreakMilestoneDays);
        Assert.Equal((byte)10, settings.EffectiveMaxStreakFreezes);
    }
}
