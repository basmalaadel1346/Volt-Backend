using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class QuizAttemptQuestionConfiguration : IEntityTypeConfiguration<QuizAttemptQuestion>
{
    public void Configure(EntityTypeBuilder<QuizAttemptQuestion> entity)
    {
        entity.ToTable("QuizAttemptQuestions", "Assessment", tb =>
        {
            tb.HasCheckConstraint("CK_QuizAttemptQuestions_Difficulty",
                "[Difficulty] IN ('Easy', 'Medium', 'Hard', 'Advanced')");
            tb.HasCheckConstraint("CK_QuizAttemptQuestions_QuestionType",
                "[QuestionType] IN ('MultipleChoice', 'TrueFalse', 'Essay')");
            // Mirrors the live constraint: an Essay has NO answer key, and every
            // other type has one. (CorrectOptionId must be NULLable in the table for
            // this to admit an Essay at all — see db/migrations/006 §2.)
            tb.HasCheckConstraint("CK_QuizAttemptQuestions_EssayHasNoKey",
                "([QuestionType] = 'Essay' AND [CorrectOptionId] IS NULL) "
              + "OR ([QuestionType] <> 'Essay' AND [CorrectOptionId] IS NOT NULL)");
            // Same rule as CK_Questions_Points: a question weighs at least 1.
            tb.HasCheckConstraint("CK_QuizAttemptQuestions_Points", "[Points] > 0");
        });

        entity.HasIndex(e => new { e.QuizAttemptId, e.QuestionId }, "UQ_QuizAttemptQuestions_AttemptId_QuestionId")
            .IsUnique();

        entity.HasIndex(e => e.QuestionId, "IX_QuizAttemptQuestions_QuestionId");

        entity.Property(e => e.Difficulty).HasMaxLength(20);
        entity.Property(e => e.QuestionType)
            .HasMaxLength(20)
            .HasDefaultValue("MultipleChoice");
        entity.Property(e => e.Points).HasDefaultValue((byte)1);

        entity.Property(e => e.CreatedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");

        // DB: ON DELETE CASCADE — a row here has no meaning outside its
        // attempt.
        entity.HasOne(d => d.QuizAttempt).WithMany(p => p.QuizAttemptQuestions)
            .HasForeignKey(d => d.QuizAttemptId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_QuizAttemptQuestions_QuizAttempts");

        // DB: no ON DELETE clause (NO ACTION) — Questions are durable
        // content and must not be orphaned by attempt records.
        entity.HasOne(d => d.Question).WithMany(p => p.QuizAttemptQuestions)
            .HasForeignKey(d => d.QuestionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptQuestions_Questions");

        // DB: no ON DELETE clause (NO ACTION) — the historical topic a past
        // attempt was classified under cannot be deleted out from under it.
        // NULL when the question had no topic at attempt start.
        entity.HasOne(d => d.Topic).WithMany()
            .HasForeignKey(d => d.TopicId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptQuestions_Topics");

        // Composite FK against UQ_QuestionOptions_QuestionId_Id, so the
        // snapshotted answer key is guaranteed to be an option of this very
        // question — and cannot be deleted while an attempt still grades
        // against it.
        entity.HasOne(d => d.CorrectOption).WithMany()
            .HasPrincipalKey(p => new { p.QuestionId, p.Id })
            .HasForeignKey(d => new { d.QuestionId, d.CorrectOptionId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptQuestions_QuestionId_CorrectOptionId");
    }
}