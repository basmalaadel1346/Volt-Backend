using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Shared.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Users;

namespace ElectroWorld.Api.Controllers;

[ApiController]
[Route("api/quiz-attempts")]
[Authorize]
public class QuizAttemptController : ControllerBase
{
    // Shared by Submit and GetResult: a result looks the same whichever of the
    // two returned it.
    // Questions 101 (1 point) and 103 (1 point) right, 102 (2 points) wrong, essay
    // 104 (3 points) graded 2: score = 2 ÷ 4 auto-graded points = 50%, and
    // earnedPoints = 2 + 2 of totalPoints 7.
    private const string ResultExample = @"{
      ""attemptId"": 42,
      ""quizId"": 15,
      ""completedAt"": ""2026-09-10T18:42:10.123"",
      ""totalQuestions"": 4,
      ""autoGradedQuestions"": 3,
      ""pendingEssayQuestions"": 0,
      ""essayResults"": [
        {
          ""questionId"": 104,
          ""status"": ""Graded"",
          ""awardedPoints"": 2,
          ""maxPoints"": 3,
          ""feedback"": ""إجابة جميلة! اذكر كمان إن الدائرة لازم تكون مقفولة.""
        }
      ],
      ""correctAnswers"": 2,
      ""wrongAnswers"": 1,
      ""scorePercentage"": 50.00,
      ""totalPoints"": 7,
      ""earnedPoints"": 4,
      ""pendingPoints"": 0,
      ""language"": ""ar"",
      ""languageFallbackApplied"": false,
      ""hintsStatus"": ""Generated"",
      ""retryQuestions"": [
        {
          ""questionId"": 102,
          ""questionText"": ""ما وحدة قياس المقاومة الكهربية؟"",
          ""questionType"": ""MultipleChoice"",
          ""imageUrl"": null,
          ""difficulty"": ""Medium"",
          ""displayOrder"": 2,
          ""points"": 2,
          ""currentHint"": ""افتكر إن الوحدة اسمها على اسم العالم الألماني."",
          ""options"": [
            { ""optionId"": 1004, ""optionText"": ""الأوم"", ""imageUrl"": null, ""displayOrder"": 1 },
            { ""optionId"": 1005, ""optionText"": ""الفولت"", ""imageUrl"": null, ""displayOrder"": 2 }
          ]
        }
      ]
    }";

    private const string AbandonedExample =
        @"{""success"":false,""message"":""المحاولة رقم 42 انتهت صلاحيتها قبل تسليمها، برجاء بدء محاولة جديدة"",""data"":null}";

    // Also what another user's attempt returns: it is never a 403, so a response
    // cannot confirm that someone else's attempt id exists.
    private const string AttemptNotFoundExample =
        @"{""success"":false,""message"":""المحاولة رقم 42 غير موجودة"",""data"":null}";

    private readonly IQuizAttemptService _quizAttemptService;
    private readonly IHintService _hintService;

    public QuizAttemptController(IQuizAttemptService quizAttemptService, IHintService hintService)
    {
        _quizAttemptService = quizAttemptService;
        _hintService = hintService;
    }

    // First attempt: only quizId is supplied.
    // Retry attempt: previousAttemptId is also supplied (service resolves
    // which questions to serve and attaches the latest hints). A
    // previousAttemptId that is not the caller's own is a 404, like a missing one.
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuizAttemptResponseDto>> Start(
        [FromQuery] int quizId,
        [FromQuery] long? previousAttemptId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var attempt = await _quizAttemptService.StartAsync(
            quizId, userId, previousAttemptId, Request.ResolveContentLanguage(language), ct);
        return CreatedAtAction(nameof(GetById), new { attemptId = attempt.AttemptId }, attempt);
    }

    /// <summary>Submits the whole attempt and returns the score, plus AI hints when available.</summary>
    /// <remarks>
    /// The score is saved and committed BEFORE any AI call. If the AI fails, times
    /// out or is not configured, this still returns 200 with the saved score and
    /// hintsStatus = "Unavailable".
    ///
    /// Every question must be answered — MCQ/TrueFalse in "mistakes", essays in
    /// "essayAnswers" — or the request is rejected with 400 listing the missing
    /// questions. Only the wrong MCQ/TrueFalse answers are stored.
    ///
    /// Each question weighs its points (1 unless the admin set another value),
    /// frozen when the attempt started. scorePercentage covers MCQ/TrueFalse only
    /// and never changes after this call. Essays are graded by the AI after the
    /// score is saved; an essay not graded yet shows as "Pending" in essayResults
    /// and its points in pendingPoints (ask GET .../result again later). "Graded"
    /// and "NotGraded" are final; a NotGraded essay (the AI declined or could not
    /// evaluate it) earns no points and has no feedback.
    ///
    /// Safe to retry: submitting an attempt that is already completed returns the
    /// saved result again (200) without re-grading it. If the response is lost,
    /// GET /api/quiz-attempts/{attemptId}/result returns the same result.
    ///
    /// 404 means no such attempt among YOUR attempts — another user's attempt is
    /// reported exactly the same way.
    /// 409 means a concurrent request interfered and nothing was saved — retry.
    /// 410 means the attempt expired unsubmitted — start a new one.
    /// </remarks>
    [HttpPost("{attemptId:long}/submit")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(QuizAttemptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status500InternalServerError)]
    [SwaggerExample(200, ResultExample)]
    [SwaggerExample(400, ApiResponseExamples.BadRequest)]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, AttemptNotFoundExample)]
    [SwaggerExample(409, @"{""success"":false,""message"":""تعذّر تسليم المحاولة رقم 42 بسبب طلب متزامن، برجاء إعادة المحاولة"",""data"":null}")]
    [SwaggerExample(410, AbandonedExample)]
    [SwaggerExample(500, ApiResponseExamples.ServerError)]
    public async Task<ActionResult<QuizAttemptResultDto>> Submit(
        long attemptId,
        [FromBody] SubmitQuizAttemptDto dto,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _quizAttemptService.SubmitAsync(
            attemptId, userId, dto, Request.ResolveContentLanguage(language), ct);
        return Ok(result);
    }

    /// <summary>Returns the saved result of your own submitted attempt.</summary>
    /// <remarks>
    /// Recovery path for a submit whose response never reached the app. The
    /// attempt owner comes from the access token, never from the request.
    ///
    /// 200 — the saved result (hints included if they were generated). Essay
    ///       grades that arrived since the submit are in essayResults and
    ///       earnedPoints; scorePercentage is the one saved at submit.
    /// 404 — no such attempt among YOUR attempts (another user's attempt is
    ///       reported exactly the same way).
    /// 409 — not submitted yet: submit it (safe to repeat).
    /// 410 — expired unsubmitted: start a new attempt.
    /// </remarks>
    [HttpGet("{attemptId:long}/result")]
    [ProducesResponseType(typeof(QuizAttemptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status410Gone)]
    [SwaggerExample(200, ResultExample)]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, AttemptNotFoundExample)]
    [SwaggerExample(409, @"{""success"":false,""message"":""المحاولة رقم 42 لم يتم تسليمها بعد، برجاء إرسال الإجابات"",""data"":null}")]
    [SwaggerExample(410, AbandonedExample)]
    public async Task<ActionResult<QuizAttemptResultDto>> GetResult(
        long attemptId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _quizAttemptService.GetResultAsync(
            attemptId, userId, Request.ResolveContentLanguage(language), ct);
        return Ok(result);
    }

    /// <summary>Returns the result of your most recently completed attempt.</summary>
    /// <remarks>
    /// No parameters: the user comes from the access token. "Latest" is the
    /// attempt submitted last (latest completedAt), of any quiz, placement
    /// included. Attempts still in progress or abandoned have no result and are
    /// skipped. The body is exactly what GET /api/quiz-attempts/{attemptId}/result
    /// returns for that attempt; use its attemptId for polling or a retry.
    /// Localized by Accept-Language (or an optional ?language=).
    ///
    /// 404 — you have not completed any attempt yet.
    /// </remarks>
    [HttpGet("latest")]
    [ProducesResponseType(typeof(QuizAttemptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, ResultExample)]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, @"{""success"":false,""message"":""لا توجد محاولات مكتملة بعد، أكمل اختبارًا لتظهر نتيجتك هنا"",""data"":null}")]
    public async Task<ActionResult<QuizAttemptResultDto>> GetLatestResult(
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _quizAttemptService.GetLatestResultAsync(
            userId, Request.ResolveContentLanguage(language), ct);
        return Ok(result);
    }

    /// <summary>Hint button: an escalating hint for one question of a live attempt.</summary>
    /// <remarks>
    /// First press returns a soft nudge, second press a more direct hint, third
    /// press is refused with 409. The level comes from the hints this attempt and
    /// question already have — it cannot be chosen by the caller.
    ///
    /// Neither level ever names the correct answer: a hint that would is dropped
    /// and the response comes back with hintsStatus "Partial" and no hint.
    /// "Partial" also covers an AI that failed or timed out; "Unavailable" means
    /// the hint AI is not configured or the question cannot be described to it.
    /// Neither one saves anything or uses up a level — hintsRemaining stays the
    /// same and the next press asks for the same level.
    ///
    /// 404 when the attempt is not one of YOUR attempts (missing or another
    /// user's — indistinguishable) or the question is not in it.
    /// 409 once the attempt is submitted, when every level is used, or when two
    /// presses for the same question arrive at once (only one keeps the level).
    /// 410 once the attempt has expired.
    /// </remarks>
    [HttpPost("{attemptId:long}/questions/{questionId:int}/hint")]
    [ProducesResponseType(typeof(HintResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status410Gone)]
    [SwaggerExample(200, @"{
      ""questionId"": 102,
      ""attemptNumber"": 1,
      ""hint"": ""افتكر إن الوحدة اسمها على اسم عالم ألماني."",
      ""hintsStatus"": ""Generated"",
      ""hintsRemaining"": 1,
      ""language"": ""ar""
    }")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, @"{""success"":false,""message"":""السؤال رقم 102 ليس ضمن المحاولة رقم 42"",""data"":null}")]
    [SwaggerExample(409, @"{""success"":false,""message"":""لا توجد تلميحات إضافية للسؤال رقم 102 في هذه المحاولة"",""data"":null}")]
    [SwaggerExample(410, AbandonedExample)]
    public async Task<ActionResult<HintResponseDto>> RequestHint(
        long attemptId,
        int questionId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var hint = await _hintService.RequestHintAsync(
            attemptId, questionId, userId, Request.ResolveContentLanguage(language), ct);
        return Ok(hint);
    }

    /// <summary>One of your attempts, with its questions and their latest hints.</summary>
    /// <remarks>
    /// Each question's points are the ones frozen when the attempt started.
    /// 404 when the attempt is not one of YOUR attempts — missing and another
    /// user's are reported exactly the same way.
    /// </remarks>
    [HttpGet("{attemptId:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [SwaggerExample(404, AttemptNotFoundExample)]
    public async Task<ActionResult<QuizAttemptResponseDto>> GetById(
        long attemptId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var attempt = await _quizAttemptService.GetByIdAsync(
            attemptId, userId, Request.ResolveContentLanguage(language), ct);
        return Ok(attempt);
    }
}
