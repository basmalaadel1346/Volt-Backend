namespace UsersDA.Entities;

public partial class EmailVerificationOtp
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Otphash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public int Attempts { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
