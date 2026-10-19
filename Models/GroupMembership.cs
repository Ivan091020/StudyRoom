using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class GroupMembership
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User Student { get; set; } = null!;

    [Required]
    public int StudentGroupId { get; set; }

    [ForeignKey(nameof(StudentGroupId))]
    public StudentGroup StudentGroup { get; set; } = null!;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
