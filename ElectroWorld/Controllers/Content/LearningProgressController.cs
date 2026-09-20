using ContentBL.DTOs;
using ContentBL.Interfaces;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;
using Shared.Users;

namespace ElectroWorld.Controllers.Content;

[ApiController]
[Route("api/content")]
[Authorize] // أي حساب مسجّل (Parent أو Child) يقدر يسجّل ويشوف تقدمه هو بس
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class LearningProgressController : ControllerBase
{
    private readonly ILearningProgressService _service;
    public LearningProgressController(ILearningProgressService service) => _service = service;

    /// <summary>كل الـ LessonId المكتملة للمستخدم الحالي (من التوكن) - يستخدمها الفلاتر
    /// لعمل الـ Checkmarks وتحديد الدروس المفتوحة.</summary>
    [HttpGet("progress")]
    [ProducesResponseType(typeof(ApiResponse<List<int>>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":[5,6,9]}""")]
    public async Task<IActionResult> GetCompleted(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _service.GetCompletedLessonIdsAsync(userId, ct);
        return Ok(ApiResponse<List<int>>.Ok(result.Value!));
    }

    /// <summary>حالة درس معيّن للمستخدم الحالي (اختياري - للتحقق من درس واحد بس).</summary>
    [HttpGet("lessons/{lessonId:int}/progress")]
    [ProducesResponseType(typeof(ApiResponse<LessonProgressStatusResponse>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":{"lessonId":5,"isCompleted":true,"completedAt":"2026-09-10T14:00:00Z"}}""")]
    public async Task<IActionResult> GetForLesson(int lessonId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _service.GetLessonProgressAsync(userId, lessonId, ct);
        return Ok(ApiResponse<LessonProgressStatusResponse>.Ok(result.Value!));
    }

    /// <summary>بتسجّل إكمال الدرس للمستخدم الحالي (Idempotent - لو مسجل بالفعل من قبل،
    /// بترجع نجاح بردو من غير ما تعمل صف تاني، بفضل Unique(UserId, LessonId)).</summary>
    [HttpPost("lessons/{lessonId:int}/progress")]
    [ProducesResponseType(typeof(ApiResponse<LessonProgressResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LessonProgressResponse>), StatusCodes.Status400BadRequest)]
    [SwaggerExample(200, """{"success":true,"message":"تم تسجيل إكمال الدرس بنجاح","data":{"lessonId":5,"completedAt":"2026-09-10T14:00:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"الدرس غير موجود","data":null}""")]
    public async Task<IActionResult> MarkComplete(int lessonId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _service.MarkLessonCompleteAsync(userId, lessonId, ct);

        return result.IsSuccess
            ? Ok(ApiResponse<LessonProgressResponse>.Ok(result.Value!, "تم تسجيل إكمال الدرس بنجاح"))
            : BadRequest(ApiResponse<LessonProgressResponse>.Fail(result.Error!));
    }
}
