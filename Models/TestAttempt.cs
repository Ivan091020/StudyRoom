using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class TestAttempt
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User Student { get; set; } = null!;

    [Required]
    public int TestId { get; set; }

    [ForeignKey(nameof(TestId))]
    public Test Test { get; set; } = null!;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime AttemptDate { get; set; } = DateTime.UtcNow;

    public double ScorePercentage { get; set; }

    public int CorrectCount { get; set; }

    public int TotalQuestions { get; set; }

    public TestAttemptStatus Status { get; set; } = TestAttemptStatus.InProgress;

    /// <summary>
    /// JSON с деталями ответов студента в рамках попытки.
    /// </summary>
    public string? DetailsJson { get; set; }
}

