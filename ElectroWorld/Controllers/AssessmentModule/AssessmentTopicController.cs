using AssessmentBL.DTOs.Taxonomy;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// Admin: Assessment topics — what a question is about. The topic picker of the
/// question form reads GET /api/assessment/topics; a question may also have no topic.
/// </summary>
[ApiController]
[Route("api/assessment/topics")]
[Authorize(Roles = "Admin")]
public class AssessmentTopicController : ControllerBase
{
    private const string TopicExample = @"{
      ""id"": 4,
      ""name"": ""الثنائي الباعث للضوء"",
      ""description"": ""كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير."",
      ""categoryId"": 2,
      ""categoryName"": ""المكونات الإلكترونية"",
      ""learningLevel"": ""Intermediate"",
      ""isActive"": true,
      ""questionsCount"": 10,
      ""createdAt"": ""2026-09-10T18:42:10.123"",
      ""updatedAt"": null,
      ""translations"": [
        { ""languageCode"": ""ar"", ""name"": ""الثنائي الباعث للضوء"", ""description"": ""كيف يعمل الـ LED ولماذا له طرف طويل وطرف قصير."" },
        { ""languageCode"": ""en"", ""name"": ""The LED"", ""description"": ""How an LED works and why it has a long leg and a short leg."" }
      ]
    }";

    private const string NotFoundExample =
        @"{""success"":false,""message"":""الموضوع رقم 4 غير موجود"",""data"":null}";

    private readonly ITopicService _topicService;

    public AssessmentTopicController(ITopicService topicService)
    {
        _topicService = topicService;
    }

    /// <summary>Topics, optionally filtered by category, learning level and active flag.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminTopicResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, "[" + TopicExample + "]")]
    public async Task<ActionResult<IReadOnlyList<AdminTopicResponseDto>>> Get(
        [FromQuery] TopicFilterDto filter, CancellationToken ct)
    {
        var topics = await _topicService.GetAsync(filter, ct);
        return Ok(topics);
    }

    [HttpGet("{topicId:int}")]
    [ProducesResponseType(typeof(AdminTopicResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, TopicExample)]
    [SwaggerExample(404, NotFoundExample)]
    public async Task<ActionResult<AdminTopicResponseDto>> GetById(int topicId, CancellationToken ct)
    {
        var topic = await _topicService.GetByIdAsync(topicId, ct);
        return Ok(topic);
    }

    /// <summary>Creates an active topic in an existing category.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminTopicResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(201, TopicExample)]
    [SwaggerExample(400, @"{""success"":false,""message"":""مستوى التعلّم 'Expert' غير صالح (Beginner | Intermediate | Advanced)"",""data"":null}")]
    [SwaggerExample(404, @"{""success"":false,""message"":""التصنيف رقم 9 غير موجود"",""data"":null}")]
    [SwaggerExample(409, @"{""success"":false,""message"":""يوجد موضوع آخر بالاسم 'الثنائي الباعث للضوء'"",""data"":null}")]
    public async Task<ActionResult<AdminTopicResponseDto>> Create([FromBody] CreateTopicDto request, CancellationToken ct)
    {
        var topic = await _topicService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { topicId = topic.Id }, topic);
    }

    /// <summary>Replaces a topic, and — when sent — its translations.</summary>
    [HttpPut("{topicId:int}")]
    [ProducesResponseType(typeof(AdminTopicResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(200, TopicExample)]
    [SwaggerExample(404, NotFoundExample)]
    public async Task<ActionResult<AdminTopicResponseDto>> Update(
        int topicId, [FromBody] UpdateTopicDto request, CancellationToken ct)
    {
        var topic = await _topicService.UpdateAsync(topicId, request, ct);
        return Ok(topic);
    }

    [HttpPatch("{topicId:int}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(404, NotFoundExample)]
    public async Task<IActionResult> SetActive(int topicId, [FromQuery] bool isActive, CancellationToken ct)
    {
        await _topicService.SetActiveAsync(topicId, isActive, ct);
        return NoContent();
    }
}
