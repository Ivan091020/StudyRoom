using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Student;

[Authorize(Policy = "StudentOrTeacher")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly LearningProgressService _progressService;

    public IndexModel(AppDbContext db, LearningProgressService progressService)
    {
        _db = db;
        _progressService = progressService;
    }

    public User? CurrentUser { get; set; }
    public string GroupNames { get; set; } = "Не назначена";
    public List<DisciplineCardViewModel> DisciplineCards { get; set; } = new();
    public List<TestAttempt> RecentAttempts { get; set; } = new();
    public List<StudentAnswer> RecentAnswers { get; set; } = new();

    public class DisciplineCardViewModel
    {
        public Discipline Discipline { get; set; } = null!;
        public DisciplineProgressSummary Progress { get; set; } = null!;
        public int TopicCount { get; set; }
    }

    public async Task OnGetAsync()
    {
        var userId = User.GetUserId();
        CurrentUser = await _db.Users
            .Include(u => u.GroupMemberships)
            .ThenInclude(gm => gm.StudentGroup)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (CurrentUser?.GroupMemberships.Any() == true)
        {
            GroupNames = string.Join(", ", CurrentUser.GroupMemberships.Select(gm => gm.StudentGroup.Name));
        }

        var disciplines = await _db.Disciplines
            .Include(d => d.Topics)
            .OrderBy(d => d.Id)
            .ToListAsync();

        foreach (var d in disciplines)
        {
            var progress = await _progressService.SynchronizeAndGetDisciplineProgressAsync(userId, d.Id);
            DisciplineCards.Add(new DisciplineCardViewModel
            {
                Discipline = d,
                Progress = progress,
                TopicCount = d.Topics.Count
            });
        }

        RecentAttempts = await _db.TestAttempts
            .Include(ta => ta.Test)
            .ThenInclude(t => t.Lecture)
            .ThenInclude(l => l.Topic)
            .ThenInclude(top => top.Discipline)
            .Where(ta => ta.StudentId == userId)
            .OrderByDescending(ta => ta.AttemptDate)
            .Take(15)
            .ToListAsync();

        RecentAnswers = await _db.StudentAnswers
            .Include(sa => sa.Question)
            .ThenInclude(q => q.Lecture)
            .Where(sa => sa.StudentId == userId)
            .OrderByDescending(sa => sa.SubmittedAt)
            .Take(10)
            .ToListAsync();
    }
}

