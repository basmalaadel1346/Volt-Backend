using Shared.Common.Results;
using UsersBL.DTOs;

namespace UsersBL.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterGuestAsync(RegisterGuestRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> RegisterWithEmailAsync(RegisterEmailRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> LoginWithEmailAsync(LoginEmailRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> LoginOrRegisterWithGoogleAsync(GoogleAuthRequest request, CancellationToken ct = default);

    /// <summary>تحويل حساب Guest لحساب بإيميل - الـ guestUserId بييجي من توكن الـ Guest نفسه (مش من الـ Body).</summary>
    Task<Result<AuthResponse>> ConvertGuestToEmailAsync(Guid guestUserId, ConvertGuestToEmailRequest request, CancellationToken ct = default);

    /// <summary>تحويل حساب Guest لحساب Google - نفس فكرة الأول.</summary>
    Task<Result<AuthResponse>> ConvertGuestToGoogleAsync(Guid guestUserId, ConvertGuestToGoogleRequest request, CancellationToken ct = default);

    Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task<Result<VerifyResetOtpResponse>> VerifyResetOtpAsync(VerifyResetOtpRequest request, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    /// <summary>لتغيير الباسورد وهو داخل بحسابه (مش ناسيه) - لازم يبعت الباسورد الحالي عشان يتأكد.</summary>
    Task<Result<AuthResponse>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);

    Task<Result> SendVerificationEmailAsync(Guid userId, CancellationToken ct = default);

    /// <summary>بتتأكد من كود التحقق وترجع Tokens جديدة (بالـ Claim emailVerified=true) - عشان الـ Access
    /// Token القديم (اللي فيه emailVerified=false) مايفضلش يمنع المستخدم من الـ Endpoints المحمية
    /// لحد ما ينتهي أو يعمل Refresh يدوي.</summary>
    Task<Result<AuthResponse>> VerifyEmailAsync(Guid userId, VerifyEmailRequest request, CancellationToken ct = default);

    /// <summary>بتمسح حساب المستخدم نهائيًا (RefreshTokens وPasswordResetOTPs بيتمسحوا تلقائي
    /// بالـ Cascade في الداتابيز، وParentChildLinks بتتمسح يدويًا هنا الأول).</summary>
    Task<Result> DeleteAccountAsync(Guid userId, CancellationToken ct = default);
}
