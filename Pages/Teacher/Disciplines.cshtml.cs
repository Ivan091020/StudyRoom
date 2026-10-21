using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Teacher;

[Authorize(Policy = "TeacherOnly")]
public class DisciplinesModel : PageModel
{
    private readonly AppDbContext _db;

    public DisciplinesModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Discipline> Disciplines { get; set; } = new();

    public async Task OnGetAsync()
    {
        Disciplines = await _db.Disciplines
            .Include(d => d.Topics.OrderBy(t => t.OrderIndex).ThenBy(t => t.Id))
            .ThenInclude(t => t.Lectures.OrderBy(l => l.OrderIndex).ThenBy(l => l.Id))
            .ThenInclude(l => l.ControlQuestions)
            .Include(d => d.Topics)
            .ThenInclude(t => t.Lectures)
            .ThenInclude(l => l.Tests)
            .OrderBy(d => d.Id)
            .ToListAsync();
    }

    public IActionResult OnPost()
    {
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateDisciplineAsync(string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["ErrorMessage"] = "Введите название дисциплины.";
            return RedirectToPage();
        }

        var discipline = new Discipline
        {
            Title = title.Trim(),
            Description = (description ?? string.Empty).Trim()
        };
        _db.Disciplines.Add(discipline);

        _db.AuditLogs.Add(new AuditLog
        {
            TeacherId = User.GetUserId(),
            ActionType = "CreateDiscipline",
            Description = $"Создана учебная дисциплина «{discipline.Title}»."
        });

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Дисциплина «{discipline.Title}» успешно добавлена.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateDisciplineAsync(int id, string title, string? description)
    {
        var discipline = await _db.Disciplines.FindAsync(id);
        if (discipline == null)
        {
            TempData["ErrorMessage"] = "Дисциплина не найдена.";
            return RedirectToPage();
        }

        discipline.Title = title.Trim();
        discipline.Description = (description ?? string.Empty).Trim();
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Дисциплина «{discipline.Title}» обновлена.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteDisciplineAsync(int id)
    {
        var discipline = await _db.Disciplines.FindAsync(id);
        if (discipline != null)
        {
            var title = discipline.Title;
            _db.Disciplines.Remove(discipline);
            _db.AuditLogs.Add(new AuditLog
            {
                TeacherId = User.GetUserId(),
                ActionType = "DeleteDiscipline",
                Description = $"Удалена дисциплина «{title}»."
            });
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Дисциплина «{title}» удалена.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateTopicAsync(int disciplineId, string title, int orderIndex)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["ErrorMessage"] = "Введите название темы.";
            return RedirectToPage();
        }

        var topic = new Topic
        {
            DisciplineId = disciplineId,
            Title = title.Trim(),
            OrderIndex = orderIndex <= 0 ? 1 : orderIndex
        };
        _db.Topics.Add(topic);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Тема «{topic.Title}» добавлена.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateTopicAsync(int id, string title, int orderIndex)
    {
        var topic = await _db.Topics.FindAsync(id);
        if (topic == null)
        {
            TempData["ErrorMessage"] = "Тема не найдена.";
            return RedirectToPage();
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            topic.Title = title.Trim();
        }
        if (orderIndex > 0)
        {
            topic.OrderIndex = orderIndex;
        }
        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Тема «{topic.Title}» обновлена.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTopicAsync(int id)
    {
        var topic = await _db.Topics.FindAsync(id);
        if (topic != null)
        {
            _db.Topics.Remove(topic);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Тема «{topic.Title}» удалена.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateLectureAsync(int topicId, string title, string? description, int orderIndex)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["ErrorMessage"] = "Введите название лекции.";
            return RedirectToPage();
        }

        var lecture = new Lecture
        {
            TopicId = topicId,
            Title = title.Trim(),
            Description = (description ?? string.Empty).Trim(),
            OrderIndex = orderIndex <= 0 ? 1 : orderIndex,
            RequireControlQuestions = true,
            WaitForTeacherApproval = true,
            RequireTestPass = true,
            ContentHtml = $"<h4>{title.Trim()}</h4><p>{description}</p>"
        };
        _db.Lectures.Add(lecture);
        await _db.SaveChangesAsync();

        // Создаём базовый тест по умолчанию для новой лекции
        _db.Tests.Add(new Test
        {
            LectureId = lecture.Id,
            Title = $"Итоговый тест: {lecture.Title}",
            PassingPercentage = 70.0,
            MaxAttempts = 3,
            TimeLimitMinutes = 15,
            ShuffleQuestions = true,
            RequireRetryOnFail = true
        });
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Лекция «{lecture.Title}» создана. Перейдите в редактор лекции для загрузки файлов и добавления вопросов.";
        return RedirectToPage("/Teacher/LectureEdit", new { id = lecture.Id });
    }

    public async Task<IActionResult> OnPostDeleteLectureAsync(int id)
    {
        var lecture = await _db.Lectures.FindAsync(id);
        if (lecture != null)
        {
            var title = lecture.Title;
            _db.Lectures.Remove(lecture);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Лекция «{title}» удалена.";
        }
        return RedirectToPage();
    }
}

