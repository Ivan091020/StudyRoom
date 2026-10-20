using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class ControlQuestion
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int LectureId { get; set; }

    [ForeignKey(nameof(LectureId))]
    public Lecture Lecture { get; set; } = null!;

    [Required(ErrorMessage = "Текст вопроса обязателен")]
    [MaxLength(2000)]
    public string QuestionText { get; set; } = string.Empty;

    [Required]
    public QuestionType QuestionType { get; set; } = QuestionType.ShortText;

    /// <summary>
    /// Правильный ответ (для ShortText, FillInBlank, SingleChoice, MultipleChoice, Matching).
    /// Для ExtendedText может содержать критерии проверки для преподавателя.
    /// </summary>
    [MaxLength(2000)]
    public string? CorrectAnswer { get; set; }

    /// <summary>
    /// Дополнительные варианты ответа (JSON или список с разделителями) для вопросов с выбором или соответствием.
    /// </summary>
    public string? OptionsJson { get; set; }

    public int OrderIndex { get; set; } = 1;

    public ICollection<StudentAnswer> StudentAnswers { get; set; } = new List<StudentAnswer>();
}

