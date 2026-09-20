using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class QuestionHintConfiguration : IEntityTypeConfiguration<QuestionHint>
{
    public void Configure(EntityTypeBuilder<QuestionHint> entity)
    {
        entity.ToTable("QuestionHints", "Assessment", tb =>
        {
            tb.HasCheckConstraint("CK_QuestionHints_HintSequence", "[HintSequence] > 0");
            // Null for a hint generated after a submission; 1..5 for the Hint button.
            tb.HasCheckConstraint("CK_QuestionHints_AttemptNumber",
                "[AttemptNumber] IS NULL OR [AttemptNumber] BETWEEN 1 AND 5");
        });

        // One chain per attempt + question + language, whichever way the hint was
        // produced. Replaces the old mistake-scoped uniqueness (db/migrations/009).
        entity.HasIndex(e => new { e.QuizAttemptId, e.QuestionId, e.LanguageCode, e.HintSequence },
            "UQ_QuestionHints_AttemptId_QuestionId_Language_Sequence").IsUnique();

        // One Hint-button hint per level per attempt + question, in ANY language.
        // The index above is per language, so two presses at the same moment (in
        // two languages, or in one where the second read the sequence after the
        // first saved) could both store level 1. Filtered: post-submission hints
        // have no level (db/migrations/002).
        entity.HasIndex(e => new { e.QuizAttemptId, e.QuestionId, e.AttemptNumber },
                "UQ_QuestionHints_AttemptId_QuestionId_AttemptNumber")
            .IsUnique()
            .HasFilter("[AttemptNumber] IS NOT NULL");

        entity.Property(e => e.LanguageCode)
            .HasMaxLength(5)
            .HasDefaultValue("ar");

        entity.HasOne<Language>().WithMany()
            .HasForeignKey(e => e.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuestionHints_Languages");

        entity.Property(e => e.GeneratedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");

        // The hint's question must be one the attempt actually contains. This is
        // also the cascade path: deleting an attempt removes its hints.
        entity.HasOne(d => d.QuizAttemptQuestion).WithMany()
            .HasPrincipalKey(p => new { p.QuizAttemptId, p.QuestionId })
            .HasForeignKey(d => new { d.QuizAttemptId, d.QuestionId })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_QuestionHints_QuizAttemptQuestions");

        // Optional now (a Hint-button hint has no mistake), and NO ACTION so there
        // is exactly one cascade path from QuizAttempts down to a hint.
        entity.HasOne(d => d.QuizAttemptMistake).WithMany(p => p.QuestionHints)
            .HasForeignKey(d => d.QuizAttemptMistakeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuestionHints_QuizAttemptMistakes");
    }
}
