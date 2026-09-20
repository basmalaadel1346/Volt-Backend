using AssessmentBL.DTOs.Taxonomy;

namespace AssessmentBL.Interfaces
{
    /// <summary>Admin authoring of Assessment topics.</summary>
    public interface ITopicService
    {
        Task<IReadOnlyList<AdminTopicResponseDto>> GetAsync(TopicFilterDto filter, CancellationToken cancellationToken = default);

        Task<AdminTopicResponseDto> GetByIdAsync(int topicId, CancellationToken cancellationToken = default);

        Task<AdminTopicResponseDto> CreateAsync(CreateTopicDto request, CancellationToken cancellationToken = default);

        Task<AdminTopicResponseDto> UpdateAsync(int topicId, UpdateTopicDto request, CancellationToken cancellationToken = default);

        Task SetActiveAsync(int topicId, bool isActive, CancellationToken cancellationToken = default);
    }
}
