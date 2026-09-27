using Microsoft.OpenApi.Any;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Models;

namespace ElectroWorld.Swagger;

/// <summary>
/// بيقرأ [SwaggerExample] من كل Action وبيحط الـ Example المطابق على الـ Response Content
/// بتاع نفس الـ Status Code، لو الـ Response ده أصلاً معرّف (عن طريق [ProducesResponseType]
/// بنوع Body). التوثيق فقط - مش بيغيّر أي حاجة في الـ Schema أو الـ Business Logic.
/// </summary>
public class ResponseExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var attributes = context.MethodInfo.GetCustomAttributes(typeof(SwaggerExampleAttribute), inherit: true)
            .Cast<SwaggerExampleAttribute>();

        foreach (var attribute in attributes)
        {
            var key = attribute.StatusCode.ToString();
            if (!operation.Responses.TryGetValue(key, out var response))
                continue;

            if (response.Content.Count == 0)
                continue; // Response من غير Body (زي 401/403 اللي بيرجعوا من الـ Middleware) - مفيش حاجة نحطها

            IOpenApiAny example;
            try
            {
                example = OpenApiAnyFactory.CreateFromJson(attribute.Json);
            }
            catch
            {
                continue; // JSON غلط بالغلط - نتجاهله بدل ما نكسر توليد الـ Swagger كله
            }

            foreach (var mediaType in response.Content.Values)
            {
                mediaType.Example = example;
            }
        }
    }
}
