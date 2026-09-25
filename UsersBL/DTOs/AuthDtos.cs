namespace UsersBL.DTOs;

public record RegisterGuestRequest(string FullName, string? Role);

public record RegisterEmailRequest(
    string Email,
    string Password,
    string FullName,
    string? Role,
    int? Age,
    Guid? ExistingGuestUserId // لو موجودة، الحساب ده هيتحول من Guest لـ Email بنفس الـ Id ونفس التقدم
);

public record LoginEmailRequest(string Email, string Password);

public record GoogleAuthRequest(
    string IdToken,
    string? Role,                // بيتستخدم بس لو حساب جديد
    Guid? ExistingGuestUserId    // لو موجودة، الحساب ده هيتحول من Guest لـ Google بنفس الـ Id
);

public record RefreshTokenRequest(string RefreshToken);

public record ForgotPasswordRequest(string Email);

public record VerifyResetOtpRequest(string Email, string Otp);

public record VerifyResetOtpResponse(string ResetToken, DateTime ResetTokenExpiresAt);

public record ResetPasswordRequest(string Email, string ResetToken, string NewPassword);

public record AuthResponse(
    Guid UserId,
    string FullName,
    string Role,
    string AuthProvider,
    int? Age,
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt);
