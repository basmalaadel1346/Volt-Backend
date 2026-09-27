namespace Shared.Content;

/// <summary>
/// The one thing another module may CHANGE in the Content module: mark lessons
/// a learner has already proven they know as completed.
///
/// Used by the placement test — a child placed at level 3 has demonstrated
/// levels 1 and 2, so those levels' lessons must show as done rather than as
/// homework they never did — and by the level-skip challenge, which is the same
/// idea for one level. Separate from ILessonAvailability because it writes:
/// a module holding only the read contract cannot change a learner's progress.
/// </summary>
public interface ILessonProgressWriter
{
    /// <summary>
    /// Marks each lesson completed for the learner, skipping the ones already
    /// marked. Idempotent — re-running it adds nothing — and returns how many
    /// lessons this call actually newly completed.
    /// </summary>
    Task<int> MarkLessonsCompletedAsync(
        Guid userId,
        IReadOnlyCollection<int> lessonIds,
        CancellationToken cancellationToken = default);
}
