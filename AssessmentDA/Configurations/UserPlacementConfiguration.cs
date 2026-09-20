using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class UserPlacementConfiguration : IEntityTypeConfiguration<UserPlacement>
{
    public void Configure(EntityTypeBuilder<UserPlacement> entity)
    {
        entity.ToTable("UserPlacements", "Assessment", tb =>
        {
            tb.HasCheckConstraint("CK_UserPlacements_ScorePercentage", "[ScorePercentage] BETWEEN 0 AND 100");
            tb.HasCheckConstraint("CK_UserPlacements_PassPercentage", "[PassPercentage] BETWEEN 1 AND 100");
        });

        // One placement per learner — a second concurrent placement submit loses here.
        entity.HasIndex(e => e.UserId, "UQ_UserPlacements_UserId").IsUnique();
        entity.HasIndex(e => e.QuizAttemptId, "UQ_UserPlacements_QuizAttemptId").IsUnique();

        entity.Property(e => e.ScorePercentage).HasColumnType("decimal(5, 2)");
        entity.Property(e => e.PlacedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");

        // DB: no ON DELETE clause (NO ACTION) — the attempt that justifies a
        // placement cannot be deleted out from under it.
        entity.HasOne(d => d.QuizAttempt).WithOne()
            .HasForeignKey<UserPlacement>(d => d.QuizAttemptId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_UserPlacements_QuizAttempts");

        // UserId / PlacedLevelId: loosely coupled references to the Users and
        // Content modules. No physical FK by design.
    }
}
