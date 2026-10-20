using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudyRoom.Models;

public class Lecture
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TopicId { get; set; }

    [ForeignKey(nameof(TopicId))]
    public Topic Topic { get; set; } = null!;

    [Required(ErrorMessage = "Название лекции обязательно")]
    [MaxLength(250)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Текст лекции (HTML/Markdown) для непосредственного чтения в браузере.
    /// </summary>
    public string? ContentHtml { get; set; }

    /// <summary>
    /// Относительный путь к загруженному учебному файлу (PDF, DOCX и др.) в защищённом хранилище.
    /// </summary>
    [MaxLength(500)]
    public string? MaterialFilePath { get; set; }

    /// <summary>
    /// Оригинальное имя загруженного файла.
    /// </summary>
    [MaxLength(255)]
    public string? MaterialOriginalFileName { get; set; }

    public int OrderIndex { get; set; } = 1;

    /// <summary>
    /// Условие открытия следующей лекции: обязательно ответить на все контрольные вопросы.
    /// </summary>
    public bool RequireControlQuestions { get; set; } = true;

    /// <summary>
    /// Условие открытия следующей лекции: если есть развёрнутые ответы, ждать проверки преподавателем.
    /// </summary>
    public bool WaitForTeacherApproval { get; set; } = true;

    /// <summary>
    /// Условие открытия следующей лекции: обязательно сдать тест на проходной процент.
    /// </summary>
    public bool RequireTestPass { get; set; } = true;

    public ICollection<ControlQuestion> ControlQuestions { get; set; } = new List<ControlQuestion>();
    public ICollection<Test> Tests { get; set; } = new List<Test>();
    public ICollection<LectureProgress> LectureProgresses { get; set; } = new List<LectureProgress>();
}

