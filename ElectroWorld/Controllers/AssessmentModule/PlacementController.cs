using AssessmentBL.DTOs.Placement;
using AssessmentBL.DTOs.QuizAttempt;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;
using Shared.Users;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// The first-run placement test. Learner accounts only — registration gives
/// every learner the Child role; a Parent or Admin has no level to place.
///
/// App flow after register/login:
///   GET  /api/placement                      → Required? show the test
///   POST /api/placement/start                → questions (resumes an open test)
///   POST /api/quiz-attempts/{id}/submit      → result, with result.placement
/// </summary>
[ApiController]
[Route("api/placement")]
[Authorize(Roles = "Child")]
public class PlacementController : ControllerBase
{
    private readonly IPlacementService _placementService;

    public PlacementController(IPlacementService placementService)
    {
        _placementService = placementService;
    }

    /// <summary>Whether to show the placement test, and the result once placed.</summary>
    /// <remarks>
    /// Required — new learner: show it now. Optional — learner already has quiz
    /// history: may offer it. InProgress — call start to resume. Completed —
    /// result included. Unavailable — nothing to place against: skip.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PlacementStatusDto), StatusCodes.Status200OK)]
    [SwaggerExample(200, @"{
      ""status"": ""Required"",
      ""placementQuizId"": 7,
      ""attemptId"": null,
      ""result"": null
    }")]
    public async Task<ActionResult<PlacementStatusDto>> GetStatus(CancellationToken ct)
    {
        var status = await _placementService.GetStatusAsync(User.GetUserId(), ct);
        return Ok(status);
    }

    /// <summary>Starts the placement test, or resumes the open one.</summary>
    /// <remarks>
    /// Submit it with POST /api/quiz-attempts/{attemptId}/submit, sending an
    /// answer for every question. 409 once the learner is already placed.
    /// </remarks>
    [HttpPost("start")]
    [ProducesResponseType(typeof(QuizAttemptResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(400, @"{""success"":false,""message"":""اختبار تحديد المستوى غير متاح حاليًا: لا توجد أسئلة تقييم مفعّلة للمستويات"",""data"":null}")]
    [SwaggerExample(404, @"{""success"":false,""message"":""لا يوجد اختبار تحديد مستوى متاح حاليًا"",""data"":null}")]
    [SwaggerExample(409, @"{""success"":false,""message"":""تم تحديد مستواك بالفعل، لا يمكن إعادة اختبار تحديد المستوى"",""data"":null}")]
    public async Task<ActionResult<QuizAttemptResponseDto>> Start(
        [FromQuery] string? language,
        CancellationToken ct)
    {
        var attempt = await _placementService.StartAsync(
            User.GetUserId(), Request.ResolveContentLanguage(language), ct);
        return Ok(attempt);
    }
}
