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
/// What a decision does to the stored essay answer. Every state the service
/// writes must satisfy the table's CHECK constraints, mirrored in
/// <see cref="SatisfiesChecks"/>; no path leaves an essay waiting for a person.
/// </summary>
public class EssayGradingLifecycleTests
{
    private const int MaxAttempts = 5;
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>A Pending answer as the evaluator holds it right after claiming it.</summary>
    private static QuizAttemptEssayAnswer Claimed(byte attempts = 1) => new()
    {
        Id = 7,
        QuizAttemptId = 3,
        QuestionId = 104,
        AnswerText = "عشان التيار ما يبقاش كبير",
        Status = EssayAnswerStatuses.Pending,
        MaxPoints = 3,
        LanguageCode = "ar",
        AiEvaluationAttempts = attempts,
        AiLastAttemptAt = Now,
        AiClaimId = Guid.NewGuid()
    };

    /// <summary>The CHECK constraints of Assessment.QuizAttemptEssayAnswers, in C#.</summary>
    private static bool SatisfiesChecks(QuizAttemptEssayAnswer e) =>
        e.Status is EssayAnswerStatuses.Pending or EssayAnswerStatuses.Graded or EssayAnswerStatuses.NotGraded
        && (e.GradedBy is null || e.GradedBy == EssayGraders.Ai)
        && ((e.Status == EssayAnswerStatuses.Graded && e.AwardedPoints is not null && e.GradedAt is not null && e.GradedBy is not null)
            || (e.Status != EssayAnswerStatuses.Graded && e.AwardedPoints is null && e.GradedAt is null && e.GradedBy is null))
        && (e.AwardedPoints is null || e.AwardedPoints <= e.MaxPoints)
        && (e.AiOutcome is null or EssayAiOutcomes.Accepted or EssayAiOutcomes.Declined or EssayAiOutcomes.Failed)
        && ((e.Status == EssayAnswerStatuses.Pending && e.AiOutcome is null)
            || (e.Status == EssayAnswerStatuses.Graded && e.AiOutcome == EssayAiOutcomes.Accepted)
            || (e.Status == EssayAnswerStatuses.NotGraded && e.AiOutcome is EssayAiOutcomes.Declined or EssayAiOutcomes.Failed))
        && (e.AiConfidence is null || (e.AiConfidence >= 0m && e.AiConfidence <= 1m));

    [Fact]
    public void Accept_GradesTheEssay_AsTheAi()
    {
        var essay = Claimed();

        EssayEvaluationService.Apply(essay, EssayDecision.Accept(2, "أحسنت", 0.855m), Now, MaxAttempts);

        Assert.Equal(EssayAnswerStatuses.Graded, essay.Status);
        Assert.Equal((byte)2, essay.AwardedPoints);
        Assert.Equal("أحسنت", essay.Feedback);
        Assert.Equal(EssayGraders.Ai, essay.GradedBy);
        Assert.Equal(Now, essay.GradedAt);
        Assert.Equal(EssayAiOutcomes.Accepted, essay.AiOutcome);
        Assert.Equal(0.86m, essay.AiConfidence);     // 2dp, away from zero: fits DECIMAL(3,2)
        Assert.True(SatisfiesChecks(essay));
    }

    [Fact]
    public void Accept_WithoutAConfidence_StoresNone()
    {
        var essay = Claimed();

        EssayEvaluationService.Apply(essay, EssayDecision.Accept(0, "حاول مرة أخرى", null), Now, MaxAttempts);

        Assert.Equal(EssayAnswerStatuses.Graded, essay.Status);
        Assert.Equal((byte)0, essay.AwardedPoints);
        Assert.Null(essay.AiConfidence);
        Assert.True(SatisfiesChecks(essay));
    }

    [Fact]
    public void Declined_IsFinal_WithNoGrade()
    {
        var essay = Claimed();

        EssayEvaluationService.Apply(essay, EssayDecision.Declined, Now, MaxAttempts);

        Assert.Equal(EssayAnswerStatuses.NotGraded, essay.Status);
        Assert.Equal(EssayAiOutcomes.Declined, essay.AiOutcome);
        Assert.Null(essay.AwardedPoints);
        Assert.Null(essay.Feedback);
        Assert.Null(essay.GradedBy);
        Assert.Null(essay.GradedAt);
        Assert.True(SatisfiesChecks(essay));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(MaxAttempts - 1)]
    public void Unusable_BeforeTheLastAttempt_StaysPendingForARetry(byte attempts)
    {
        var essay = Claimed(attempts);

        EssayEvaluationService.Apply(essay, EssayDecision.Unusable, Now, MaxAttempts);

        Assert.Equal(EssayAnswerStatuses.Pending, essay.Status);
        Assert.Null(essay.AiOutcome);
        Assert.True(SatisfiesChecks(essay));
    }

    [Fact]
    public void Unusable_OnTheLastAttempt_ClosesTheEssayAsFailed()
    {
        var essay = Claimed(MaxAttempts);

        EssayEvaluationService.Apply(essay, EssayDecision.Unusable, Now, MaxAttempts);

        Assert.Equal(EssayAnswerStatuses.NotGraded, essay.Status);
        Assert.Equal(EssayAiOutcomes.Failed, essay.AiOutcome);
        Assert.Null(essay.AwardedPoints);
        Assert.True(SatisfiesChecks(essay));
    }

    [Fact]
    public void AnEssayIsNeverLeftWithAnOutcomeButStillPending()
    {
        foreach (var decision in new[] { EssayDecision.Accept(1, "f", null), EssayDecision.Declined, EssayDecision.Unusable })
            for (byte attempts = 1; attempts <= MaxAttempts; attempts++)
            {
                var essay = Claimed(attempts);
                EssayEvaluationService.Apply(essay, decision, Now, MaxAttempts);

                Assert.True(SatisfiesChecks(essay), $"{decision.Kind} at attempt {attempts}");
            }
    }
}

/// <summary>The EF model mirrors the schema: AI-only grading, no proposal columns.</summary>
public class EssayAnswerModelTests
{
    private static IEntityType EssayEntity()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;Trusted_Connection=True;")
            .Options;

        using var db = new AssessmentDbContext(options);

        // Check constraints are design-time metadata; the runtime model drops them.
        return db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(QuizAttemptEssayAnswer))!;
    }

    private static string CheckSql(IEntityType entity, string name) =>
        Assert.Single(entity.GetCheckConstraints(), c => c.Name == name).Sql;

    [Fact]
    public void TheAiProposalColumns_AreGone()
    {
        var entity = EssayEntity();

        Assert.Null(entity.FindProperty("AiProposedPoints"));
        Assert.Null(entity.FindProperty("AiFeedback"));
        Assert.NotNull(entity.FindProperty(nameof(QuizAttemptEssayAnswer.AiConfidence)));
        Assert.NotNull(entity.FindProperty(nameof(QuizAttemptEssayAnswer.AiOutcome)));
    }

    [Fact]
    public void TheCheckConstraints_AllowOnlyTheAiLifecycle()
    {
        var entity = EssayEntity();

        var status = CheckSql(entity, "CK_QuizAttemptEssayAnswers_Status");
        foreach (var value in new[] { EssayAnswerStatuses.Pending, EssayAnswerStatuses.Graded, EssayAnswerStatuses.NotGraded })
            Assert.Contains($"'{value}'", status);

        Assert.Equal("[GradedBy] IS NULL OR [GradedBy] = 'Ai'", CheckSql(entity, "CK_QuizAttemptEssayAnswers_GradedBy"));

        var outcome = CheckSql(entity, "CK_QuizAttemptEssayAnswers_AiOutcome");
        foreach (var value in new[] { EssayAiOutcomes.Accepted, EssayAiOutcomes.Declined, EssayAiOutcomes.Failed })
            Assert.Contains($"'{value}'", outcome);

        var matches = CheckSql(entity, "CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus");
        Assert.Contains("[Status] = 'Pending' AND [AiOutcome] IS NULL", matches);
        Assert.Contains("[Status] = 'Graded' AND [AiOutcome] = 'Accepted'", matches);
        Assert.Contains("[Status] = 'NotGraded' AND [AiOutcome] IN ('Declined', 'Failed')", matches);

        var everyCheck = string.Join(" ", entity.GetCheckConstraints().Select(c => c.Sql));
        foreach (var gone in new[] { "Human", "NeedsReview", "Skipped" })
            Assert.DoesNotContain(gone, everyCheck);
    }
}
