using Shared.Common.Abstractions;
using Shared.Common.Results;
using UsersBL.DTOs;
using UsersBL.Interfaces;
using UsersDA.Interfaces;

namespace UsersBL.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UserService(IUserRepository userRepository, IUnitOfWork unitOfWork, IDateTimeProvider dateTimeProvider)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
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

        var today = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);
        if (!BirthDateRules.IsValidBirthDate(request.BirthDate, today))
            return Result<UserProfileResponse>.Failure($"السن لازم يكون بين {BirthDateRules.MinAge} و {BirthDateRules.MaxAge} سنة");

        user.FullName = request.FullName;
        user.BirthDate = request.BirthDate;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<UserProfileResponse>.Success(ToResponse(user));
    }

    public async Task<Result<UserProfileResponse>> SetBirthDateAsync(Guid userId, SetBirthDateRequest request, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<UserProfileResponse>.Failure("المستخدم غير موجود");

        var today = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);
        if (!BirthDateRules.IsValidBirthDate(request.BirthDate, today))
            return Result<UserProfileResponse>.Failure($"السن لازم يكون بين {BirthDateRules.MinAge} و {BirthDateRules.MaxAge} سنة");

        user.BirthDate = request.BirthDate;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<UserProfileResponse>.Success(ToResponse(user));
    }

    private UserProfileResponse ToResponse(UsersDA.Entities.User user)
    {
        var age = BirthDateRules.CalculateAge(user.BirthDate, DateOnly.FromDateTime(_dateTimeProvider.UtcNow));

        return new UserProfileResponse(
            user.Id, user.Email, user.FullName, user.Role, user.AuthProvider,
            age, user.EmailVerifiedAt is not null, user.IsActive, user.ConvertedFromGuestAt, user.CreatedAt);
    }
}
