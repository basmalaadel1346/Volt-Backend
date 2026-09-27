using Shared.Assessment.AI;
using Xunit;

namespace Assessment.Tests;

public class FakeAiHintGeneratorTests
{
    private static GenerateHintsRequest Request(string language) => new()
    {
        RequestId = Guid.NewGuid(),
        Language = language,
        Items =
        [
            new HintRequestItem
            {
                QuestionId = 101,
                QuestionType = "MultipleChoice",
                Question = new AiQuestion { Text = "q" },
                Options =
                [
                    new AiOption { OptionId = 1, Text = "الأوم" },
                    new AiOption { OptionId = 2, Text = "الفولت" }
                ],
                StudentAnswer = new HintStudentAnswer { SelectedOptionId = 2 },
                Reference = new HintReference { CorrectOptionId = 1 }
            }
        ]
    };

    [Theory]
    [InlineData("ar", "تلميح للسؤال 101")]
    [InlineData("en", "Hint for question 101")]
    public async Task Succeeding_HonoursTheRequestedLanguage(string language, string expected)
    {
        var fake = FakeAiHintGenerator.Succeeding();

        var response = await fake.GenerateHintsAsync(Request(language));

        Assert.Equal(expected, Assert.Single(response.Results).Hint);
        Assert.Equal(language, Assert.Single(fake.Received).Language);
    }

    [Fact]
    public async Task RevealingTheAnswer_NamesTheCorrectOption()
    {
        var response = await FakeAiHintGenerator.RevealingTheAnswer().GenerateHintsAsync(Request("ar"));

        Assert.Contains("الأوم", Assert.Single(response.Results).Hint);
    }

    [Fact]
    public async Task Unavailable_ReproducesTheOutageException()
        => await Assert.ThrowsAsync<HttpRequestException>(
            () => FakeAiHintGenerator.Unavailable().GenerateHintsAsync(Request("ar")));

    [Fact]
    public async Task NotConfigured_ReportsItselfUnconfigured()
    {
        var fake = FakeAiHintGenerator.NotConfigured();

        Assert.False(fake.IsConfigured);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fake.GenerateHintsAsync(Request("ar")));
        Assert.Equal("AI hints endpoint is not configured", ex.Message);
    }

    [Fact]
    public async Task TimingOut_ReproducesTheTimeoutException()
        => await Assert.ThrowsAsync<TaskCanceledException>(
            () => FakeAiHintGenerator.TimingOut().GenerateHintsAsync(Request("ar")));

    [Fact]
    public async Task DegenerateResponses_AreDeliveredAsIs_ForTheServiceToReject()
    {
        Assert.Empty((await FakeAiHintGenerator.ReturningNothing().GenerateHintsAsync(Request("ar"))).Results);

        Assert.True(string.IsNullOrWhiteSpace(
            Assert.Single((await FakeAiHintGenerator.ReturningBlankHints().GenerateHintsAsync(Request("ar"))).Results).Hint));

        Assert.Equal(-999,
            Assert.Single((await FakeAiHintGenerator.ReturningUnrelatedQuestionIds().GenerateHintsAsync(Request("ar"))).Results).QuestionId);
    }
}
