using AssessmentBL.DTOs.Quiz;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// Child-facing quiz lookup. Separate from QuizController on purpose: that one is
/// Admin-only at class level, and an action-level [Authorize] cannot relax a
/// class-level role requirement — both would apply.
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

    /// <summary>The quiz of the lesson the child is learning, ready to preview.</summary>
    /// <remarks>
    /// Returns the active LessonQuiz of a published lesson with its active
    /// questions and options in display order. Correct answers and image
    /// descriptions are never included. To answer, start an attempt with
    /// POST /api/quiz-attempts?quizId={quizId}.
    /// </remarks>
    [HttpGet("for-lesson/{lessonId:int}")]
    [ProducesResponseType(typeof(LessonQuizResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, @"{
      ""quizId"": 15,
      ""lessonId"": 5,
      ""title"": ""اختبار الدائرة الكهربية"",
      ""description"": null,
      ""totalQuestions"": 2,
      ""language"": ""ar"",
      ""languageFallbackApplied"": false,
      ""questions"": [
        {
          ""questionId"": 101,
          ""questionText"": ""ما هو الجهد الكهربي؟"",
          ""questionType"": ""MultipleChoice"",
          ""imageUrl"": null,
          ""difficulty"": ""Easy"",
          ""displayOrder"": 1,
          ""points"": 1,
          ""currentHint"": null,
          ""options"": [
            { ""optionId"": 1001, ""optionText"": ""فرق الجهد بين نقطتين"", ""imageUrl"": null, ""displayOrder"": 1 },
            { ""optionId"": 1002, ""optionText"": ""مقاومة مرور التيار"", ""imageUrl"": null, ""displayOrder"": 2 }
          ]
        },
        {
          ""questionId"": 104,
          ""questionText"": ""المصباح يضيء بدون مصدر كهربي."",
          ""questionType"": ""TrueFalse"",
          ""imageUrl"": null,
          ""difficulty"": ""Easy"",
          ""displayOrder"": 2,
          ""points"": 1,
          ""currentHint"": null,
          ""options"": [
            { ""optionId"": 1010, ""optionText"": ""صح"", ""imageUrl"": null, ""displayOrder"": 1 },
            { ""optionId"": 1011, ""optionText"": ""خطأ"", ""imageUrl"": null, ""displayOrder"": 2 }
          ]
        }
      ]
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
}
