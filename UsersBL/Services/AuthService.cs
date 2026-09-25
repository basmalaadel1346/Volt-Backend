using Shared.Common.Abstractions;
using Shared.Common.Email;
using Shared.Common.Results;
using Shared.Users;
using UsersBL.DTOs;
using UsersBL.Interfaces;
using UsersDA.Interfaces;
using UsersDA.Entities;

namespace UsersBL.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordResetOtpRepository _passwordResetOtpRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IHashGenerator _hashGenerator;
    private readonly IGoogleAuthValidator _googleAuthValidator;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IEmailSender _emailSender;

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordResetOtpRepository passwordResetOtpRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IHashGenerator hashGenerator,
        IGoogleAuthValidator googleAuthValidator,
        IDateTimeProvider dateTimeProvider,
        IEmailSender emailSender)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordResetOtpRepository = passwordResetOtpRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _hashGenerator = hashGenerator;
        _googleAuthValidator = googleAuthValidator;
        _dateTimeProvider = dateTimeProvider;
        _emailSender = emailSender;
    }

    // ---------- Guest ----------

    // الافتراضي دايمًا Child، إلا لو الفلاتر بعت "Parent" صراحةً (مش حساس لحالة الحروف)
    private static string NormalizeRole(string? role) =>
        role is not null && role.Equals(UserRoles.Parent, StringComparison.OrdinalIgnoreCase)
            ? UserRoles.Parent
            : UserRoles.Child;

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            // System.Net.Mail.MailAddress بيتأكد من الصيغة العامة للإيميل (فيه @، دومين صحيح...)
            var address = new System.Net.Mail.MailAddress(email);
            return address.Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private const int MinAge = 6;
    private const int MaxAge = 14;

    private static bool IsValidAge(int? age) => age is null || (age >= MinAge && age <= MaxAge);

    public async Task<Result<AuthResponse>> RegisterGuestAsync(RegisterGuestRequest request, CancellationToken ct = default)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName,
            Role = NormalizeRole(request.Role),
            AuthProvider = AuthProviders.Guest,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _userRepository.AddAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    // ---------- Email ----------

    public async Task<Result<AuthResponse>> RegisterWithEmailAsync(RegisterEmailRequest request, CancellationToken ct = default)
    {
        if (!IsValidEmail(request.Email))
            return Result<AuthResponse>.Failure("صيغة الإيميل غير صحيحة");

        if (!IsValidAge(request.Age))
            return Result<AuthResponse>.Failure($"السن لازم يكون بين {MinAge} و {MaxAge} سنة");

        if (await _userRepository.EmailExistsAsync(request.Email, ct))
            return Result<AuthResponse>.Failure("البريد الإلكتروني مستخدم بالفعل");

        // الحالة الأولى: فيه Guest عايز يتحول لحساب حقيقي - بنعدل نفس الصف بنفس الـ Id
        // عشان كل تقدمه (Progress, Circuits...) يفضل مربوط بيه من غير أي نقل بيانات
        if (request.ExistingGuestUserId is Guid guestId)
        {
            var guestUser = await _userRepository.GetByIdAsync(guestId, ct);
            if (guestUser is null || guestUser.AuthProvider != AuthProviders.Guest)
                return Result<AuthResponse>.Failure("حساب الـ Guest غير موجود أو اتحول قبل كده");

            guestUser.Email = request.Email;
            guestUser.PasswordHash = _passwordHasher.Hash(request.Password);
            guestUser.FullName = request.FullName;
            guestUser.Age = request.Age;
            guestUser.AuthProvider = AuthProviders.Email;
            guestUser.ConvertedFromGuestAt = _dateTimeProvider.UtcNow;

            await _unitOfWork.SaveChangesAsync(ct);
            return await IssueTokensAsync(guestUser, ct);
        }

        // الحالة التانية: تسجيل عادي من الصفر
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FullName = request.FullName,
            Age = request.Age,
            Role = NormalizeRole(request.Role),
            AuthProvider = AuthProviders.Email,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _userRepository.AddAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<Result<AuthResponse>> LoginWithEmailAsync(LoginEmailRequest request, CancellationToken ct = default)
    {
        if (!IsValidEmail(request.Email))
            return Result<AuthResponse>.Failure("صيغة الإيميل غير صحيحة");

        var user = await _userRepository.GetByEmailAsync(request.Email, ct);
        if (user is null || user.PasswordHash is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            return Result<AuthResponse>.Failure("بيانات الدخول غير صحيحة");

        if (!user.IsActive)
            return Result<AuthResponse>.Failure("الحساب غير مفعّل");

        return await IssueTokensAsync(user, ct);
    }

    // ---------- Google ----------

    public async Task<Result<AuthResponse>> LoginOrRegisterWithGoogleAsync(GoogleAuthRequest request, CancellationToken ct = default)
    {
        var googleInfo = await _googleAuthValidator.ValidateAsync(request.IdToken, ct);
        if (googleInfo is null)
            return Result<AuthResponse>.Failure("Google Token غير صالح");

        // موجود بالفعل بحساب Google؟ يبقى Login عادي
        var existing = await _userRepository.GetByProviderAsync(AuthProviders.Google, googleInfo.ProviderUserId, ct);
        if (existing is not null)
        {
            if (!existing.IsActive)
                return Result<AuthResponse>.Failure("الحساب غير مفعّل");

            return await IssueTokensAsync(existing, ct);
        }

        // Guest بيتحول لحساب Google - بنفس الـ Id ونفس التقدم
        if (request.ExistingGuestUserId is Guid guestId)
        {
            var guestUser = await _userRepository.GetByIdAsync(guestId, ct);
            if (guestUser is null || guestUser.AuthProvider != AuthProviders.Guest)
                return Result<AuthResponse>.Failure("حساب الـ Guest غير موجود أو اتحول قبل كده");

            guestUser.ProviderUserId = googleInfo.ProviderUserId;
            guestUser.Email = googleInfo.Email;
            guestUser.FullName = string.IsNullOrWhiteSpace(guestUser.FullName) ? (googleInfo.FullName ?? guestUser.FullName) : guestUser.FullName;
            guestUser.AuthProvider = AuthProviders.Google;
            guestUser.ConvertedFromGuestAt = _dateTimeProvider.UtcNow;

            await _unitOfWork.SaveChangesAsync(ct);
            return await IssueTokensAsync(guestUser, ct);
        }

        // حساب Google جديد تمامًا
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = googleInfo.Email,
            FullName = googleInfo.FullName ?? googleInfo.Email,
            Role = NormalizeRole(request.Role),
            AuthProvider = AuthProviders.Google,
            ProviderUserId = googleInfo.ProviderUserId,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _userRepository.AddAsync(user, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    // ---------- Refresh / Logout ----------

    public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var incomingHash = _hashGenerator.Hash(request.RefreshToken);
        var storedToken = await _refreshTokenRepository.GetByHashAsync(incomingHash, ct);

        var tokenIsActive = storedToken is not null
            && storedToken.RevokedAt is null
            && storedToken.ExpiresAt > _dateTimeProvider.UtcNow;

        if (storedToken is null || !tokenIsActive)
            return Result<AuthResponse>.Failure("Refresh Token غير صالح أو منتهي");

        storedToken.RevokedAt = _dateTimeProvider.UtcNow; // Rotation: كل Refresh بيلغي القديم

        var user = await _userRepository.GetByIdAsync(storedToken.UserId, ct);
        if (user is null || !user.IsActive)
            return Result<AuthResponse>.Failure("المستخدم غير موجود أو غير مفعّل");

        await _unitOfWork.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var hash = _hashGenerator.Hash(request.RefreshToken);
        var storedToken = await _refreshTokenRepository.GetByHashAsync(hash, ct);

        if (storedToken is null)
            return Result.Failure("Token غير موجود");

        storedToken.RevokedAt = _dateTimeProvider.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    // ---------- Forgot / Reset Password ----------

    public async Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, ct);

        // بنرجع Success حتى لو الإيميل مش موجود، عشان محدش يقدر يكتشف إيميلات مسجلة (User enumeration)
        if (user is null || user.AuthProvider != AuthProviders.Email)
            return Result.Success();

        var otpPlain = Random.Shared.Next(100000, 999999).ToString();

        // نمسح أي كود قديم لسه شغال لنفس اليوزر - يفضل الكود الأخير بس هو الصالح
        await _passwordResetOtpRepository.DeleteAllUsableForUserAsync(user.Id, ct);

        var otp = new PasswordResetOtp
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Otphash = _hashGenerator.Hash(otpPlain),
            ExpiresAt = _dateTimeProvider.UtcNow.AddMinutes(15),
            Attempts = 0,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _passwordResetOtpRepository.AddAsync(otp, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var subject = "كود إعادة تعيين كلمة المرور - ElectroWorld";
        var body = $"""
            <p>كود إعادة تعيين كلمة المرور بتاعك هو:</p>
            <h2>{otpPlain}</h2>
            <p>الكود ده صالح لمدة 15 دقيقة، ولو مطلبتيهوش ، تجاهل الإيميل ده.</p>
            """;

        // ملحوظة: لو الإيميل مش قادر يتبعت (SMTP لسه مش متظبط في appsettings)، مبنعملش
        // Fail للـ Request كله عشان محدش يعرف من الـ Response إن الإيميل ده مسجل أو لأ.
        try
        {
            await _emailSender.SendAsync(request.Email, subject, body, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL SEND FAILED] {request.Email}: {ex.Message}. OTP (dev fallback): {otpPlain}");
        }

        return Result.Success();
    }

    public async Task<Result<VerifyResetOtpResponse>> VerifyResetOtpAsync(VerifyResetOtpRequest request, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, ct);
        if (user is null)
            return Result<VerifyResetOtpResponse>.Failure("بيانات غير صحيحة");

        var otp = await _passwordResetOtpRepository.GetLatestUsableForUserAsync(user.Id, ct);
        if (otp is null || otp.Attempts >= 5)
            return Result<VerifyResetOtpResponse>.Failure("الكود منتهي أو غير صالح، اطلب كود جديد");

        if (otp.Otphash != _hashGenerator.Hash(request.Otp))
        {
            otp.Attempts += 1;
            await _unitOfWork.SaveChangesAsync(ct);
            return Result<VerifyResetOtpResponse>.Failure("الكود غير صحيح");
        }

        // الكود صح - نولّد Reset Token مؤقت (10 دقايق) تستخدمه صفحة "الباسورد الجديد"
        var resetTokenPlain = _jwtTokenGenerator.GenerateRefreshTokenValue();
        var resetTokenExpiresAt = _dateTimeProvider.UtcNow.AddMinutes(10);

        otp.VerifiedAt = _dateTimeProvider.UtcNow;
        otp.ResetTokenHash = _hashGenerator.Hash(resetTokenPlain);
        otp.ResetTokenExpiresAt = resetTokenExpiresAt;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<VerifyResetOtpResponse>.Success(new VerifyResetOtpResponse(resetTokenPlain, resetTokenExpiresAt));
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, ct);
        if (user is null)
            return Result.Failure("بيانات غير صحيحة");

        var resetTokenHash = _hashGenerator.Hash(request.ResetToken);
        var otp = await _passwordResetOtpRepository.GetByResetTokenHashAsync(user.Id, resetTokenHash, ct);
        if (otp is null)
            return Result.Failure("جلسة إعادة التعيين منتهية أو غير صالحة، ابدأ من كود جديد");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);

        // نلغي التوكن عشان محدش يقدر يستخدمه تاني (Single Use)
        otp.ResetTokenHash = null;
        otp.ResetTokenExpiresAt = null;

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- Helper ----------

    private async Task<Result<AuthResponse>> IssueTokensAsync(User user, CancellationToken ct)
    {
        var (accessToken, expiresAt) = _jwtTokenGenerator.GenerateAccessToken(user.Id, user.Role, user.AuthProvider);
        var refreshTokenPlain = _jwtTokenGenerator.GenerateRefreshTokenValue();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = _hashGenerator.Hash(refreshTokenPlain),
            ExpiresAt = _dateTimeProvider.UtcNow.AddDays(30),
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _refreshTokenRepository.AddAsync(refreshTokenEntity, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var response = new AuthResponse(
            user.Id, user.FullName, user.Role, user.AuthProvider, user.Age,
            accessToken, refreshTokenPlain, expiresAt);

        return Result<AuthResponse>.Success(response);
    }
}
