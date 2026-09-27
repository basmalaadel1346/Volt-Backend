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
    // 104 (3 points) not graded yet: score = 2 ÷ 4 auto-graded points = 50%, and
    // earnedPoints = 2 of totalPoints 7, with 3 still pending.
    // Note the absent fields: a null is left out of every response in this API.
    private const string ResultExample = @"{
      ""attemptId"": 42,
      ""quizId"": 15,
      ""completedAt"": ""2026-09-10T18:42:10.123"",
      ""totalQuestions"": 4,
      ""autoGradedQuestions"": 3,
      ""pendingEssayQuestions"": 1,
      ""essayResults"": [
        { ""questionId"": 104, ""status"": ""Pending"", ""maxPoints"": 3 }
      ],
      ""correctAnswers"": 2,
      ""wrongAnswers"": 1,
      ""scorePercentage"": 50.00,
      ""totalPoints"": 7,
      ""earnedPoints"": 2,
      ""pendingPoints"": 3,
      ""language"": ""ar"",
      ""languageFallbackApplied"": false,
      ""hintsStatus"": ""Pending"",
      ""rewards"": {
        ""sparksEarned"": 5,
        ""sparksBalance"": 145,
        ""currentStreakDays"": 4,
        ""longestStreakDays"": 9,
        ""streakExtendedToday"": true,
        ""freezesSpent"": 0,
        ""freezesAvailable"": 1,
        ""lines"": [
          { ""reason"": ""QuizCompleted"", ""sparks"": 5, ""message"": ""‏+5 شرارة لإنهاء الاختبار"" }
        ]
      }
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

    /// <summary>Starts an attempt — or resumes the one already open. Safe to press twice.</summary>
    /// <remarks>
    /// First attempt: send only quizId.
    /// Retry: also send previousAttemptId, and only the questions that were wrong
    /// are served, each with its latest hint.
    ///
    /// A learner may have only ONE live attempt per quiz. A second start — a
    /// double-tapped button, an app retrying after a lost response — RESUMES the
    /// first instead of creating another, and says so with `resumed: true`. It
    /// used to create a second attempt, leaving the first orphaned InProgress
    /// until the sweep abandoned it, with only one of the two ever submittable.
    /// The database enforces this too, so even two simultaneous requests can only
    /// produce one attempt.
    ///
    /// The questions in THIS response are the frozen set the submission is graded
    /// against, with the points each one is worth in this attempt. A timed quiz
    /// also carries `expiresAt`, `timeLimitSeconds` and `hearts`.
    ///
    /// On a retry, `removedQuestionIds` and `notice` list the questions an admin
    /// has deactivated since — they are not in the retry, and the child is told so
    /// rather than silently handed a shorter quiz.
    ///
    /// A previousAttemptId that is not the caller's own is a 404, like a missing one.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(QuizAttemptResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(400, @"{""success"":false,""message"":""الاختبار رقم 15 لا يحتوي على أسئلة مفعّلة"",""data"":null}")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
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

    /// <summary>Submits the whole attempt and returns the saved score immediately.</summary>
    /// <remarks>
    /// The score is graded, committed and returned WITHOUT waiting for any AI. The
    /// AI work that belongs to a submission — hints for the wrong answers, grading
    /// of the essays — runs on a background worker afterwards, and the app is told
    /// it finished over the learner hub (/hubs/learner). This request used to wait
    /// up to 15 seconds on the AI for a score that had already been committed
    /// before the AI was called at all.
    ///
    /// So a fresh submission comes back with hintsStatus = "Pending" and every
    /// essay "Pending". Neither affects scorePercentage, which is final here and
    /// never changes.
    ///
    /// Send an answer for EVERY question — MCQ/TrueFalse in "answers", essays in
    /// "essayAnswers" — or the request is rejected with 400 listing the missing
    /// ones. ("answers" was called "mistakes": it always meant every answer, and
    /// the old name described the backend's storage rather than what the client
    /// sends.) Only the wrong MCQ/TrueFalse answers are stored.
    ///
    /// Each question weighs the points frozen when the attempt started.
    /// scorePercentage covers MCQ/TrueFalse only. Essays are graded afterwards:
    /// "Pending" in essayResults with their points in pendingPoints (ask
    /// GET .../result again, or wait for the essaysGraded push). "Graded" and
    /// "NotGraded" are final; a NotGraded essay earns no points and has no
    /// feedback.
    ///
    /// The retry questions are NOT here — GET .../retry-questions serves them,
    /// once their hints exist.
    ///
    /// `rewards` carries the Sparks this attempt earned and the streak after it.
    ///
    /// Safe to retry: submitting an already-completed attempt returns the saved
    /// result (200) without re-grading, re-rewarding or re-queuing anything.
    ///
    /// 404 means no such attempt among YOUR attempts.
    /// 409 means a concurrent request interfered and nothing was saved — retry.
    /// 410 means the attempt expired, or a timed quiz ran out — start a new one.
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
    /// Recovery path for a submit whose response never reached the app, and the
    /// way to pick up essay grades that landed afterwards. The attempt owner comes
    /// from the access token, never from the request.
    ///
    /// 200 — the saved result. Essay grades that arrived since the submit are in
    ///       essayResults and earnedPoints; scorePercentage is the one saved at
    ///       submit and never changes. hintsStatus says whether the hints are
    ///       written yet.
    /// 404 — no such attempt among YOUR attempts.
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

    /// <summary>The questions you got wrong in an attempt, each with its AI hint.</summary>
    /// <remarks>
    /// Its own endpoint because the hints are written AFTER the result is
    /// committed: folding them into the submit response is what used to make that
    /// request wait on the AI.
    ///
    /// Call it when the child taps "try again", or as soon as the hintsReady push
    /// arrives on /hubs/learner. hintsStatus says what to expect:
    ///   NotRequired — nothing was wrong.
    ///   Pending     — the AI is still writing; ask again, or wait for the push.
    ///   Generated   — every question has a hint.
    ///   Partial     — some do; the rest have currentHint absent.
    ///   Unavailable — no hint could be produced. The retry still works.
    ///
    /// Then start the retry with
    /// POST /api/quiz-attempts?quizId={quizId}&amp;previousAttemptId={attemptId}.
    ///
    /// Placement and level-skip attempts are never retried, so they always answer
    /// NotRequired with an empty list.
    /// </remarks>
    [HttpGet("{attemptId:long}/retry-questions")]
    [ProducesResponseType(typeof(RetryQuestionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status410Gone)]
    [SwaggerExample(200, @"{
      ""attemptId"": 42,
      ""quizId"": 15,
      ""hintsStatus"": ""Generated"",
      ""language"": ""ar"",
      ""languageFallbackApplied"": false,
      ""questions"": [
        {
          ""questionId"": 102,
          ""questionText"": ""ما وحدة قياس المقاومة الكهربية؟"",
          ""questionType"": ""MultipleChoice"",
          ""difficulty"": ""Medium"",
          ""displayOrder"": 2,
          ""points"": 2,
          ""currentHint"": ""افتكر إن الوحدة اسمها على اسم العالم الألماني."",
          ""options"": [
            { ""optionId"": 1004, ""optionText"": ""الأوم"", ""displayOrder"": 1 },
            { ""optionId"": 1005, ""optionText"": ""الفولت"", ""displayOrder"": 2 }
          ]
        }
      ]
    }")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, AttemptNotFoundExample)]
    [SwaggerExample(410, AbandonedExample)]
    public async Task<ActionResult<RetryQuestionsDto>> GetRetryQuestions(
        long attemptId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var retry = await _quizAttemptService.GetRetryQuestionsAsync(
            attemptId, userId, Request.ResolveContentLanguage(language), ct);
        return Ok(retry);
    }

    /// <summary>Returns the result of your most recently completed attempt.</summary>
    /// <remarks>
    /// No parameters: the user comes from the access token. "Latest" is the
    /// attempt submitted last (latest completedAt), of any quiz, placement
    /// included. Attempts still in progress or abandoned have no result and are
    /// skipped. The body is exactly what GET /api/quiz-attempts/{attemptId}/result
    /// returns for that attempt; use its attemptId for a retry.
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
    /// Each question's points are the ones frozen when the attempt started, and a
    /// timed attempt carries the deadline it started with — not a fresh window.
    /// 404 when the attempt is not one of YOUR attempts — missing and another
    /// user's are reported exactly the same way.
    /// </remarks>
    [HttpGet("{attemptId:long}")]
    [ProducesResponseType(typeof(QuizAttemptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
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
