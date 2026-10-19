using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Student;

[Authorize(Policy = "StudentOrTeacher")]
public class LectureModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly LearningProgressService _progressService;

    public LectureModel(AppDbContext db, LearningProgressService progressService)
    {
        _db = db;
        _progressService = progressService;
    }

    public Lecture Lecture { get; set; } = null!;
    public LectureStatus CurrentStatus { get; set; }
    public Dictionary<int, StudentAnswer> StudentAnswersByQuestion { get; set; } = new();
    public Test? LectureTest { get; set; }
    public List<TestAttempt> LectureTestAttempts { get; set; } = new();
    public int AllowedAttempts { get; set; }
    public Lecture? PreviousLecture { get; set; }
    public Lecture? NextLecture { get; set; }
    public bool IsNextLectureUnlocked { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        return await LoadAndAuthorizeLectureAsync(id);
    }

    public async Task<IActionResult> OnPostSubmitControlAnswersAsync(int id)
    {
        var userId = User.GetUserId();
        if (!User.IsTeacher())
        {
            var access = await _progressService.CheckStudentAccessToLectureAsync(userId, id);
            if (!access.Allowed)
            {
                return RedirectToPage("/Account/AccessDenied", new { Reason = access.DenialReason });
            }
        }

        var lecture = await _db.Lectures
            .Include(l => l.Topic)
            .Include(l => l.ControlQuestions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id))
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lecture == null)
            return NotFound();

        var existingAnswers = await _db.StudentAnswers
            .Where(sa => sa.StudentId == userId && sa.Question.LectureId == id)
            .ToDictionaryAsync(sa => sa.QuestionId);

        int autoApprovedCount = 0;
        int pendingTeacherCount = 0;
        int failedCount = 0;

        foreach (var q in lecture.ControlQuestions)
        {
            string rawAnswer = ExtractAnswerFromForm(q);
            if (string.IsNullOrWhiteSpace(rawAnswer))
            {
                failedCount++;
                continue;
            }

            var (isApproved, checkedByTeacher) = _progressService.CheckControlQuestionAnswer(q, rawAnswer);

            if (existingAnswers.TryGetValue(q.Id, out var existing))
            {
                // Если развёрнутый ответ уже был одобрен преподавателем и текст не изменился — сохраняем одобрение
                if (q.QuestionType == QuestionType.ExtendedText &&
                    existing.CheckedByTeacher &&
                    existing.IsApproved == true &&
                    string.Equals(existing.AnswerText.Trim(), rawAnswer.Trim(), StringComparison.Ordinal))
                {
                    autoApprovedCount++;
                    continue;
                }

                existing.AnswerText = rawAnswer;
                existing.IsApproved = isApproved;
                existing.CheckedByTeacher = checkedByTeacher;
                existing.SubmittedAt = DateTime.UtcNow;
            }
            else
            {
                _db.StudentAnswers.Add(new StudentAnswer
                {
                    StudentId = userId,
                    QuestionId = q.Id,
                    AnswerText = rawAnswer,
                    IsApproved = isApproved,
                    CheckedByTeacher = checkedByTeacher,
                    SubmittedAt = DateTime.UtcNow
                });
            }

            if (isApproved == true) autoApprovedCount++;
            else if (isApproved == null) pendingTeacherCount++;
            else failedCount++;
        }

        await _db.SaveChangesAsync();
        await _progressService.SynchronizeAndGetDisciplineProgressAsync(userId, lecture.Topic.DisciplineId);

        if (failedCount == 0 && pendingTeacherCount == 0)
        {
            TempData["SuccessMessage"] = $"Все контрольные вопросы ({autoApprovedCount} из {lecture.ControlQuestions.Count}) успешно зачтены! Теперь вы можете пройти итоговый тест.";
        }
        else if (pendingTeacherCount > 0 && failedCount == 0)
        {
            TempData["SuccessMessage"] = $"Автоматические вопросы зачтены ({autoApprovedCount}), а развёрнутых ответов на проверке у преподавателя: {pendingTeacherCount}.";
        }
        else
        {
            TempData["ErrorMessage"] = $"Зачтено ответов: {autoApprovedCount}. Требуют исправления: {failedCount}. Проверьте выделенные вопросы ниже.";
        }

        return RedirectToPage(new { id });
    }

    private string ExtractAnswerFromForm(ControlQuestion q)
    {
        var form = Request.Form;
        switch (q.QuestionType)
        {
            case QuestionType.MultipleChoice:
            {
                var selected = form[$"q_{q.Id}"].ToArray();
                return string.Join(";", selected);
            }
            case QuestionType.Matching:
            {
                var pairs = ParseMatchingPairs(q.OptionsJson);
                var resultPairs = new List<string>();
                for (int i = 0; i < pairs.Count; i++)
                {
                    var rightVal = form[$"q_{q.Id}_match_{i}"].ToString();
                    if (!string.IsNullOrWhiteSpace(rightVal))
                    {
                        resultPairs.Add($"{pairs[i].Left}={rightVal}");
                    }
                }
                return string.Join(";", resultPairs);
            }
            default:
                return form[$"q_{q.Id}"].ToString().Trim();
        }
    }

    public static List<(string Left, string Right)> ParseMatchingPairs(string? optionsJson)
    {
        var list = new List<(string Left, string Right)>();
        if (string.IsNullOrWhiteSpace(optionsJson)) return list;

        var rawItems = optionsJson.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in rawItems)
        {
            var parts = item.Split('=', 2);
            if (parts.Length == 2)
            {
                list.Add((parts[0].Trim(), parts[1].Trim()));
            }
        }
        return list;
    }

    private async Task<IActionResult> LoadAndAuthorizeLectureAsync(int id)
    {
        var userId = User.GetUserId();

        // Строгая серверная проверка последовательного открытия лекций
        if (!User.IsTeacher())
        {
            var access = await _progressService.CheckStudentAccessToLectureAsync(userId, id);
            if (!access.Allowed)
            {
                return RedirectToPage("/Account/AccessDenied", new { Reason = access.DenialReason });
            }
            CurrentStatus = access.CurrentStatus;
        }

        var lecture = await _db.Lectures
            .Include(l => l.Topic)
            .ThenInclude(t => t.Discipline)
            .Include(l => l.ControlQuestions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id))
            .Include(l => l.Tests)
            .ThenInclude(t => t.Questions)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lecture == null)
        {
            return NotFound();
        }

        Lecture = lecture;
        LectureTest = lecture.Tests.FirstOrDefault();

        var orderedLectures = await _progressService.GetOrderedDisciplineLecturesAsync(lecture.Topic.DisciplineId);
        var summary = await _progressService.SynchronizeAndGetDisciplineProgressAsync(userId, lecture.Topic.DisciplineId);
        CurrentStatus = summary.LectureStatuses.GetValueOrDefault(id, LectureStatus.Open);

        var idx = orderedLectures.FindIndex(l => l.Id == id);
        if (idx > 0) PreviousLecture = orderedLectures[idx - 1];
        if (idx >= 0 && idx + 1 < orderedLectures.Count)
        {
            NextLecture = orderedLectures[idx + 1];
            var nextStatus = summary.LectureStatuses.GetValueOrDefault(NextLecture.Id, LectureStatus.Locked);
            IsNextLectureUnlocked = nextStatus != LectureStatus.Locked || User.IsTeacher();
        }

        StudentAnswersByQuestion = await _db.StudentAnswers
            .Where(sa => sa.StudentId == userId && sa.Question.LectureId == id)
            .ToDictionaryAsync(sa => sa.QuestionId);

        var progressRecord = await _db.LectureProgresses
            .FirstOrDefaultAsync(lp => lp.StudentId == userId && lp.LectureId == id);

        if (LectureTest != null)
        {
            AllowedAttempts = LectureTest.MaxAttempts + (progressRecord?.ExtraAttemptsGranted ?? 0);
            LectureTestAttempts = await _db.TestAttempts
                .Where(ta => ta.StudentId == userId && ta.TestId == LectureTest.Id)
                .OrderByDescending(ta => ta.AttemptDate)
                .ToListAsync();
        }

        return Page();
    }
}
