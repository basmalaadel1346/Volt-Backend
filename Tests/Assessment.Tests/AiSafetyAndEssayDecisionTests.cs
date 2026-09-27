using System.Reflection;
using AssessmentBL;
using AssessmentBL.Services;
using Shared.Assessment.AI;
using Xunit;

namespace Assessment.Tests;

/// <summary>A hint must never name the correct answer.</summary>
public class HintSafetyTests
{
    [Theory]
    [InlineData("الإجابة هي الأوم", "الأوم")]
    [InlineData("بالأوم نقيس المقاومة", "الأوم")]      // fused prefix ب
    [InlineData("فكّر في وحدة الأوم", "أوم")]          // article ال
    [InlineData("الإجابةُ هي الأُوم", "الأوم")]         // diacritics
    [InlineData("الإجابة صح", "صح")]
    [InlineData("It is the Ohm, of course", "ohm")]  // case
    [InlineData("the answer is 20 volts", "20")]
    [InlineData("وحدة أوم", "الأوم")]                  // article dropped on the hint side
    [InlineData("نقيس وبالأوم المقاومة", "الأوم")]      // two fused prefixes
    [InlineData("للأوم علاقة بالمقاومة", "الأوم")]      // ل + ال contracted
    [InlineData("الناتج ٢٠ فولت", "20")]               // Arabic-Indic digits
    public void NamingTheAnswer_IsCaught(string hint, string correctOptionText)
        => Assert.True(HintSafety.RevealsAnswer(hint, correctOptionText));

    [Theory]
    [InlineData("الإجابة خطأ", "خطأ")]
    [InlineData("الإجابة الصحيحة هي صح", "صح")]
    [InlineData("العبارة خطأ لأن المصباح يحتاج بطارية", "خطأ")]
    [InlineData("The answer is true", "true")]
    public void TrueFalse_AnExplicitVerdict_IsCaught(string hint, string correctOptionText)
        => Assert.True(HintSafety.RevealsAnswer(hint, correctOptionText, isBinaryChoice: true));

    [Theory]
    [InlineData("الإجابة ليست صح", "خطأ", "صح")]
    [InlineData("the answer is not true", "false", "true")]
    [InlineData("The answer isn't true", "false", "true")]
    public void TrueFalse_NegatingTheOtherOption_IsCaught(string hint, string correct, string other)
        => Assert.True(HintSafety.RevealsAnswer(hint, correct, isBinaryChoice: true, otherOptionText: other));

    [Theory]
    [InlineData("خطأ شائع إن المصباح يضيء لوحده — فكّر في مصدر الكهرباء", "خطأ")]
    [InlineData("Think about whether a lamp can glow with nothing true to power it", "true")]
    public void TrueFalse_TheWordAlone_IsNotAVerdict(string hint, string correctOptionText)
        => Assert.False(HintSafety.RevealsAnswer(hint, correctOptionText, isBinaryChoice: true));

    [Theory]
    [InlineData("افتكر إن الوحدة اسمها على اسم عالم ألماني", "الأوم")]
    [InlineData("هل هذا صحيح؟ فكّر في مصدر الكهرباء", "صح")]   // "صحيح" is not "صح"
    [InlineData("Ohmic behaviour depends on temperature", "ohm")]
    [InlineData("the answer is A", "A")]                         // one character: unchecked
    [InlineData("any hint", null)]
    [InlineData("any hint", "   ")]
    public void AHintThatDoesNotNameTheAnswer_IsAllowed(string hint, string? correctOptionText)
        => Assert.False(HintSafety.RevealsAnswer(hint, correctOptionText));
}

/// <summary>
/// Essays are graded by the AI only: a well-formed grade is final, Skipped is
/// final with no grade, and anything unusable is retried. Nothing waits for a person.
/// </summary>
public class EssayDecisionTests
{
    private const int MaxFeedback = 1000;

    private static EssayDecision Decide(
        int? points = 2, string? feedback = "أحسنت، ولكن اذكر دور المقاومة.", decimal? confidence = 0.9m,
        string? status = "Ok", int maxPoints = 3) =>
        EssayEvaluationService.Decide(
            new EssayEvaluationResult
            {
                ItemId = "1", Status = status, Points = points,
                Feedback = feedback, Confidence = confidence
            },
            maxPoints, MaxFeedback);

    [Fact]
    public void AWellFormedGrade_IsAccepted()
    {
        var decision = Decide(feedback: "  أحسنت  ");

        Assert.Equal(EssayDecisionKind.Accept, decision.Kind);
        Assert.Equal(2, decision.Points);
        Assert.Equal("أحسنت", decision.Feedback);
        Assert.Equal(0.9m, decision.Confidence);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.79)]
    public void ALowConfidence_NeverBlocksTheGrade(double confidence)
        => Assert.Equal(EssayDecisionKind.Accept, Decide(confidence: (decimal)confidence).Kind);

    [Fact]
    public void ConfidenceIsOptional()
    {
        var decision = Decide(confidence: null);

        Assert.Equal(EssayDecisionKind.Accept, decision.Kind);
        Assert.Null(decision.Confidence);
    }

    [Theory]
    [InlineData(0)]    // no points is still a grade
    [InlineData(3)]    // exactly MaxPoints
    public void TheBoundsOfTheScore_AreValidGrades(int points)
        => Assert.Equal(EssayDecisionKind.Accept, Decide(points: points).Kind);

    [Theory]
    [InlineData("Skipped")]
    [InlineData("skipped")]
    [InlineData("SKIPPED")]
    public void TheAiDeclining_IsFinal_WithNoGrade(string status)
    {
        var decision = Decide(status: status);

        Assert.Equal(EssayDecisionKind.Declined, decision.Kind);
        Assert.Null(decision.Points);
        Assert.Null(decision.Feedback);
    }

    [Fact]
    public void AMissingStatus_IsReadAsOk()
        => Assert.Equal(EssayDecisionKind.Accept, Decide(status: null).Kind);

    [Theory]
    [InlineData("Error")]
    [InlineData("NeedsReview")]
    [InlineData("")]
    public void AnUnknownStatus_IsRetried(string status)
        => Assert.Equal(EssayDecisionKind.Unusable, Decide(status: status).Kind);

    [Theory]
    [InlineData(4)]    // above MaxPoints
    [InlineData(-1)]
    public void AScoreOutOfRange_IsUnusable(int points)
        => Assert.Equal(EssayDecisionKind.Unusable, Decide(points: points).Kind);

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    public void AConfidenceOutsideZeroToOne_IsUnusable(double confidence)
        => Assert.Equal(EssayDecisionKind.Unusable, Decide(confidence: (decimal)confidence).Kind);

    [Fact]
    public void FeedbackUpToTheLimit_IsAccepted()
        => Assert.Equal(EssayDecisionKind.Accept, Decide(feedback: new string('x', MaxFeedback)).Kind);

    [Fact]
    public void MissingPiecesOrAMissingResult_AreUnusable()
    {
        Assert.Equal(EssayDecisionKind.Unusable, Decide(points: null).Kind);
        Assert.Equal(EssayDecisionKind.Unusable, Decide(feedback: null).Kind);
        Assert.Equal(EssayDecisionKind.Unusable, Decide(feedback: "  ").Kind);
        Assert.Equal(EssayDecisionKind.Unusable, Decide(feedback: new string('x', MaxFeedback + 1)).Kind);
        Assert.Equal(EssayDecisionKind.Unusable, EssayEvaluationService.Decide(null, 3, MaxFeedback).Kind);
    }

    [Fact]
    public void ThereIsNoReviewOutcome()
        => Assert.Equal(
            new[] { nameof(EssayDecisionKind.Accept), nameof(EssayDecisionKind.Declined), nameof(EssayDecisionKind.Unusable) },
            Enum.GetNames<EssayDecisionKind>());
}

public class SubmissionCompletenessTests
{
    private static void EnsureEveryQuestionAnswered(IEnumerable<int> all, IReadOnlySet<int> answered) =>
        typeof(QuizAttemptService)
            .GetMethod("EnsureEveryQuestionAnswered", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [all, answered]);

    [Fact]
    public void EveryQuestionAnswered_Passes()
        => EnsureEveryQuestionAnswered([101, 102, 104], new HashSet<int> { 101, 102, 104 });

    [Fact]
    public void AnyUnansweredQuestion_IsRejected_ListingThemAll()
    {
        // 102 is an MCQ, 104 an essay: both must be answered.
        var ex = Assert.Throws<TargetInvocationException>(() =>
            EnsureEveryQuestionAnswered([101, 102, 104], new HashSet<int> { 101 }));

        var argument = Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains("102, 104", argument.Message);
    }
}

public class AiSettingsDefaultsTests
{
    [Fact]
    public void Defaults_AreConservative()
    {
        var settings = new AssessmentSettings();

        Assert.False(settings.AiSendImageContent);          // no image bytes unless a vision model is set up
        Assert.Equal(400, settings.EffectiveMaxHintLength);
        Assert.Equal(1000, settings.EffectiveMaxEssayFeedbackLength);
        Assert.Equal(5, settings.EffectiveEssayEvaluationMaxAttempts);
    }

    [Fact]
    public void ThereIsNoConfidenceGateToConfigure()
        => Assert.DoesNotContain(typeof(AssessmentSettings).GetProperties(), p => p.Name.Contains("Confidence"));

    /// <summary>
    /// The max-attempts pre-close may only close a claim no live run still holds.
    /// Inline, a run is bounded by AiHintTimeout. In the background, a request can
    /// start just before the batch deadline and then take a full
    /// EssayEvaluationTimeout of its own. The lifetime must outlast both, including
    /// at the clamp limits.
    /// </summary>
    [Theory]
    [InlineData(15, 30)]      // defaults
    [InlineData(60, 5)]       // long inline budget, short request deadline
    [InlineData(1, 120)]      // short inline budget, long request deadline
    [InlineData(0, 999)]      // out of range: clamped to 1 s and 120 s
    [InlineData(999, 0)]      // out of range: clamped to 60 s and 5 s
    public void AClaim_OutlivesEveryRunThatCanStillHoldIt(int hintSeconds, int evaluationSeconds)
    {
        var settings = new AssessmentSettings
        {
            AiHintTimeoutSeconds = hintSeconds,
            EssayEvaluationTimeoutSeconds = evaluationSeconds
        };

        Assert.True(settings.EssayClaimLifetime > settings.AiHintTimeout);
        Assert.True(settings.EssayClaimLifetime > 2 * settings.EssayEvaluationTimeout);
    }

    [Fact]
    public void AClaim_WithDefaultSettings_IsStaleAfterTwoMinutes()
        => Assert.Equal(TimeSpan.FromMinutes(2), new AssessmentSettings().EssayClaimLifetime);
}
