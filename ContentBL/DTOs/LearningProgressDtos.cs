using Shared.Gamification;

namespace ContentBL.DTOs;

/// <summary>
/// نتيجة تسجيل إكمال درس. بتحمل المكافأة (الشرارات والسلسلة) جوه نفس الرد عشان
/// الفلاتر تعرض "+5 شرارة" من غير طلب تاني.
/// </summary>
public record LessonProgressResponse(int LessonId, DateTime CompletedAt, RewardOutcome Rewards);

public record LessonProgressStatusResponse(int LessonId, bool IsCompleted, DateTime? CompletedAt);

/// <summary>
/// هل الطفل يقدر يفتح الدرس ده ولا لأ، ولو لأ فليه. الدرس بيتقفل لحد ما الطفل
/// ينجح في اختبار الدرس اللي قبله.
/// </summary>
public record LessonAccessResponse(
    int LessonId,
    bool IsUnlocked,
    bool IsCompleted,
    int? RequiredLessonId,
    string? RequiredLessonTitle,
    int? RequiredQuizId,
    string Reason,
    string Message);

/// <summary>أسباب قفل الدرس، كود ثابت الفلاتر تقدر تعمل عليه Switch.</summary>
public static class LessonAccessReasons
{
    /// <summary>مفتوح - أول درس في المستوى أو الطفل عدّى اللي قبله.</summary>
    public const string Unlocked = "Unlocked";

    /// <summary>مقفول: لازم ينجح في اختبار الدرس السابق الأول.</summary>
    public const string PreviousLessonQuizNotPassed = "PreviousLessonQuizNotPassed";

    /// <summary>الدرس نفسه مش منشور.</summary>
    public const string LessonNotPublished = "LessonNotPublished";
}
