using Microsoft.EntityFrameworkCore;
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
    private readonly IEmailVerificationOtpRepository _emailVerificationOtpRepository;
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
        IEmailVerificationOtpRepository emailVerificationOtpRepository,
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
        _emailVerificationOtpRepository = emailVerificationOtpRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _hashGenerator = hashGenerator;
        _googleAuthValidator = googleAuthValidator;
        _dateTimeProvider = dateTimeProvider;
        _emailSender = emailSender;
    }

    // ---------- Helpers ----------

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

    private const int MinPasswordLength = 8;

    private static bool IsValidPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length >= MinPasswordLength;

    // ---------- Guest ----------

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
        var today = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);

        if (!IsValidEmail(request.Email))
            return Result<AuthResponse>.Failure("صيغة الإيميل غير صحيحة");

        if (!BirthDateRules.IsValidBirthDate(request.BirthDate, today))
            return Result<AuthResponse>.Failure($"السن لازم يكون بين {BirthDateRules.MinAge} و {BirthDateRules.MaxAge} سنة");

        if (!IsValidPassword(request.Password))
            return Result<AuthResponse>.Failure($"كلمة المرور لازم تكون {MinPasswordLength} حروف على الأقل");

        if (await _userRepository.EmailExistsAsync(request.Email, ct))
            return Result<AuthResponse>.Failure("البريد الإلكتروني مستخدم بالفعل");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            FullName = request.FullName,
            BirthDate = request.BirthDate,
            Role = NormalizeRole(request.Role),
            AuthProvider = AuthProviders.Email,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _userRepository.AddAsync(user, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // لو طلبين تسجيل بنفس الإيميل جم في نفس اللحظة بالظبط، الفحص فوق (EmailExistsAsync)
            // ممكن يكون فات عليهم الاتنين قبل ما أي حد يعمل Commit - الـ Unique Index هي خط
            // الدفاع الأخير، وهنا بنحولها لرسالة واضحة بدل ما تطلع Exception خام (500)
            return Result<AuthResponse>.Failure("البريد الإلكتروني مستخدم بالفعل");
        }

        // بعت إيميل التحقق (Best-effort - مابنوقفش التسجيل لو فشل الإرسال)
        await TrySendVerificationEmailAsync(user, ct);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<Result<AuthResponse>> LoginWithEmailAsync(LoginEmailRequest request, CancellationToken ct = default)
    {
        if (!IsValidEmail(request.Email))
            return Result<AuthResponse>.Failure("صيغة الإيميل غير صحيحة");

        var user = await _userRepository.GetByEmailAsync(request.Email, ct);

        // بنرجع نفس رسالة الخطأ العامة سواء الإيميل مش موجود، الباسورد غلط، أو الحساب معطّل -
        // عشان محدش يقدر يعرف "الحساب ده موجود بس معطّل" من رسالة مختلفة (User Enumeration)
        if (user is null || user.PasswordHash is null || !_passwordHasher.Verify(request.Password, user.PasswordHash) || !user.IsActive)
            return Result<AuthResponse>.Failure("بيانات الدخول غير صحيحة");

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

        // الإيميل ده مسجل بالفعل بحساب تاني (Email/Password مثلاً)؟ نرجع رسالة واضحة
        // بدل ما نسيب الداتابيز ترفض الـ Insert بـ Exception داخلي غير مفهوم
        if (await _userRepository.EmailExistsAsync(googleInfo.Email, ct))
            return Result<AuthResponse>.Failure("الإيميل ده مسجل بالفعل بحساب تاني، سجّل دخول بالإيميل والباسورد بدل كده");

        // حساب Google جديد تمامًا - Google أصلاً متأكد من الإيميل ده (جزء من الـ OAuth
        // نفسه)، فمفيش داعي نعمل له تحقق تاني بالـ OTP - بنعتبره Verified من لحظة الإنشاء
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = googleInfo.Email,
            FullName = googleInfo.FullName ?? googleInfo.Email,
            Role = NormalizeRole(request.Role),
            AuthProvider = AuthProviders.Google,
            ProviderUserId = googleInfo.ProviderUserId,
            IsActive = true,
            EmailVerifiedAt = _dateTimeProvider.UtcNow,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _userRepository.AddAsync(user, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // نفس فكرة الـ Race Condition في RegisterWithEmailAsync - لو طلبين جم في نفس
            // اللحظة (مثلاً حد ضغط "دخول بجوجل" مرتين بسرعة)، واحد بس ينجح
            return Result<AuthResponse>.Failure("الإيميل ده مسجل بالفعل بحساب تاني، سجّل دخول بالإيميل والباسورد بدل كده");
        }

        return await IssueTokensAsync(user, ct);
    }

    // ---------- Guest Conversion (لازم توكن الـ Guest نفسه - شوف الـ Controller) ----------

    public async Task<Result<AuthResponse>> ConvertGuestToEmailAsync(Guid guestUserId, ConvertGuestToEmailRequest request, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);

        if (!IsValidEmail(request.Email))
            return Result<AuthResponse>.Failure("صيغة الإيميل غير صحيحة");

        if (!BirthDateRules.IsValidBirthDate(request.BirthDate, today))
            return Result<AuthResponse>.Failure($"السن لازم يكون بين {BirthDateRules.MinAge} و {BirthDateRules.MaxAge} سنة");

        var guestUser = await _userRepository.GetByIdAsync(guestUserId, ct);
        if (guestUser is null || guestUser.AuthProvider != AuthProviders.Guest)
            return Result<AuthResponse>.Failure("حساب الـ Guest غير موجود أو اتحول قبل كده");

        if (await _userRepository.EmailExistsAsync(request.Email, ct))
            return Result<AuthResponse>.Failure("البريد الإلكتروني مستخدم بالفعل");

        guestUser.Email = request.Email;
        guestUser.PasswordHash = _passwordHasher.Hash(request.Password);
        guestUser.FullName = request.FullName;
        guestUser.BirthDate = request.BirthDate;
        guestUser.AuthProvider = AuthProviders.Email;
        guestUser.ConvertedFromGuestAt = _dateTimeProvider.UtcNow;

        // نلغي أي Refresh Token قديم كان شغال وقت ما كان Guest - عشان لو حد تاني حاصل
        // على التوكن القديم ده بأي شكل، مايقدرش يستخدمه بعد التحويل
        await _refreshTokenRepository.RevokeAllActiveForUserAsync(guestUserId, _dateTimeProvider.UtcNow, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        await TrySendVerificationEmailAsync(guestUser, ct);

        return await IssueTokensAsync(guestUser, ct);
    }

    public async Task<Result<AuthResponse>> ConvertGuestToGoogleAsync(Guid guestUserId, ConvertGuestToGoogleRequest request, CancellationToken ct = default)
    {
        var googleInfo = await _googleAuthValidator.ValidateAsync(request.IdToken, ct);
        if (googleInfo is null)
            return Result<AuthResponse>.Failure("Google Token غير صالح");

        var guestUser = await _userRepository.GetByIdAsync(guestUserId, ct);
        if (guestUser is null || guestUser.AuthProvider != AuthProviders.Guest)
            return Result<AuthResponse>.Failure("حساب الـ Guest غير موجود أو اتحول قبل كده");

        if (await _userRepository.GetByProviderAsync(AuthProviders.Google, googleInfo.ProviderUserId, ct) is not null)
            return Result<AuthResponse>.Failure("حساب Google ده مربوط بحساب تاني بالفعل");

        if (await _userRepository.EmailExistsAsync(googleInfo.Email, ct))
            return Result<AuthResponse>.Failure("الإيميل ده مسجل بالفعل بحساب تاني، سجّل دخول بالإيميل والباسورد بدل كده");

        guestUser.ProviderUserId = googleInfo.ProviderUserId;
        guestUser.Email = googleInfo.Email;
        guestUser.FullName = string.IsNullOrWhiteSpace(guestUser.FullName) ? (googleInfo.FullName ?? guestUser.FullName) : guestUser.FullName;
        guestUser.AuthProvider = AuthProviders.Google;
        guestUser.EmailVerifiedAt = _dateTimeProvider.UtcNow; // Google متأكد من الإيميل أصلاً
        guestUser.ConvertedFromGuestAt = _dateTimeProvider.UtcNow;

        await _refreshTokenRepository.RevokeAllActiveForUserAsync(guestUserId, _dateTimeProvider.UtcNow, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return await IssueTokensAsync(guestUser, ct);
    }

    // ---------- Refresh / Logout ----------

    public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var incomingHash = _hashGenerator.Hash(request.RefreshToken);
        var storedToken = await _refreshTokenRepository.GetByHashAsync(incomingHash, ct);

        if (storedToken is null)
            return Result<AuthResponse>.Failure("Refresh Token غير صالح أو منتهي");

        var now = _dateTimeProvider.UtcNow;

        // Rotation ذرّي (Atomic) على مستوى الداتابيز - لو طلبين Refresh جم في نفس اللحظة
        // بنفس التوكن، واحد بس هينجح يلغيه، والتاني هيرجع Failure بدل ما ياخد Exception
        var revoked = await _refreshTokenRepository.RevokeIfActiveAsync(storedToken.Id, now, ct);
        if (!revoked)
            return Result<AuthResponse>.Failure("Refresh Token غير صالح أو منتهي أو مستخدم بالفعل");

        var user = await _userRepository.GetByIdAsync(storedToken.UserId, ct);
        if (user is null || !user.IsActive)
            return Result<AuthResponse>.Failure("المستخدم غير موجود أو غير مفعّل");

        return await IssueTokensAsync(user, ct);
    }

    public async Task<Result> LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var hash = _hashGenerator.Hash(request.RefreshToken);
        var storedToken = await _refreshTokenRepository.GetByHashAsync(hash, ct);

        if (storedToken is null)
            return Result.Failure("Token غير موجود");

        await _refreshTokenRepository.RevokeIfActiveAsync(storedToken.Id, _dateTimeProvider.UtcNow, ct);
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
            <p>الكود ده صالح لمدة 15 دقيقة، ولو محدش طلبه، تجاهل الإيميل ده.</p>
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

        if (!IsValidPassword(request.NewPassword))
            return Result.Failure($"كلمة المرور لازم تكون {MinPasswordLength} حروف على الأقل");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);

        // نلغي التوكن عشان محدش يقدر يستخدمه تاني (Single Use)
        otp.ResetTokenHash = null;
        otp.ResetTokenExpiresAt = null;

        // نلغي كل الجلسات الشغالة (Refresh Tokens) - لو حد سرق الحساب، الباسورد الجديد
        // مايكفيش، لازم يخرج من كل مكان داخل بيه
        await _refreshTokenRepository.RevokeAllActiveForUserAsync(user.Id, _dateTimeProvider.UtcNow, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<AuthResponse>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<AuthResponse>.Failure("المستخدم غير موجود");

        if (user.AuthProvider != AuthProviders.Email || user.PasswordHash is null)
            return Result<AuthResponse>.Failure("الحساب ده مسجل بـ Google/Guest ومالوش باسورد يتغيّر");

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result<AuthResponse>.Failure("كلمة المرور الحالية غير صحيحة");

        if (!IsValidPassword(request.NewPassword))
            return Result<AuthResponse>.Failure($"كلمة المرور الجديدة لازم تكون {MinPasswordLength} حروف على الأقل");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);

        // نلغي كل الجلسات التانية (أي جهاز/متصفح تاني داخل بيه الحساب) - وهنصدر توكن جديد
        // للجهاز الحالي بس عشان المستخدم يفضل داخل من غير ما يضطر يسجل دخول تاني دلوقتي
        await _refreshTokenRepository.RevokeAllActiveForUserAsync(userId, _dateTimeProvider.UtcNow, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return await IssueTokensAsync(user, ct);
    }

    // ---------- Email Verification ----------

    private async Task TrySendVerificationEmailAsync(User user, CancellationToken ct)
    {
        try
        {
            await SendVerificationEmailAsync(user.Id, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VERIFICATION EMAIL FAILED] {user.Email}: {ex.Message}");
        }
    }

    public async Task<Result> SendVerificationEmailAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null || user.Email is null || user.AuthProvider != AuthProviders.Email)
            return Result.Failure("مفيش إيميل مرتبط بالحساب ده يحتاج تحقق");

        if (user.EmailVerifiedAt is not null)
            return Result.Success(); // متحقق منه بالفعل - مفيش داعي نبعت تاني

        var otpPlain = Random.Shared.Next(100000, 999999).ToString();

        await _emailVerificationOtpRepository.DeleteAllUsableForUserAsync(user.Id, ct);

        var otp = new EmailVerificationOtp
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Otphash = _hashGenerator.Hash(otpPlain),
            ExpiresAt = _dateTimeProvider.UtcNow.AddMinutes(15),
            Attempts = 0,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _emailVerificationOtpRepository.AddAsync(otp, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var subject = "تأكيد البريد الإلكتروني - ElectroWorld";
        var body = $"""
            <p>كود تأكيد البريد الإلكتروني بتاعك هو:</p>
            <h2>{otpPlain}</h2>
            <p>الكود ده صالح لمدة 15 دقيقة.</p>
            """;

        await _emailSender.SendAsync(user.Email, subject, body, ct);

        return Result.Success();
    }

    public async Task<Result<AuthResponse>> VerifyEmailAsync(Guid userId, VerifyEmailRequest request, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<AuthResponse>.Failure("المستخدم غير موجود");

        // متحقق منه بالفعل - بنرجّع توكنات جديدة برضو (Idempotent) بدل Failure، عشان لو الفلاتر
        // نادت على الـ Endpoint ده تاني بالغلط (مثلاً بعد إعادة إرسال الطلب)، تفضل تاخد رد ناجح ومتسق
        if (user.EmailVerifiedAt is not null)
            return await IssueTokensAsync(user, ct);

        var otp = await _emailVerificationOtpRepository.GetLatestUsableForUserAsync(userId, ct);
        if (otp is null || otp.Attempts >= 5)
            return Result<AuthResponse>.Failure("الكود منتهي أو غير صالح، اطلب كود جديد");

        if (otp.Otphash != _hashGenerator.Hash(request.Otp))
        {
            otp.Attempts += 1;
            await _unitOfWork.SaveChangesAsync(ct);
            return Result<AuthResponse>.Failure("الكود غير صحيح");
        }

        otp.VerifiedAt = _dateTimeProvider.UtcNow;
        user.EmailVerifiedAt = _dateTimeProvider.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);

        // بنصدر Tokens جديدة هنا (بدل Result.Success العادي) عشان الـ Access Token القديم كان فيه
        // Claim emailVerified=false، ولو سبناه زي ما هو المستخدم هيفضل ممنوع من الـ Endpoints المحمية
        // لحد ما التوكن القديم ينتهي (شوف VerifiedEmailAuthorizationHandler)
        return await IssueTokensAsync(user, ct);
    }

    // ---------- Delete Account ----------

    public async Task<Result> DeleteAccountAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct);
        if (user is null)
            return Result.Failure("المستخدم غير موجود");

        // لازم نمسح ParentChildLinks الأول - جانب الـ Child مافيهوش Cascade في الداتابيز،
        // فمسح اليوزر هيتعمله Fail بـ FK Violation لو سيبناها زي ما هي
        await _userRepository.RemoveParentChildLinksForUserAsync(userId, ct);

        // RefreshTokens و PasswordResetOTPs و EmailVerificationOTPs بتتمسح تلقائي
        // (ON DELETE CASCADE في الداتابيز) - مش محتاجين نمسحها يدويًا هنا
        _userRepository.Remove(user);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------- Helper ----------

    private async Task<Result<AuthResponse>> IssueTokensAsync(User user, CancellationToken ct)
    {
        var (accessToken, expiresAt) = _jwtTokenGenerator.GenerateAccessToken(
            user.Id, user.Role, user.AuthProvider, user.EmailVerifiedAt is not null);
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

        var age = BirthDateRules.CalculateAge(user.BirthDate, DateOnly.FromDateTime(_dateTimeProvider.UtcNow));

        var response = new AuthResponse(
            user.Id, user.FullName, user.Role, user.AuthProvider, age, user.EmailVerifiedAt is not null,
            accessToken, refreshTokenPlain, expiresAt);

        return Result<AuthResponse>.Success(response);
    }
}
