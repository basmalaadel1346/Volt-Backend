using ContentBL.DTOs;
using Shared.Common.Results;

namespace ContentBL.Interfaces;

public interface ILessonService
{
    /// <summary>كل الدروس بتاعت مستوى معيّن. لو isAdmin=false (Parent/Child)، بترجع الدروس المنشورة بس -
    /// عشان محدش يشوف دروس لسه تحت المراجعة قبل ما الأدمن ينشرها.</summary>
    Task<Result<List<LessonSummaryResponse>>> GetByLevelIdAsync(int levelId, bool isAdmin, CancellationToken ct = default);

    /// <summary>شكل شاشة الهوم الكامل: كل الدروس المنشورة بترتيب المسار، مع النوع وحالة تقدم المستخدم الحالي.</summary>
    Task<Result<List<PublishedLessonResponse>>> GetPublishedHomeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>تفاصيل الدرس كاملة. لو isAdmin=false (Parent/Child) وكان الدرس مش منشور، بترجع فشل
    /// (نفس رسالة "الدرس غير موجود") عشان محدش يقدر يوصل لمحتوى لسه تحت المراجعة.</summary>
    Task<Result<LessonDetailResponse>> GetDetailAsync(int id, bool isAdmin, CancellationToken ct = default);
    Task<Result<LessonSummaryResponse>> CreateAsync(CreateLessonRequest request, CancellationToken ct = default);
    Task<Result<LessonSummaryResponse>> UpdateAsync(int id, UpdateLessonRequest request, CancellationToken ct = default);
    Task<Result<LessonSummaryResponse>> SetPublishedAsync(int id, bool isPublished, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<Result> SwapOrderAsync(SwapLessonsOrderRequest request, CancellationToken ct = default);
}
