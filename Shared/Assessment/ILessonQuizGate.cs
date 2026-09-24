namespace Shared.Assessment;

/// <summary>
/// What the Content module may ask the Assessment module: has this child passed
/// a lesson's quiz?
///
/// It is the rule behind lesson unlocking — a child moves on only once they have
/// shown they learned the lesson before — and it has to live in Shared because
/// the knowledge is split: Content owns lesson order, Assessment owns quiz
/// attempts, and neither references the other's project.
/// </summary>
public interface ILessonQuizGate
{
    /// <summary>
    /// Of the lessons asked about, the ones whose quiz this learner has PASSED.
    /// A lesson with no active quiz is treated as passed: a lesson the admin
    /// never gave a quiz cannot be a locked door.
    /// </summary>
    Task<IReadOnlySet<int>> GetPassedLessonIdsAsync(
        Guid userId,
        IReadOnlyCollection<int> lessonIds,
        CancellationToken cancellationToken = default);
}
