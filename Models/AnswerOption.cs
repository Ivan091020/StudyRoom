using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class AnswerOption
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public TestQuestion Question { get; set; } = null!;

    [Required]
    [MaxLength(1000)]
    public string OptionText { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }

    /// <summary>
    /// Для вопросов на соответствие (Matching): правая часть пары, соответствующая OptionText.
    /// </summary>
    [MaxLength(1000)]
    public string? MatchPair { get; set; }
}

