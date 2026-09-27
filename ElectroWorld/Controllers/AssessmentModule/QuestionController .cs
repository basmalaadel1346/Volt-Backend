using AssessmentBL.DTOs.Question;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// Admin: authoring the questions of a quiz. Children never see this controller —
/// it returns the answer key and the image descriptions written for the AI.
/// </summary>
[ApiController]
[Route("api/Admin/questions")]
[Authorize(Roles = "Admin")]
public class QuestionController : ControllerBase
{
    private readonly IQuestionServiceForAdmin _questionService;

    public QuestionController(IQuestionServiceForAdmin questionService)
    {
        _questionService = questionService;
    }

    /// <summary>Admin: every question of a quiz, inactive ones included, in display order.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminQuestionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AdminQuestionResponseDto>>> GetByQuizId([FromQuery] int quizId, CancellationToken ct)
    {
        var questions = await _questionService.GetByQuizIdAsync(quizId, ct);
        return Ok(questions);
    }

    /// <summary>Admin: one question with its options and the correct answer.</summary>
    [HttpGet("{questionId:int}")]
    [ProducesResponseType(typeof(AdminQuestionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminQuestionResponseDto>> GetById(int questionId, CancellationToken ct)
    {
        var question = await _questionService.GetByQuestionIdAsync(questionId, ct);
        return Ok(question);
    }

    /// <summary>Admin: adds a question to a quiz. It starts INACTIVE.</summary>
    /// <remarks>
    /// A new question is not part of any attempt until it is activated with
    /// PATCH .../active, which is also where the answerability rules are checked
    /// (enough options, exactly one correct, every image described).
    ///
    /// questionType and difficulty are case-insensitive: "multiplechoice" and
    /// "MultipleChoice" are the same value.
    ///
    /// A placement or level-skip quiz owns no questions — they sample other
    /// quizzes — so adding one to either is rejected with 400.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(AdminQuestionResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminQuestionResponseDto>> Create([FromBody] CreateQuestionDto request, CancellationToken ct)
    {
        var question = await _questionService.CreateQuestionAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { questionId = question.Id }, question);
    }

    /// <summary>Admin: replaces a question's fields. QuizId cannot be changed.</summary>
    [HttpPut("{questionId:int}")]
    [ProducesResponseType(typeof(AdminQuestionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminQuestionResponseDto>> Update(int questionId, [FromBody] UpdateQuestionDto request, CancellationToken ct)
    {
        var question = await _questionService.UpdateQuestionAsync(questionId, request, ct);
        return Ok(question);
    }

    /// <summary>Admin: sets the order of a quiz's questions from one absolute list.</summary>
    /// <remarks>
    /// Send EVERY question id of the quiz, exactly once, in the order wanted; the
    /// server renumbers them 1..n in a single transaction.
    ///
    /// This replaces the pairwise swap, which was not idempotent: a retried swap
    /// put the two questions back where they started, so a lost response silently
    /// left the admin's screen and the database disagreeing. Sending the whole
    /// order describes a state, so repeating the request changes nothing.
    ///
    /// A list that misses an id, repeats one, or names one from another quiz is
    /// rejected with 400 rather than half-applied — a stale screen should reload,
    /// not overwrite.
    /// </remarks>
    [HttpPut("order")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminQuestionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(400, @"{""success"":false,""message"":""يجب إرسال ترتيب الأسئلة كاملة؛ العناصر الناقصة: 104"",""data"":null}")]
    public async Task<ActionResult<IReadOnlyList<AdminQuestionResponseDto>>> Reorder(
        [FromQuery] int quizId,
        [FromBody] ReorderRequestDto request,
        CancellationToken ct)
    {
        var questions = await _questionService.ReorderAsync(quizId, request.OrderedIds, ct);
        return Ok(questions);
    }

    /// <summary>Admin: publishes a question to children, or withdraws it.</summary>
    /// <remarks>
    /// Activating checks that the question is answerable: MultipleChoice needs at
    /// least two options and exactly one correct, TrueFalse exactly two, an Essay
    /// none at all, and every image — the question's and each option's — must
    /// carry a description for the AI.
    ///
    /// Deactivating a question a child answered wrong removes it from their retry;
    /// the child is told so in the retry's `notice`.
    /// </remarks>
    [HttpPatch("{questionId:int}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(int questionId, [FromQuery] bool isActive, CancellationToken ct)
    {
        await _questionService.SetActiveAsync(questionId, isActive, ct);
        return NoContent();
    }
}
