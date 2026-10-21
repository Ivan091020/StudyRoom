using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Teacher;

[Authorize(Policy = "TeacherOnly")]
public class ReviewsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly LearningProgressService _progressService;

    public ReviewsModel(AppDbContext db, LearningProgressService progressService)
    {
        _db = db;
        _progressService = progressService;
    }

    [BindProperty(SupportsGet = true)]
    public bool ShowAll { get; set; }

    public List<StudentAnswer> Answers { get; set; } = new();

    public async Task OnGetAsync()
    {
        var query = _db.StudentAnswers
            .Include(sa => sa.Student)
            .Include(sa => sa.Question)
            .ThenInclude(q => q.Lecture)
            .ThenInclude(l => l.Topic)
            .ThenInclude(t => t.Discipline)
            .AsQueryable();

        if (!ShowAll)
        {
            query = query.Where(sa => sa.IsApproved == null || sa.Question.QuestionType == QuestionType.ExtendedText);
        }

        Answers = await query
            .OrderBy(sa => sa.IsApproved == null ? 0 : 1)
            .ThenByDescending(sa => sa.SubmittedAt)
            .ToListAsync();
    }

    public IActionResult OnPost()
    {
        return RedirectToPage(new { ShowAll });
    }

    public async Task<IActionResult> OnPostReviewAnswerAsync(int answerId, bool approve, string? teacherComment)
    {
        var answer = await _db.StudentAnswers
            .Include(sa => sa.Student)
            .Include(sa => sa.Question)
            .ThenInclude(q => q.Lecture)
            .ThenInclude(l => l.Topic)
            .FirstOrDefaultAsync(sa => sa.Id == answerId);

        if (answer == null)
        {
            TempData["ErrorMessage"] = "Ответ студента не найден.";
            return RedirectToPage(new { ShowAll });
        }

        answer.IsApproved = approve;
        answer.CheckedByTeacher = true;
        answer.TeacherComment = string.IsNullOrWhiteSpace(teacherComment) ? null : teacherComment.Trim();
        answer.CheckedAt = DateTime.UtcNow;

        _db.AuditLogs.Add(new AuditLog
        {
            TeacherId = User.GetUserId(),
            TargetStudentId = answer.StudentId,
            LectureId = answer.Question.LectureId,
            ActionType = "ReviewAnswer",
            Description = $"Преподаватель {(approve ? "зачёл" : "отклонил")} ответ студента {answer.Student.FullName} на вопрос лекции «{answer.Question.Lecture.Title}»."
        });

        await _db.SaveChangesAsync();

        // Пересчитываем прогресс студента (при выполнении всех условий откроется следующая лекция)
        await _progressService.SynchronizeAndGetDisciplineProgressAsync(
            answer.StudentId,
            answer.Question.Lecture.Topic.DisciplineId);

        TempData["SuccessMessage"] = $"Ответ студента {answer.Student.FullName} {(approve ? "зачтён" : "отклонён")}. Прогресс студента обновлён.";
        return RedirectToPage(new { ShowAll });
    }
}

