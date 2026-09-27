using System.ComponentModel.DataAnnotations;

namespace ContentBL.DTOs;

public record LessonContentResponse(
    int Id, int LessonId, int ContentTypeId, string ContentTypeName,
    string? Content, string? MediaUrl, int SortOrder);

// مفيش SortOrder هنا - السيرفر بيحدده تلقائي (آخر ترتيب في نفس الدرس + 1)
public record CreateLessonContentRequest(
    int ContentTypeId,
    string? Content,
    [StringLength(500, ErrorMessage = "رابط الصورة أطول من اللازم")] string? MediaUrl);

// مفيش SortOrder هنا برضو - الترتيب ثابت لحد ما يتغيّر بـ Swap
public record UpdateLessonContentRequest(
    int ContentTypeId,
    string? Content,
    [StringLength(500, ErrorMessage = "رابط الصورة أطول من اللازم")] string? MediaUrl);

public record SwapLessonContentsOrderRequest(int FirstContentId, int SecondContentId);
