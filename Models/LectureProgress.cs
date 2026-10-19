using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class LectureProgress
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User Student { get; set; } = null!;

    [Required]
    public int LectureId { get; set; }

    [ForeignKey(nameof(LectureId))]
    public Lecture Lecture { get; set; } = null!;

    public LectureStatus Status { get; set; } = LectureStatus.Locked;

    /// <summary>
    /// Признак принудительного (ручного) открытия лекции преподавателем для конкретного студента.
    /// </summary>
    public bool IsManuallyUnlocked { get; set; }

    /// <summary>
    /// Дополнительные попытки теста, предоставленные преподавателем вручную.
    /// </summary>
    public int ExtraAttemptsGranted { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
