using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class Topic
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "Название темы обязательно")]
    [MaxLength(250)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public int DisciplineId { get; set; }

    [ForeignKey(nameof(DisciplineId))]
    public Discipline Discipline { get; set; } = null!;

    public int OrderIndex { get; set; } = 1;

    public ICollection<Lecture> Lectures { get; set; } = new List<Lecture>();
}

