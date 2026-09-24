using Microsoft.AspNetCore.Mvc;
using Shared.Common.Api;

namespace ElectroWorld.Api;

/// <summary>
/// Makes the framework's own failures speak the API's language.
///
/// ASP.NET Core answers a malformed body — bad JSON, a string where a number was
/// expected, a missing required field — with ValidationProblemDetails: a payload
/// with "title", "status" and "errors" and NO "success" field. Every other error
/// in this API is the ApiResponse envelope (success, message, data), so a client
/// interceptor that reads `response.success` crashed on exactly the responses it
/// was most likely to meet while a developer was still getting the request right.
///
/// This replaces that payload with the same envelope, so there is one error shape
/// in the whole API and no client needs a special case.
/// </summary>
public static class ApiBehaviorConfiguration
{
    public static IMvcBuilder AddUnifiedErrorEnvelope(this IMvcBuilder builder) =>
        builder.ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var message = BuildMessage(context.ModelState);

                return new BadRequestObjectResult(ApiResponse<object>.Fail(message))
                {
                    ContentTypes = { "application/json" }
                };
            };
        });

    /// <summary>
    /// One readable Arabic sentence out of the model-state errors, naming the
    /// fields that were wrong. The framework's own messages are English and often
    /// mention CLR types, so a field list the client can act on is more use than
    /// a translation of "could not convert Int32".
    /// </summary>
    private static string BuildMessage(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
    {
        var fields = modelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            // An empty key is the body itself — malformed JSON rather than a bad field.
            .Select(entry => entry.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key) && key != "$")
            .Distinct()
            .Take(10)
            .ToList();

        if (fields.Count == 0)
            return "صيغة الطلب غير صحيحة، تأكد من إرسال JSON صالح بالحقول المطلوبة";

        return $"بيانات الطلب غير صحيحة في الحقول: {string.Join("، ", fields)}";
    }
}
