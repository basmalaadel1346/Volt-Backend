using System.ComponentModel.DataAnnotations;

namespace UsersBL.DTOs;

public record UserProfileResponse(
    Guid Id,
    string? Email,
    string FullName,
    string Role,
    string AuthProvider,
    int? Age,
    bool IsEmailVerified,
    bool IsActive,
    DateTime? ConvertedFromGuestAt,
    DateTime CreatedAt);

public record UpdateProfileRequest(
    [Required(ErrorMessage = "الاسم مطلوب"), StringLength(150, ErrorMessage = "الاسم أطول من اللازم")] string FullName,
    DateOnly? BirthDate);

public record SetBirthDateRequest(DateOnly BirthDate);
