using AssessmentBL.DTOs.Quiz;
using AssessmentBL.DTOs.Quiz.Common;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// Admin: authoring quizzes. A quiz has two independent states — its CONTENT
/// (title, questions) and whether it is PUBLISHED — and this controller keeps
/// them separate: create and edit freely, publish when ready.
/// </summary>
[ApiController]
[Route("api/Admin/quizzes")]
[Authorize(Roles = "Admin")]
public class QuizController : ControllerBase
{
    private const string QuizExample = @"{
      ""id"": 15,
      ""title"": ""اختبار الدائرة الكهربية"",
      ""description"": null,
      ""quizType"": ""LessonQuiz"",
      ""lessonId"": 5,
      ""isActive"": false,
      ""createdAt"": ""2026-09-10T10:00:00""
    }";

    private readonly IQuizService _quizService;

    public QuizController(IQuizService quizService)
    {
        _quizService = quizService;
    }

    /// <summary>Admin: one quiz, draft or published.</summary>
    [HttpGet("{quizId:int}")]
    [ProducesResponseType(typeof(QuizResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, QuizExample)]
    public async Task<ActionResult<QuizResponseDto>> GetById(int quizId, CancellationToken ct)
    {
        var quiz = await _quizService.GetByIdAsync(quizId, ct);
        return Ok(quiz);
    }

    /// <summary>Admin: browse quizzes, filtered and paged.</summary>
    /// <remarks>
    /// ?isActive=false lists the drafts, ?isActive=true the published ones, and
    /// omitting it lists both. quizType is case-insensitive.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<QuizResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<QuizResponseDto>>> Get([FromQuery] QuizFilterDto filter, CancellationToken ct)
    {
        var result = await _quizService.GetAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>Admin: creates a quiz as a DRAFT.</summary>
    /// <remarks>
    /// A new quiz is never live: it comes back with isActive = false and no child
    /// can reach it. Publish it with PATCH /api/quizzes/{id}/active when it has
    /// its questions.
    ///
    /// Creation used to publish immediately, which collided with "only one active
    /// placement test" and "only one active quiz per lesson" — so preparing a
    /// replacement meant taking the live one down FIRST and leaving children with
    /// no quiz while the new one was written. Drafts take no slot, so any number
    /// of them can be prepared alongside the quiz that is running.
    ///
    /// quizType is case-insensitive ("lessonquiz" = "LessonQuiz") and must match
    /// the reference sent with it: LevelAssessment and LevelSkip take a levelId,
    /// LessonQuiz and LessonReview a lessonId, Standalone and Placement neither.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(QuizResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(201, QuizExample)]
    [SwaggerExample(400, @"{""success"":false,""message"":""نوع الاختبار 'LessonQuiz' لا يتوافق مع LevelId/LessonId المرسلة"",""data"":null}")]
    public async Task<ActionResult<QuizResponseDto>> Create([FromBody] CreateQuizDto request, CancellationToken ct)
    {
        var quiz = await _quizService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { quizId = quiz.Id }, quiz);
    }

    /// <summary>Admin: replaces a quiz's title, description and published state.</summary>
    /// <remarks>
    /// quizType, levelId and lessonId are deliberately absent: a quiz cannot be
    /// re-pointed at another lesson or level after it exists, because attempts
    /// already reference it.
    ///
    /// Setting isActive = true here publishes it exactly as PATCH .../active does,
    /// retiring whichever quiz held the slot.
    /// </remarks>
    [HttpPut("{quizId:int}")]
    [ProducesResponseType(typeof(QuizResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QuizResponseDto>> Update(int quizId, [FromBody] UpdateQuizDto request, CancellationToken ct)
    {
        var quiz = await _quizService.UpdateAsync(quizId, request, ct);
        return Ok(quiz);
    }

    /// <summary>Admin: publishes a draft, or takes a running quiz back to draft.</summary>
    /// <remarks>
    /// This is where the "only one active" rule applies — one placement test
    /// overall, one quiz per lesson, one level-skip challenge per level — and it
    /// applies by SWAPPING, not by refusing: publishing a draft automatically
    /// moves the quiz that held the slot back to draft, in the same transaction.
    /// The admin never has to stop the old one first, and there is never an
    /// instant with two live quizzes or none.
    ///
    /// Attempts already in progress on the retired quiz are unaffected: their
    /// questions and answer key were frozen when they started.
    ///
    /// 409 only when another admin published into the same slot at the same
    /// instant — nothing was saved, so publishing again succeeds.
    /// </remarks>
    [HttpPatch("{quizId:int}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(409, @"{""success"":false,""message"":""تم تفعيل اختبار آخر لنفس الغرض في نفس اللحظة، برجاء إعادة المحاولة"",""data"":null}")]
    public async Task<IActionResult> SetActive(int quizId, [FromQuery] bool isActive, CancellationToken ct)
    {
        await _quizService.SetActiveAsync(quizId, isActive, ct);
        return NoContent();
    }
}
