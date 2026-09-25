using System;
using System.Collections.Generic;

namespace UsersDA.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public string? Email { get; set; }

    public string? PasswordHash { get; set; }

    public string FullName { get; set; } = null!;

    public string Role { get; set; } = null!;

    public string AuthProvider { get; set; } = null!;

    public string? ProviderUserId { get; set; }

    public int? Age { get; set; }

    public bool IsActive { get; set; }

    public DateTime? ConvertedFromGuestAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ParentChildLink> ParentChildLinkChildUsers { get; set; } = new List<ParentChildLink>();

    public virtual ICollection<ParentChildLink> ParentChildLinkParentUsers { get; set; } = new List<ParentChildLink>();

    public virtual ICollection<PasswordResetOtp> PasswordResetOtps { get; set; } = new List<PasswordResetOtp>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
