using System.ComponentModel.DataAnnotations;

namespace UsersBL.DTOs;

public record RegisterGuestRequest(
    [Required(ErrorMessage = "الاسم مطلوب"), StringLength(150, ErrorMessage = "الاسم أطول من اللازم")] string FullName,
    string? Role);

// مفيش ExistingGuestUserId هنا خالص - ده دايمًا تسجيل حساب جديد من الصفر.
// تحويل حساب Guest بقى له Endpoint منفصل بيتطلب توكن الـ Guest نفسه (شوف ConvertGuestToEmailRequest).
public record RegisterEmailRequest(
    [Required(ErrorMessage = "الإيميل مطلوب"), StringLength(255, ErrorMessage = "الإيميل أطول من اللازم")] string Email,
    [Required(ErrorMessage = "كلمة المرور مطلوبة")] string Password,
    [Required(ErrorMessage = "الاسم مطلوب"), StringLength(150, ErrorMessage = "الاسم أطول من اللازم")] string FullName,
    string? Role,
    DateOnly? BirthDate);

public record LoginEmailRequest(
    [Required(ErrorMessage = "الإيميل مطلوب")] string Email,
    [Required(ErrorMessage = "كلمة المرور مطلوبة")] string Password);

// مفيش ExistingGuestUserId هنا برضو - دايمًا حساب Google جديد.
public record GoogleAuthRequest(
    [Required(ErrorMessage = "Google Token مطلوب")] string IdToken,
    string? Role);

// الـ GuestUserId بياخده الـ Controller من التوكن (Authorize) مش من الـ Body - عشان محدش
// يقدر "يسرق" تقدم Guest حد تاني بمجرد ما يخمن أو يجيب الـ Id بتاعه من مكان تاني.
public record ConvertGuestToEmailRequest(
    [Required(ErrorMessage = "الإيميل مطلوب"), StringLength(255, ErrorMessage = "الإيميل أطول من اللازم")] string Email,
    [Required(ErrorMessage = "كلمة المرور مطلوبة")] string Password,
    [Required(ErrorMessage = "الاسم مطلوب"), StringLength(150, ErrorMessage = "الاسم أطول من اللازم")] string FullName,
    DateOnly? BirthDate);

public record ConvertGuestToGoogleRequest([Required(ErrorMessage = "Google Token مطلوب")] string IdToken);

public record RefreshTokenRequest([Required(ErrorMessage = "Refresh Token مطلوب")] string RefreshToken);

public record ForgotPasswordRequest([Required(ErrorMessage = "الإيميل مطلوب")] string Email);

public record VerifyResetOtpRequest(
    [Required(ErrorMessage = "الإيميل مطلوب")] string Email,
    [Required(ErrorMessage = "الكود مطلوب")] string Otp);

public record VerifyResetOtpResponse(string ResetToken, DateTime ResetTokenExpiresAt);

public record ResetPasswordRequest(
    [Required(ErrorMessage = "الإيميل مطلوب")] string Email,
    [Required(ErrorMessage = "Reset Token مطلوب")] string ResetToken,
    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة")] string NewPassword);

public record ChangePasswordRequest(
    [Required(ErrorMessage = "كلمة المرور الحالية مطلوبة")] string CurrentPassword,
    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة")] string NewPassword);

public record VerifyEmailRequest([Required(ErrorMessage = "الكود مطلوب")] string Otp);

public record AuthResponse(
    Guid UserId,
    string FullName,
    string Role,
    string AuthProvider,
    int? Age,
    bool IsEmailVerified,
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt);
