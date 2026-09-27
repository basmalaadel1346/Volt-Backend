using AssessmentBL.DTOs.Taxonomy;

namespace AssessmentBL.Interfaces
{
    /// <summary>Admin authoring of Assessment categories.</summary>
    public interface ICategoryService
    {
        Task<IReadOnlyList<AdminCategoryResponseDto>> GetAllAsync(bool? isActive, CancellationToken cancellationToken = default);

        Task<AdminCategoryResponseDto> GetByIdAsync(byte categoryId, CancellationToken cancellationToken = default);

        Task<AdminCategoryResponseDto> CreateAsync(CreateCategoryDto request, CancellationToken cancellationToken = default);

        Task<AdminCategoryResponseDto> UpdateAsync(byte categoryId, UpdateCategoryDto request, CancellationToken cancellationToken = default);

        Task SetActiveAsync(byte categoryId, bool isActive, CancellationToken cancellationToken = default);
    }
}
