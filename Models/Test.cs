using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class Test
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int LectureId { get; set; }

    [ForeignKey(nameof(LectureId))]
    public Lecture Lecture { get; set; } = null!;

    [Required]
    [MaxLength(250)]
    public string Title { get; set; } = "Итоговый тест по лекции";

    [Range(1, 100)]
    public double PassingPercentage { get; set; } = 70.0;

    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 3;

    public int? TimeLimitMinutes { get; set; } = 15;

    public bool ShuffleQuestions { get; set; } = true;

    public bool RequireRetryOnFail { get; set; } = true;

    public ICollection<TestQuestion> Questions { get; set; } = new List<TestQuestion>();
    public ICollection<TestAttempt> TestAttempts { get; set; } = new List<TestAttempt>();
}
