using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ElectroWorld.Swagger;
using ElectroWorld.Authorization;
using ElectroWorld.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Common.Api;
using Shared.Users;
using UsersBL;
using ContentBL;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // بيلف أخطاء الـ Model Validation (زي [Required]/[StringLength] اللي هنضيفها على الـ DTOs)
        // في نفس شكل ApiResponse الموحّد، بدل الشكل الافتراضي (ProblemDetails) اللي مختلف
        // عن باقي الأخطاء في المشروع كله
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .ToList();

            var message = errors.Count > 0 ? string.Join(" - ", errors) : "بيانات الطلب غير صحيحة";
            return new BadRequestObjectResult(ApiResponse.Fail(message));
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "اكتب التوكن هنا"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // بيحط الـ Example الحقيقي (المأخوذ من رسائل الكود نفسها) على كل Response موثقة بـ [SwaggerExample]
    options.OperationFilter<ResponseExamplesOperationFilter>();

    // بيضيف "[ADMIN]" في بداية الـ Summary لأي Endpoint عليه [Authorize(Roles = "Admin")] -
    // توثيق بصري بس، من غير ما يغيّر الـ Grouping أو يكرر الـ Endpoint في مكان تاني
    options.OperationFilter<AdminEndpointLabelOperationFilter>();
});

builder.Services.AddShared(builder.Configuration);
builder.Services.AddUsersModule(builder.Configuration);
builder.Services.AddContentModule(builder.Configuration);
builder.Services.AddHostedService<ElectroWorld.BackgroundServices.ExpiredTokensCleanupService>();

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is missing from appsettings.json");

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // من غير السطر ده، .NET بيحول أسماء الـ Claims القياسية (زي "sub") لأسماء تانية
        // (ClaimTypes.NameIdentifier) تلقائيًا، وده بيلخبط قراءة الـ Claims. بنسيبها زي
        // ما إحنا كتبناها بالظبط في JwtTokenGenerator.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };
    });

builder.Services.AddSingleton<IAuthorizationHandler, VerifiedEmailAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    // الـ Policy الافتراضية بتاعت أي [Authorize] عادي (من غير Policy/Roles) - بتتطلب مستخدم
    // مسجّل دخول، وكمان (لو AuthProvider == Email) إنه يكون أكّد إيميله. بما إن [Authorize(Roles = "Admin")]
    // من غير Policy صريحة بتورّث الـ DefaultPolicy وتضيف عليها شرط الـ Role، الشرط ده بيتطبق
    // تلقائي على كل الـ Endpoints المحمية الحالية (Users + Content) من غير ما نلمس ولا كونترولر.
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new VerifiedEmailRequirement())
        .Build();

    // Policy أضعف: مستخدم مسجّل دخول بس، من غير شرط التحقق من الإيميل - مخصصة للـ 2 Endpoint
    // اللي المستخدم غير الموثّق نفسه محتاج يستخدمهم عشان يخلّص التحقق (Verify/Resend).
    options.AddPolicy("AuthenticatedOnly", policy => policy.RequireAuthenticatedUser());
});

// ---------- Rate Limiting ----------
// بيحمي من محاولات تخمين الباسورد (Brute Force) وإغراق الإيميلات بالـ OTP (Spam) -
// التقسيم بيتم حسب IP بتاع الجهاز اللي بيبعت الطلب.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Login: 5 محاولات كل دقيقة لكل IP - كافية لأي استخدام حقيقي، وبتوقف أي Brute Force
    options.AddPolicy("LoginPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Register/Google/Convert-Guest: 10 محاولات كل دقيقة لكل IP - بتمنع إنشاء حسابات بالجملة
    options.AddPolicy("AuthPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // أي حاجة بتبعت أو تتحقق من OTP (Forgot Password, Verify Email...): 3 محاولات كل 5 دقايق
    // لكل IP - بتمنع إغراق إيميل حد بمئات الرسايل
    options.AddPolicy("OtpPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// ---------- Pipeline ----------
// لازم يكون أول حاجة في الـ Pipeline عشان يلف أي Exception يحصل في أي Middleware بعده
app.UseMiddleware<ExceptionHandlingMiddleware>();

//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseHttpsRedirection();

app.UseStaticFiles(); // عشان الصور اللي جوه wwwroot/uploads تبقى قابلة للوصول من رابط مباشر

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
