namespace Shared.Content;

/// <summary>
/// What other modules may ask the Content module about lessons: does a lesson
/// exist and may a learner see it, and which lessons does a level publish, in
/// order. Lives in Shared for the same reason IAiHintGenerator does — Assessment
/// references lessons only by id (no FK, no project reference), so it depends on
/// this contract, not on ContentBL.
/// </summary>
public interface ILessonAvailability
{
    /// <summary>
    /// Null when the lesson does not exist; otherwise whether it is published.
    /// </summary>
    Task<bool?> IsPublishedAsync(int lessonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The published lessons of one level, in the order a child meets them.
    /// Empty when the level has none, or does not exist.
    /// </summary>
    Task<IReadOnlyList<LessonSummary>> GetPublishedLessonsAsync(
        int levelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The published lessons of every level, in level order and then lesson
    /// order. One call, because the callers that need it (level-skip sampling,
    /// placement completion) would otherwise fan out per level.
    /// </summary>
    Task<IReadOnlyList<LessonSummary>> GetPublishedLessonsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>A lesson as other modules see it: an id, where it sits, and its name.</summary>
public sealed record LessonSummary(int Id, int LevelId, string Title, int SortOrder);
