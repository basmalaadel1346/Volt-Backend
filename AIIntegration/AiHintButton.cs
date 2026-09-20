using Microsoft.Extensions.Options;
using Shared.Assessment.AI;

namespace AIIntegration;

internal sealed class AiHintButton : IAiHintButton
{
    private readonly IExternalAiProvider _provider;
    private readonly AiSettings _settings;

    public AiHintButton(IExternalAiProvider provider, IOptions<AiSettings> settings)
    {
        _provider = provider;
        _settings = settings.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.HintsEndpoint);

    public Task<HintResponse> RequestHintAsync(HintRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AttemptNumber < 1)
            throw new ArgumentException("AttemptNumber starts at 1", nameof(request));

        return _provider.RequestHintAsync(request, cancellationToken);
    }
}
