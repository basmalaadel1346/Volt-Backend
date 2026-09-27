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
[Authorize]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class LessonsController : ControllerBase
{
    private readonly ILessonService _service;
    public LessonsController(ILessonService service) => _service = service;

    /// <summary>كل الدروس بتاعت مستوى معيّن، مرتبة حسب SortOrder.</summary>
    // ملحوظة: GetByLevelIdAsync بترجع قائمة فاضية لو المستوى مش موجود أو مفيهوش دروس -
    // مفيش 404/400 موثّق هنا عمدًا لأنه مفيش مسار فشل فعلي.
    [HttpGet("levels/{levelId:int}/lessons")]
    [ProducesResponseType(typeof(ApiResponse<List<LessonSummaryResponse>>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":[{"id":5,"levelId":1,"title":"مقدمة عن الدائرة الكهربية","description":null,"sortOrder":1,"isPublished":true,"createdAt":"2026-08-05T09:00:00Z"}]}""")]
    public async Task<IActionResult> GetByLevel(int levelId, CancellationToken ct)
    {
        var result = await _service.GetByLevelIdAsync(levelId, User.IsInRole("Admin"), ct);
        return Ok(ApiResponse<List<LessonSummaryResponse>>.Ok(result.Value!));
    }

    /// <summary>شكل شاشة الهوم الكامل: كل الدروس المنشورة بترتيب المسار (كل المستويات مع بعض)،
    /// كل درس معاه نوعه (Lesson/FinalLevelQuiz)، وهل هو أول درس في مستواه (لشكل النجمة)،
    /// وحالة تقدم المستخدم الحالي فيه (completed/inProgress/locked).</summary>
    // مفيش [ResponseCache] هنا عمدًا (وده الفرق عن GetDetail تحت) - الـ Response ده بيتغيّر
    // كل ما الطفل يخلّص درس (completed/inProgress/locked كله بيتحرك)، فلازم يوصل Fresh من
    // السيرفر في كل طلب، مش من Cache المتصفح/Swagger. [ResponseCache(NoStore = true)] بتحط
    // Cache-Control: no-store صراحة عشان نمنع أي كاش (متصفح/Proxy) يخزّنه من الأساس.
    [HttpGet("lessons/published")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<List<PublishedLessonResponse>>), StatusCodes.Status200OK)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":[{"lessonId":1,"levelName":"كوكب الكهرباء","lessonName":"ما هي الكهرباء","isFirstLevelLesson":true,"lessonType":"lesson","lessonStatus":"inProgress"},{"lessonId":2,"levelName":"كوكب الكهرباء","lessonName":"الدائرة الكهربية","isFirstLevelLesson":false,"lessonType":"lesson","lessonStatus":"locked"},{"lessonId":4,"levelName":"كوكب الكهرباء","lessonName":"الاختبار النهائي","isFirstLevelLesson":false,"lessonType":"finalLevelQuiz","lessonStatus":"locked"}]}""")]
    public async Task<IActionResult> GetPublished(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _service.GetPublishedHomeAsync(userId, ct);
        return Ok(ApiResponse<List<PublishedLessonResponse>>.Ok(result.Value!));
    }

    /// <summary>تفاصيل الدرس كاملة مع كل عناصر المحتوى بتاعته مرتبة.</summary>
    [HttpGet("lessons/{id:int}")]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType(typeof(ApiResponse<LessonDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LessonDetailResponse>), StatusCodes.Status404NotFound)]
    [SwaggerExample(200, """{"success":true,"message":"تمت العملية بنجاح","data":{"id":5,"levelId":1,"title":"مقدمة عن الدائرة الكهربية","description":null,"sortOrder":1,"isPublished":true,"createdAt":"2026-08-05T09:00:00Z","contents":[{"id":10,"lessonId":5,"contentTypeId":1,"contentTypeName":"Text","content":"الدائرة الكهربية بتتكون من...","mediaUrl":null,"sortOrder":1},{"id":11,"lessonId":5,"contentTypeId":2,"contentTypeName":"Image","content":null,"mediaUrl":"/uploads/lessons/8f1c2b3a.png","sortOrder":2}]}}""")]
    [SwaggerExample(404, """{"success":false,"message":"الدرس غير موجود","data":null}""")]
    public async Task<IActionResult> GetDetail(int id, CancellationToken ct)
    {
        var result = await _service.GetDetailAsync(id, User.IsInRole("Admin"), ct);
        return result.IsSuccess
            ? Ok(ApiResponse<LessonDetailResponse>.Ok(result.Value!))
            : NotFound(ApiResponse<LessonDetailResponse>.Fail(result.Error!));
    }

    /// <summary>هيديله ترتيب (SortOrder) تلقائي = آخر ترتيب في نفس المستوى + 1. متبعتش SortOrder في الـ Body.</summary>
    [HttpPost("lessons")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<LessonSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LessonSummaryResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم إنشاء الدرس بنجاح","data":{"id":6,"levelId":1,"title":"القياسات الكهربية","description":null,"sortOrder":2,"isPublished":false,"createdAt":"2026-09-10T10:00:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"المستوى المحدد غير موجود","data":null}""")]
    public async Task<IActionResult> Create([FromBody] CreateLessonRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<LessonSummaryResponse>.Ok(result.Value!, "تم إنشاء الدرس بنجاح"))
            : BadRequest(ApiResponse<LessonSummaryResponse>.Fail(result.Error!));
    }

    /// <summary>بتعدّل بيانات الدرس بس - الترتيب مايتغيرش هنا حتى لو غيّرت المستوى (هيتحدد تلقائي في نهاية المستوى الجديد). لتغيير الترتيب استخدم swap-order.</summary>
    [HttpPut("lessons/{id:int}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<LessonSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LessonSummaryResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم تعديل الدرس بنجاح","data":{"id":6,"levelId":1,"title":"القياسات الكهربية - محدّث","description":"وصف جديد","sortOrder":2,"isPublished":false,"createdAt":"2026-09-10T10:00:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"الدرس غير موجود","data":null}""")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLessonRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<LessonSummaryResponse>.Ok(result.Value!, "تم تعديل الدرس بنجاح"))
            : BadRequest(ApiResponse<LessonSummaryResponse>.Fail(result.Error!));
    }

    [HttpPatch("lessons/{id:int}/publish")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<LessonSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LessonSummaryResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم نشر الدرس","data":{"id":6,"levelId":1,"title":"القياسات الكهربية","description":null,"sortOrder":2,"isPublished":true,"createdAt":"2026-09-10T10:00:00Z"}}""")]
    [SwaggerExample(400, """{"success":false,"message":"الدرس غير موجود","data":null}""")]
    public async Task<IActionResult> SetPublished(int id, [FromBody] SetPublishedRequest request, CancellationToken ct)
    {
        var result = await _service.SetPublishedAsync(id, request.IsPublished, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<LessonSummaryResponse>.Ok(result.Value!, request.IsPublished ? "تم نشر الدرس" : "تم إخفاء الدرس"))
            : BadRequest(ApiResponse<LessonSummaryResponse>.Fail(result.Error!));
    }

    //[HttpDelete("lessons/{id:int}")]
    //[Authorize(Roles = "Admin")]
    //[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    //[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    //[ProducesResponseType(StatusCodes.Status403Forbidden)]
    //[SwaggerExample(200, """{"success":true,"message":"تم مسح الدرس بنجاح"}""")]
    //[SwaggerExample(400, """{"success":false,"message":"الدرس غير موجود"}""")]
    //public async Task<IActionResult> Delete(int id, CancellationToken ct)
    //{
    //    var result = await _service.DeleteAsync(id, ct);
    //    return result.IsSuccess
    //        ? Ok(ApiResponse.Ok("تم مسح الدرس بنجاح"))
    //        : BadRequest(ApiResponse.Fail(result.Error!));
    //}

    /// <summary>بتبدّل ترتيب درسين في بعض - لازم يكونوا في نفس المستوى.</summary>
    [HttpPost("lessons/swap-order")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [SwaggerExample(200, """{"success":true,"message":"تم تبديل الترتيب بنجاح"}""")]
    [SwaggerExample(400, """{"success":false,"message":"واحد من الدرسين غير موجود"}""")]
    public async Task<IActionResult> SwapOrder([FromBody] SwapLessonsOrderRequest request, CancellationToken ct)
    {
        var result = await _service.SwapOrderAsync(request, ct);
        return result.IsSuccess
            ? Ok(ApiResponse.Ok("تم تبديل الترتيب بنجاح"))
            : BadRequest(ApiResponse.Fail(result.Error!));
    }
}
