using System.Net;
using System.Text.Json;
using Shared.Common.Api;

namespace ElectroWorld.Middleware;

/// <summary>
/// بيمسك أي Exception غير متوقع (مش متغطي بـ try/catch محلي جوه الـ Services) في أي مكان
/// في المشروع كله، ويرجّعه بنفس شكل ApiResponse الموحّد بدل ما يتسرّب كـ Response خام من
/// ASP.NET (ممكن يكشف تفاصيل داخلية عن الكود/الداتابيز، وبيكسر الشكل المتوقع من الفلاتر).
///
/// لازم يكون أول Middleware في الـ Pipeline (شوف Program.cs) عشان يلف أي حاجة جاية بعده.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // العميل قفل الطلب (Timeout/إلغاء) - مش خطأ حقيقي في السيرفر، مفيش داعي نعمله Log كـ Error
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                // الـ Response بدأ يتبعت بالفعل (مثلاً Streaming) - مينفعش نغيّر الـ Status Code دلوقتي
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            // رسالة عامة بس للعميل - التفاصيل الحقيقية اتسجّلت في الـ Log فوق، مش بترجع في الـ Response
            var response = ApiResponse.Fail("حصل خطأ غير متوقع في السيرفر، حاول تاني بعد شوية");
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }
}
