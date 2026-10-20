using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Teacher;

[Authorize(Policy = "TeacherOnly")]
public class LectureEditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly MaterialStorageService _storage;

    public LectureEditModel(AppDbContext db, MaterialStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    public Lecture Lecture { get; set; } = null!;
    public Test CurrentTest { get; set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (id <= 0 && int.TryParse(Request.Query["id"], out var queryId))
        {
            id = queryId;
        }
        return await LoadLectureDataAsync(id);
    }

    private async Task<IActionResult> LoadLectureDataAsync(int id)
    {
        var lecture = await _db.Lectures
            .Include(l => l.Topic)
            .ThenInclude(t => t.Discipline)
            .Include(l => l.ControlQuestions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id))
            .Include(l => l.Tests)
            .ThenInclude(t => t.Questions.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id))
            .ThenInclude(q => q.AnswerOptions)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (lecture == null)
        {
            return NotFound();
        }

        Lecture = lecture;
        var test = lecture.Tests.FirstOrDefault();
        if (test == null)
        {
            test = new Test
            {
                LectureId = lecture.Id,
                Title = $"Итоговый тест: {lecture.Title}",
                PassingPercentage = 70.0,
                MaxAttempts = 3,
                TimeLimitMinutes = 15,
                ShuffleQuestions = true,
                RequireRetryOnFail = true
            };
            _db.Tests.Add(test);
            await _db.SaveChangesAsync();
        }
        CurrentTest = test;
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateLectureAsync(
        int id,
        string title,
        string? description,
        int orderIndex,
        string? contentHtml,
        bool requireControlQuestions,
        bool waitForTeacherApproval,
        bool requireTestPass,
        IFormFile? materialFile)
    {
        var lecture = await _db.Lectures.FindAsync(id);
        if (lecture == null) return NotFound();

        lecture.Title = title.Trim();
        lecture.Description = (description ?? string.Empty).Trim();
        lecture.OrderIndex = orderIndex <= 0 ? 1 : orderIndex;
        lecture.ContentHtml = contentHtml;
        lecture.RequireControlQuestions = requireControlQuestions;
        lecture.WaitForTeacherApproval = waitForTeacherApproval;
        lecture.RequireTestPass = requireTestPass;

        if (materialFile != null && materialFile.Length > 0)
        {
            try
            {
                bool isReplacement = !string.IsNullOrWhiteSpace(lecture.MaterialFilePath);
                var (storedFileName, originalFileName) = await _storage.SaveMaterialAsync(materialFile, lecture.MaterialFilePath);
                lecture.MaterialFilePath = storedFileName;
                lecture.MaterialOriginalFileName = originalFileName;

                _db.AuditLogs.Add(new AuditLog
                {
                    TeacherId = User.GetUserId(),
                    LectureId = lecture.Id,
                    ActionType = isReplacement ? "MaterialReplaced" : "MaterialUploaded",
                    Description = $"{(isReplacement ? "Заменён" : "Загружен")} учебный материал лекции «{lecture.Title}»: файл «{originalFileName}»."
                });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToPage(new { id });
            }
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Параметры и материалы лекции успешно сохранены.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddControlQuestionAsync(
        int lectureId,
        int orderIndex,
        QuestionType questionType,
        string questionText,
        string? optionsJson,
        string? correctAnswer)
    {
        if (string.IsNullOrWhiteSpace(questionText))
        {
            TempData["ErrorMessage"] = "Введите текст контрольного вопроса.";
            return RedirectToPage(new { id = lectureId });
        }

        var cq = new ControlQuestion
        {
            LectureId = lectureId,
            OrderIndex = orderIndex <= 0 ? 1 : orderIndex,
            QuestionType = questionType,
            QuestionText = questionText.Trim(),
            OptionsJson = string.IsNullOrWhiteSpace(optionsJson) ? null : optionsJson.Trim(),
            CorrectAnswer = string.IsNullOrWhiteSpace(correctAnswer) ? null : correctAnswer.Trim()
        };
        _db.ControlQuestions.Add(cq);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Контрольный вопрос добавлен.";
        return RedirectToPage(new { id = lectureId });
    }

    public async Task<IActionResult> OnPostDeleteControlQuestionAsync(int lectureId, int questionId)
    {
        var cq = await _db.ControlQuestions.FindAsync(questionId);
        if (cq != null)
        {
            _db.ControlQuestions.Remove(cq);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Контрольный вопрос удалён.";
        }
        return RedirectToPage(new { id = lectureId });
    }

    public async Task<IActionResult> OnPostUpdateTestSettingsAsync(
        int lectureId,
        int testId,
        string title,
        double passingPercentage,
        int maxAttempts,
        int? timeLimitMinutes,
        bool shuffleQuestions,
        bool requireRetryOnFail)
    {
        var test = await _db.Tests.FindAsync(testId);
        if (test == null) return NotFound();

        test.Title = string.IsNullOrWhiteSpace(title) ? "Итоговый тест" : title.Trim();
        test.PassingPercentage = Math.Clamp(passingPercentage, 1, 100);
        test.MaxAttempts = Math.Max(1, maxAttempts);
        test.TimeLimitMinutes = timeLimitMinutes is > 0 ? timeLimitMinutes : null;
        test.ShuffleQuestions = shuffleQuestions;
        test.RequireRetryOnFail = requireRetryOnFail;

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Настройки теста сохранены.";
        return RedirectToPage(new { id = lectureId });
    }

    /// <summary>
    /// Добавляет тестовый вопрос и автоматически разбирает варианты ответов из текстового блока.
    /// Формат строк:
    /// - Для SingleChoice / MultipleChoice: каждая строка — вариант ответа, правильные помечаются звездочкой * в начале строки.
    /// - Для ShortText / FillInBlank: каждая строка — допустимый правильный ответ.
    /// - Для Matching: каждая строка в формате Левая часть=Правая часть.
    /// </summary>
    public async Task<IActionResult> OnPostAddTestQuestionAsync(
        int lectureId,
        int testId,
        int orderIndex,
        QuestionType questionType,
        string questionText,
        string optionsRaw)
    {
        if (string.IsNullOrWhiteSpace(questionText))
        {
            TempData["ErrorMessage"] = "Введите текст тестового вопроса.";
            return RedirectToPage(new { id = lectureId });
        }

        var tq = new TestQuestion
        {
            TestId = testId,
            OrderIndex = orderIndex <= 0 ? 1 : orderIndex,
            QuestionType = questionType,
            QuestionText = questionText.Trim()
        };
        _db.TestQuestions.Add(tq);
        await _db.SaveChangesAsync();

        var lines = (optionsRaw ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        foreach (var line in lines)
        {
            if (questionType == QuestionType.Matching)
            {
                var parts = line.Split('=', 2);
                var left = parts[0].Trim();
                var right = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                _db.AnswerOptions.Add(new AnswerOption
                {
                    QuestionId = tq.Id,
                    OptionText = left,
                    MatchPair = right,
                    IsCorrect = true
                });
            }
            else if (questionType == QuestionType.ShortText || questionType == QuestionType.FillInBlank)
            {
                var cleaned = line.TrimStart('*').Trim();
                _db.AnswerOptions.Add(new AnswerOption
                {
                    QuestionId = tq.Id,
                    OptionText = cleaned,
                    IsCorrect = true
                });
            }
            else
            {
                bool isCorrect = line.StartsWith('*');
                var cleaned = line.TrimStart('*').Trim();
                _db.AnswerOptions.Add(new AnswerOption
                {
                    QuestionId = tq.Id,
                    OptionText = cleaned,
                    IsCorrect = isCorrect
                });
            }
        }

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Вопрос теста успешно добавлен.";
        return RedirectToPage(new { id = lectureId });
    }

    public async Task<IActionResult> OnPostDeleteTestQuestionAsync(int lectureId, int testQuestionId)
    {
        var tq = await _db.TestQuestions.FindAsync(testQuestionId);
        if (tq != null)
        {
            _db.TestQuestions.Remove(tq);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Тестовый вопрос удалён.";
        }
        return RedirectToPage(new { id = lectureId });
    }
}

