using Shared.Common.Results;
using UsersBL.DTOs;
using UsersBL.Interfaces;
using UsersDA.Interfaces;

namespace UsersBL.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UserProfileResponse>> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<UserProfileResponse>.Failure("المستخدم غير موجود");

        return Result<UserProfileResponse>.Success(ToResponse(user));
    }

    public async Task<Result<UserProfileResponse>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<UserProfileResponse>.Failure("المستخدم غير موجود");

        if (!IsValidAge(request.Age))
            return Result<UserProfileResponse>.Failure($"السن لازم يكون بين {MinAge} و {MaxAge} سنة");

        user.FullName = request.FullName;
        user.Age = request.Age;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<UserProfileResponse>.Success(ToResponse(user));
    }

    public async Task<Result<UserProfileResponse>> SetAgeAsync(Guid userId, SetAgeRequest request, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<UserProfileResponse>.Failure("المستخدم غير موجود");

        if (!IsValidAge(request.Age))
            return Result<UserProfileResponse>.Failure($"السن لازم يكون بين {MinAge} و {MaxAge} سنة");

        user.Age = request.Age;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<UserProfileResponse>.Success(ToResponse(user));
    }

    private const int MinAge = 6;
    private const int MaxAge = 14;

    private static bool IsValidAge(int? age) => age is null || (age >= MinAge && age <= MaxAge);

    private static UserProfileResponse ToResponse(UsersDA.Entities.User user) => new(
        user.Id, user.Email, user.FullName, user.Role, user.AuthProvider,
        user.Age, user.IsActive, user.ConvertedFromGuestAt, user.CreatedAt);
}
