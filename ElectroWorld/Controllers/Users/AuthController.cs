using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shared.Common.Api;
using Shared.Users;
using UsersBL.DTOs;
using UsersBL.Interfaces;

namespace ElectroWorld.Controllers.Users;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IUserService _userService;

    public AuthController(IAuthService authService, IUserService userService)
    {
        _authService = authService;
        _userService = userService;
    }

    // ملحوظة: RegisterGuestAsync في الـ Service مفيهاش أي مسار فشل حاليًا (بتنجح دايمًا)،
    // فمفيش 400 موثّق هنا عمدًا - توثيق حالة مش ممكن تحصل فعليًا هيكون تضليل.
    [HttpPost("guest")]
    [EnableRateLimiting("AuthPolicy")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تم إنشاء حساب Guest بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed","role":"Child","authProvider":"Guest","age":null,"isEmailVerified":false,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    public async Task<IActionResult> RegisterGuest([FromBody] RegisterGuestRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterGuestAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم إنشاء حساب Guest بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    /// <summary>تسجيل حساب جديد من الصفر بس - لتحويل حساب Guest موجود استخدمي
    /// /api/auth/convert-guest/email بدل كده (لازم توكن الـ Guest).</summary>
    [HttpPost("register")]
    [EnableRateLimiting("AuthPolicy")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم التسجيل بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"isEmailVerified":false,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"البريد الإلكتروني مستخدم بالفعل","data":null}""")]
    public async Task<IActionResult> Register([FromBody] RegisterEmailRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterWithEmailAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم التسجيل بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    [HttpPost("login")]
    [EnableRateLimiting("LoginPolicy")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تسجيل الدخول بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"isEmailVerified":true,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(401, """{"success":false,"message":"بيانات الدخول غير صحيحة","data":null}""")]
    public async Task<IActionResult> Login([FromBody] LoginEmailRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginWithEmailAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم تسجيل الدخول بنجاح"))
            : Unauthorized(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    /// <summary>دخول أو تسجيل حساب جديد بـ Google - لتحويل حساب Guest موجود استخدمي
    /// /api/auth/convert-guest/google بدل كده (لازم توكن الـ Guest).</summary>
    [HttpPost("google")]
    [EnableRateLimiting("AuthPolicy")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم الدخول عن طريق Google بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Google","age":null,"isEmailVerified":true,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"الإيميل ده مسجل بالفعل بحساب تاني، سجّل دخول بالإيميل والباسورد بدل كده","data":null}""")]
    public async Task<IActionResult> GoogleAuth([FromBody] GoogleAuthRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginOrRegisterWithGoogleAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم الدخول عن طريق Google بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    /// <summary>بتحوّل حساب Guest الحالي لحساب بإيميل، بنفس الـ Id ونفس التقدم. لازم تبعتي
    /// توكن الـ Guest نفسه في الـ Header (Authorization: Bearer ...) - الـ UserId بياخده
    /// السيرفر من التوكن مش من الـ Body، عشان محدش يقدر يربط تقدم حساب Guest حد تاني بحسابه.</summary>
    [HttpPost("convert-guest/email")]
    [Authorize]
    [EnableRateLimiting("AuthPolicy")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تحويل الحساب بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Child","authProvider":"Email","age":10,"isEmailVerified":false,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"حساب الـ Guest غير موجود أو اتحول قبل كده","data":null}""")]
    public async Task<IActionResult> ConvertGuestToEmail([FromBody] ConvertGuestToEmailRequest request, CancellationToken ct)
    {
        var guestUserId = User.GetUserId();
        var result = await _authService.ConvertGuestToEmailAsync(guestUserId, request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم تحويل الحساب بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    /// <summary>بتحوّل حساب Guest الحالي لحساب Google - نفس فكرة convert-guest/email بالظبط.</summary>
    [HttpPost("convert-guest/google")]
    [Authorize]
    [EnableRateLimiting("AuthPolicy")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تحويل الحساب بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Child","authProvider":"Google","age":null,"isEmailVerified":true,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"حساب الـ Guest غير موجود أو اتحول قبل كده","data":null}""")]
    public async Task<IActionResult> ConvertGuestToGoogle([FromBody] ConvertGuestToGoogleRequest request, CancellationToken ct)
    {
        var guestUserId = User.GetUserId();
        var result = await _authService.ConvertGuestToGoogleAsync(guestUserId, request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم تحويل الحساب بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تجديد التوكن بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"isEmailVerified":true,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(401, """{"success":false,"message":"Refresh Token غير صالح أو منتهي أو مستخدم بالفعل","data":null}""")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshTokenAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم تجديد التوكن بنجاح"))
            : Unauthorized(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    // ملحوظة: الـ 401 هنا لو الـ Token نفسه (Access Token في الـ Header) غير صالح/منتهي -
    // ده بيرجع من الـ [Authorize] Middleware قبل ما يوصل لكودنا، فمفيش Body ليه (موثّق بالـ Status Code لوحده تحت).
    [HttpPost("logout")]
    [Authorize(Policy = "AuthenticatedOnly")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تسجيل الخروج بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"Token غير موجود"}""")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _authService.LogoutAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم تسجيل الخروج بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }

    // ملحوظة: بترجع 200 دايمًا حتى لو الإيميل مش مسجل خالص (User Enumeration Protection) -
    // مفيش مسار فشل، فمفيش 400 موثّق هنا عمدًا.
    [HttpPost("forgot-password")]
    [EnableRateLimiting("OtpPolicy")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"لو الإيميل مسجل، هيوصلك كود إعادة التعيين"}""")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await _authService.ForgotPasswordAsync(request, ct);
        return Ok(ApiResponse.Ok("لو الإيميل مسجل، هيوصلك كود إعادة التعيين"));
    }

    [HttpPost("verify-reset-otp")]
    [EnableRateLimiting("OtpPolicy")]
    [ProducesResponseType(typeof(ApiResponse<VerifyResetOtpResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<VerifyResetOtpResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"الكود صحيح","data":{"resetToken":"xY9k2mP7qzR4tL...","resetTokenExpiresAt":"2026-09-10T12:10:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"الكود منتهي أو غير صالح، اطلب كود جديد","data":null}""")]
    public async Task<IActionResult> VerifyResetOtp([FromBody] VerifyResetOtpRequest request, CancellationToken ct)
    {
        var result = await _authService.VerifyResetOtpAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<VerifyResetOtpResponse>.Ok(result.Value!, "الكود صحيح"))
            : BadRequest(ApiResponse<VerifyResetOtpResponse>.Fail(result.Error!));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("OtpPolicy")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم تغيير كلمة المرور بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"جلسة إعادة التعيين منتهية أو غير صالحة، ابدأ من كود جديد"}""")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _authService.ResetPasswordAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم تغيير كلمة المرور بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }

    /// <summary>بتغيّر الباسورد وهو داخل بحسابه (مش ناسيه) - لازم يبعت الباسورد الحالي عشان
    /// نتأكد إنه هو فعلًا (Session Hijacking Protection)، حتى لو معاه Access Token صالح.
    /// بتلغي كل الجلسات التانية وتديله توكن جديد للجهاز الحالي.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("AuthPolicy")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تغيير كلمة المرور بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"isEmailVerified":true,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"كلمة المرور الحالية غير صحيحة","data":null}""")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _authService.ChangePasswordAsync(userId, request, ct);

        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم تغيير كلمة المرور بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    /// <summary>بتبعت كود تحقق جديد للإيميل المسجّل بالحساب الحالي (من التوكن). بتتبعت تلقائي
    /// أول ما تسجّلي بإيميل، الـ Endpoint ده لو الكود ضاع أو خلصت صلاحيته وعايزة واحد جديد.</summary>
    [HttpPost("resend-verification-email")]
    [Authorize(Policy = "AuthenticatedOnly")]
    [EnableRateLimiting("OtpPolicy")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم إرسال كود التحقق"}""")]
    [SwaggerExample(400, """{"success":false,"message":"مفيش إيميل مرتبط بالحساب ده يحتاج تحقق"}""")]
    public async Task<IActionResult> ResendVerificationEmail(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _authService.SendVerificationEmailAsync(userId, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم إرسال كود التحقق"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }

    /// <summary>بتتأكد من كود التحقق اللي وصل للإيميل بتاع الحساب الحالي (من التوكن)، وبترجع Tokens
    /// جديدة (بالـ Claim emailVerified=true) - لازم الفلاتر تستبدل التوكن القديم بالجديد ده فورًا،
    /// عشان التوكن القديم هيفضل يرجع 403 على الـ Endpoints المحمية لحد ما ينتهي.</summary>
    [HttpPost("verify-email")]
    [Authorize(Policy = "AuthenticatedOnly")]
    [EnableRateLimiting("OtpPolicy")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تأكيد الإيميل بنجاح","data":{"userId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","fullName":"Ahmed Ali","role":"Parent","authProvider":"Email","age":35,"isEmailVerified":true,"accessToken":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...","refreshToken":"cXVpY2tSZWZyZXNoVG9rZW5WYWx1ZUV4YW1wbGU=","accessTokenExpiresAt":"2026-09-10T12:15:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"الكود غير صحيح","data":null}""")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _authService.VerifyEmailAsync(userId, request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<AuthResponse>.Ok(result.Value!, "تم تأكيد الإيميل بنجاح"))
            : BadRequest(ApiResponse<AuthResponse>.Fail(result.Error!));
    }

    /// <summary>
    /// لإدخال تاريخ الميلاد بعد تسجيل Google (اللي مبيدخلش تاريخ الميلاد وقت التسجيل نفسه) -
    /// الفلاتر بتعرض شاشة بعد الـ Google Sign-In مباشرة وتنادي على الـ Endpoint ده.
    /// الـ UserId بياخده من الـ Access Token في الـ Header، مش من الـ Body.
    /// </summary>
    [HttpPatch("birth-date")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserProfileResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [SwaggerExample(200, """{"success":true,"message":"تم تحديث تاريخ الميلاد بنجاح","data":{"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","email":"ahmed@example.com","fullName":"Ahmed Ali","role":"Child","authProvider":"Google","age":12,"isEmailVerified":true,"isActive":true,"convertedFromGuestAt":null,"createdAt":"2026-08-01T10:00:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"السن لازم يكون بين 6 و 14 سنة","data":null}""")]
    public async Task<IActionResult> SetBirthDate([FromBody] SetBirthDateRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId(); // من التوكن، مش من الفلاتر
        var result = await _userService.SetBirthDateAsync(userId, request, ct);

        return result.IsSuccess
            ? Ok(ApiResponse<UserProfileResponse>.Ok(result.Value!, "تم تحديث تاريخ الميلاد بنجاح"))
            : BadRequest(ApiResponse<UserProfileResponse>.Fail(result.Error!));
    }
}
