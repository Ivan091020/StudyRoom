using System.ComponentModel.DataAnnotations;

namespace StudyRoom.Models;

public class StudentGroup
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "Название группы обязательно")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    public ICollection<GroupMembership> GroupMemberships { get; set; } = new List<GroupMembership>();
}

