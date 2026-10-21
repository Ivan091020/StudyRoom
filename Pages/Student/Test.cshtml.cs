using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Student;

[Authorize(Policy = "StudentOrTeacher")]
public class TestModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly LearningProgressService _progressService;

    public TestModel(AppDbContext db, LearningProgressService progressService)
    {
        _db = db;
        _progressService = progressService;
    }

    [BindProperty(SupportsGet = true)]
    public int? LectureId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? TestId { get; set; }

    public Lecture Lecture { get; set; } = null!;
    public Test Test { get; set; } = null!;
    public List<TestQuestion> DisplayedQuestions { get; set; } = new();
    public int UsedAttempts { get; set; }
    public int AllowedAttempts { get; set; }
    public long StartedAtUnixSeconds { get; set; }

    private async Task<int> ResolveLectureIdAsync(int? lectureId = null, int? id = null, int? testId = null)
    {
        int resolved = lectureId ?? id ?? LectureId ?? Id ?? 0;

        if (resolved <= 0 && int.TryParse(Request.Query["lectureId"], out var qLectureId) && qLectureId > 0)
            resolved = qLectureId;
        if (resolved <= 0 && int.TryParse(Request.Query["id"], out var qId) && qId > 0)
            resolved = qId;

        if (resolved <= 0 && Request.HasFormContentType)
        {
            if (int.TryParse(Request.Form["lectureId"], out var fLectureId) && fLectureId > 0)
                resolved = fLectureId;
            else if (int.TryParse(Request.Form["id"], out var fId) && fId > 0)
                resolved = fId;
        }

        if (resolved <= 0)
        {
            int tId = testId ?? TestId ?? 0;
            if (tId <= 0 && int.TryParse(Request.Query["testId"], out var qTestId))
                tId = qTestId;
            if (tId <= 0 && Request.HasFormContentType && int.TryParse(Request.Form["testId"], out var fTestId))
                tId = fTestId;

            if (tId > 0)
            {
                var testEntity = await _db.Tests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tId);
                if (testEntity != null)
                    resolved = testEntity.LectureId;
            }
        }

        return resolved;
    }

    public async Task<IActionResult> OnGetAsync(int? lectureId = null, int? id = null, int? testId = null)
    {
        var resolvedLectureId = await ResolveLectureIdAsync(lectureId, id, testId);
        if (resolvedLectureId <= 0)
        {
            TempData["ErrorMessage"] = "Не указан идентификатор лекции для прохождения теста.";
            return RedirectToPage("/Student/Index");
        }

        var userId = User.GetUserId();
        if (!User.IsTeacher())
        {
            var access = await _progressService.CheckStudentAccessToLectureAsync(userId, resolvedLectureId);
            if (!access.Allowed)
            {
                return RedirectToPage("/Account/AccessDenied", new { Reason = access.DenialReason });
            }
        }

        var lecture = await _db.Lectures
            .Include(l => l.Topic)
            .ThenInclude(t => t.Discipline)
            .Include(l => l.Tests)
            .ThenInclude(t => t.Questions)
            .ThenInclude(q => q.AnswerOptions)
            .FirstOrDefaultAsync(l => l.Id == resolvedLectureId);

        if (lecture == null)
        {
            TempData["ErrorMessage"] = "Запрашиваемая лекция не найдена.";
            return RedirectToPage("/Student/Index");
        }

        var test = lecture.Tests.FirstOrDefault();
        if (test == null)
        {
            TempData["ErrorMessage"] = "Для данной лекции тест ещё не создан.";
            return RedirectToPage("/Student/Lecture", new { id = resolvedLectureId });
        }

        Lecture = lecture;
        Test = test;

        var progress = await _db.LectureProgresses
            .FirstOrDefaultAsync(lp => lp.StudentId == userId && lp.LectureId == resolvedLectureId);

        AllowedAttempts = test.MaxAttempts + (progress?.ExtraAttemptsGranted ?? 0);
        UsedAttempts = await _db.TestAttempts.CountAsync(ta => ta.StudentId == userId && ta.TestId == test.Id);

        if (UsedAttempts >= AllowedAttempts && !User.IsTeacher())
        {
            TempData["ErrorMessage"] = $"Вы исчерпали доступное количество попыток ({AllowedAttempts}). Обратитесь к преподавателю для добавления попыток.";
            return RedirectToPage("/Student/Lecture", new { id = resolvedLectureId });
        }

        var questions = test.Questions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).ToList();
        if (test.ShuffleQuestions)
        {
            var rng = Random.Shared;
            questions = questions.OrderBy(_ => rng.Next()).ToList();
            foreach (var q in questions)
            {
                if (q.QuestionType == QuestionType.SingleChoice || q.QuestionType == QuestionType.MultipleChoice)
                {
                    q.AnswerOptions = q.AnswerOptions.OrderBy(_ => rng.Next()).ToList();
                }
            }
        }

        DisplayedQuestions = questions;
        StartedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Page();
    }

    public Task<IActionResult> OnPostSubmitTestAsync(int? lectureId = null, int? id = null, int? testId = null, long? startedAtUnixSeconds = null)
        => OnPostAsync(lectureId, id, testId, startedAtUnixSeconds);

    public async Task<IActionResult> OnPostAsync(int? lectureId = null, int? id = null, int? testId = null, long? startedAtUnixSeconds = null)
    {
        var resolvedLectureId = await ResolveLectureIdAsync(lectureId, id, testId);
        if (resolvedLectureId <= 0)
        {
            TempData["ErrorMessage"] = "Не удалось определить лекцию при отправке теста.";
            return RedirectToPage("/Student/Index");
        }

        var userId = User.GetUserId();
        if (!User.IsTeacher())
        {
            var access = await _progressService.CheckStudentAccessToLectureAsync(userId, resolvedLectureId);
            if (!access.Allowed)
            {
                return RedirectToPage("/Account/AccessDenied", new { Reason = access.DenialReason });
            }
        }

        var lecture = await _db.Lectures
            .Include(l => l.Topic)
            .Include(l => l.Tests)
            .ThenInclude(t => t.Questions)
            .ThenInclude(q => q.AnswerOptions)
            .FirstOrDefaultAsync(l => l.Id == resolvedLectureId);

        if (lecture == null)
        {
            TempData["ErrorMessage"] = "Запрашиваемая лекция не найдена.";
            return RedirectToPage("/Student/Index");
        }

        var test = lecture.Tests.FirstOrDefault();
        if (test == null)
        {
            TempData["ErrorMessage"] = "Тест для данной лекции не найден.";
            return RedirectToPage("/Student/Lecture", new { id = resolvedLectureId });
        }

        var progress = await _db.LectureProgresses
            .FirstOrDefaultAsync(lp => lp.StudentId == userId && lp.LectureId == resolvedLectureId);

        var allowed = test.MaxAttempts + (progress?.ExtraAttemptsGranted ?? 0);
        var used = await _db.TestAttempts.CountAsync(ta => ta.StudentId == userId && ta.TestId == test.Id);
        if (used >= allowed && !User.IsTeacher())
        {
            TempData["ErrorMessage"] = "Лимит попыток прохождения теста исчерпан.";
            return RedirectToPage("/Student/Lecture", new { id = resolvedLectureId });
        }

        long resolvedStartedUnix = startedAtUnixSeconds ?? 0;
        if (resolvedStartedUnix <= 0 && Request.HasFormContentType && long.TryParse(Request.Form["startedAtUnixSeconds"], out var fStarted))
        {
            resolvedStartedUnix = fStarted;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var startedUtc = resolvedStartedUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(resolvedStartedUnix)
            : nowUtc;

        bool isExpired = false;
        if (test.TimeLimitMinutes.HasValue)
        {
            var elapsedMinutes = (nowUtc - startedUtc).TotalMinutes;
            if (elapsedMinutes > test.TimeLimitMinutes.Value + 1.0) // +1 мин допуск на сетевую задержку
            {
                isExpired = true;
            }
        }

        int correctCount = 0;
        int totalQuestions = test.Questions.Count;
        var details = new Dictionary<int, string>();

        foreach (var q in test.Questions)
        {
            string rawAnswer = ExtractTestAnswerFromForm(q);
            details[q.Id] = rawAnswer;

            if (_progressService.CheckTestQuestionAnswer(q, rawAnswer))
            {
                correctCount++;
            }
        }

        double scorePercentage = totalQuestions == 0
            ? 100.0
            : Math.Round((double)correctCount / totalQuestions * 100.0, 1);

        TestAttemptStatus status;
        if (isExpired)
        {
            status = TestAttemptStatus.Expired;
        }
        else if (scorePercentage >= test.PassingPercentage)
        {
            status = TestAttemptStatus.Passed;
        }
        else
        {
            status = TestAttemptStatus.Failed;
        }

        var attempt = new TestAttempt
        {
            StudentId = userId,
            TestId = test.Id,
            StartedAt = startedUtc.UtcDateTime,
            AttemptDate = nowUtc.UtcDateTime,
            CorrectCount = correctCount,
            TotalQuestions = totalQuestions,
            ScorePercentage = scorePercentage,
            Status = status,
            DetailsJson = JsonSerializer.Serialize(details)
        };

        _db.TestAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        var summary = await _progressService.SynchronizeAndGetDisciplineProgressAsync(userId, lecture.Topic.DisciplineId);
        var updatedLectureStatus = summary.LectureStatuses.GetValueOrDefault(resolvedLectureId, LectureStatus.InProgress);

        if (status == TestAttemptStatus.Passed)
        {
            if (updatedLectureStatus == LectureStatus.Completed)
            {
                TempData["SuccessMessage"] = $"Поздравляем! Вы сдали тест с результатом {scorePercentage}% ({correctCount} из {totalQuestions}). Все условия лекции выполнены — следующая лекция открыта!";
            }
            else
            {
                TempData["SuccessMessage"] = $"Тест успешно сдан с результатом {scorePercentage}% ({correctCount} из {totalQuestions})! Для открытия следующей лекции завершите контрольные вопросы (или дождитесь проверки развёрнутого ответа преподавателем).";
            }
        }
        else if (status == TestAttemptStatus.Expired)
        {
            TempData["ErrorMessage"] = $"Превышен лимит времени ({test.TimeLimitMinutes} мин.). Ваш результат: {scorePercentage}%. Повторите попытку.";
        }
        else
        {
            TempData["ErrorMessage"] = $"Тест не сдан: набрано {scorePercentage}% ({correctCount} из {totalQuestions}) при необходимом пороге {test.PassingPercentage}%. Следующая лекция остаётся закрытой.";
        }

        return RedirectToPage("/Student/Lecture", new { id = resolvedLectureId });
    }

    private string ExtractTestAnswerFromForm(TestQuestion q)
    {
        var form = Request.Form;
        switch (q.QuestionType)
        {
            case QuestionType.MultipleChoice:
            {
                var selected = form[$"tq_{q.Id}"].ToArray();
                return string.Join(";", selected);
            }
            case QuestionType.Matching:
            {
                var options = q.AnswerOptions.OrderBy(o => o.Id).ToList();
                var pairs = new List<string>();
                for (int i = 0; i < options.Count; i++)
                {
                    var rightVal = form[$"tq_{q.Id}_match_{options[i].Id}"].ToString();
                    if (!string.IsNullOrWhiteSpace(rightVal))
                    {
                        pairs.Add($"{options[i].OptionText}={rightVal}");
                    }
                }
                return string.Join(";", pairs);
            }
            default:
                return form[$"tq_{q.Id}"].ToString().Trim();
        }
    }
}

