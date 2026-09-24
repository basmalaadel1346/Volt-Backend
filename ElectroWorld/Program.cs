using AIIntegration;
using AssessmentBL;
using ContentBL;
using ElectroWorld.Api;
using ElectroWorld.BackgroundJobs;
using ElectroWorld.Middleware;
using ElectroWorld.Realtime;
using GamificationBL;
using Microsoft.AspNetCore.Mvc;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Shared;
using Shared.Common.BackgroundWork;
using Shared.Common.Realtime;
using Shared.Users;
using System.Text;
using System.Text.Json.Serialization;
using UsersBL;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------
builder.Services.AddControllers(options =>
{
    // Global application/json for every response. Applied as a filter rather
    // than per-action so no endpoint can drift.
    // NOTE: ConsumesAttribute is deliberately NOT added globally — it would make
    // the multipart image-upload endpoint return 415.
    options.Filters.Add(new ProducesAttribute("application/json"));
})
.AddJsonOptions(options =>
{
    // A null field carries no information, so it is left out of the response
    // entirely rather than serialized as "field": null. Smaller payloads on a
    // phone connection, and a client reading `if ('x' in json)` sees the same
    // thing a client reading `json.x != null` does.
    //
    // Clients must therefore treat an ABSENT field exactly like a null one. Every
    // collection is still serialized (an empty list is a fact, not an absence),
    // and no non-nullable field can disappear.
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
})
// Replaces ASP.NET Core's ValidationProblemDetails with the API's own envelope,
// so a malformed body cannot produce a response shape no client expects.
.AddUnifiedErrorEnvelope();

// The learner's live channel: background AI work announces itself instead of
// being polled for.
builder.Services.AddSignalR();

// Caching for the shared, non-personal reads only — see ResponseCachingPolicies.
builder.Services.AddOutputCache(options =>
{
    // .Cache() is REQUIRED here, not decoration: the built-in default policy
    // refuses to cache any request that carries an Authorization header, and
    // every endpoint in this API is authenticated — without it these policies
    // would silently cache nothing. The rest of the default policy is kept on
    // purpose, because it is what restricts storage to GET and to 200 responses.
    options.AddPolicy(ResponseCachingPolicies.PublicContent, policy => policy
        .Cache()
        .Expire(ResponseCachingPolicies.PublicContentDuration)
        // The catalogue is localized, so the language must be part of the key or
        // an Arabic response would be served to an English request.
        //
        // The caller is deliberately NOT part of the key: these responses are
        // identical for every learner (no endpoint under this policy reads the
        // user id), which is the only reason they may be shared at all.
        .SetVaryByHeader("Accept-Language")
        .SetVaryByQuery("language", "levelId", "lessonId", "isActive", "pageNumber", "pageSize", "quizType"));

    options.AddPolicy(ResponseCachingPolicies.PerLearner, policy => policy
        .Cache()
        .Expire(ResponseCachingPolicies.PerLearnerDuration)
        // Keyed on the caller's token: without this, one learner's cached wallet
        // would be served to the next learner who asked for the same URL.
        .SetVaryByHeader("Authorization", "Accept-Language")
        .SetVaryByQuery("language"));
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
        Description = "اكتب التوكن هنا."
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

    // Filter order matters and is deliberate:
    //  1. add the error responses every endpoint can return (401/403/500),
    //  2. overlay any per-action [SwaggerExample],
    //  3. strip every media type except application/json — must run LAST so it
    //     also normalizes whatever the first two added.
    options.OperationFilter<DefaultApiResponsesOperationFilter>();
    options.OperationFilter<ResponseExamplesOperationFilter>();
    options.OperationFilter<JsonOnlyOperationFilter>();
});

builder.Services.AddShared(builder.Configuration);
builder.Services.AddUsersModule(builder.Configuration);
builder.Services.AddContentModule(builder.Configuration);
builder.Services.AddAssessmentModule(builder.Configuration);
builder.Services.AddGamificationModule(builder.Configuration);
builder.Services.AddAiIntegration(builder.Configuration);

// The real transport for ILearnerNotifier, replacing the no-op SharedModule
// registers so the modules run without an API host.
builder.Services.AddSingleton<ILearnerNotifier, SignalRLearnerNotifier>();

// Where a submitted attempt's AI work goes, and what runs it. Registered as one
// instance behind both the contract and the worker's concrete dependency: the
// queue only works if the writer and the reader are the same object.
builder.Services.AddSingleton<AttemptFollowUpQueue>();
builder.Services.AddSingleton<IAttemptFollowUpQueue>(sp => sp.GetRequiredService<AttemptFollowUpQueue>());
builder.Services.AddHostedService<AttemptFollowUpWorker>();

// Moves quiz attempts left InProgress past Assessment:InProgressAttemptTimeoutMinutes to Abandoned.
builder.Services.AddHostedService<AbandonedQuizAttemptSweeper>();

// Evaluates essay answers with the AI when the submission itself could not, and
// closes the ones that have waited past the grading deadline.
builder.Services.AddHostedService<EssayEvaluationWorker>();

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

        // A browser WebSocket cannot send an Authorization header, so the hub's
        // handshake carries the token in the query string instead. Accepted for
        // the hub path ONLY — anywhere else a token in a URL would end up in logs
        // and proxy history.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ---------- Pipeline ----------
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseHttpsRedirection();
app.UseMiddleware<ExceptionMiddleware>();
app.UseStaticFiles(); // عشان الصور اللي جوه wwwroot/uploads تبقى قابلة للوصول من رابط مباشر

app.UseAuthentication();
app.UseAuthorization();

// AFTER authentication: the per-learner policy varies on the Authorization
// header, and a cache in front of authentication could serve a cached body to a
// request that was never entitled to it.
app.UseOutputCache();

app.MapControllers();
app.MapHub<LearnerHub>("/hubs/learner");

app.Run();
