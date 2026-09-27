using ContentBL.DTOs;
using Shared.Common.Results;

namespace ContentBL.Interfaces;

public interface ILessonContentService
{
    Task<Result<LessonContentResponse>> CreateAsync(int lessonId, CreateLessonContentRequest request, CancellationToken ct = default);

    // مبقاش محتاجين lessonId هنا - الـ contentId لوحده كافي لتحديد الصف (PK فريد)
    Task<Result<LessonContentResponse>> UpdateAsync(int contentId, UpdateLessonContentRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(int contentId, CancellationToken ct = default);

    Task<Result> SwapOrderAsync(SwapLessonContentsOrderRequest request, CancellationToken ct = default);
}
