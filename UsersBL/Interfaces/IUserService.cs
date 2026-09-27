using Shared.Common.Results;
using UsersBL.DTOs;

namespace UsersBL.Interfaces;

public interface IUserService
{
    Task<Result<UserProfileResponse>> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<Result<UserProfileResponse>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);

    /// <summary>لتحديد تاريخ ميلاد المستخدم بعد تسجيل Google (اللي مبيدخلش تاريخ الميلاد وقت التسجيل نفسه).</summary>
    Task<Result<UserProfileResponse>> SetBirthDateAsync(Guid userId, SetBirthDateRequest request, CancellationToken ct = default);
}
