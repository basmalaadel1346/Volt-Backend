using AssessmentBL;
using AssessmentBL.Services;
using AssessmentBL.Services.Constants;
using AssessmentDA.Context;
using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Assessment.Tests;

/// <summary>
/// The second guard on a hint: not quoting the answer is not enough, it must not
/// come within a hair of it either.
/// </summary>
public class HintSimilarityTests
{
    private const decimal Threshold = 0.80m;

    [Theory]
    [InlineData("الوحدة هي الاووم", "الأوم")]          // one letter added
    [InlineData("the unit is the ohmm", "ohm")]        // misspelled on purpose
    [InlineData("فكّر في قانون أوم البسيط", "أوم")]     // exact word, different sentence
    public void AHintThatAllButSpellsTheAnswer_IsCaught(string hint, string correctOptionText)
        => Assert.True(HintSafety.IsTooSimilar(hint, correctOptionText, Threshold));

    [Theory]
    [InlineData("افتكر إن الوحدة اسمها على اسم عالم ألماني", "الأوم")]
    [InlineData("think about which scientist the unit is named after", "ohm")]
    [InlineData("قارن بين الجهد والتيار", "المقاومة الكهربية")]
    public void AHintThatOnlyTalksAround_TheAnswerIsAllowed(string hint, string correctOptionText)
        => Assert.False(HintSafety.IsTooSimilar(hint, correctOptionText, Threshold));

    [Fact]
    public void AMultiWordAnswer_IsCaughtEvenWhenSlightlyReworded()
        => Assert.True(HintSafety.IsTooSimilar(
            "الإجابة هي المقاومه الكهربيه يا بطل", "المقاومة الكهربية", Threshold));

    [Fact]
    public void AThresholdAboveOne_TurnsTheCheckOff()
        => Assert.False(HintSafety.IsTooSimilar("الوحدة هي الأوم", "الأوم", 1.01m));

    [Fact]
    public void AtAThresholdOfOne_OnlyWhatReadsAsTheAnswerItselfCounts()
    {
        Assert.True(HintSafety.IsTooSimilar("الوحدة هي الأوم", "الأوم", 1m));
        Assert.False(HintSafety.IsTooSimilar("افتكر في عالم ألماني", "الأوم", 1m));
    }

    [Theory]
    [InlineData("", "الأوم")]
    [InlineData("أي تلميح", null)]
    public void NothingToCompare_IsNotALeak(string hint, string? correctOptionText)
        => Assert.False(HintSafety.IsTooSimilar(hint, correctOptionText, Threshold));
}

public class HintEscalationSettingsTests
{
    [Fact]
    public void TwoLevels_AndAStrictSimilarityThreshold_AreTheDefaults()
    {
        var settings = new AssessmentSettings();

        Assert.Equal(2, settings.EffectiveMaxHintLevels);
        Assert.Equal(0.80m, settings.EffectiveHintSimilarityThreshold);
    }

    [Fact]
    public void TheSimilarityThreshold_CannotBeConfiguredBelowAHalf()
        => Assert.Equal(0.5m, new AssessmentSettings { HintSimilarityThreshold = 0.1m }.EffectiveHintSimilarityThreshold);
}

/// <summary>
/// Only a hint the child is shown uses a level: a Partial or Unavailable press is
/// not counted against them.
/// </summary>
public class HintsRemainingTests
{
    [Theory]
    [InlineData(1, 1)]   // first level shown: one left
    [InlineData(2, 0)]   // second level shown: the button can be disabled
    public void AGeneratedHint_UsesItsLevel(byte attemptNumber, int remaining)
        => Assert.Equal(remaining, HintService.HintsRemaining(2, attemptNumber, HintStatuses.Generated));

    [Theory]
    [InlineData(HintStatuses.Partial)]
    [InlineData(HintStatuses.Unavailable)]
    public void APressWithNoHintShown_IsNotCounted(string status)
    {
        // Nothing was used yet, and nothing is used now.
        Assert.Equal(2, HintService.HintsRemaining(2, 1, status));

        // One level already shown; this press for level 2 leaves it available.
        Assert.Equal(1, HintService.HintsRemaining(2, 2, status));
    }

    [Fact]
    public void HintsRemaining_NeverGoesBelowZero()
        => Assert.Equal(0, HintService.HintsRemaining(1, 3, HintStatuses.Generated));
}

public class HintModelTests
{
    private static AssessmentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AssessmentDbContext>()
            .UseSqlServer("Server=none;Database=VoltDB;Trusted_Connection=True;")
            .Options;

        return new AssessmentDbContext(options);
    }

    [Fact]
    public void AHint_BelongsToAnAttemptAndQuestion_NotOnlyToAMistake()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(QuestionHint))!;

        // The Hint button writes hints while the attempt is still in progress,
        // before any mistake row exists.
        Assert.False(entity.FindProperty(nameof(QuestionHint.QuizAttemptId))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(QuestionHint.QuestionId))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(QuestionHint.QuizAttemptMistakeId))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(QuestionHint.AttemptNumber))!.IsNullable);
    }

    [Fact]
    public void HintsAreUnique_PerAttemptQuestionLanguageAndSequence()
    {
        using var db = CreateContext();

        var index = db.Model.FindEntityType(typeof(QuestionHint))!
            .GetIndexes()
            .Single(i => i.Name == "UQ_QuestionHints_AttemptId_QuestionId_Language_Sequence");

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[]
            {
                nameof(QuestionHint.QuizAttemptId),
                nameof(QuestionHint.QuestionId),
                nameof(QuestionHint.LanguageCode),
                nameof(QuestionHint.HintSequence)
            },
            index.Properties.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void EachHintLevel_IsSavedOncePerAttemptAndQuestion_InAnyLanguage()
    {
        using var db = CreateContext();

        var index = db.Model.FindEntityType(typeof(QuestionHint))!
            .GetIndexes()
            .Single(i => i.Name == "UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber");

        // No LanguageCode and no HintSequence: two presses at the same moment, in
        // one language or in two, cannot both keep level 1.
        Assert.True(index.IsUnique);
        Assert.Equal(
            new[]
            {
                nameof(QuestionHint.QuizAttemptId),
                nameof(QuestionHint.QuestionId),
                nameof(QuestionHint.AttemptNumber)
            },
            index.Properties.Select(p => p.Name).ToArray());

        // Post-submission hints have no level and are not limited by it.
        Assert.Equal("[AttemptNumber] IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void OnlyOneCascadePathReachesAHint()
    {
        using var db = CreateContext();

        var foreignKeys = db.Model.FindEntityType(typeof(QuestionHint))!.GetForeignKeys().ToList();

        // Deleting an attempt cascades through its questions to their hints; the
        // mistake link must not cascade too, or SQL Server rejects the schema.
        Assert.Equal(DeleteBehavior.Cascade,
            foreignKeys.Single(fk => fk.GetConstraintName() == "FK_QuestionHints_QuizAttemptQuestions").DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict,
            foreignKeys.Single(fk => fk.GetConstraintName() == "FK_QuestionHints_QuizAttemptMistakes").DeleteBehavior);
    }
}
