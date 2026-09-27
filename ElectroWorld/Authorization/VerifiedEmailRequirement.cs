using Microsoft.AspNetCore.Authorization;

namespace ElectroWorld.Authorization;

/// <summary>
/// Requirement فاضية (Marker) - المنطق كله في VerifiedEmailAuthorizationHandler.
/// </summary>
public class VerifiedEmailRequirement : IAuthorizationRequirement
{
}
