namespace UsersBL.DTOs;

public record UserProfileResponse(
    Guid Id,
    string? Email,
    string FullName,
    string Role,
    string AuthProvider,
    int? Age,
    bool IsActive,
    DateTime? ConvertedFromGuestAt,
    DateTime CreatedAt);

public record UpdateProfileRequest(string FullName, int? Age);

public record SetAgeRequest(int Age);
