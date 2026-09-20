using AssessmentBL;
using AssessmentBL.Services;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Shared.Content;
using Xunit;

namespace Assessment.Tests;

/// <summary>
/// Pins the placement rule: the child is placed at the first level, in learning
/// order, they have not shown they master — and never skipped past a level the
/// test could not check.
/// </summary>
public class PlacementRuleTests
{
    private static readonly LevelSummary Level1 = new(10, "Level 1", 1);
    private static readonly LevelSummary Level2 = new(20, "Level 2", 2);
    private static readonly LevelSummary Level3 = new(30, "Level 3", 3);

    private static readonly IReadOnlyList<LevelSummary> Levels = [Level1, Level2, Level3];

    // Four questions per level: 101-104 → L1, 201-204 → L2, 301-304 → L3.
    private static Dictionary<int, int> FourPerLevel() =>
        Enumerable.Range(1, 4).SelectMany(i => new[]
        {
            (Question: 100 + i, Level: Level1.Id),
            (Question: 200 + i, Level: Level2.Id),
            (Question: 300 + i, Level: Level3.Id)
        }).ToDictionary(x => x.Question, x => x.Level);

    // Every question worth 1 — the default Points.
    private static Dictionary<int, byte> OnePointEach(Dictionary<int, int> levelOfQuestion) =>
        levelOfQuestion.Keys.ToDictionary(id => id, _ => (byte)1);

    private static PlacementDecision Decide(Dictionary<int, int> levelOfQuestion, params int[] wrong) =>
        PlacementEngine.Decide(
            Levels, levelOfQuestion, OnePointEach(levelOfQuestion), wrong.ToHashSet(), passPercentage: 75m);

    private static PlacementDecision DecideWeighted(
        Dictionary<int, int> levelOfQuestion, Dictionary<int, byte> points, params int[] wrong) =>
        PlacementEngine.Decide(Levels, levelOfQuestion, points, wrong.ToHashSet(), passPercentage: 75m);

    [Fact]
    public void MasteringEveryLevel_PlacesAtTheLastLevel()
        => Assert.Equal(Level3, Decide(FourPerLevel()).PlacedLevel);

    [Fact]
    public void FailingTheFirstLevel_PlacesAtTheFirstLevel()
        => Assert.Equal(Level1, Decide(FourPerLevel(), 101, 102).PlacedLevel);

    [Fact]
    public void PlacesAtTheFirstLevelNotMastered_EvenIfALaterOneWasAced()
    {
        // L2 at 50% stops the climb; the perfect L3 does not skip the child past it.
        var decision = Decide(FourPerLevel(), 201, 202);

        Assert.Equal(Level2, decision.PlacedLevel);
        Assert.True(decision.Levels.Single(l => l.Level == Level3).Mastered);
    }

    [Fact]
    public void ThePassThresholdIsInclusive()
    {
        // 3 of 4 = 75% meets a 75% threshold, so L1 counts as mastered.
        var decision = Decide(FourPerLevel(), 101);

        Assert.True(decision.Levels.Single(l => l.Level == Level1).Mastered);
        Assert.Equal(Level3, decision.PlacedLevel);
    }

    [Fact]
    public void ALevelWithNoPlacementQuestions_CannotBeSkipped()
    {
        // L2 has no assessment questions: the child cannot show they master it,
        // so they are placed there even though L1 and L3 are perfect.
        var withoutLevel2 = FourPerLevel().Where(q => q.Value != Level2.Id).ToDictionary(q => q.Key, q => q.Value);

        var decision = Decide(withoutLevel2);

        Assert.Equal(Level2, decision.PlacedLevel);
        Assert.Equal(0, decision.Levels.Single(l => l.Level == Level2).QuestionsAsked);
    }

    [Fact]
    public void Decide_RequiresAtLeastOneLevel()
        => Assert.Throws<ArgumentException>(() =>
            PlacementEngine.Decide([], FourPerLevel(), OnePointEach(FourPerLevel()), new HashSet<int>(), 75m));

    [Fact]
    public void MissingTheHeavyQuestion_FailsALevelThatCountingAloneWouldPass()
    {
        // L1: 101-103 worth 1, 104 worth 9. Wrong on 104 only: 3 of 4 questions
        // (75%) but 3 of 12 points (25%) — not mastered.
        var levels = FourPerLevel();
        var points = OnePointEach(levels);
        points[104] = 9;

        var decision = DecideWeighted(levels, points, 104);
        var level1 = decision.Levels.Single(l => l.Level == Level1);

        Assert.False(level1.Mastered);
        Assert.Equal(25m, level1.ScorePercentage);
        Assert.Equal((3, 4, 3, 12), (level1.CorrectAnswers, level1.QuestionsAsked, level1.EarnedPoints, level1.TotalPoints));
        Assert.Equal(Level1, decision.PlacedLevel);
    }

    [Fact]
    public void GettingTheHeavyQuestionRight_MastersALevelThatCountingAloneWouldFail()
    {
        // Wrong on the three 1-point questions: 1 of 4 questions (25%) but 9 of 12
        // points (75%) — mastered at the inclusive 75% threshold.
        var levels = FourPerLevel();
        var points = OnePointEach(levels);
        points[104] = 9;

        var decision = DecideWeighted(levels, points, 101, 102, 103);
        var level1 = decision.Levels.Single(l => l.Level == Level1);

        Assert.True(level1.Mastered);
        Assert.Equal(75m, level1.ScorePercentage);
        Assert.Equal(Level3, decision.PlacedLevel);
    }

    [Fact]
    public void Decide_RefusesAQuestionWithoutPoints()
    {
        var levels = FourPerLevel();
        var points = OnePointEach(levels);
        points.Remove(203);

        Assert.Throws<ArgumentException>(() => DecideWeighted(levels, points));
    }

    [Fact]
    public void TheResult_ListsOnlyAssessedLevels_AndNamesThePlacedOne()
    {
        var withoutLevel2 = FourPerLevel().Where(q => q.Value != Level2.Id).ToDictionary(q => q.Key, q => q.Value);
        var decision = Decide(withoutLevel2, 101);

        var dto = PlacementEngine.ToResultDto(decision, decision.PlacedLevel.Id, 87.5m, DateTime.UtcNow);

        Assert.Equal(Level2.Id, dto.LevelId);
        Assert.Equal("Level 2", dto.LevelTitle);
        Assert.Equal(new[] { Level1.Id, Level3.Id }, dto.Levels.Select(l => l.LevelId).ToArray());
        Assert.Equal(75m, dto.Levels[0].ScorePercentage);
        Assert.Equal(4, dto.Levels[0].TotalPoints);
        Assert.Equal(3, dto.Levels[0].EarnedPoints);
    }
}

public class PlacementSettingsTests
{
    [Fact]
    public void Defaults_AskFourQuestionsPerLevel_AndNeedThreeRight()
    {
        var settings = new AssessmentSettings();

        Assert.Equal(4, settings.EffectivePlacementQuestionsPerLevel);
        Assert.Equal(75m, settings.EffectivePlacementPassPercentage);
        Assert.Equal(4000, settings.EffectiveEssayAnswerMaxLength);
    }

    [Fact]
    public void MisconfiguredValues_AreClamped()
    {
        var settings = new AssessmentSettings
        {
            PlacementQuestionsPerLevel = 0,
            PlacementPassPercentage = 500,
            EssayAnswerMaxLength = -1
        };

        Assert.Equal(1, settings.EffectivePlacementQuestionsPerLevel);
        Assert.Equal(100m, settings.EffectivePlacementPassPercentage);
        Assert.Equal(100, settings.EffectiveEssayAnswerMaxLength);
    }

    [Fact]
    public void AbandonCutoff_IsStartPlusTheTimeout()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now.AddMinutes(-180), new AssessmentSettings().AbandonCutoff(now));
    }

    [Fact]
    public void PlacementIsAValidQuizType()
        => Assert.Contains(QuizTypes.Placement, QuizTypes.All);
}

public class PlacementModelTests
{
    private static AssessmentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;Trusted_Connection=True;")
            .Options;

        return new AssessmentDbContext(options);
    }

    [Fact]
    public void UserPlacement_IsOnePerLearnerAndOnePerAttempt()
    {
        using var db = CreateContext();

        var unique = db.Model.FindEntityType(typeof(UserPlacement))!
            .GetIndexes()
            .Where(i => i.IsUnique)
            .Select(i => i.GetDatabaseName())
            .ToList();

        Assert.Contains("UQ_UserPlacements_UserId", unique);
        Assert.Contains("UQ_UserPlacements_QuizAttemptId", unique);
    }

    [Fact]
    public void UserTopicStat_RowVersion_IsAConcurrencyToken()
    {
        using var db = CreateContext();

        var rowVersion = db.Model.FindEntityType(typeof(UserTopicStat))!
            .FindProperty(nameof(UserTopicStat.RowVersion))!;

        Assert.True(rowVersion.IsConcurrencyToken);
    }

    [Theory]
    [InlineData("UQ_Quizzes_OneActivePlacement", "Placement")]
    [InlineData("UQ_Quizzes_OneActiveLessonQuizPerLesson", "LessonQuiz")]
    public void Quiz_HasTheOneActivePerSlotIndexes(string indexName, string quizType)
    {
        using var db = CreateContext();

        var index = db.Model.FindEntityType(typeof(Quiz))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == indexName);

        Assert.True(index.IsUnique);
        Assert.Contains(quizType, index.GetFilter());
    }
}
