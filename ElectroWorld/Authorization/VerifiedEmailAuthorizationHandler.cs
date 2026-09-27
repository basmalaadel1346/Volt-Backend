using Microsoft.AspNetCore.Authorization;

namespace ElectroWorld.Authorization;

/// <summary>
/// بيمنع مستخدم AuthProvider = Email لسه معملش Email Verification (Claim "emailVerified" = "false")
/// من أي Endpoint بيستخدم الـ DefaultPolicy (يعني أي [Authorize] عادي من غير Policy/Roles محدّدة).
///
/// Guest وGoogle مش متأثرين خالص - الشرط بيتفعّل بس لو authProvider == "Email".
/// (Google بيتحط له emailVerified = true تلقائي وقت الإنشاء أصلاً - شوف AuthService).
/// </summary>
public class VerifiedEmailAuthorizationHandler : AuthorizationHandler<VerifiedEmailRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, VerifiedEmailRequirement requirement)
    {
        var authProvider = context.User.FindFirst("authProvider")?.Value;
        var isEmailVerified = context.User.FindFirst("emailVerified")?.Value == "true";

        var isUnverifiedEmailUser = authProvider == "Email" && !isEmailVerified;

        if (!isUnverifiedEmailUser)
            context.Succeed(requirement);

        // لو isUnverifiedEmailUser=true، مبنعملش Succeed ولا Fail صريح - ببساطة الـ Requirement
        // بتفضل مش متحققة، فالـ Authorization كله بيفشل ويرجع 403 Forbidden تلقائي.
        return Task.CompletedTask;
    }
}
