using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class AuditLog
{
    [Key]
    public int Id { get; set; }

    public int? TeacherId { get; set; }

    [ForeignKey(nameof(TeacherId))]
    public User? Teacher { get; set; }

    public int? TargetStudentId { get; set; }

    [ForeignKey(nameof(TargetStudentId))]
    public User? TargetStudent { get; set; }

    public int? LectureId { get; set; }

    [ForeignKey(nameof(LectureId))]
    public Lecture? Lecture { get; set; }

    [Required]
    [MaxLength(100)]
    public string ActionType { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
