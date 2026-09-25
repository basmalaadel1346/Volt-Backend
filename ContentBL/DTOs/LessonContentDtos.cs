namespace ContentBL.DTOs;

public record LessonContentResponse(
    int Id, int LessonId, int ContentTypeId, string ContentTypeName,
    string? Content, string? MediaUrl, int SortOrder);

// مفيش SortOrder هنا - السيرفر بيحدده تلقائي (آخر ترتيب في نفس الدرس + 1)
public record CreateLessonContentRequest(int ContentTypeId, string? Content, string? MediaUrl);

// مفيش SortOrder هنا برضو - الترتيب ثابت لحد ما يتغيّر بـ Swap
public record UpdateLessonContentRequest(int ContentTypeId, string? Content, string? MediaUrl);

public record SwapLessonContentsOrderRequest(int FirstContentId, int SecondContentId);

/// <summary>الترتيب الجديد لعناصر محتوى درس معيّن: قايمة الـ IDs من الأول للآخر.</summary>
public record ReorderLessonContentsRequest(List<int> OrderedIds);
