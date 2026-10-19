using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;

namespace StudyRoom.Pages.Teacher;

[Authorize(Policy = "TeacherOnly")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public int DisciplineCount { get; set; }
    public int TopicCount { get; set; }
    public int LectureCount { get; set; }
    public int StudentCount { get; set; }
    public int GroupCount { get; set; }
    public int PendingReviewCount { get; set; }
    public int TotalAttemptsCount { get; set; }

    public List<AuditLog> RecentAuditLogs { get; set; } = new();
    public List<StudentAnswer> PendingAnswers { get; set; } = new();

    public async Task OnGetAsync()
    {
        DisciplineCount = await _db.Disciplines.CountAsync();
        TopicCount = await _db.Topics.CountAsync();
        LectureCount = await _db.Lectures.CountAsync();
        StudentCount = await _db.Users.CountAsync(u => u.Role == UserRole.Student);
        GroupCount = await _db.StudentGroups.CountAsync();
        PendingReviewCount = await _db.StudentAnswers.CountAsync(sa => sa.IsApproved == null);
        TotalAttemptsCount = await _db.TestAttempts.CountAsync();

        PendingAnswers = await _db.StudentAnswers
            .Include(sa => sa.Student)
            .Include(sa => sa.Question)
            .ThenInclude(q => q.Lecture)
            .Where(sa => sa.IsApproved == null)
            .OrderByDescending(sa => sa.SubmittedAt)
            .Take(5)
            .ToListAsync();

        RecentAuditLogs = await _db.AuditLogs
            .Include(a => a.Teacher)
            .Include(a => a.TargetStudent)
            .Include(a => a.Lecture)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .ToListAsync();
    }
}
