using System;
using System.Collections.Generic;
using ContentDA.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentDA.Context;

public partial class ContentDbContext : DbContext
{
    public ContentDbContext()
    {
    }

    public ContentDbContext(DbContextOptions<ContentDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ContentType> ContentTypes { get; set; }

    public virtual DbSet<Lesson> Lessons { get; set; }

    public virtual DbSet<LessonContent> LessonContents { get; set; }

    public virtual DbSet<Level> Levels { get; set; }

    public virtual DbSet<LearningProgress> LearningProgresses { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // الكونكشن سترينج الحقيقي بييجي من appsettings.json عن طريق الـ DI (شوف ContentModule).
        // السطر ده بيفضل بس Fallback لو حد شغّل الـ DbContext مباشرة من غير DI.
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=.;Database=VoltDB;Trusted_Connection=True;TrustServerCertificate=True");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContentType>(entity =>
        {
            entity.ToTable("ContentTypes", "LearningContent");

            entity.HasIndex(e => e.Name, "UQ_ContentTypes_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.ToTable("Lessons", "LearningContent");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.LessonType).HasMaxLength(30).HasDefaultValue("lesson");

            entity.HasOne(d => d.Level).WithMany(p => p.Lessons)
                .HasForeignKey(d => d.LevelId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Lessons_Levels");
        });

        modelBuilder.Entity<LessonContent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_ContentItems");

            entity.ToTable("LessonContents", "LearningContent");

            entity.Property(e => e.MediaUrl).HasMaxLength(500);

            entity.HasOne(d => d.ContentType).WithMany(p => p.LessonContents)
                .HasForeignKey(d => d.ContentTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_LessonContents_ContentTypes");

            entity.HasOne(d => d.Lesson).WithMany(p => p.LessonContents)
                .HasForeignKey(d => d.LessonId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_LessonContents_Lessons");
        });

        modelBuilder.Entity<Level>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Levels__3214EC07D9877EB1");

            entity.ToTable("Levels", "LearningContent");

            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Title).HasMaxLength(200);
        });

        modelBuilder.Entity<LearningProgress>(entity =>
        {
            entity.ToTable("LearningProgress", "LearningContent");

            entity.HasIndex(e => new { e.UserId, e.LessonId }, "UQ_LearningProgress_UserId_LessonId").IsUnique();

            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Lesson).WithMany(p => p.LearningProgresses)
                .HasForeignKey(d => d.LessonId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_LearningProgress_Lesson");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
