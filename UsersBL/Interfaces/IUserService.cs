using Shared.Common.Results;
using UsersBL.DTOs;

namespace UsersBL.Interfaces;

public interface IUserService
{
    Task<Result<UserProfileResponse>> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<Result<UserProfileResponse>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);

    /// <summary>لتحديد سن المستخدم بعد تسجيل Google (اللي مبيدخلش السن وقت التسجيل نفسه).</summary>
    Task<Result<UserProfileResponse>> SetAgeAsync(Guid userId, SetAgeRequest request, CancellationToken ct = default);
}
