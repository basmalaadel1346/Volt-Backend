namespace Shared.Assessment.AI;

public interface IAiHintGenerator
{
    /// <summary>False while no AI endpoint is configured — callers skip the call entirely.</summary>
    bool IsConfigured { get; }

    Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request,
        CancellationToken cancellationToken = default);
}
