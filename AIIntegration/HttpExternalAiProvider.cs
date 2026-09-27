using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;

namespace AIIntegration;

internal sealed class HttpExternalAiProvider : IExternalAiProvider
{
    // Web defaults → camelCase, the same as PostAsJsonAsync.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AiSettings _settings;

    public HttpExternalAiProvider(HttpClient httpClient, IOptions<AiSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
    }

    public Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<GenerateHintsRequest, GenerateHintsResponse>(
            _settings.HintsEndpoint, "AI hints endpoint is not configured", request, cancellationToken);

    // Same endpoint as the batch hints: the payload's "task" tells them apart
    // ("Hints" vs "Hint"), as the contract specifies.
    public Task<HintResponse> RequestHintAsync(
        HintRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<HintRequest, HintResponse>(
            _settings.HintsEndpoint, "AI hints endpoint is not configured", request, cancellationToken);

    public Task<EssayEvaluationResponse> EvaluateEssaysAsync(
        EssayEvaluationRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<EssayEvaluationRequest, EssayEvaluationResponse>(
            _settings.EssayEvaluationEndpoint, "AI essay evaluation endpoint is not configured", request, cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string endpoint,
        string notConfiguredMessage,
        TRequest request,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(notConfiguredMessage);

        // Serialized up front so the request carries a Content-Length: some
        // gateways reject a chunked body with 411.
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            message.Headers.TryAddWithoutValidation(_settings.ApiKeyHeaderName, _settings.ApiKey);

        using var response = await _httpClient.SendAsync(message, cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            ?? throw new InvalidOperationException("The AI provider returned an empty response");
    }
}
