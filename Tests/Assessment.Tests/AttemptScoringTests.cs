using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Services;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Assessment.Tests;

/// <summary>
/// Pins how an attempt's points are counted: every question weighs its Points
/// frozen at attempt start, the score covers MultipleChoice/TrueFalse only, and
/// the totals add the essays the AI has graded or not yet graded.
/// </summary>
public class AttemptScoringTests
{
    private static ScoredQuestion Mcq(int id, byte points) => new(id, QuestionTypes.MultipleChoice, points);

    private static ScoredQuestion TrueFalse(int id, byte points) => new(id, QuestionTypes.TrueFalse, points);

    private static ScoredQuestion Essay(int id, byte points) => new(id, QuestionTypes.Essay, points);

    private static EssayResultDto EssayResult(int id, string status, byte maxPoints, byte? awarded = null) =>
        new() { QuestionId = id, Status = status, MaxPoints = maxPoints, AwardedPoints = awarded };

    // 101 (1) right, 102 (2) wrong, 103 (1) right, essay 104 (3).
    private static readonly ScoredQuestion[] Attempt =
        [Mcq(101, 1), Mcq(102, 2), TrueFalse(103, 1), Essay(104, 3)];

    private static readonly HashSet<int> WrongOn102 = [102];

    [Fact]
    public void Score_WeighsEachQuestionByItsPoints()
        // 2 of 4 auto-graded points; by count it would have been 2 of 3 = 66.67.
        => Assert.Equal(50.00m, AttemptScoring.ScorePercentage(Attempt, WrongOn102));

    [Fact]
    public void Score_WhenEveryQuestionIsWorthOne_IsCorrectOverAsked()
        => Assert.Equal(66.67m, AttemptScoring.ScorePercentage(
            [Mcq(1, 1), Mcq(2, 1), TrueFalse(3, 1)], new HashSet<int> { 2 }));

    [Fact]
    public void Score_NeverIncludesEssays_WhateverTheirPoints()
    {
        var withHeavyEssay = Attempt.Append(Essay(105, 255)).ToArray();

        Assert.Equal(
            AttemptScoring.ScorePercentage(Attempt, WrongOn102),
            AttemptScoring.ScorePercentage(withHeavyEssay, WrongOn102));
    }

    [Fact]
    public void Score_IsZero_WhenNothingIsAutoGraded()
        => Assert.Equal(0m, AttemptScoring.ScorePercentage([Essay(1, 5)], new HashSet<int>()));

    [Fact]
    public void Score_RoundsMidpointsAwayFromZero()
    {
        // 1 of 800 points = 0.125% → 0.13 (banker's rounding would give 0.12).
        ScoredQuestion[] questions = [Mcq(1, 1), Mcq(2, 255), Mcq(3, 255), Mcq(4, 255), Mcq(5, 34)];

        Assert.Equal(0.13m, AttemptScoring.ScorePercentage(questions, new HashSet<int> { 2, 3, 4, 5 }));
    }

    [Fact]
    public void Points_CountEssays_GradedEarnAndPendingWait()
    {
        var points = AttemptScoring.CountPoints(
            Attempt, WrongOn102, [EssayResult(104, EssayAnswerStatuses.Graded, 3, awarded: 2)]);

        Assert.Equal(new AttemptPoints(TotalPoints: 7, EarnedPoints: 4, PendingPoints: 0), points);
    }

    [Fact]
    public void Points_APendingEssay_IsPendingNotEarned()
    {
        var points = AttemptScoring.CountPoints(
            Attempt, WrongOn102, [EssayResult(104, EssayAnswerStatuses.Pending, 3)]);

        Assert.Equal(new AttemptPoints(TotalPoints: 7, EarnedPoints: 2, PendingPoints: 3), points);
    }

    [Fact]
    public void Points_ANotGradedEssay_EarnsNothingAndIsNoLongerPending()
    {
        var points = AttemptScoring.CountPoints(
            Attempt, WrongOn102, [EssayResult(104, EssayAnswerStatuses.NotGraded, 3)]);

        Assert.Equal(new AttemptPoints(TotalPoints: 7, EarnedPoints: 2, PendingPoints: 0), points);
    }

    [Fact]
    public void Points_AreSummedAsInt_SoTheyCannotOverflowAByteOrAShort()
    {
        var questions = Enumerable.Range(1, 300).Select(id => Mcq(id, 255)).ToList();

        var points = AttemptScoring.CountPoints(questions, new HashSet<int>(), []);

        Assert.Equal(76_500, points.TotalPoints);
        Assert.Equal(76_500, points.EarnedPoints);
        Assert.Equal(100m, AttemptScoring.ScorePercentage(questions, new HashSet<int>()));
    }
}

public class AttemptPointsModelTests
{
    private static AssessmentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;Trusted_Connection=True;")
            .Options;

        return new AssessmentDbContext(options);
    }

    [Fact]
    public void QuizAttemptQuestion_FreezesPoints_DefaultingToOne_AndAlwaysPositive()
    {
        using var db = CreateContext();

        // Check constraints live only in the design-time model; the runtime one drops them.
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(QuizAttemptQuestion))!;
        var points = entity.FindProperty(nameof(QuizAttemptQuestion.Points))!;

        Assert.False(points.IsNullable);
        Assert.Equal((byte)1, points.GetDefaultValue());
        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "CK_QuizAttemptQuestions_Points");
    }
}
