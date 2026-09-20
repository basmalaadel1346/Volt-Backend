using AssessmentBL.DTOs.Taxonomy;
using AssessmentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Api.Controllers;

/// <summary>
/// Admin: the categories that group Assessment topics. Not the Content module's
/// levels (/api/content/levels) — a category is what questions are about, and it
/// is how the child's progress map is organised.
/// </summary>
[ApiController]
[Route("api/assessment/categories")]
[Authorize(Roles = "Admin")]
public class AssessmentCategoryController : ControllerBase
{
    private const string CategoryExample = @"{
      ""id"": 1,
      ""name"": ""أساسيات الكهرباء"",
      ""sortOrder"": 1,
      ""isActive"": true,
      ""topicsCount"": 3,
      ""translations"": [
        { ""languageCode"": ""ar"", ""name"": ""أساسيات الكهرباء"" },
        { ""languageCode"": ""en"", ""name"": ""Electricity basics"" }
      ]
    }";

    private const string NotFoundExample =
        @"{""success"":false,""message"":""التصنيف رقم 9 غير موجود"",""data"":null}";

    private readonly ICategoryService _categoryService;

    public AssessmentCategoryController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>Every category, by SortOrder, with its translations and topic count.</summary>
    /// <remarks>Filter with ?isActive=true|false; omit it for all.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminCategoryResponseDto>), StatusCodes.Status200OK)]
    [SwaggerExample(200, "[" + CategoryExample + "]")]
    public async Task<ActionResult<IReadOnlyList<AdminCategoryResponseDto>>> GetAll(
        [FromQuery] bool? isActive, CancellationToken ct)
    {
        var categories = await _categoryService.GetAllAsync(isActive, ct);
        return Ok(categories);
    }

    [HttpGet("{categoryId:int:range(1,255)}")]
    [ProducesResponseType(typeof(AdminCategoryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, CategoryExample)]
    [SwaggerExample(404, NotFoundExample)]
    public async Task<ActionResult<AdminCategoryResponseDto>> GetById(byte categoryId, CancellationToken ct)
    {
        var category = await _categoryService.GetByIdAsync(categoryId, ct);
        return Ok(category);
    }

    /// <summary>Creates an active category. Its id is assigned by the server.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminCategoryResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(201, CategoryExample)]
    [SwaggerExample(409, @"{""success"":false,""message"":""يوجد تصنيف آخر بالاسم 'أساسيات الكهرباء'"",""data"":null}")]
    public async Task<ActionResult<AdminCategoryResponseDto>> Create([FromBody] CreateCategoryDto request, CancellationToken ct)
    {
        var category = await _categoryService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { categoryId = category.Id }, category);
    }

    /// <summary>Replaces a category's name, order and active flag, and — when sent — its translations.</summary>
    [HttpPut("{categoryId:int:range(1,255)}")]
    [ProducesResponseType(typeof(AdminCategoryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [SwaggerExample(200, CategoryExample)]
    [SwaggerExample(404, NotFoundExample)]
    public async Task<ActionResult<AdminCategoryResponseDto>> Update(
        byte categoryId, [FromBody] UpdateCategoryDto request, CancellationToken ct)
    {
        var category = await _categoryService.UpdateAsync(categoryId, request, ct);
        return Ok(category);
    }

    [HttpPatch("{categoryId:int:range(1,255)}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [SwaggerExample(404, NotFoundExample)]
    public async Task<IActionResult> SetActive(byte categoryId, [FromQuery] bool isActive, CancellationToken ct)
    {
        await _categoryService.SetActiveAsync(categoryId, isActive, ct);
        return NoContent();
    }
}
