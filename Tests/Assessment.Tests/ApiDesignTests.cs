using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Services;
using AssessmentBL.Services.Constants;
using Shared.Common.Exceptions;
using Shared.Common.Text;

namespace Assessment.Tests;

/// <summary>
/// Enum-shaped text is accepted whatever its casing. It used to be strictly
/// case-sensitive everywhere except learningLevel, so "multiplechoice" was
/// rejected by one field and accepted by another — the kind of inconsistency a
/// client can only discover by hitting it.
/// </summary>
public class CanonicalValueTests
{
    [Theory]
    [InlineData("multiplechoice", QuestionTypes.MultipleChoice)]
    [InlineData("MULTIPLECHOICE", QuestionTypes.MultipleChoice)]
    [InlineData("  TrueFalse  ", QuestionTypes.TrueFalse)]
    [InlineData("essay", QuestionTypes.Essay)]
    public void QuestionTypes_AreCaseInsensitive_AndComeBackCanonical(string sent, string expected)
        => Assert.Equal(expected, QuestionTypes.Normalize(sent));

    [Theory]
    [InlineData("lessonquiz", QuizTypes.LessonQuiz)]
    [InlineData("PLACEMENT", QuizTypes.Placement)]
    [InlineData("levelSkip", QuizTypes.LevelSkip)]
    public void QuizTypes_AreCaseInsensitive_AndComeBackCanonical(string sent, string expected)
        => Assert.Equal(expected, QuizTypes.Normalize(sent));

    [Theory]
    [InlineData("medium", QuestionDifficulties.Medium)]
    [InlineData("ADVANCED", QuestionDifficulties.Advanced)]
    public void Difficulties_AreCaseInsensitive_AndComeBackCanonical(string sent, string expected)
        => Assert.Equal(expected, QuestionDifficulties.Normalize(sent));

    [Theory]
    [InlineData("beginner", TopicLearningLevels.Beginner)]
    [InlineData("INTERMEDIATE", TopicLearningLevels.Intermediate)]
    public void LearningLevels_KeepTheirFlexibility(string sent, string expected)
        => Assert.Equal(expected, TopicLearningLevels.Normalize(sent));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("NotAType")]
    public void SomethingThatIsNotAValue_IsStillRejected(string? sent)
    {
        Assert.Null(QuestionTypes.Normalize(sent));
        Assert.Null(QuizTypes.Normalize(sent));
        Assert.Null(QuestionDifficulties.Normalize(sent));
        Assert.Null(TopicLearningLevels.Normalize(sent));
    }

    [Fact]
    public void TheAllowedValues_AreListedForAnErrorMessage()
        => Assert.Equal("MultipleChoice | TrueFalse | Essay", CanonicalValues.Describe(QuestionTypes.All));

    [Fact]
    public void TheCanonicalSpelling_IsWhatTheCheckConstraintAllows()
    {
        // Normalizing must return the EXACT constant, not the caller's spelling:
        // it is what reaches the column CK_Questions_QuestionType guards.
        Assert.Same(QuestionTypes.MultipleChoice, QuestionTypes.Normalize("multiplechoice"));
        Assert.Same(QuizTypes.Placement, QuizTypes.Normalize("placement"));
    }
}

/// <summary>
/// An absolute reorder describes a STATE, so it must cover the whole set. Half a
/// list would half-apply an order and leave the admin's screen and the database
/// disagreeing — which is the very failure the pairwise swap had.
/// </summary>
public class DisplayOrderingTests
{
    private static void Check(params int[] ordered) =>
        DisplayOrdering.EnsureCoversExactly(ordered, [1, 2, 3], "الأسئلة", "الاختبار رقم 15");

    [Fact]
    public void AFullOrder_IsAccepted()
    {
        Check(3, 1, 2);
        Check(1, 2, 3);
    }

    [Fact]
    public void AMissingItem_IsRejected_NotSilentlyLeftWhereItWas()
    {
        var error = Assert.Throws<BusinessRuleException>(() => Check(1, 2));
        Assert.Contains("3", error.Message);
    }

    [Fact]
    public void ADuplicate_IsRejected()
        => Assert.Throws<ArgumentException>(() => Check(1, 2, 2));

    [Fact]
    public void AnItemFromSomewhereElse_IsRejected()
        => Assert.Throws<ArgumentException>(() => Check(1, 2, 3, 99));

    [Fact]
    public void AnEmptyOrder_IsRejected()
        => Assert.Throws<ArgumentException>(() => Check());
}

/// <summary>
/// hintsStatus has to distinguish "not written yet" from "the AI had nothing",
/// because a submission now returns before the hints exist.
/// </summary>
public class HintStatusTests
{
    [Fact]
    public void NothingWrong_NeedsNoHints()
        => Assert.Equal(HintStatuses.NotRequired, HintStatuses.Resolve(0, 0, hintingSettled: false));

    [Fact]
    public void BeforeTheHintJobRuns_NoHintsMeansPending()
        => Assert.Equal(HintStatuses.Pending, HintStatuses.Resolve(2, 0, hintingSettled: false));

    [Fact]
    public void AfterTheHintJobRan_NoHintsMeansUnavailable()
        => Assert.Equal(HintStatuses.Unavailable, HintStatuses.Resolve(2, 0, hintingSettled: true));

    [Fact]
    public void SomeHints_ArePartial_WhicheverSideOfTheJob()
    {
        Assert.Equal(HintStatuses.Pending, HintStatuses.Resolve(3, 1, hintingSettled: false));
        Assert.Equal(HintStatuses.Partial, HintStatuses.Resolve(3, 1, hintingSettled: true));
    }

    [Fact]
    public void EveryQuestionHinted_IsGenerated_EvenBeforeTheJobIsConsideredSettled()
        => Assert.Equal(HintStatuses.Generated, HintStatuses.Resolve(2, 2, hintingSettled: false));
}

/// <summary>
/// The submit request talks about ANSWERS. It used to be called "mistakes",
/// which described what the backend stored rather than what the client sends,
/// and read as "list the ones you got wrong" — which is not what it meant.
/// </summary>
public class SubmitContractTests
{
    [Fact]
    public void TheRequest_AsksForAnswers_NotMistakes()
    {
        var dto = typeof(SubmitQuizAttemptDto);

        Assert.NotNull(dto.GetProperty(nameof(SubmitQuizAttemptDto.Answers)));
        Assert.NotNull(dto.GetProperty(nameof(SubmitQuizAttemptDto.EssayAnswers)));
        Assert.Null(dto.GetProperty("Mistakes"));
    }

    [Fact]
    public void TheResult_DoesNotCarryTheRetryQuestions()
    {
        // They depend on hints the AI writes after this result is committed, so
        // they have their own endpoint; keeping them here is what made a
        // submission wait on the AI.
        Assert.Null(typeof(QuizAttemptResultDto).GetProperty("RetryQuestions"));
        Assert.NotNull(typeof(RetryQuestionsDto).GetProperty(nameof(RetryQuestionsDto.Questions)));
    }

    [Fact]
    public void AStartedAttempt_CanSayItWasResumedRatherThanCreated()
        => Assert.NotNull(typeof(QuizAttemptResponseDto).GetProperty(nameof(QuizAttemptResponseDto.Resumed)));

    [Fact]
    public void ARetry_CanSayWhichQuestionsWereRemoved()
    {
        var dto = typeof(QuizAttemptResponseDto);

        Assert.NotNull(dto.GetProperty(nameof(QuizAttemptResponseDto.RemovedQuestionIds)));
        Assert.NotNull(dto.GetProperty(nameof(QuizAttemptResponseDto.Notice)));
    }
}
