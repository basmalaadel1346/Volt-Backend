using ContentBL.DTOs;
using Shared.Common.Results;

namespace ContentBL.Interfaces;

public interface ILessonService
{
    Task<Result<List<LessonSummaryResponse>>> GetByLevelIdAsync(int levelId, CancellationToken ct = default);

    /// <summary>الدروس المنشورة بس، مرتبة حسب ترتيب المستوى ثم الدرس - للـ Home Feed.</summary>
    Task<Result<List<LessonHomeResponse>>> GetPublishedAsync(Guid userId, CancellationToken ct = default);
    Task<Result<LessonDetailResponse>> GetDetailAsync(int id, CancellationToken ct = default);
    Task<Result<LessonSummaryResponse>> CreateAsync(CreateLessonRequest request, CancellationToken ct = default);
    Task<Result<LessonSummaryResponse>> UpdateAsync(int id, UpdateLessonRequest request, CancellationToken ct = default);
    Task<Result<LessonSummaryResponse>> SetPublishedAsync(int id, bool isPublished, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<Result> SwapOrderAsync(SwapLessonsOrderRequest request, CancellationToken ct = default);
}
