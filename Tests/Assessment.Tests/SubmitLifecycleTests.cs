using AssessmentBL;
using AssessmentBL.DTOs.Quiz;
using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Assessment.Tests;

public class HintStatusesTests
{
    [Theory]
    [InlineData(0, 0, HintStatuses.NotRequired)]   // perfect score / essay-only
    [InlineData(3, 0, HintStatuses.Unavailable)]   // AI down, timed out, not configured
    [InlineData(3, 2, HintStatuses.Partial)]       // some hints dropped or skipped
    [InlineData(3, 3, HintStatuses.Generated)]
    public void Resolve_DescribesWhatTheChildWillSee(int retryQuestions, int hinted, string expected)
        => Assert.Equal(expected, HintStatuses.Resolve(retryQuestions, hinted));
}

public class AssessmentSettingsTests
{
    [Fact]
    public void Defaults_AreGenerousForTheAttemptAndShortForTheAi()
    {
        var settings = new AssessmentSettings();

        Assert.Equal(TimeSpan.FromMinutes(180), settings.InProgressAttemptTimeout);
        Assert.Equal(TimeSpan.FromMinutes(15), settings.AbandonedAttemptSweepInterval);
        Assert.Equal(TimeSpan.FromSeconds(15), settings.AiHintTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void AMisconfiguredValue_CanNeverAbandonLiveAttemptsOrDisableTheAiBound(int bad)
    {
        var settings = new AssessmentSettings
        {
            InProgressAttemptTimeoutMinutes = bad,
            AbandonedAttemptSweepIntervalMinutes = bad,
            AiHintTimeoutSeconds = bad
        };

        Assert.True(settings.InProgressAttemptTimeout >= TimeSpan.FromMinutes(30));
        Assert.True(settings.AbandonedAttemptSweepInterval >= TimeSpan.FromMinutes(1));
        Assert.InRange(settings.AiHintTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
    }
}

public class SubmitLifecycleModelTests
{
    private static AssessmentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;Trusted_Connection=True;")
            .Options;

        return new AssessmentDbContext(options);
    }

    [Fact]
    public void QuizAttempt_HasTheFilteredIndexTheSweepReliesOn()
    {
        using var db = CreateContext();

        var index = db.Model
            .FindEntityType(typeof(QuizAttempt))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == "IX_QuizAttempts_InProgress_StartedAt");

        Assert.Equal(new[] { nameof(QuizAttempt.StartedAt) }, index.Properties.Select(p => p.Name).ToArray());
        Assert.Contains("InProgress", index.GetFilter());
    }

    [Fact]
    public void RetryUniqueness_IsFiltered_SoFirstAttemptsAreNotUnique()
    {
        using var db = CreateContext();

        var index = db.Model
            .FindEntityType(typeof(QuizAttempt))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == "UQ_QuizAttempts_PreviousAttemptId");

        // Unfiltered, this unique index would treat every NULL as equal and allow
        // exactly one first attempt in the whole database.
        Assert.True(index.IsUnique);
        Assert.Contains("IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void ResultDto_CarriesWhatARecoveringClientNeeds()
    {
        Assert.NotNull(typeof(QuizAttemptResultDto).GetProperty(nameof(QuizAttemptResultDto.AttemptId)));
        Assert.NotNull(typeof(QuizAttemptResultDto).GetProperty(nameof(QuizAttemptResultDto.QuizId)));
        Assert.NotNull(typeof(QuizAttemptResultDto).GetProperty(nameof(QuizAttemptResultDto.CompletedAt)));
        Assert.NotNull(typeof(QuizAttemptResultDto).GetProperty(nameof(QuizAttemptResultDto.HintsStatus)));
    }

    [Theory]
    [InlineData(nameof(QuizAttemptResultDto.TotalPoints))]
    [InlineData(nameof(QuizAttemptResultDto.EarnedPoints))]
    [InlineData(nameof(QuizAttemptResultDto.PendingPoints))]
    public void ResultDto_PointTotalsAreInt_BecauseTinyintSumsOverflowAByte(string property)
        => Assert.Equal(typeof(int), typeof(QuizAttemptResultDto).GetProperty(property)!.PropertyType);

    [Fact]
    public void EssayResult_NeverNamesAGrader_TheAiIsTheOnlyOne()
        => Assert.Null(typeof(EssayResultDto).GetProperty("GradedBy"));

    [Theory]
    [InlineData(typeof(LessonQuizResponseDto))]
    [InlineData(typeof(QuizQuestionForAttemptDto))]
    [InlineData(typeof(QuizAnswerOptionDto))]
    public void ChildFacingDtos_NeverCarryTheAnswerKeyOrAdminMetadata(Type dto)
    {
        Assert.Null(dto.GetProperty("IsCorrect"));
        Assert.Null(dto.GetProperty("CorrectOptionId"));
        Assert.Null(dto.GetProperty("ImageDescription"));
        Assert.Null(dto.GetProperty("IsActive"));
    }
}
