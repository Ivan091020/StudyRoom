using System.ComponentModel.DataAnnotations;

namespace StudyRoom.Models;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "Логин обязателен")]
    [MaxLength(100)]
    public string Login { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required(ErrorMessage = "ФИО обязательно")]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public UserRole Role { get; set; } = UserRole.Student;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GroupMembership> GroupMemberships { get; set; } = new List<GroupMembership>();
    public ICollection<StudentAnswer> StudentAnswers { get; set; } = new List<StudentAnswer>();
    public ICollection<TestAttempt> TestAttempts { get; set; } = new List<TestAttempt>();
    public ICollection<LectureProgress> LectureProgresses { get; set; } = new List<LectureProgress>();
}

