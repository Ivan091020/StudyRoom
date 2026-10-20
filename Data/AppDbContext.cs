using Microsoft.EntityFrameworkCore;
using StudyRoom.Models;

namespace StudyRoom.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<StudentGroup> StudentGroups => Set<StudentGroup>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<Discipline> Disciplines => Set<Discipline>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Lecture> Lectures => Set<Lecture>();
    public DbSet<ControlQuestion> ControlQuestions => Set<ControlQuestion>();
    public DbSet<Test> Tests => Set<Test>();
    public DbSet<TestQuestion> TestQuestions => Set<TestQuestion>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();
    public DbSet<TestAttempt> TestAttempts => Set<TestAttempt>();
    public DbSet<LectureProgress> LectureProgresses => Set<LectureProgress>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Users
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasIndex(u => u.Login).IsUnique();
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);
        });

        // 2. StudentGroups
        modelBuilder.Entity<StudentGroup>(entity =>
        {
            entity.ToTable("StudentGroups");
            entity.HasIndex(g => g.Name).IsUnique();
        });

        // 3. GroupMemberships
        modelBuilder.Entity<GroupMembership>(entity =>
        {
            entity.ToTable("GroupMemberships");
            entity.HasIndex(gm => new { gm.StudentId, gm.StudentGroupId }).IsUnique();

            entity.HasOne(gm => gm.Student)
                .WithMany(u => u.GroupMemberships)
                .HasForeignKey(gm => gm.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(gm => gm.StudentGroup)
                .WithMany(g => g.GroupMemberships)
                .HasForeignKey(gm => gm.StudentGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 4. Disciplines
        modelBuilder.Entity<Discipline>(entity =>
        {
            entity.ToTable("Disciplines");
        });

        // 5. Topics
        modelBuilder.Entity<Topic>(entity =>
        {
            entity.ToTable("Topics");
            entity.HasOne(t => t.Discipline)
                .WithMany(d => d.Topics)
                .HasForeignKey(t => t.DisciplineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 6. Lectures
        modelBuilder.Entity<Lecture>(entity =>
        {
            entity.ToTable("Lectures");
            entity.HasOne(l => l.Topic)
                .WithMany(t => t.Lectures)
                .HasForeignKey(l => l.TopicId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 7. ControlQuestions
        modelBuilder.Entity<ControlQuestion>(entity =>
        {
            entity.ToTable("ControlQuestions");
            entity.Property(q => q.QuestionType).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(q => q.Lecture)
                .WithMany(l => l.ControlQuestions)
                .HasForeignKey(q => q.LectureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 8. Tests
        modelBuilder.Entity<Test>(entity =>
        {
            entity.ToTable("Tests");
            entity.HasOne(t => t.Lecture)
                .WithMany(l => l.Tests)
                .HasForeignKey(t => t.LectureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 9. TestQuestions
        modelBuilder.Entity<TestQuestion>(entity =>
        {
            entity.ToTable("TestQuestions");
            entity.Property(q => q.QuestionType).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(q => q.Test)
                .WithMany(t => t.Questions)
                .HasForeignKey(q => q.TestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 10. AnswerOptions
        modelBuilder.Entity<AnswerOption>(entity =>
        {
            entity.ToTable("AnswerOptions");
            entity.HasOne(o => o.Question)
                .WithMany(q => q.AnswerOptions)
                .HasForeignKey(o => o.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 11. StudentAnswers
        modelBuilder.Entity<StudentAnswer>(entity =>
        {
            entity.ToTable("StudentAnswers");
            entity.HasIndex(sa => new { sa.StudentId, sa.QuestionId }).IsUnique();

            entity.HasOne(sa => sa.Student)
                .WithMany(u => u.StudentAnswers)
                .HasForeignKey(sa => sa.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(sa => sa.Question)
                .WithMany(q => q.StudentAnswers)
                .HasForeignKey(sa => sa.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 12. TestAttempts
        modelBuilder.Entity<TestAttempt>(entity =>
        {
            entity.ToTable("TestAttempts");
            entity.Property(ta => ta.Status).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(ta => ta.Student)
                .WithMany(u => u.TestAttempts)
                .HasForeignKey(ta => ta.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ta => ta.Test)
                .WithMany(t => t.TestAttempts)
                .HasForeignKey(ta => ta.TestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 13. LectureProgress
        modelBuilder.Entity<LectureProgress>(entity =>
        {
            entity.ToTable("LectureProgress");
            entity.HasIndex(lp => new { lp.StudentId, lp.LectureId }).IsUnique();
            entity.Property(lp => lp.Status).HasConversion<string>().HasMaxLength(32);

            entity.HasOne(lp => lp.Student)
                .WithMany(u => u.LectureProgresses)
                .HasForeignKey(lp => lp.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(lp => lp.Lecture)
                .WithMany(l => l.LectureProgresses)
                .HasForeignKey(lp => lp.LectureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 14. AuditLogs
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");

            entity.HasOne(a => a.Teacher)
                .WithMany()
                .HasForeignKey(a => a.TeacherId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.TargetStudent)
                .WithMany()
                .HasForeignKey(a => a.TargetStudentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.Lecture)
                .WithMany()
                .HasForeignKey(a => a.LectureId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

