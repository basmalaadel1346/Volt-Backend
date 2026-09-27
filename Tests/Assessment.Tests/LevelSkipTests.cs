using AssessmentBL;
using AssessmentBL.Services;
using AssessmentBL.Services.Constants;

namespace Assessment.Tests;

/// <summary>
/// The sampling rule of the level-skip challenge. Round-robin across the level's
/// lessons is the whole point: a child who only knows lesson one must not be able
/// to skip the level.
/// </summary>
public class LevelSkipSamplingTests
{
    private static readonly int[][] ThreeLessons =
    [
        [101, 102, 103, 104],
        [201, 202, 203, 204],
        [301, 302, 303, 304]
    ];

    private static List<int> Take(int count, int rotation = 0) =>
        LevelSkipEngine.TakeRoundRobin(
            ThreeLessons.Select(p => (IReadOnlyList<int>)p).ToList(), count, rotation);

    [Fact]
    public void Questions_AreSpreadAcrossLessons_NotTakenLessonByLesson()
    {
        // The first three picks come from three different lessons.
        Assert.Equal(new[] { 101, 201, 301 }, Take(3).ToArray());

        // And ten picks cover all three lessons rather than draining the first.
        var ten = Take(10);
        Assert.Equal(10, ten.Count);
        Assert.Equal(4, ten.Count(id => id < 200));
        Assert.Equal(3, ten.Count(id => id is >= 200 and < 300));
        Assert.Equal(3, ten.Count(id => id >= 300));
    }

    [Fact]
    public void ARetake_DrawsADifferentPaper()
    {
        var first = Take(3);
        var second = Take(3, rotation: 1);

        Assert.Equal(new[] { 101, 201, 301 }, first.ToArray());
        Assert.Equal(new[] { 102, 202, 302 }, second.ToArray());
    }

    [Fact]
    public void ARotation_LargerThanAPool_Wraps()
    {
        // Four questions per lesson, rotated five: wraps back to the second.
        Assert.Equal(new[] { 102, 202, 302 }, Take(3, rotation: 5).ToArray());
    }

    [Fact]
    public void AskingForMoreThanExists_ReturnsEverythingOnce()
    {
        var all = Take(100);

        Assert.Equal(12, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void UnevenLessons_DoNotStallOnTheShortOne()
    {
        var pools = new List<IReadOnlyList<int>> { new[] { 101 }, new[] { 201, 202, 203 } };

        // Lesson one runs out after its single question; the rest come from two.
        Assert.Equal(new[] { 101, 201, 202, 203 }, LevelSkipEngine.TakeRoundRobin(pools, 10).ToArray());
    }

    [Fact]
    public void NoPools_YieldNothing()
        => Assert.Empty(LevelSkipEngine.TakeRoundRobin([], 10));
}

/// <summary>The terms the challenge is played on, and that only it is timed.</summary>
public class QuizPlayRuleTests
{
    private static QuizPlayRules RulesFor(string quizType) =>
        new QuizRules(null!, Microsoft.Extensions.Options.Options.Create(new AssessmentSettings())).For(quizType);

    [Fact]
    public void ALevelSkipChallenge_RunsOnAClockAndHearts()
    {
        var rules = RulesFor(QuizTypes.LevelSkip);

        Assert.Equal(180, rules.TimeLimitSeconds);
        Assert.Equal((byte)3, rules.Hearts);
    }

    [Theory]
    [InlineData(QuizTypes.LessonQuiz)]
    [InlineData(QuizTypes.LevelAssessment)]
    [InlineData(QuizTypes.Placement)]
    [InlineData(QuizTypes.Standalone)]
    [InlineData(QuizTypes.LessonReview)]
    public void EveryOtherQuiz_IsUntimedAndHeartless(string quizType)
    {
        var rules = RulesFor(quizType);

        Assert.Null(rules.TimeLimitSeconds);
        Assert.Null(rules.Hearts);
        Assert.False(rules.HasRunOut(DateTime.UtcNow.AddYears(-1), DateTime.UtcNow));
    }

    [Fact]
    public void ATimedAttempt_RunsOutExactlyAtItsDeadline()
    {
        var rules = RulesFor(QuizTypes.LevelSkip);
        var startedAt = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(startedAt.AddSeconds(180), rules.ExpiresAt(startedAt));
        Assert.False(rules.HasRunOut(startedAt, startedAt.AddSeconds(180)));
        Assert.True(rules.HasRunOut(startedAt, startedAt.AddSeconds(181)));
    }

    [Fact]
    public void MisconfiguredValues_AreClamped()
    {
        var settings = new AssessmentSettings
        {
            LevelSkipQuestionCount = 0,
            LevelSkipTimeLimitSeconds = 1,
            LevelSkipHearts = 0,
            LevelSkipPassPercentage = 500
        };

        Assert.Equal(3, settings.EffectiveLevelSkipQuestionCount);
        Assert.Equal(30, settings.EffectiveLevelSkipTimeLimitSeconds);
        Assert.Equal((byte)1, settings.EffectiveLevelSkipHearts);
        Assert.Equal(100m, settings.EffectiveLevelSkipPassPercentage);
    }

    [Fact]
    public void Defaults_AreTenQuestionsThreeMinutesThreeHearts()
    {
        var settings = new AssessmentSettings();

        Assert.Equal(10, settings.EffectiveLevelSkipQuestionCount);
        Assert.Equal(180, settings.EffectiveLevelSkipTimeLimitSeconds);
        Assert.Equal((byte)3, settings.EffectiveLevelSkipHearts);
    }
}
