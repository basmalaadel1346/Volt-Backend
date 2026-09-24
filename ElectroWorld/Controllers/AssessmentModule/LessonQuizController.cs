using AssessmentBL.DTOs.Quiz;
using AssessmentBL.Interfaces;
using ElectroWorld.Api;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Shared.Common.Api;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// Child-facing quiz lookup: which quiz belongs to a lesson, and which to a
/// level. Separate from QuizController on purpose: that one is Admin-only at
/// class level, and an action-level [Authorize] cannot relax a class-level role
/// requirement — both would apply.
/// </summary>
[ApiController]
[Route("api/quizzes")]
[Authorize]
public class LessonQuizController : ControllerBase
{
    private readonly IQuizService _quizService;

    public LessonQuizController(IQuizService quizService)
    {
        _quizService = quizService;
    }

    /// <summary>The quiz of a lesson: its title and how big it is.</summary>
    /// <remarks>
    /// Enough to draw the "start the quiz" card, and nothing more. The QUESTIONS
    /// are not here: they come from POST /api/quiz-attempts?quizId={quizId},
    /// which is the only place they are frozen, and grading is against that frozen
    /// set. Serving a second, unfrozen copy here invited the app to render one set
    /// and submit against another, and doubled the payload of a screen that only
    /// needs a title and a count.
    ///
    /// 404 when the lesson is unpublished, missing, or has no active quiz — a
    /// lesson a child cannot see is reported exactly like one that does not exist.
    /// </remarks>
    [HttpGet("for-lesson/{lessonId:int}")]
    [OutputCache(PolicyName = ResponseCachingPolicies.PublicContent)]
    [ProducesResponseType(typeof(LessonQuizResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, @"{
      ""quizId"": 15,
      ""lessonId"": 5,
      ""title"": ""اختبار الدائرة الكهربية"",
      ""totalQuestions"": 2,
      ""totalPoints"": 3,
      ""language"": ""ar"",
      ""languageFallbackApplied"": false
    }")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, @"{""success"":false,""message"":""لا يوجد اختبار متاح للدرس رقم 5"",""data"":null}")]
    public async Task<ActionResult<LessonQuizResponseDto>> GetForLesson(
        int lessonId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var quiz = await _quizService.GetForLessonAsync(lessonId, Request.ResolveContentLanguage(language), ct);
        return Ok(quiz);
    }

    /// <summary>The quiz of a LEVEL, by level id — "the child is on level 3, which quiz do I open?"</summary>
    /// <remarks>
    /// Returns the level's active LevelAssessment quiz. The app used to have to
    /// page through the admin quiz list and guess which one belonged to the level;
    /// this answers it in one call, with the same shape as the per-lesson lookup.
    ///
    /// Start it with POST /api/quiz-attempts?quizId={quizId}. Like the per-lesson
    /// lookup, the questions are served by that call, not by this one.
    ///
    /// 404 when the level does not exist, or has no active quiz with questions.
    /// </remarks>
    [HttpGet("for-level/{levelId:int}")]
    [OutputCache(PolicyName = ResponseCachingPolicies.PublicContent)]
    [ProducesResponseType(typeof(LevelQuizResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, @"{
      ""levelId"": 3,
      ""quizId"": 21,
      ""title"": ""تقييم المستوى الثالث"",
      ""totalQuestions"": 8,
      ""totalPoints"": 12,
      ""language"": ""ar"",
      ""languageFallbackApplied"": false
    }")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, @"{""success"":false,""message"":""لا يوجد اختبار متاح للمستوى رقم 3"",""data"":null}")]
    public async Task<ActionResult<LevelQuizResponseDto>> GetForLevel(
        int levelId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var quiz = await _quizService.GetForLevelAsync(levelId, Request.ResolveContentLanguage(language), ct);
        return Ok(quiz);
    }
}
