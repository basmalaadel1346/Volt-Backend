using AssessmentBL.DTOs.UserTopicStat;
namespace AssessmentBL.Interfaces
{
    public interface IUserTopicStatService
    {
        /// <summary>
        /// The caller's progress map: every active topic (and any retired one they
        /// practised) grouped by category, with XP, mastery, stars and messages in
        /// the requested language.
        /// </summary>
        Task<MyProgressResponseDto> GetMyProgressAsync(
            Guid userId,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// One topic of the caller's progress map. A topic the caller has not
        /// practised yet comes back NotStarted; only a topic that does not exist is a 404.
        /// </summary>
        Task<TopicProgressDto> GetTopicProgressAsync(
            Guid userId,
            int topicId,
            string? language = null,
            CancellationToken cancellationToken = default);

        Task UpdateAfterQuizAttemptAsync(
            long quizAttemptId,
            Guid userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Recomputes HintsUsedCount for the (topic, difficulty) buckets of one
        /// attempt from the hints saved so far. Idempotent.
        /// </summary>
        Task RefreshHintsUsedCountAsync(
            long quizAttemptId,
            Guid userId,
            CancellationToken cancellationToken = default);
    }
}
