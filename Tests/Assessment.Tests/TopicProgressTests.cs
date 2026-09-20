using AssessmentBL;
using AssessmentBL.Services;
using AssessmentBL.Services.Constants;
using Xunit;

namespace Assessment.Tests;

/// <summary>
/// Pins how the progress screen reads a child's topic: accuracy, mastery, stars
/// and the messages that go with them.
/// </summary>
public class TopicProgressTests
{
    private const int MasteryPercentage = 80;
    private const int MinQuestions = 5;

    private static string Mastery(int answered, int correct, bool practised = false) =>
        TopicProgress.Mastery(answered, correct, practised, MasteryPercentage, MinQuestions);

    [Fact]
    public void NothingDone_IsNotStarted_WithNoStars()
    {
        Assert.Equal(TopicMasteryLevels.NotStarted, Mastery(0, 0));
        Assert.Equal(0, TopicProgress.Stars(TopicMasteryLevels.NotStarted));
    }

    [Fact]
    public void AGradedEssayOrAHint_StartsATopic_EvenWithNoCountedAnswer()
        => Assert.Equal(TopicMasteryLevels.Learning, Mastery(0, 0, practised: true));

    [Theory]
    [InlineData(4, 1)]
    [InlineData(10, 4)]
    public void LessThanHalfCorrect_IsLearning(int answered, int correct)
        => Assert.Equal(TopicMasteryLevels.Learning, Mastery(answered, correct));

    [Theory]
    [InlineData(4, 2)]    // exactly half
    [InlineData(10, 7)]   // 70%: below mastery
    public void HalfOrMoreCorrect_IsPracticing(int answered, int correct)
        => Assert.Equal(TopicMasteryLevels.Practicing, Mastery(answered, correct));

    [Theory]
    [InlineData(5, 4)]    // 80% over the minimum
    [InlineData(8, 8)]
    public void EnoughCorrectAnswers_OverEnoughQuestions_IsMastered(int answered, int correct)
        => Assert.Equal(TopicMasteryLevels.Mastered, Mastery(answered, correct));

    [Fact]
    public void OneLuckyAnswer_IsNotMastery()
        => Assert.Equal(TopicMasteryLevels.Practicing, Mastery(1, 1));

    [Fact]
    public void Mastery_ComparesExactly_NotTheRoundedPercentage()
    {
        // 159 of 200 is 79.5%, which rounds to 80 but is below it.
        Assert.Equal(80, TopicProgress.AccuracyPercentage(200, 159));
        Assert.Equal(TopicMasteryLevels.Practicing, Mastery(200, 159));
    }

    [Theory]
    [InlineData(TopicMasteryLevels.Learning, 1)]
    [InlineData(TopicMasteryLevels.Practicing, 2)]
    [InlineData(TopicMasteryLevels.Mastered, 3)]
    public void EveryStartedTopic_HasAtLeastOneStar(string mastery, byte stars)
        => Assert.Equal(stars, TopicProgress.Stars(mastery));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 2, 67)]
    [InlineData(8, 1, 13)]   // 12.5 rounds away from zero
    public void Accuracy_IsAWholePercentage(int answered, int correct, int expected)
        => Assert.Equal(expected, TopicProgress.AccuracyPercentage(answered, correct));

    [Fact]
    public void Practicing_WithNothingWrong_AsksForMoreAnswers_NotForReview()
    {
        var perfect = TopicProgress.TopicMessage(TopicMasteryLevels.Practicing, 2, 2, ContentLanguages.English);
        var withMistakes = TopicProgress.TopicMessage(TopicMasteryLevels.Practicing, 10, 7, ContentLanguages.English);

        Assert.DoesNotContain("missed", perfect);
        Assert.Contains("missed", withMistakes);
    }

    [Theory]
    [InlineData(TopicMasteryLevels.NotStarted)]
    [InlineData(TopicMasteryLevels.Learning)]
    [InlineData(TopicMasteryLevels.Practicing)]
    [InlineData(TopicMasteryLevels.Mastered)]
    public void EveryMastery_HasAMessage_InBothLanguages(string mastery)
    {
        var ar = TopicProgress.TopicMessage(mastery, 5, 3, ContentLanguages.Arabic);
        var en = TopicProgress.TopicMessage(mastery, 5, 3, ContentLanguages.English);

        Assert.False(string.IsNullOrWhiteSpace(ar));
        Assert.False(string.IsNullOrWhiteSpace(en));
        Assert.NotEqual(ar, en);
    }

    [Fact]
    public void Headline_BeforeAnything_InvitesTheFirstQuiz()
        => Assert.Contains("first quiz", TopicProgress.Headline(0, 0, 6, started: false, ContentLanguages.English));

    [Fact]
    public void Headline_CelebratesXpAndMasteredTopics()
    {
        var headline = TopicProgress.Headline(23, 1, 6, started: true, ContentLanguages.English);

        Assert.Contains("23 XP", headline);
        Assert.Contains("1 of 6", headline);
    }

    [Fact]
    public void Headline_WithNoXpYet_EncouragesAnotherTry()
        => Assert.Contains("try again", TopicProgress.Headline(0, 0, 6, started: true, ContentLanguages.English));

    [Fact]
    public void MasterySettings_HaveSafeDefaultsAndBounds()
    {
        var defaults = new AssessmentSettings();

        Assert.Equal(80, defaults.EffectiveTopicMasteryPercentage);
        Assert.Equal(5, defaults.EffectiveTopicMasteryMinQuestions);
        Assert.Equal(50, new AssessmentSettings { TopicMasteryPercentage = 10 }.EffectiveTopicMasteryPercentage);
        Assert.Equal(1, new AssessmentSettings { TopicMasteryMinQuestions = 0 }.EffectiveTopicMasteryMinQuestions);
    }
}
