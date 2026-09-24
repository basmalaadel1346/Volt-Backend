using AssessmentBL;
using AssessmentBL.Services;
using AssessmentBL.Services.Constants;

namespace Assessment.Tests;

/// <summary>
/// The fallback that closes an essay the AI never graded. It only ever runs after
/// the AI has definitively failed, so the bar it has to clear is "better than
/// NotGraded and zero points", not "as good as the AI".
/// </summary>
public class EssayKeywordGradingTests
{
    private const int FullCredit = 80;

    private static KeywordGrade? Grade(string? answer, string? keywords, byte maxPoints = 4) =>
        EssayKeywordGrading.Grade(answer, keywords, maxPoints, FullCredit, ContentLanguages.Arabic);

    [Fact]
    public void WithoutKeywords_ThereIsNothingToGradeAgainst()
    {
        // Null rather than zero: "we cannot judge this" and "this was wrong" are
        // different answers, and the caller closes the essay as NotGraded instead.
        Assert.Null(Grade("أي إجابة", null));
        Assert.Null(Grade("أي إجابة", "   "));
    }

    [Fact]
    public void MentioningEnoughKeywords_EarnsEveryPoint()
    {
        var grade = Grade("الدائرة لازم تكون مقفولة وفيها مصدر للجهد وسلك موصل", "مقفولة, مصدر, سلك, جهد");

        Assert.NotNull(grade);
        Assert.Equal((byte)4, grade!.AwardedPoints);
        Assert.Equal(4, grade.MatchedKeywords);
        Assert.Equal(4, grade.TotalKeywords);
    }

    [Fact]
    public void MentioningSome_EarnsPointsInProportion()
    {
        // Two of four is 50%: below the 80% full-credit bar, so 4 × 0.5 = 2.
        var grade = Grade("الدائرة لازم تكون مقفولة وفيها سلك", "مقفولة, مصدر, سلك, جهد");

        Assert.Equal((byte)2, grade!.AwardedPoints);
        Assert.Equal(2, grade.MatchedKeywords);
    }

    [Fact]
    public void MentioningSomethingRight_IsNeverWorthZero()
    {
        // One of four is 25%; 4 × 0.25 = 1, and even a coarser split floors at 1.
        var grade = Grade("فيها سلك", "مقفولة, مصدر, سلك, جهد, بطارية, مقاومة, مفتاح, لمبة");

        Assert.Equal(1, grade!.MatchedKeywords);
        Assert.Equal((byte)1, grade.AwardedPoints);
    }

    [Fact]
    public void MentioningNothing_EarnsNothing()
    {
        var grade = Grade("مش عارف", "مقفولة, مصدر, سلك");

        Assert.Equal(0, grade!.MatchedKeywords);
        Assert.Equal((byte)0, grade.AwardedPoints);
    }

    [Fact]
    public void ArabicSpellingVariants_StillMatch()
    {
        // A child typing quickly writes "الاوم" for "الأوم" and drops the diacritics;
        // refusing those would make the fallback punish handwriting, not knowledge.
        Assert.Equal(1, Grade("الوحدة هي الاوم", "الأوم")!.MatchedKeywords);
        Assert.Equal(1, Grade("الوحدة هي الأُوم", "الاوم")!.MatchedKeywords);
        Assert.Equal(1, Grade("طاقه كهربائيه", "طاقة")!.MatchedKeywords);
    }

    [Fact]
    public void EnglishKeywords_MatchWhateverTheCasing()
        => Assert.Equal(2, Grade("A closed CIRCUIT needs a Battery", "circuit, battery, switch")!.MatchedKeywords);

    [Fact]
    public void EveryGrade_ComesWithAReadableSentence()
    {
        Assert.NotEmpty(Grade("مقفولة ومصدر وسلك", "مقفولة, مصدر, سلك")!.Feedback);
        Assert.NotEmpty(Grade("مقفولة", "مقفولة, مصدر, سلك")!.Feedback);
        Assert.NotEmpty(Grade("لا شيء", "مقفولة, مصدر, سلك")!.Feedback);
    }

    [Fact]
    public void AGrade_NeverExceedsTheQuestionsMaximum()
    {
        // CK_QuizAttemptEssayAnswers_AwardedWithinMax would reject it otherwise.
        for (byte max = 1; max <= 10; max++)
        {
            var grade = Grade("مقفولة ومصدر وسلك وجهد", "مقفولة, مصدر, سلك, جهد", max);
            Assert.True(grade!.AwardedPoints <= max);
        }
    }

    [Theory]
    [InlineData("a, b, c", 3)]
    [InlineData("a،b؛c", 3)]
    [InlineData("a\nb\nc", 3)]
    [InlineData("a, A, a", 1)]
    [InlineData("  a  ,  , b ", 2)]
    public void KeywordsSplitOnEverySeparatorAnAdminMightUse(string keywords, int expected)
        => Assert.Equal(expected, EssayKeywordGrading.ParseKeywords(keywords).Count);

    [Fact]
    public void TheDeadlineAndFullCreditBar_HaveSafeDefaultsAndBounds()
    {
        var defaults = new AssessmentSettings();

        Assert.Equal(TimeSpan.FromMinutes(60), defaults.EssayGradingDeadline);
        Assert.Equal(80, defaults.EffectiveEssayKeywordFullCreditPercentage);

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            new AssessmentSettings { EssayGradingDeadlineMinutes = 0 }.EssayGradingDeadline);
        Assert.Equal(
            100,
            new AssessmentSettings { EssayKeywordFullCreditPercentage = 500 }.EffectiveEssayKeywordFullCreditPercentage);
    }
}
