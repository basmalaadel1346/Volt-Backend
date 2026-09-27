using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using AIIntegration;
using AssessmentBL;
using ContentBL;
using ElectroWorld.Api;
using ElectroWorld.Authorization;
using ElectroWorld.BackgroundJobs;
using ElectroWorld.Middleware;
using ElectroWorld.Realtime;
using ElectroWorld.Swagger;
using GamificationBL;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using Shared;
using Shared.Common.Api;
using Shared.Common.BackgroundWork;
using Shared.Common.Realtime;
using Shared.Users;

using UsersBL;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------

builder.Services.AddControllers(options =>
{
    // كل الـ API responses تكون JSON
    options.Filters.Add(new ProducesAttribute("application/json"));
})
.ConfigureApiBehaviorOptions(options =>
{
    // توحيد أخطاء Model Validation داخل ApiResponse
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors)
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToList();

        var message = errors.Count > 0
            ? string.Join(" - ", errors)
            : "بيانات الطلب غير صحيحة";

        return new BadRequestObjectResult(ApiResponse.Fail(message));
    };
})
.AddJsonOptions(options =>
{
    // عدم إرسال properties التي قيمتها null
    options.JsonSerializerOptions.DefaultIgnoreCondition =
        JsonIgnoreCondition.WhenWritingNull;
})
.AddUnifiedErrorEnvelope();

// ---------- SignalR ----------

builder.Services.AddSignalR();

// ---------- Output Cache ----------

builder.Services.AddOutputCache(options =>
{
    options.AddPolicy(
        ResponseCachingPolicies.PublicContent,
        policy => policy
            .Cache()
            .Expire(ResponseCachingPolicies.PublicContentDuration)
            .SetVaryByHeader("Accept-Language")
            .SetVaryByQuery(
                "language",
                "levelId",
                "lessonId",
                "isActive",
                "pageNumber",
                "pageSize",
                "quizType"));

    options.AddPolicy(
        ResponseCachingPolicies.PerLearner,
        policy => policy
            .Cache()
            .Expire(ResponseCachingPolicies.PerLearnerDuration)
            .SetVaryByHeader("Authorization", "Accept-Language")
            .SetVaryByQuery("language"));
});

// ---------- Swagger ----------

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
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Responses عامة مثل 401 / 403 / 500
    options.OperationFilter<DefaultApiResponsesOperationFilter>();

    // الـ Examples الموجودة على الـ endpoints
    options.OperationFilter<ResponseExamplesOperationFilter>();

    // إظهار [ADMIN] في Swagger للـ Admin endpoints
    options.OperationFilter<AdminEndpointLabelOperationFilter>();

    // توحيد الـ media type إلى application/json
    options.OperationFilter<JsonOnlyOperationFilter>();
});

// ---------- Modules ----------

builder.Services.AddShared(builder.Configuration);

builder.Services.AddUsersModule(builder.Configuration);

builder.Services.AddContentModule(builder.Configuration);

// Modules الخاصة بصاحبتك
builder.Services.AddAssessmentModule(builder.Configuration);
builder.Services.AddGamificationModule(builder.Configuration);
builder.Services.AddAiIntegration(builder.Configuration);

// ---------- Background Services ----------

// تنظيف الـ expired tokens
builder.Services.AddHostedService<
    ElectroWorld.BackgroundServices.ExpiredTokensCleanupService>();

// ---------- SignalR / Learner Notifications ----------

builder.Services.AddSingleton<
    ILearnerNotifier,
    SignalRLearnerNotifier>();

// ---------- AI Attempt Follow-up Queue ----------

builder.Services.AddSingleton<AttemptFollowUpQueue>();

builder.Services.AddSingleton<IAttemptFollowUpQueue>(
    sp => sp.GetRequiredService<AttemptFollowUpQueue>());

builder.Services.AddHostedService<AttemptFollowUpWorker>();

// ---------- Assessment Background Jobs ----------

// تحويل المحاولات القديمة من InProgress إلى Abandoned
builder.Services.AddHostedService<AbandonedQuizAttemptSweeper>();

// تقييم Essay answers بواسطة AI
builder.Services.AddHostedService<EssayEvaluationWorker>();

// ---------- JWT ----------

var jwtSettings =
    builder.Configuration
        .GetSection(JwtSettings.SectionName)
        .Get<JwtSettings>()
    ?? throw new InvalidOperationException(
        "Jwt section is missing from appsettings.json");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // مهم عشان نقرأ الـ claims بنفس الأسماء الموجودة في الـ JWT
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,

            IssuerSigningKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };

        // SignalR WebSocket لا يستطيع إرسال Authorization header
        // لذلك يسمح بقراءة access_token من Query String للـ /hubs فقط.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken =
                    context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path
                        .StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// ---------- Authorization ----------

// Handler الخاص بالـ Email Verification
builder.Services.AddSingleton<
    IAuthorizationHandler,
    VerifiedEmailAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    // أي [Authorize] عادي يحتاج:
    // 1. المستخدم يكون Authenticated
    // 2. لو AuthProvider = Email لازم يكون Verified
    options.DefaultPolicy =
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new VerifiedEmailRequirement())
            .Build();

    // Policy تسمح للمستخدم المسجل بالدخول
    // بدون شرط Email Verification
    // لاستخدام Verify / Resend Verification
    options.AddPolicy(
        "AuthenticatedOnly",
        policy => policy.RequireAuthenticatedUser());
});

// ---------- Rate Limiting ----------

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    // Login:
    // 5 محاولات لكل IP في الدقيقة
    options.AddPolicy(
        "LoginPolicy",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey:
                    httpContext.Connection.RemoteIpAddress?
                        .ToString() ?? "unknown",
                factory: _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

    // Register / Google / Convert Guest:
    // 10 محاولات لكل IP في الدقيقة
    options.AddPolicy(
        "AuthPolicy",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey:
                    httpContext.Connection.RemoteIpAddress?
                        .ToString() ?? "unknown",
                factory: _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

    // OTP:
    // 3 محاولات لكل IP كل 5 دقائق
    options.AddPolicy(
        "OtpPolicy",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey:
                    httpContext.Connection.RemoteIpAddress?
                        .ToString() ?? "unknown",
                factory: _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    }));
});

var app = builder.Build();

// ---------- Pipeline ----------

// لازم يكون مبكر في الـ Pipeline
// عشان يمسك الـ Exceptions اللي تحصل بعده.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger
//if (app.Environment.IsDevelopment())
//{
app.UseSwagger();
app.UseSwaggerUI();
//}

app.UseHttpsRedirection();

app.UseStaticFiles();

// Rate Limiting
app.UseRateLimiter();

// Authentication
app.UseAuthentication();

// Authorization
app.UseAuthorization();

// Output Cache
// بعد Authentication و Authorization
app.UseOutputCache();

// Controllers
app.MapControllers();

// SignalR Hub
app.MapHub<LearnerHub>("/hubs/learner");

app.Run();