using AssessmentBL.DTOs.LevelSkip;
using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;
using Shared.Users;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// The level-skip challenge: a child who already knows a level proves it once
/// instead of sitting through every lesson.
///
/// About ten questions drawn from the level's OWN lesson quizzes, spread across
/// its lessons so knowing only the first one is not enough, against a three-minute
/// clock and three hearts. Passing marks the level's lessons complete, which is
/// what actually opens the next level.
///
/// App flow:
///   GET  /api/level-skip/{levelId}          → Available? offer the challenge
///   POST /api/level-skip/{levelId}/start    → questions (resumes an open run)
///   POST /api/quiz-attempts/{id}/submit     → result, with result.levelSkip
/// </summary>
[ApiController]
[Route("api/level-skip")]
[Authorize(Roles = "Child")]
public class LevelSkipController : ControllerBase
{
    private readonly ILevelSkipService _levelSkipService;

    public LevelSkipController(ILevelSkipService levelSkipService)
    {
        _levelSkipService = levelSkipService;
    }

    /// <summary>Whether the child may try to skip this level, and on what terms.</summary>
    /// <remarks>
    /// Available — offer it. InProgress — a run is open; start resumes it.
    /// Passed — the level is already skipped. Unavailable — no active level-skip
    /// quiz, or the level's lesson quizzes have nothing to sample.
    ///
    /// questionCount, timeLimitSeconds, hearts and passPercentage are the terms to
    /// show the child BEFORE they commit to a timed challenge.
    /// </remarks>
    [HttpGet("{levelId:int}")]
    [ProducesResponseType(typeof(LevelSkipStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, @"{
      ""levelId"": 2,
      ""status"": ""Available"",
      ""quizId"": 31,
      ""questionCount"": 10,
      ""timeLimitSeconds"": 180,
      ""hearts"": 3,
      ""passPercentage"": 80,
      ""previousAttempts"": 0
    }")]
    [SwaggerExample(401, ApiResponseExamples.Unauthorized)]
    [SwaggerExample(404, @"{""success"":false,""message"":""المستوى رقم 2 غير موجود"",""data"":null}")]
    public async Task<ActionResult<LevelSkipStatusDto>> GetStatus(int levelId, CancellationToken ct)
    {
        var status = await _levelSkipService.GetStatusAsync(levelId, User.GetUserId(), ct);
        return Ok(status);
    }

    /// <summary>Starts the challenge, or resumes the open one.</summary>
    /// <remarks>
    /// The response carries `expiresAt` and `timeLimitSeconds` (the clock started
    /// when the attempt did — a resumed run shows the time it has LEFT, not a
    /// fresh window) and `hearts`.
    ///
    /// Submit it with POST /api/quiz-attempts/{attemptId}/submit, answering every
    /// question. The result's `levelSkip` says whether the level was skipped, how
    /// many hearts survived, and how many lessons the pass completed. Submitting
    /// after the clock runs out is a 410 — start again.
    ///
    /// A failed challenge can be taken again; the next run draws a different
    /// sample from the same lessons.
    /// </remarks>
    [HttpPost("{levelId:int}/start")]
    [ProducesResponseType(typeof(QuizAttemptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(400, @"{""success"":false,""message"":""اختبار تخطي المستوى رقم 2 غير متاح حاليًا: لا توجد أسئلة دروس مفعّلة في هذا المستوى"",""data"":null}")]
    [SwaggerExample(404, @"{""success"":false,""message"":""لا يوجد اختبار تخطي متاح للمستوى رقم 2"",""data"":null}")]
    public async Task<ActionResult<QuizAttemptResponseDto>> Start(
        int levelId,
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var attempt = await _levelSkipService.StartAsync(
            levelId, User.GetUserId(), Request.ResolveContentLanguage(language), ct);
        return Ok(attempt);
    }
}
