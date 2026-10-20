using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class StudentAnswer
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User Student { get; set; } = null!;

    [Required]
    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public ControlQuestion Question { get; set; } = null!;

    [Required]
    public string AnswerText { get; set; } = string.Empty;

    /// <summary>
    /// true = ответ зачтён, false = отклонён/неверен, null = ожидает проверки преподавателем.
    /// </summary>
    public bool? IsApproved { get; set; }

    public bool CheckedByTeacher { get; set; }

    [MaxLength(1000)]
    public string? TeacherComment { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CheckedAt { get; set; }
}

