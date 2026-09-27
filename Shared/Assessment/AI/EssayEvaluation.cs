namespace Shared.Assessment.AI;

/// <summary>
/// Essay answers to evaluate. The AI grades each one — points and feedback —
/// and the backend only checks that the grade is well-formed before storing it.
/// Essays have no model answer and no rubric: the AI judges the answer against
/// the question alone. Carries no user, attempt or account identifier of any kind.
/// </summary>
public sealed class EssayEvaluationRequest
{
    public string ContractVersion { get; init; } = AiContract.Version;

    /// <summary>Random per call. Never a database id.</summary>
    public Guid RequestId { get; init; }

    /// <summary>Always "EssayEvaluation".</summary>
    public string Task { get; init; } = AiTasks.EssayEvaluation;

    /// <summary>The language the child answered in; feedback must be in it too.</summary>
    public string Language { get; init; } = "ar";

    public IReadOnlyList<EssayRequestItem> Items { get; init; } = [];
}

public sealed class EssayRequestItem
{
    /// <summary>
    /// Opaque key for this item within this request ("1", "2", …), echoed back.
    /// Not a database id — one request may hold answers of different children.
    /// </summary>
    public string ItemId { get; init; } = string.Empty;

    public string? Difficulty { get; init; }

    public string? Topic { get; init; }

    /// <summary>
    /// The question text and, when it has an image, the image's description —
    /// the AI does not look at the image itself unless its bytes are sent.
    /// </summary>
    public AiQuestion Question { get; init; } = new();

    /// <summary>The most the answer can earn. Points above it are not accepted.</summary>
    public int MaxPoints { get; init; }

    public EssayStudentAnswer StudentAnswer { get; init; } = new();
}

public sealed class EssayStudentAnswer
{
    public string Text { get; init; } = string.Empty;
}

public sealed class EssayEvaluationResponse
{
    public string? ContractVersion { get; init; }

    public string? RequestId { get; init; }

    public IReadOnlyList<EssayEvaluationResult> Results { get; init; } = [];
}

public sealed class EssayEvaluationResult
{
    public string? ItemId { get; init; }

    /// <summary>
    /// Ok | Skipped. Missing = Ok. Skipped means the AI will not grade this
    /// answer; it is final — the essay earns no points and nobody else grades it.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>The grade: a whole number, 0 … MaxPoints. Required when Ok.</summary>
    public int? Points { get; init; }

    /// <summary>Child-facing feedback, in the request language. Required when Ok.</summary>
    public string? Feedback { get; init; }

    /// <summary>
    /// Optional, 0.00 … 1.00. Stored for monitoring only: it never decides
    /// whether the grade is accepted.
    /// </summary>
    public decimal? Confidence { get; init; }

    /// <summary>Optional. Why the item was Skipped, for logs only; never shown to the child.</summary>
    public string? Reason { get; init; }
}

public interface IAiEssayEvaluator
{
    /// <summary>False while no essay endpoint is configured — nothing is attempted or counted.</summary>
    bool IsConfigured { get; }

    Task<EssayEvaluationResponse> EvaluateAsync(
        EssayEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
