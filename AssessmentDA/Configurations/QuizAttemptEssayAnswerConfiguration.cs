using AssessmentDA.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AssessmentDA.Configurations;

public class QuizAttemptEssayAnswerConfiguration : IEntityTypeConfiguration<QuizAttemptEssayAnswer>
{
    public void Configure(EntityTypeBuilder<QuizAttemptEssayAnswer> entity)
    {
        entity.ToTable("QuizAttemptEssayAnswers", "Assessment", tb =>
        {
            tb.HasCheckConstraint("CK_QuizAttemptEssayAnswers_Status",
                "[Status] IN ('Pending', 'Graded', 'NotGraded')");
            // The AI is the only grader.
            tb.HasCheckConstraint("CK_QuizAttemptEssayAnswers_GradedBy",
                "[GradedBy] IS NULL OR [GradedBy] = 'Ai'");
            tb.HasCheckConstraint("CK_QuizAttemptEssayAnswers_GradedIsComplete",
                "([Status] = 'Graded' AND [AwardedPoints] IS NOT NULL AND [GradedAt] IS NOT NULL AND [GradedBy] IS NOT NULL) "
              + "OR ([Status] <> 'Graded' AND [AwardedPoints] IS NULL AND [GradedAt] IS NULL AND [GradedBy] IS NULL)");
            tb.HasCheckConstraint("CK_QuizAttemptEssayAnswers_AiOutcome",
                "[AiOutcome] IS NULL OR [AiOutcome] IN ('Accepted', 'Declined', 'Failed')");
            // Pending has no outcome yet; a final status always says how it ended.
            tb.HasCheckConstraint("CK_QuizAttemptEssayAnswers_OutcomeMatchesStatus",
                "([Status] = 'Pending' AND [AiOutcome] IS NULL) "
              + "OR ([Status] = 'Graded' AND [AiOutcome] = 'Accepted') "
              + "OR ([Status] = 'NotGraded' AND [AiOutcome] IN ('Declined', 'Failed'))");
            tb.HasCheckConstraint("CK_QuizAttemptEssayAnswers_AiConfidence",
                "[AiConfidence] IS NULL OR [AiConfidence] BETWEEN 0 AND 1");
            tb.HasCheckConstraint("CK_QuizAttemptEssayAnswers_AwardedWithinMax",
                "[AwardedPoints] IS NULL OR [AwardedPoints] <= [MaxPoints]");
        });

        // Serves the background evaluator: essays still waiting for the AI.
        entity.HasIndex(e => e.CreatedAt, "IX_QuizAttemptEssayAnswers_AiDue")
            .HasFilter("([Status]=N'Pending' AND [AiOutcome] IS NULL)")
            .IncludeProperties(e => new { e.AiLastAttemptAt, e.AiEvaluationAttempts });

        entity.Property(e => e.MaxPoints).HasDefaultValue((byte)1);

        // Created by db/migrations/008.
        entity.Property(e => e.LanguageCode).HasMaxLength(5).HasDefaultValue("ar");
        entity.Property(e => e.AiOutcome).HasMaxLength(20);
        entity.Property(e => e.AiEvaluationAttempts).HasDefaultValue((byte)0);
        entity.Property(e => e.AiLastAttemptAt).HasPrecision(3);
        entity.Property(e => e.AiConfidence).HasColumnType("decimal(3, 2)");

        entity.HasOne<Language>().WithMany()
            .HasForeignKey(e => e.LanguageCode)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptEssayAnswers_Languages");

        entity.HasIndex(e => new { e.QuizAttemptId, e.QuestionId },
            "UQ_QuizAttemptEssayAnswers_AttemptId_QuestionId").IsUnique();

        entity.HasIndex(e => e.QuestionId, "IX_QuizAttemptEssayAnswers_QuestionId");

        entity.HasIndex(e => e.Status, "IX_QuizAttemptEssayAnswers_Status")
            .HasFilter("([Status]='Pending')");

        entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Pending");
        entity.Property(e => e.GradedBy).HasMaxLength(20);
        entity.Property(e => e.GradedAt).HasPrecision(3);
        entity.Property(e => e.CreatedAt)
            .HasPrecision(3)
            .HasDefaultValueSql("(sysutcdatetime())");

        // DB: ON DELETE CASCADE — an essay answer has no meaning outside its attempt.
        entity.HasOne(d => d.QuizAttempt).WithMany(p => p.QuizAttemptEssayAnswers)
            .HasForeignKey(d => d.QuizAttemptId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_QuizAttemptEssayAnswers_QuizAttempts");

        entity.HasOne(d => d.Question).WithMany(p => p.QuizAttemptEssayAnswers)
            .HasForeignKey(d => d.QuestionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptEssayAnswers_Questions");

        // The essay must belong to a question this attempt actually contained.
        entity.HasOne<QuizAttemptQuestion>().WithMany()
            .HasPrincipalKey(p => new { p.QuizAttemptId, p.QuestionId })
            .HasForeignKey(d => new { d.QuizAttemptId, d.QuestionId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_QuizAttemptEssayAnswers_QuizAttemptQuestions");
    }
}
