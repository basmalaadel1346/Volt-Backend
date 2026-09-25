using Shared.Common.Results;
using UsersBL.DTOs;

namespace UsersBL.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterGuestAsync(RegisterGuestRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> RegisterWithEmailAsync(RegisterEmailRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> LoginWithEmailAsync(LoginEmailRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> LoginOrRegisterWithGoogleAsync(GoogleAuthRequest request, CancellationToken ct = default);
    Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task<Result<VerifyResetOtpResponse>> VerifyResetOtpAsync(VerifyResetOtpRequest request, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
}
