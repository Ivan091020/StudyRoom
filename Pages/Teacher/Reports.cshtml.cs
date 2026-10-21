using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Teacher;

[Authorize(Policy = "TeacherOnly")]
public class ReportsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly LearningProgressService _progressService;

    public ReportsModel(AppDbContext db, LearningProgressService progressService)
    {
        _db = db;
        _progressService = progressService;
    }

    [BindProperty(SupportsGet = true)]
    public int? DisciplineId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? GroupId { get; set; }

    public List<Discipline> Disciplines { get; set; } = new();
    public List<StudentGroup> Groups { get; set; } = new();
    public Discipline? SelectedDiscipline { get; set; }
    public List<Lecture> OrderedLectures { get; set; } = new();
    public List<StudentReportRow> StudentRows { get; set; } = new();
    public List<TestAttempt> RecentAttempts { get; set; } = new();

    public class StudentReportRow
    {
        public User Student { get; set; } = null!;
        public string GroupNames { get; set; } = string.Empty;
        public DisciplineProgressSummary Progress { get; set; } = null!;
        public int TotalAttempts { get; set; }
        public int FailedAttempts { get; set; }
        public double AverageBestTestScore { get; set; }
        public DateTime? LastActivity { get; set; }
        public bool IsLagging => Progress.CompletionPercentage < 100.0;
    }

    public async Task OnGetAsync()
    {
        Disciplines = await _db.Disciplines.OrderBy(d => d.Id).ToListAsync();
        Groups = await _db.StudentGroups.OrderBy(g => g.Name).ToListAsync();

        if (!DisciplineId.HasValue && Disciplines.Any())
        {
            DisciplineId = Disciplines.First().Id;
        }

        if (!DisciplineId.HasValue) return;

        SelectedDiscipline = Disciplines.FirstOrDefault(d => d.Id == DisciplineId.Value);
        if (SelectedDiscipline == null) return;

        OrderedLectures = await _progressService.GetOrderedDisciplineLecturesAsync(SelectedDiscipline.Id);

        var studentsQuery = _db.Users
            .Include(u => u.GroupMemberships)
            .ThenInclude(gm => gm.StudentGroup)
            .Where(u => u.Role == UserRole.Student);

        if (GroupId.HasValue)
        {
            studentsQuery = studentsQuery.Where(u => u.GroupMemberships.Any(gm => gm.StudentGroupId == GroupId.Value));
        }

        var students = await studentsQuery.OrderBy(u => u.FullName).ToListAsync();

        var allAttempts = await _db.TestAttempts
            .Include(ta => ta.Test)
            .ThenInclude(t => t.Lecture)
            .Include(ta => ta.Student)
            .Where(ta => ta.Test.Lecture.Topic.DisciplineId == SelectedDiscipline.Id)
            .OrderByDescending(ta => ta.AttemptDate)
            .ToListAsync();

        RecentAttempts = allAttempts.Take(20).ToList();

        foreach (var student in students)
        {
            var progress = await _progressService.SynchronizeAndGetDisciplineProgressAsync(student.Id, SelectedDiscipline.Id);
            var stAttempts = allAttempts.Where(a => a.StudentId == student.Id).ToList();

            var bestScoresPerTest = stAttempts
                .GroupBy(a => a.TestId)
                .Select(g => g.Max(x => x.ScorePercentage))
                .ToList();

            StudentRows.Add(new StudentReportRow
            {
                Student = student,
                GroupNames = student.GroupMemberships.Any()
                    ? string.Join(", ", student.GroupMemberships.Select(gm => gm.StudentGroup.Name))
                    : "Без группы",
                Progress = progress,
                TotalAttempts = stAttempts.Count,
                FailedAttempts = stAttempts.Count(a => a.Status == TestAttemptStatus.Failed),
                AverageBestTestScore = bestScoresPerTest.Any() ? Math.Round(bestScoresPerTest.Average(), 1) : 0,
                LastActivity = stAttempts.Any() ? stAttempts.Max(a => a.AttemptDate) : null
            });
        }
    }

    public IActionResult OnPost()
    {
        return RedirectToPage(new { DisciplineId, GroupId });
    }

    /// <summary>
    /// Ручное принудительное открытие лекции конкретному студенту с записью в журнал событий (AuditLogs).
    /// </summary>
    public async Task<IActionResult> OnPostManualUnlockAsync(int studentId, int lectureId, int? disciplineId, int? groupId)
    {
        var student = await _db.Users.FindAsync(studentId);
        var lecture = await _db.Lectures.FindAsync(lectureId);
        if (student == null || lecture == null)
        {
            TempData["ErrorMessage"] = "Студент или лекция не найдены.";
            return RedirectToPage(new { DisciplineId = disciplineId, GroupId = groupId });
        }

        var lp = await _db.LectureProgresses
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.LectureId == lectureId);

        if (lp == null)
        {
            lp = new LectureProgress
            {
                StudentId = studentId,
                LectureId = lectureId,
                Status = LectureStatus.Open,
                IsManuallyUnlocked = true,
                UpdatedAt = DateTime.UtcNow
            };
            _db.LectureProgresses.Add(lp);
        }
        else
        {
            lp.IsManuallyUnlocked = true;
            if (lp.Status == LectureStatus.Locked)
            {
                lp.Status = LectureStatus.Open;
            }
            lp.UpdatedAt = DateTime.UtcNow;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            TeacherId = User.GetUserId(),
            TargetStudentId = studentId,
            LectureId = lectureId,
            ActionType = "ManualUnlock",
            Description = $"Преподаватель вручную открыл лекцию «{lecture.Title}» для студента {student.FullName}."
        });

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Лекция «{lecture.Title}» принудительно открыта для студента {student.FullName}. Событие зафиксировано в AuditLogs.";
        return RedirectToPage(new { DisciplineId = disciplineId, GroupId = groupId });
    }

    /// <summary>
    /// Добавление дополнительных попыток тестирования и повторное открытие лекции студенту.
    /// </summary>
    public async Task<IActionResult> OnPostGrantExtraAttemptsAsync(int studentId, int lectureId, int? disciplineId, int? groupId)
    {
        var student = await _db.Users.FindAsync(studentId);
        var lecture = await _db.Lectures.FindAsync(lectureId);
        if (student == null || lecture == null)
        {
            TempData["ErrorMessage"] = "Студент или лекция не найдены.";
            return RedirectToPage(new { DisciplineId = disciplineId, GroupId = groupId });
        }

        var lp = await _db.LectureProgresses
            .FirstOrDefaultAsync(x => x.StudentId == studentId && x.LectureId == lectureId);

        if (lp == null)
        {
            lp = new LectureProgress
            {
                StudentId = studentId,
                LectureId = lectureId,
                Status = LectureStatus.Open,
                IsManuallyUnlocked = true,
                ExtraAttemptsGranted = 3,
                UpdatedAt = DateTime.UtcNow
            };
            _db.LectureProgresses.Add(lp);
        }
        else
        {
            lp.ExtraAttemptsGranted += 3;
            lp.IsManuallyUnlocked = true;
            lp.UpdatedAt = DateTime.UtcNow;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            TeacherId = User.GetUserId(),
            TargetStudentId = studentId,
            LectureId = lectureId,
            ActionType = "GrantExtraAttempts",
            Description = $"Преподаватель предоставил +3 дополнительные попытки тестирования по лекции «{lecture.Title}» студенту {student.FullName}."
        });

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Студенту {student.FullName} добавлено +3 попытки по лекции «{lecture.Title}».";
        return RedirectToPage(new { DisciplineId = disciplineId, GroupId = groupId });
    }
}

