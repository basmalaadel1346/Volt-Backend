namespace ContentBL.DTOs;

public record LessonSummaryResponse(
    int Id, int LevelId, string Title, string? Description,
    int SortOrder, bool IsPublished, string LessonType, DateTime CreatedAt);

public record LessonDetailResponse(
    int Id, int LevelId, string Title, string? Description,
    int SortOrder, bool IsPublished, string LessonType, DateTime CreatedAt,
    List<LessonContentResponse> Contents);

// مفيش SortOrder هنا - السيرفر بيحدده تلقائي (آخر ترتيب في نفس المستوى + 1).
// LessonType اختياري - لو معمولوش Pass، بيبقى "lesson" عادي (Backward-compatible مع
// أي كود Flutter قديم لسه ماضفش الحقل ده في الـ Request).
public record CreateLessonRequest(int LevelId, string Title, string? Description, string LessonType = LessonTypes.Lesson);

// مفيش SortOrder هنا برضو - لو LevelId اتغيّر (نقل الدرس لمستوى تاني)، السيرفر
// هيدّيله ترتيب جديد تلقائي في نهاية المستوى الجديد. غير كده الترتيب مايتلمسش.
public record UpdateLessonRequest(int LevelId, string Title, string? Description, string LessonType = LessonTypes.Lesson);

public record SetPublishedRequest(bool IsPublished);

public record SwapLessonsOrderRequest(int FirstLessonId, int SecondLessonId);

// للـ Home Feed في الفلاتر - الدروس المنشورة بس، مرتبة حسب ترتيب المستوى ثم ترتيب الدرس.
// LessonType و LessonStatus بيترجعوا كنص (مش رقم) عشان أي Type جديد يتضاف بعدين
// (Enum جديد) ميكسرش أي نسخة قديمة من الفلاتر لسه بتقارن بالنص.
public record LessonHomeResponse(
    int LessonId,
    string LevelName,
    string LessonName,
    bool IsFirstLevelLesson,
    string LessonType,
    string LessonStatus);

/// <summary>القيم المسموحة لـ LessonType - Class ثابتة بدل ما نكتب الـ String حرفيًا في كذا
/// مكان (وممكن نضيف نوع جديد هنا بس في المستقبل، زي "practiceLesson" مثلاً).</summary>
public static class LessonTypes
{
    public const string Lesson = "lesson";
    public const string FinalLevelQuiz = "finalLevelQuiz";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase) { Lesson, FinalLevelQuiz };
}

/// <summary>القيم المسموحة لـ LessonStatus - بتتحسب وقت التشغيل، مفيش عمود في الداتابيز ليها.</summary>
public static class LessonStatuses
{
    public const string Completed = "completed";
    public const string InProgress = "inProgress";
    public const string Locked = "locked";
}

/// <summary>الترتيب الجديد لدروس مستوى معيّن: قايمة الـ IDs من الأول للآخر.</summary>
public record ReorderLessonsRequest(List<int> OrderedIds);
