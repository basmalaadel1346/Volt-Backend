using System.ComponentModel.DataAnnotations;

namespace ContentBL.DTOs;

public record LevelResponse(int Id, string Title, string? Description, int Order);

// مفيش Order هنا - السيرفر هو اللي بيحددها تلقائي (آخر ترتيب + 1)
public record CreateLevelRequest(
    [Required(ErrorMessage = "العنوان مطلوب"), StringLength(200, ErrorMessage = "العنوان أطول من اللازم")] string Title,
    [StringLength(1000, ErrorMessage = "الوصف أطول من اللازم")] string? Description);

// مفيش Order هنا برضو - التعديل بيغيّر البيانات بس، الترتيب ثابت لحد ما يتغيّر بـ Swap
public record UpdateLevelRequest(
    [Required(ErrorMessage = "العنوان مطلوب"), StringLength(200, ErrorMessage = "العنوان أطول من اللازم")] string Title,
    [StringLength(1000, ErrorMessage = "الوصف أطول من اللازم")] string? Description);

public record SwapLevelsOrderRequest(int FirstLevelId, int SecondLevelId);
