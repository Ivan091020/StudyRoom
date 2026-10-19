using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Student;

[Authorize(Policy = "StudentOrTeacher")]
public class DisciplineModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly LearningProgressService _progressService;

    public DisciplineModel(AppDbContext db, LearningProgressService progressService)
    {
        _db = db;
        _progressService = progressService;
    }

    public Discipline Discipline { get; set; } = null!;
    public DisciplineProgressSummary Progress { get; set; } = null!;
    public Dictionary<int, int> AnsweredQuestionsByLecture { get; set; } = new();
    public Dictionary<int, double?> BestTestScoreByLecture { get; set; } = new();
    public Dictionary<int, int> AttemptsCountByLecture { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var discipline = await _db.Disciplines
            .Include(d => d.Topics.OrderBy(t => t.OrderIndex).ThenBy(t => t.Id))
            .ThenInclude(t => t.Lectures.OrderBy(l => l.OrderIndex).ThenBy(l => l.Id))
            .ThenInclude(l => l.ControlQuestions)
            .Include(d => d.Topics)
            .ThenInclude(t => t.Lectures)
            .ThenInclude(l => l.Tests)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (discipline == null)
        {
            return NotFound();
        }

        Discipline = discipline;
        var userId = User.GetUserId();
        Progress = await _progressService.SynchronizeAndGetDisciplineProgressAsync(userId, discipline.Id);

        var answers = await _db.StudentAnswers
            .Include(sa => sa.Question)
            .Where(sa => sa.StudentId == userId && sa.Question.Lecture.Topic.DisciplineId == discipline.Id)
            .ToListAsync();

        var attempts = await _db.TestAttempts
            .Include(ta => ta.Test)
            .Where(ta => ta.StudentId == userId && ta.Test.Lecture.Topic.DisciplineId == discipline.Id)
            .ToListAsync();

        foreach (var lec in discipline.Topics.SelectMany(t => t.Lectures))
        {
            AnsweredQuestionsByLecture[lec.Id] = answers.Count(a => a.Question.LectureId == lec.Id && a.IsApproved == true);
            var lecAttempts = attempts.Where(a => a.Test.LectureId == lec.Id).ToList();
            AttemptsCountByLecture[lec.Id] = lecAttempts.Count;
            BestTestScoreByLecture[lec.Id] = lecAttempts.Any() ? lecAttempts.Max(a => a.ScorePercentage) : null;
        }

        return Page();
    }
}
