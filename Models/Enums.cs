using System.ComponentModel.DataAnnotations;

namespace StudyRoom.Models;

public enum UserRole
{
    [Display(Name = "Студент")]
    Student = 0,

    [Display(Name = "Преподаватель")]
    Teacher = 1
}

public enum QuestionType
{
    [Display(Name = "Краткий текстовый ответ")]
    ShortText = 0,

    [Display(Name = "Выбор одного правильного ответа")]
    SingleChoice = 1,

    [Display(Name = "Выбор нескольких правильных ответов")]
    MultipleChoice = 2,

    [Display(Name = "Установление соответствия")]
    Matching = 3,

    [Display(Name = "Ввод пропущенного слова или термина")]
    FillInBlank = 4,

    [Display(Name = "Развёрнутый ответ (проверка преподавателем)")]
    ExtendedText = 5
}

public enum LectureStatus
{
    [Display(Name = "Заблокировано")]
    Locked = 0,

    [Display(Name = "Открыто")]
    Open = 1,

    [Display(Name = "В процессе изучения")]
    InProgress = 2,

    [Display(Name = "Ожидает проверки преподавателем")]
    WaitingTeacherReview = 3,

    [Display(Name = "Изучено")]
    Completed = 4
}

public enum TestAttemptStatus
{
    [Display(Name = "В процессе")]
    InProgress = 0,

    [Display(Name = "Сдан")]
    Passed = 1,

    [Display(Name = "Не сдан")]
    Failed = 2,

    [Display(Name = "Время истекло")]
    Expired = 3
}

