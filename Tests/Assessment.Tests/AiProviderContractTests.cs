using System.Net;
using System.Text;
using System.Text.Json;
using AIIntegration;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;
using Xunit;
using Xunit.Abstractions;

namespace Assessment.Tests;

/// <summary>
/// Captures the ACTUAL wire contract of the AI integration (HttpExternalAiProvider,
/// docs/AI_CONTRACT.md v2) without contacting any external service, and pins each
/// documented failure mode to the exception the code really throws.
/// </summary>
public class AiProviderContractTests
{
    private readonly ITestOutputHelper _output;
    public AiProviderContractTests(ITestOutputHelper output) => _output = output;

    /// <summary>Intercepts the outgoing request and returns a canned reply.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (HttpExternalAiProvider Provider, CapturingHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string hintsEndpoint = "https://ai.example.internal/v1/hints",
        string essaysEndpoint = "https://ai.example.internal/v1/essays",
        string apiKey = "")
    {
        var handler = new CapturingHandler(respond);
        var client = new HttpClient(handler);
        var settings = Options.Create(new AiSettings
        {
            HintsEndpoint = hintsEndpoint,
            EssayEvaluationEndpoint = essaysEndpoint,
            ApiKey = apiKey
        });
        return (new HttpExternalAiProvider(client, settings), handler);
    }

    private static GenerateHintsRequest SampleHintRequest(string language) => new()
    {
        RequestId = Guid.NewGuid(),
        Language = language,
        Items =
        [
            new HintRequestItem
            {
                QuestionId = 101,
                QuestionType = "MultipleChoice",
                Difficulty = "Medium",
                Topic = "المقاومة الكهربية",
                Question = new AiQuestion { Text = "ما وحدة قياس المقاومة الكهربية؟" },
                Options =
                [
                    new AiOption { OptionId = 1004, Text = "الأوم" },
                    new AiOption { OptionId = 1005, Text = "الفولت" }
                ],
                StudentAnswer = new HintStudentAnswer { SelectedOptionId = 1005 },
                Reference = new HintReference { CorrectOptionId = 1004 },
                PreviousHints = ["فكّر في العالم الألماني."]
            }
        ]
    };

    private static EssayEvaluationRequest SampleEssayRequest() => new()
    {
        RequestId = Guid.NewGuid(),
        Language = "ar",
        Items =
        [
            new EssayRequestItem
            {
                ItemId = "1",
                Difficulty = "Medium",
                Question = new AiQuestion { Text = "اشرح بكلماتك ليه لازم نحط مقاومة مع الـ LED." },
                MaxPoints = 3,
                StudentAnswer = new EssayStudentAnswer { Text = "عشان التيار ما يبقاش كبير ويحرق الـ LED" }
            }
        ]
    };

    // ---------------------------------------------------------------- hints

    [Fact]
    public async Task HintRequest_IsCamelCaseV2_WithTheFullQuestionAndTheReference()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK,
            """{"results":[{"questionId":101,"status":"Ok","hint":"تلميح"}]}"""));

        var response = await provider.GenerateHintsAsync(SampleHintRequest("ar"));

        _output.WriteLine("BODY   : " + handler.Body);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://ai.example.internal/v1/hints", handler.Request!.RequestUri!.ToString());

        var body = JsonDocument.Parse(handler.Body!).RootElement;
        var item = body.GetProperty("items")[0];

        Assert.Equal("2", body.GetProperty("contractVersion").GetString());
        Assert.Equal("ar", body.GetProperty("language").GetString());
        Assert.Equal("MultipleChoice", item.GetProperty("questionType").GetString());
        Assert.Equal(2, item.GetProperty("options").GetArrayLength());
        Assert.Equal(1005, item.GetProperty("studentAnswer").GetProperty("selectedOptionId").GetInt32());
        Assert.Equal(1004, item.GetProperty("reference").GetProperty("correctOptionId").GetInt32());

        // One reference id — never a per-option flag.
        Assert.DoesNotContain("isCorrect", handler.Body!, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("تلميح", Assert.Single(response.Results).Hint);
    }

    [Fact]
    public async Task Requests_CarryNoUserIdentifierOrImagePath()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, """{"results":[]}"""));

        await provider.GenerateHintsAsync(SampleHintRequest("en"));

        // The request types have no field for these, so they cannot leak.
        foreach (var forbidden in new[] { "userId", "attemptId", "email", "token", "password", "jwt", "imageUrl", "/uploads/" })
            Assert.DoesNotContain(forbidden, handler.Body!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoApiKey_SendsNoAuthHeader()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, """{"results":[]}"""));

        await provider.GenerateHintsAsync(SampleHintRequest("ar"));

        Assert.Null(handler.Request!.Headers.Authorization);
        Assert.DoesNotContain("x-api-key", handler.Request!.Headers.Select(h => h.Key.ToLowerInvariant()));
    }

    [Fact]
    public async Task ConfiguredApiKey_IsSentInTheConfiguredHeader()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, """{"results":[]}"""), apiKey: "secret-key");

        await provider.GenerateHintsAsync(SampleHintRequest("ar"));

        Assert.Equal("secret-key", Assert.Single(handler.Request!.Headers.GetValues("X-Api-Key")));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Language_ReachesTheProviderVerbatim(string language)
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, """{"results":[]}"""));

        await provider.GenerateHintsAsync(SampleHintRequest(language));

        Assert.Equal(language,
            JsonDocument.Parse(handler.Body!).RootElement.GetProperty("language").GetString());
    }

    // ---------------------------------------------------------------- essays

    [Fact]
    public async Task EssayRequest_IsCamelCaseV2_WithAnOpaqueItemKey()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK,
            """{"results":[{"itemId":"1","status":"Ok","points":2,"feedback":"أحسنت","confidence":0.9}]}"""));

        var response = await provider.EvaluateEssaysAsync(SampleEssayRequest());

        Assert.Equal("https://ai.example.internal/v1/essays", handler.Request!.RequestUri!.ToString());

        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("2", body.GetProperty("contractVersion").GetString());
        Assert.Equal("EssayEvaluation", body.GetProperty("task").GetString());
        Assert.Equal("ar", body.GetProperty("language").GetString());

        var item = body.GetProperty("items")[0];
        Assert.Equal("1", item.GetProperty("itemId").GetString());
        Assert.Equal(3, item.GetProperty("maxPoints").GetInt32());
        Assert.Equal("Medium", item.GetProperty("difficulty").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("question").GetProperty("text").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("studentAnswer").GetProperty("text").GetString()));

        // Essays have no model answer or rubric to send.
        foreach (var absent in new[] { "modelAnswer", "rubric", "reference", "correct" })
            Assert.DoesNotContain(absent, handler.Body!, StringComparison.OrdinalIgnoreCase);

        var result = Assert.Single(response.Results);
        Assert.Equal("1", result.ItemId);
        Assert.Equal(2, result.Points);
        Assert.Equal("أحسنت", result.Feedback);
        Assert.Equal(0.9m, result.Confidence);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task EssayResponse_ASkippedItem_CarriesItsReason_AndConfidenceMayBeOmitted()
    {
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK,
            """{"contractVersion":"2","results":[{"itemId":"1","status":"Skipped","reason":"The answer is not about the question."},{"itemId":"2","points":1,"feedback":"جيد"}]}"""));

        var response = await provider.EvaluateEssaysAsync(SampleEssayRequest());

        Assert.Equal("2", response.ContractVersion);
        Assert.Collection(response.Results,
            skipped =>
            {
                Assert.Equal("Skipped", skipped.Status);
                Assert.Equal("The answer is not about the question.", skipped.Reason);
                Assert.Null(skipped.Points);
                Assert.Null(skipped.Confidence);
            },
            graded =>
            {
                Assert.Null(graded.Status);          // missing = Ok
                Assert.Equal(1, graded.Points);
                Assert.Null(graded.Confidence);      // optional
            });
    }

    [Fact]
    public async Task EssayResponse_V1FieldNames_AreNotReadAsAGrade()
    {
        // "proposedPoints" and "flags" are gone: a v1-shaped result has no points,
        // so the service retries it instead of misreading it.
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK,
            """{"results":[{"itemId":"1","status":"Ok","proposedPoints":2,"feedback":"أحسنت","confidence":0.9,"flags":["Unclear"]}]}"""));

        var result = Assert.Single((await provider.EvaluateEssaysAsync(SampleEssayRequest())).Results);

        Assert.Null(result.Points);
    }

    [Fact]
    public void EssayResult_WireShape_IsPinned()
    {
        var json = JsonSerializer.Serialize(
            new EssayEvaluationResult { ItemId = "1", Status = "Ok", Points = 2, Feedback = "f", Confidence = 0.5m, Reason = "r" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var names = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "itemId", "status", "points", "feedback", "confidence", "reason" }, names);
    }

    [Fact]
    public async Task UnconfiguredEssayEndpoint_ThrowsBeforeAnyHttpCall()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, "{}"), essaysEndpoint: "");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EvaluateEssaysAsync(SampleEssayRequest()));

        Assert.Equal("AI essay evaluation endpoint is not configured", ex.Message);
        Assert.Null(handler.Request);
    }

    // ---------------------------------------------------------------- failures

    [Fact]
    public async Task UnconfiguredHintsEndpoint_ThrowsInvalidOperation_BeforeAnyHttpCall()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, "{}"), hintsEndpoint: "");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GenerateHintsAsync(SampleHintRequest("ar")));

        Assert.Equal("AI hints endpoint is not configured", ex.Message);
        Assert.Null(handler.Request);   // never left the process
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]      // invalid key
    [InlineData(HttpStatusCode.TooManyRequests)]   // rate limited
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task NonSuccessStatus_ThrowsHttpRequestException(HttpStatusCode code)
    {
        var (provider, _) = Build(_ => Json(code, """{"error":"nope"}"""));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GenerateHintsAsync(SampleHintRequest("ar")));
    }

    [Fact]
    public async Task MalformedJson_ThrowsJsonException()
    {
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, "{ this is not json "));

        await Assert.ThrowsAsync<JsonException>(
            () => provider.GenerateHintsAsync(SampleHintRequest("ar")));
    }

    [Fact]
    public async Task LiteralJsonNullBody_ThrowsInvalidOperation()
    {
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, "null"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GenerateHintsAsync(SampleHintRequest("ar")));

        Assert.Equal("The AI provider returned an empty response", ex.Message);
    }

    [Fact]
    public async Task MissingResultsProperty_DoesNotThrow_ItYieldsAnEmptyList()
    {
        // The provider does not validate shape; QuizAttemptService does.
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, """{"somethingElse":true}"""));

        var response = await provider.GenerateHintsAsync(SampleHintRequest("ar"));

        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task NetworkFailure_ThrowsHttpRequestException()
    {
        var handler = new CapturingHandler(_ => throw new HttpRequestException("no route to host"));
        var provider = new HttpExternalAiProvider(
            new HttpClient(handler), Options.Create(new AiSettings { HintsEndpoint = "https://x/y" }));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GenerateHintsAsync(SampleHintRequest("ar")));
    }

    [Fact]
    public async Task CallerCancellation_ThrowsOperationCanceled()
    {
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, """{"results":[]}"""));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateHintsAsync(SampleHintRequest("ar"), cts.Token));
    }

    // ------------------------------------------------- generator / evaluator wrappers

    [Fact]
    public async Task Generator_RejectsAnEmptyItemList_WithoutCallingTheProvider()
    {
        var (provider, handler) = Build(_ => Json(HttpStatusCode.OK, """{"results":[]}"""));
        var generator = new AiHintGenerator(provider, Options.Create(new AiSettings { HintsEndpoint = "https://x/y" }));

        await Assert.ThrowsAsync<ArgumentException>(
            () => generator.GenerateHintsAsync(new GenerateHintsRequest { Language = "ar" }));

        Assert.Null(handler.Request);
    }

    [Fact]
    public void Wrappers_ReportWhetherTheirEndpointIsConfigured()
    {
        var (provider, _) = Build(_ => Json(HttpStatusCode.OK, "{}"));

        Assert.True(new AiHintGenerator(provider, Options.Create(new AiSettings { HintsEndpoint = "https://x" })).IsConfigured);
        Assert.False(new AiHintGenerator(provider, Options.Create(new AiSettings())).IsConfigured);
        Assert.True(new AiEssayEvaluator(provider, Options.Create(new AiSettings { EssayEvaluationEndpoint = "https://x" })).IsConfigured);
        Assert.False(new AiEssayEvaluator(provider, Options.Create(new AiSettings())).IsConfigured);
    }
}
