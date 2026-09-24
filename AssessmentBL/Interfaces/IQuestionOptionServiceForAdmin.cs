using AssessmentBL.DTOs.Question;
using AssessmentBL.DTOs.QuestionOption;

namespace AssessmentBL.Interfaces
{
    public interface IQuestionOptionServiceForAdmin
    {
        Task<IReadOnlyList<AdminQuestionOptionResponseDto>> GetByQuestionIdAsync(
            int questionId, CancellationToken cancellationToken = default);

        Task<AdminQuestionOptionResponseDto> CreateOptionAsync(
            CreateQuestionOptionDto request, CancellationToken cancellationToken = default);

        Task<AdminQuestionOptionResponseDto> UpdateOptionAsync(
            int optionId,
            UpdateQuestionOptionDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves the question's correct answer to another of its options in ONE
        /// transaction, replacing the four-request deactivate → edit → edit →
        /// activate dance. Returns the question's options in display order.
        /// </summary>
        Task<IReadOnlyList<AdminQuestionOptionResponseDto>> SetCorrectOptionAsync(
            int questionId,
            SetCorrectOptionDto request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Renumbers a question's options from the absolute ordered list of their
        /// ids. Idempotent: sending the same order twice changes nothing.
        /// </summary>
        Task<IReadOnlyList<AdminQuestionOptionResponseDto>> ReorderAsync(
            int questionId,
            IReadOnlyList<int> orderedOptionIds,
            CancellationToken cancellationToken = default);

        Task DeleteOptionAsync(int optionId, CancellationToken cancellationToken = default);
    }
}