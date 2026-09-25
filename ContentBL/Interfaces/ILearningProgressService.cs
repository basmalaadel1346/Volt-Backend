using ContentBL.DTOs;
using Shared.Common.Results;

namespace ContentBL.Interfaces;

public interface ILearningProgressService
{
    /// <summary>كل الـ LessonId المكتملة لليوزر الحالي - يستخدمها الفلاتر لعمل الـ Checkmarks وفتح الدروس.</summary>
    Task<Result<List<int>>> GetCompletedLessonIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>حالة درس معيّن (مكتمل ولا لأ) - بترجع IsCompleted=false لو مفيش صف، مش Failure.</summary>
    Task<Result<LessonProgressStatusResponse>> GetLessonProgressAsync(Guid userId, int lessonId, CancellationToken ct = default);

    /// <summary>بتسجّل إكمال الدرس (Idempotent - لو مسجل بالفعل، بترجع نجاح من غير Duplicate).</summary>
    Task<Result<LessonProgressResponse>> MarkLessonCompleteAsync(Guid userId, int lessonId, CancellationToken ct = default);

    /// <summary>
    /// هل الطفل يقدر يفتح الدرس ده؟ الدرس بيفضل مقفول لحد ما ينجح في اختبار الدرس
    /// اللي قبله في نفس المستوى. أول درس في المستوى مفتوح دايمًا.
    /// </summary>
    Task<Result<LessonAccessResponse>> GetLessonAccessAsync(Guid userId, int lessonId, CancellationToken ct = default);

    /// <summary>
    /// نفس السؤال لكل دروس مستوى مرة واحدة - عشان شاشة قائمة الدروس تعرف تحط
    /// القفل على كل درس من غير ما تعمل نداء لكل درس لوحده.
    /// </summary>
    Task<Result<List<LessonAccessResponse>>> GetLevelAccessAsync(Guid userId, int levelId, CancellationToken ct = default);
}
