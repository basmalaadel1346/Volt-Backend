namespace Shared.Content;

/// <summary>
/// Read-only view of the Content module's levels for other modules (Assessment
/// uses it for placement and to validate LevelAssessment quizzes) without
/// referencing ContentBL. Same pattern as <see cref="ILessonAvailability"/>.
/// </summary>
public interface ILevelCatalog
{
    /// <summary>All levels in learning order (Order ascending). Empty when none exist.</summary>
    Task<IReadOnlyList<LevelSummary>> GetLevelsInOrderAsync(CancellationToken cancellationToken = default);
}

public sealed record LevelSummary(int Id, string Title, int Order);
