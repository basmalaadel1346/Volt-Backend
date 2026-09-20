using Microsoft.Extensions.Options;
using Shared.Assessment.AI;

namespace AIIntegration;

internal sealed class AiEssayEvaluator : IAiEssayEvaluator
{
    private readonly IExternalAiProvider _provider;
    private readonly AiSettings _settings;

    public AiEssayEvaluator(IExternalAiProvider provider, IOptions<AiSettings> settings)
    {
        _provider = provider;
        _settings = settings.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.EssayEvaluationEndpoint);

    public Task<EssayEvaluationResponse> EvaluateAsync(
        EssayEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items.Count == 0)
            throw new ArgumentException("At least one item is required", nameof(request));

        return _provider.EvaluateEssaysAsync(request, cancellationToken);
    }
}
