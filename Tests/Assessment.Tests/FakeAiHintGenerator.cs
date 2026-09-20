using Shared.Assessment.AI;

namespace Assessment.Tests;

/// <summary>
/// Test double for <see cref="IAiHintGenerator"/>. Implements the same contract
/// as the real provider, so the whole submit flow can be exercised — including
/// every AI failure mode — without a network call or an API key.
/// </summary>
public sealed class FakeAiHintGenerator : IAiHintGenerator
{
    private readonly Func<GenerateHintsRequest, GenerateHintsResponse> _behaviour;

    /// <summary>Every request this fake was given, for assertion.</summary>
    public List<GenerateHintsRequest> Received { get; } = [];

    public bool IsConfigured { get; init; } = true;

    private FakeAiHintGenerator(Func<GenerateHintsRequest, GenerateHintsResponse> behaviour)
        => _behaviour = behaviour;

    /// <summary>One well-formed hint per requested item, tagged with the language.</summary>
    public static FakeAiHintGenerator Succeeding() => new(request => new GenerateHintsResponse
    {
        Results = request.Items
            .Select(i => new HintResult
            {
                QuestionId = i.QuestionId,
                Status = AiResultStatuses.Ok,
                Hint = request.Language == "ar"
                    ? $"تلميح للسؤال {i.QuestionId}"
                    : $"Hint for question {i.QuestionId}"
            })
            .ToList()
    });

    /// <summary>A hint that names the correct option — the service must drop it.</summary>
    public static FakeAiHintGenerator RevealingTheAnswer() => new(request => new GenerateHintsResponse
    {
        Results = request.Items
            .Select(i => new HintResult
            {
                QuestionId = i.QuestionId,
                Status = AiResultStatuses.Ok,
                Hint = "الإجابة هي " + i.Options.First(o => o.OptionId == i.Reference.CorrectOptionId).Text
            })
            .ToList()
    });

    /// <summary>Provider down, unreachable, or rate limited.</summary>
    public static FakeAiHintGenerator Unavailable() =>
        new(_ => throw new HttpRequestException("simulated AI outage"));

    /// <summary>Endpoint not configured — the current production default.</summary>
    public static FakeAiHintGenerator NotConfigured() =>
        new(_ => throw new InvalidOperationException("AI hints endpoint is not configured")) { IsConfigured = false };

    /// <summary>Timed out.</summary>
    public static FakeAiHintGenerator TimingOut() =>
        new(_ => throw new TaskCanceledException("simulated AI timeout"));

    /// <summary>Returns nothing. The service returns the saved result with HintsStatus = Unavailable.</summary>
    public static FakeAiHintGenerator ReturningNothing() =>
        new(_ => new GenerateHintsResponse { Results = [] });

    /// <summary>Returns a blank hint, which the service drops (that question gets no hint).</summary>
    public static FakeAiHintGenerator ReturningBlankHints() =>
        new(request => new GenerateHintsResponse
        {
            Results = request.Items
                .Select(i => new HintResult { QuestionId = i.QuestionId, Status = AiResultStatuses.Ok, Hint = "   " })
                .ToList()
        });

    /// <summary>Answers a question that was never asked.</summary>
    public static FakeAiHintGenerator ReturningUnrelatedQuestionIds() =>
        new(_ => new GenerateHintsResponse
        {
            Results = [new HintResult { QuestionId = -999, Status = AiResultStatuses.Ok, Hint = "unrelated" }]
        });

    public Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request, CancellationToken cancellationToken = default)
    {
        Received.Add(request);
        return Task.FromResult(_behaviour(request));
    }
}
