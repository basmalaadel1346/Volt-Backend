using System;
using System.Collections.Generic;

namespace UsersDA.Entities;

public partial class PasswordResetOtp
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Otphash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public int Attempts { get; set; }

    public string? ResetTokenHash { get; set; }

    public DateTime? ResetTokenExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
