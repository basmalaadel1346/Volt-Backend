using Shared.Assessment.AI;

namespace AIIntegration;

internal interface IExternalAiProvider
{
    Task<GenerateHintsResponse> GenerateHintsAsync(
        GenerateHintsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>One question, on demand, escalating — the Hint button.</summary>
    Task<HintResponse> RequestHintAsync(
        HintRequest request,
        CancellationToken cancellationToken = default);

    Task<EssayEvaluationResponse> EvaluateEssaysAsync(
        EssayEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
