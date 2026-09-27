using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ElectroWorld.Swagger;

/// <summary>
/// بيدوّر على أي Endpoint عليه [Authorize(Roles = "Admin")] (Action-level أو Controller-level)
/// ويضيف "[ADMIN]" في بداية الـ Summary + ملاحظة في الـ Description. توثيق بصري بس داخل نفس
/// الـ Group الأصلي بتاع الكونترولر - مش بيغيّر الـ Grouping ولا بيكرر الـ Endpoint في مكان تاني،
/// ومش بيلمس الـ Authorize أو الـ Routes الفعلية بأي شكل.
/// </summary>
public class AdminEndpointLabelOperationFilter : IOperationFilter
{
    private const string Label = "[ADMIN]";
    private const string Note = "يتطلب صلاحية Admin - غير متاح لحساب Parent أو Child.";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var actionAttributes = context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>();
        var controllerAttributes = context.MethodInfo.DeclaringType?
            .GetCustomAttributes(true).OfType<AuthorizeAttribute>() ?? Enumerable.Empty<AuthorizeAttribute>();

        var isAdminOnly = actionAttributes.Concat(controllerAttributes).Any(a =>
            a.Roles is not null &&
            a.Roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains("Admin", StringComparer.OrdinalIgnoreCase));

        if (!isAdminOnly)
            return;

        operation.Summary = string.IsNullOrWhiteSpace(operation.Summary)
            ? Label
            : $"{Label} {operation.Summary}";

        operation.Description = string.IsNullOrWhiteSpace(operation.Description)
            ? Note
            : $"{operation.Description}\n\n{Note}";
    }
}
