using System.Security.Claims;
using StudyRoom.Models;

namespace StudyRoom.Services;

public static class AuthExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : 0;
    }

    public static string GetFullName(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue("FullName") ?? principal.Identity?.Name ?? "Пользователь";
    }

    public static bool IsTeacher(this ClaimsPrincipal principal)
    {
        return principal.IsInRole(nameof(UserRole.Teacher));
    }

    public static bool IsStudent(this ClaimsPrincipal principal)
    {
        return principal.IsInRole(nameof(UserRole.Student));
    }

    public static string GetQuestionTypeDisplayName(this QuestionType type) => type switch
    {
        QuestionType.ShortText => "Краткий текстовый ответ",
        QuestionType.SingleChoice => "Один правильный вариант",
        QuestionType.MultipleChoice => "Несколько правильных вариантов",
        QuestionType.Matching => "Установление соответствия",
        QuestionType.FillInBlank => "Ввод пропущенного слова/термина",
        QuestionType.ExtendedText => "Развёрнутый ответ (проверка преподавателем)",
        _ => type.ToString()
    };

    public static string GetStatusDisplayName(this LectureStatus status) => status switch
    {
        LectureStatus.Locked => "Заблокировано",
        LectureStatus.Open => "Открыто",
        LectureStatus.InProgress => "В процессе",
        LectureStatus.WaitingTeacherReview => "Ожидает проверки",
        LectureStatus.Completed => "Изучено",
        _ => status.ToString()
    };

    public static string GetBadgeClass(this LectureStatus status) => status switch
    {
        LectureStatus.Completed => "bg-success",
        LectureStatus.Open => "bg-primary",
        LectureStatus.InProgress => "bg-info text-dark",
        LectureStatus.WaitingTeacherReview => "bg-warning text-dark",
        LectureStatus.Locked => "bg-secondary",
        _ => "bg-secondary"
    };
}

