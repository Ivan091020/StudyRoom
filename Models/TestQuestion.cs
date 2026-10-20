using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class TestQuestion
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TestId { get; set; }

    [ForeignKey(nameof(TestId))]
    public Test Test { get; set; } = null!;

    [Required(ErrorMessage = "Текст тестового вопроса обязателен")]
    [MaxLength(2000)]
    public string QuestionText { get; set; } = string.Empty;

    [Required]
    public QuestionType QuestionType { get; set; } = QuestionType.SingleChoice;

    public int OrderIndex { get; set; } = 1;

    public ICollection<AnswerOption> AnswerOptions { get; set; } = new List<AnswerOption>();
}

