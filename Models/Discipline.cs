using System.ComponentModel.DataAnnotations;

namespace StudyRoom.Models;

public class Discipline
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "Название дисциплины обязательно")]
    [MaxLength(250)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
}

