using AssessmentBL.DTOs.Quiz;
using AssessmentBL.DTOs.Quiz.Common;
namespace AssessmentBL.Interfaces
{
    public interface IQuizService
    {
        Task<QuizResponseDto> GetByIdAsync(int quizId, CancellationToken cancellationToken = default);

        Task<PagedResult<QuizResponseDto>> GetAsync(QuizFilterDto filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Child-facing: the active LessonQuiz of a published lesson, as a summary
        /// (title, question count, points). The questions themselves come from
        /// POST /api/quiz-attempts, which is the only place they are frozen.
        /// </summary>
        Task<LessonQuizResponseDto> GetForLessonAsync(
            int lessonId,
            string? language = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Child-facing: the active LevelAssessment quiz of a level, by level id.
        /// 404 when the level does not exist or has no active quiz with questions.
        /// </summary>
        Task<LevelQuizResponseDto> GetForLevelAsync(
            int levelId,
            string? language = null,
            CancellationToken cancellationToken = default);

        Task<QuizResponseDto> CreateAsync(CreateQuizDto request, CancellationToken cancellationToken = default);

        Task<QuizResponseDto> UpdateAsync(int quizId, UpdateQuizDto request, CancellationToken cancellationToken = default);

        Task SetActiveAsync(int quizId, bool isActive, CancellationToken cancellationToken = default);
    }
}