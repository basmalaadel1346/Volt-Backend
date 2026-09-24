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

    /// <summary>بتسجّل إكمال الدرس للمستخدم الحالي، وبتدّي الشرارات وبتمدّد السلسلة.</summary>
    /// <remarks>
    /// Idempotent: لو الدرس مسجّل خلاص، بترجع نجاح من غير صف تاني (بفضل
    /// Unique(UserId, LessonId)) ومن غير مكافأة تانية - الدرس الواحد بيتدفع مرة
    /// واحدة مهما الطلب اتعاد.
    ///
    /// الرد بيحمل `rewards`: الشرارات اللي الطفل كسبها، السلسلة بعد الدرس ده، وهل
    /// اتصرف تجميد عشان يوم فايت - كل ده في نفس النداء عشان شريط المكافآت يتحرّك
    /// من غير طلب تاني.
    /// </remarks>
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

    /// <summary>هل الدرس ده مفتوح للطفل ولا مقفول؟</summary>
    /// <remarks>
    /// الطفل مايفتحش الدرس التالي غير لما ينجح في اختبار الدرس اللي قبله في نفس
    /// المستوى. أول درس في المستوى مفتوح دايمًا، والدرس اللي مالوش اختبار مفعّل
    /// بيتحسب "ناجح" - مينفعش قفل الكورس على إدمن نسي يعمل اختبار.
    ///
    /// لو مقفول، الرد بيقول الدرس المطلوب إيه واسمه، ورسالة جاهزة للعرض.
    /// `reason` كود ثابت تقدروا تعملوا عليه Switch:
    /// Unlocked | PreviousLessonQuizNotPassed | LessonNotPublished.
    ///
    /// ملحوظة: ده استعلام - الحماية الحقيقية على GET /api/content/lessons/{id}
    /// نفسه، اللي بيرجّع 403 لو الدرس لسه مقفول.
    /// </remarks>
    [HttpGet("lessons/{lessonId:int}/access")]
    [ProducesResponseType(typeof(ApiResponse<LessonAccessResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LessonAccessResponse>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":{"lessonId":6,"isUnlocked":false,"isCompleted":false,"requiredLessonId":5,"requiredLessonTitle":"ما هي الكهرباء","reason":"PreviousLessonQuizNotPassed","message":"لازم تنجح في اختبار درس \"ما هي الكهرباء\" الأول عشان تفتح الدرس ده."}}""")]
    [SwaggerExample(404, """{"success":false,"message":"الدرس غير موجود","data":null}""")]
    public async Task<IActionResult> GetLessonAccess(int lessonId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _service.GetLessonAccessAsync(userId, lessonId, ct);

        return result.IsSuccess
            ? Ok(ApiResponse<LessonAccessResponse>.Ok(result.Value!))
            : NotFound(ApiResponse<LessonAccessResponse>.Fail(result.Error!));
    }

    /// <summary>نفس السؤال لكل دروس المستوى مرة واحدة - عشان شاشة قائمة الدروس.</summary>
    /// <remarks>
    /// بترجّع حالة القفل لكل درس منشور في المستوى بالترتيب، في نداء واحد، بدل ما
    /// الشاشة تسأل عن كل درس لوحده. بتستخدم استعلامين بس مهما كان عدد الدروس.
    /// </remarks>
    [HttpGet("levels/{levelId:int}/access")]
    [ProducesResponseType(typeof(ApiResponse<List<LessonAccessResponse>>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":[{"lessonId":5,"isUnlocked":true,"isCompleted":true,"reason":"Unlocked","message":"الدرس متاح، يلا نبدأ!"},{"lessonId":6,"isUnlocked":false,"isCompleted":false,"requiredLessonId":5,"requiredLessonTitle":"ما هي الكهرباء","reason":"PreviousLessonQuizNotPassed","message":"لازم تنجح في اختبار درس \"ما هي الكهرباء\" الأول عشان تفتح الدرس ده."}]}""")]
    public async Task<IActionResult> GetLevelAccess(int levelId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _service.GetLevelAccessAsync(userId, levelId, ct);

        return Ok(ApiResponse<List<LessonAccessResponse>>.Ok(result.Value!));
    }
}
