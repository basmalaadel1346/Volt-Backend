using AssessmentBL.DTOs.Question;
using AssessmentBL.DTOs.QuestionOption;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// Admin: the answer options of a question, including which one is correct.
/// Never reachable by a child — the child-facing projections drop IsCorrect.
/// </summary>
[ApiController]
[Route("api/Admin/question-options")]
[Authorize(Roles = "Admin")]
public class QuestionOptionController : ControllerBase
{
    private readonly IQuestionOptionServiceForAdmin _questionOptionService;

    public QuestionOptionController(IQuestionOptionServiceForAdmin questionOptionService)
    {
        _questionOptionService = questionOptionService;
    }

    /// <summary>Admin: the options of one question, in display order, with the answer key.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminQuestionOptionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AdminQuestionOptionResponseDto>>> GetByQuestionId([FromQuery] int questionId, CancellationToken ct)
    {
        var options = await _questionOptionService.GetByQuestionIdAsync(questionId, ct);
        return Ok(options);
    }

    // NOTE: the service exposes no single-option "get by id" method, only
    // GetByQuestionIdAsync. CreatedAtAction therefore points back at the
    // by-question listing endpoint instead of a single-resource URL.
    /// <summary>Admin: adds an option to a question.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminQuestionOptionResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminQuestionOptionResponseDto>> Create([FromBody] CreateQuestionOptionDto request, CancellationToken ct)
    {
        var option = await _questionOptionService.CreateOptionAsync(request, ct);
        return CreatedAtAction(nameof(GetByQuestionId), new { questionId = request.QuestionId }, option);
    }

    /// <summary>Admin: replaces one option's text, image, order and correctness.</summary>
    [HttpPut("{optionId:int}")]
    [ProducesResponseType(typeof(AdminQuestionOptionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminQuestionOptionResponseDto>> Update(int optionId, [FromBody] UpdateQuestionOptionDto request, CancellationToken ct)
    {
        var option = await _questionOptionService.UpdateOptionAsync(optionId, request, ct);
        return Ok(option);
    }

    /// <summary>Admin: moves a question's correct answer to another option, in one request.</summary>
    /// <remarks>
    /// Changing the correct answer used to take FOUR requests — deactivate the
    /// question, clear the old option, set the new one, activate again — because
    /// a question may never have two correct options and an active one may never
    /// have none. If the admin's connection dropped at step 3, the question was
    /// left deactivated and broken.
    ///
    /// This does the whole change in one database transaction: either the answer
    /// moves, or nothing changed and the question is still answerable. The
    /// question is never deactivated on the way, and attempts already in flight
    /// are unaffected — every attempt is graded against the answer key frozen in
    /// its own snapshot when it started.
    ///
    /// Returns the question's options in display order. Idempotent: sending the
    /// option that is already correct returns 200 and changes nothing.
    /// </remarks>
    [HttpPatch("~/api/Admin/questions/{questionId:int}/correct-option")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminQuestionOptionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(404, @"{""success"":false,""message"":""الاختيار رقم 1005 لا يخص السؤال رقم 102"",""data"":null}")]
    [SwaggerExample(400, @"{""success"":false,""message"":""السؤال رقم 104 سؤال مقالي وليس له إجابة صحيحة"",""data"":null}")]
    public async Task<ActionResult<IReadOnlyList<AdminQuestionOptionResponseDto>>> SetCorrectOption(
        int questionId,
        [FromBody] SetCorrectOptionDto request,
        CancellationToken ct)
    {
        var options = await _questionOptionService.SetCorrectOptionAsync(questionId, request, ct);
        return Ok(options);
    }

    /// <summary>Admin: sets the order of a question's options from one absolute list.</summary>
    /// <remarks>
    /// Send EVERY option id of the question, exactly once, in the order wanted.
    /// Idempotent, unlike a pairwise swap: repeating the request changes nothing.
    /// A list that misses an id, repeats one, or names one from another question
    /// is rejected with 400 rather than half-applied.
    /// </remarks>
    [HttpPut("~/api/Admin/questions/{questionId:int}/options/order")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminQuestionOptionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AdminQuestionOptionResponseDto>>> ReorderOptions(
        int questionId,
        [FromBody] ReorderRequestDto request,
        CancellationToken ct)
    {
        var options = await _questionOptionService.ReorderAsync(questionId, request.OrderedIds, ct);
        return Ok(options);
    }

    /// <summary>Admin: deletes an option. Refused when it is used by past attempts.</summary>
    [HttpDelete("~/api/Admin/question-options/{optionId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int optionId, CancellationToken ct)
    {
        await _questionOptionService.DeleteOptionAsync(optionId, ct);
        return NoContent();
    }
}
