using System.ComponentModel.DataAnnotations;

namespace ContentBL.DTOs;

public record LessonSummaryResponse(
    int Id, int LevelId, string Title, string? Description,
    int SortOrder, bool IsPublished, DateTime CreatedAt);

public record LessonDetailResponse(
    int Id, int LevelId, string Title, string? Description,
    int SortOrder, bool IsPublished, DateTime CreatedAt,
    List<LessonContentResponse> Contents);

// مفيش SortOrder هنا - السيرفر بيحدده تلقائي (آخر ترتيب في نفس المستوى + 1)
public record CreateLessonRequest(
    int LevelId,
    [Required(ErrorMessage = "العنوان مطلوب"), StringLength(200, ErrorMessage = "العنوان أطول من اللازم")] string Title,
    [StringLength(1000, ErrorMessage = "الوصف أطول من اللازم")] string? Description);

// مفيش SortOrder هنا برضو - لو LevelId اتغيّر (نقل الدرس لمستوى تاني)، السيرفر
// هيدّيله ترتيب جديد تلقائي في نهاية المستوى الجديد. غير كده الترتيب مايتلمسش.
public record UpdateLessonRequest(
    int LevelId,
    [Required(ErrorMessage = "العنوان مطلوب"), StringLength(200, ErrorMessage = "العنوان أطول من اللازم")] string Title,
    [StringLength(1000, ErrorMessage = "الوصف أطول من اللازم")] string? Description);

public record SetPublishedRequest(bool IsPublished);

public record SwapLessonsOrderRequest(int FirstLessonId, int SecondLessonId);

// lessonStatus بتتحسب وقت الطلب (مش مخزّنة) بناءً على تقدم المستخدم في LearningProgress:
// "completed" لو خلّصه، "inProgress" لو هو الدرس الجاي بعد آخر درس مكمّل، "locked" لو لسه ماوصلوش.
// الشكل ده مطابق تمامًا لشاشة الهوم بتاعت الفلاتر (levelName/lessonName بدل الـ Id/Title المجردين).
public record PublishedLessonResponse(
    int LessonId,
    string LevelName,
    string LessonName,
    bool IsFirstLevelLesson,
    string LessonType,
    string LessonStatus);
