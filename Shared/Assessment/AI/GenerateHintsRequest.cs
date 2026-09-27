namespace Shared.Assessment.AI;

/// <summary>
/// Hints for the wrong MultipleChoice / TrueFalse answers of one submission.
/// Carries no user, attempt or account identifier of any kind.
/// </summary>
public sealed class GenerateHintsRequest
{
    public string ContractVersion { get; init; } = AiContract.Version;

    /// <summary>Random per call, to correlate logs on both sides. Never a database id.</summary>
    public Guid RequestId { get; init; }

    /// <summary>Always "Hints" — the single-question Hint button sends "Hint".</summary>
    public string Task { get; init; } = AiTasks.Hints;

    /// <summary>Omitted entirely when the learner's age is unknown.</summary>
    public LearnerContext? LearnerContext { get; init; }

    /// <summary>
    /// The language the hints must be written in ("en" | "ar"). The provider is
    /// responsible for producing text in this language — hints are generated per
    /// language, never translated after the fact.
    /// </summary>
    public string Language { get; init; } = "ar";

    public IReadOnlyList<HintRequestItem> Items { get; init; } = [];
}

/// <summary>One wrong answer to write a hint for.</summary>
public sealed class HintRequestItem
{
    /// <summary>Content id, echoed back to map the hint to its question.</summary>
    public int QuestionId { get; init; }

    /// <summary>MultipleChoice | TrueFalse.</summary>
    public string QuestionType { get; init; } = string.Empty;

    public string? Difficulty { get; init; }

    public string? Topic { get; init; }

    public AiQuestion Question { get; init; } = new();

    /// <summary>Every option, in display order. Never carries an isCorrect flag.</summary>
    public IReadOnlyList<AiOption> Options { get; init; } = [];

    public HintStudentAnswer StudentAnswer { get; init; } = new();

    /// <summary>
    /// The correct option from the attempt's frozen answer key, so the AI steers
    /// toward it instead of solving the question itself. The hint must not reveal
    /// it — the backend rejects a hint that names it.
    /// </summary>
    public HintReference Reference { get; init; } = new();

    /// <summary>Earlier hints for this question, in the same language, oldest first.</summary>
    public IReadOnlyList<string> PreviousHints { get; init; } = [];
}

public sealed class HintStudentAnswer
{
    public int SelectedOptionId { get; init; }
}

public sealed class HintReference
{
    public int CorrectOptionId { get; init; }
}
