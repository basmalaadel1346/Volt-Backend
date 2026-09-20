namespace Shared.Content;

/// <summary>
/// The one question other modules may ask the Content module about a lesson:
/// does it exist, and may a learner see it? Lives in Shared for the same reason
/// IAiHintGenerator does — Assessment references lessons only by id (no FK, no
/// project reference), so it depends on this contract, not on ContentBL.
/// </summary>
public interface ILessonAvailability
{
    /// <summary>
    /// Null when the lesson does not exist; otherwise whether it is published.
    /// </summary>
    Task<bool?> IsPublishedAsync(int lessonId, CancellationToken cancellationToken = default);
}
