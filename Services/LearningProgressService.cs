using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;

namespace StudyRoom.Services;

public class LectureAccessResult
{
    public bool Allowed { get; set; }
    public string? DenialReason { get; set; }
    public LectureStatus CurrentStatus { get; set; }
}

public class DisciplineProgressSummary
{
    public int DisciplineId { get; set; }
    public int TotalLectures { get; set; }
    public int CompletedLectures { get; set; }
    public double CompletionPercentage => TotalLectures == 0 ? 0 : Math.Round((double)CompletedLectures / TotalLectures * 100.0, 1);
    public Dictionary<int, LectureStatus> LectureStatuses { get; set; } = new();
    public Dictionary<int, bool> LectureUnlockedFlags { get; set; } = new();
}

public class LearningProgressService
{
    private readonly AppDbContext _db;

    public LearningProgressService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Возвращает упорядоченный список всех лекций дисциплины (по порядку тем и порядку лекций).
    /// </summary>
    public async Task<List<Lecture>> GetOrderedDisciplineLecturesAsync(int disciplineId)
    {
        var lectures = await _db.Lectures
            .Include(l => l.Topic)
            .Include(l => l.ControlQuestions)
            .Include(l => l.Tests)
            .Where(l => l.Topic.DisciplineId == disciplineId)
            .OrderBy(l => l.Topic.OrderIndex)
            .ThenBy(l => l.TopicId)
            .ThenBy(l => l.OrderIndex)
            .ThenBy(l => l.Id)
            .ToListAsync();

        return lectures;
    }

    /// <summary>
    /// Актуализирует статусы всех лекций дисциплины для указанного студента и возвращает сводку прогресса.
    /// </summary>
    public async Task<DisciplineProgressSummary> SynchronizeAndGetDisciplineProgressAsync(int studentId, int disciplineId)
    {
        var orderedLectures = await GetOrderedDisciplineLecturesAsync(disciplineId);
        var lectureIds = orderedLectures.Select(l => l.Id).ToList();

        var existingProgresses = await _db.LectureProgresses
            .Where(lp => lp.StudentId == studentId && lectureIds.Contains(lp.LectureId))
            .ToDictionaryAsync(lp => lp.LectureId);

        var studentAnswers = await _db.StudentAnswers
            .Where(sa => sa.StudentId == studentId && sa.Question.Lecture.Topic.DisciplineId == disciplineId)
            .ToListAsync();

        var testAttempts = await _db.TestAttempts
            .Where(ta => ta.StudentId == studentId && ta.Test.Lecture.Topic.DisciplineId == disciplineId)
            .ToListAsync();

        bool previousAllowsNext = true; // Первая лекция в дисциплине всегда открыта
        var summary = new DisciplineProgressSummary
        {
            DisciplineId = disciplineId,
            TotalLectures = orderedLectures.Count
        };

        foreach (var lecture in orderedLectures)
        {
            if (!existingProgresses.TryGetValue(lecture.Id, out var progress))
            {
                progress = new LectureProgress
                {
                    StudentId = studentId,
                    LectureId = lecture.Id,
                    Status = previousAllowsNext ? LectureStatus.Open : LectureStatus.Locked,
                    IsManuallyUnlocked = false,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.LectureProgresses.Add(progress);
                existingProgresses[lecture.Id] = progress;
            }

            bool isAccessible = previousAllowsNext || progress.IsManuallyUnlocked;

            if (!isAccessible)
            {
                progress.Status = LectureStatus.Locked;
                previousAllowsNext = false;
            }
            else
            {
                var (isCompleted, isWaitingReview, hasAnyActivity) = EvaluateLectureCompletion(
                    lecture,
                    studentAnswers.Where(a => lecture.ControlQuestions.Any(q => q.Id == a.QuestionId)).ToList(),
                    testAttempts.Where(t => lecture.Tests.Any(test => test.Id == t.TestId)).ToList());

                if (isCompleted)
                {
                    progress.Status = LectureStatus.Completed;
                    previousAllowsNext = true;
                }
                else if (isWaitingReview)
                {
                    progress.Status = LectureStatus.WaitingTeacherReview;
                    previousAllowsNext = false;
                }
                else if (hasAnyActivity)
                {
                    progress.Status = LectureStatus.InProgress;
                    previousAllowsNext = false;
                }
                else
                {
                    progress.Status = LectureStatus.Open;
                    previousAllowsNext = false;
                }
            }

            summary.LectureStatuses[lecture.Id] = progress.Status;
            summary.LectureUnlockedFlags[lecture.Id] = progress.IsManuallyUnlocked;
            if (progress.Status == LectureStatus.Completed)
            {
                summary.CompletedLectures++;
            }
        }

        await _db.SaveChangesAsync();
        return summary;
    }

    /// <summary>
    /// Строгая серверная проверка доступа студента к конкретной лекции.
    /// </summary>
    public async Task<LectureAccessResult> CheckStudentAccessToLectureAsync(int studentId, int lectureId)
    {
        var lecture = await _db.Lectures
            .Include(l => l.Topic)
            .FirstOrDefaultAsync(l => l.Id == lectureId);

        if (lecture == null)
        {
            return new LectureAccessResult
            {
                Allowed = false,
                DenialReason = "Запрашиваемая лекция не найдена.",
                CurrentStatus = LectureStatus.Locked
            };
        }

        var orderedLectures = await GetOrderedDisciplineLecturesAsync(lecture.Topic.DisciplineId);
        var summary = await SynchronizeAndGetDisciplineProgressAsync(studentId, lecture.Topic.DisciplineId);

        if (!summary.LectureStatuses.TryGetValue(lectureId, out var status))
        {
            return new LectureAccessResult
            {
                Allowed = false,
                DenialReason = "Нет данных о прогрессе по лекции.",
                CurrentStatus = LectureStatus.Locked
            };
        }

        if (status != LectureStatus.Locked)
        {
            return new LectureAccessResult
            {
                Allowed = true,
                CurrentStatus = status
            };
        }

        // Определяем, какая предыдущая лекция блокирует доступ
        var index = orderedLectures.FindIndex(l => l.Id == lectureId);
        var prevLecture = index > 0 ? orderedLectures[index - 1] : null;
        var reason = prevLecture != null
            ? $"Лекция «{lecture.Title}» заблокирована. Для получения доступа необходимо сначала полностью выполнить контрольные задания и сдать итоговый тест по предыдущей лекции «{prevLecture.Title}»."
            : $"Лекция «{lecture.Title}» в данный момент заблокирована.";

        return new LectureAccessResult
        {
            Allowed = false,
            DenialReason = reason,
            CurrentStatus = LectureStatus.Locked
        };
    }

    /// <summary>
    /// Проверяет ответ студента на контрольный вопрос.
    /// </summary>
    public (bool? isApproved, bool checkedByTeacher) CheckControlQuestionAnswer(ControlQuestion question, string rawStudentAnswer)
    {
        var answer = (rawStudentAnswer ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(answer))
        {
            return (false, false);
        }

        if (question.QuestionType == QuestionType.ExtendedText)
        {
            // Развёрнутый ответ отправляется на проверку преподавателю
            return (null, false);
        }

        var expected = (question.CorrectAnswer ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(expected))
        {
            return (true, false);
        }

        return (CompareStructuredAnswer(question.QuestionType, answer, expected), false);
    }

    /// <summary>
    /// Проверяет ответ студента на вопрос теста.
    /// </summary>
    public bool CheckTestQuestionAnswer(TestQuestion question, string rawStudentAnswer)
    {
        var answer = (rawStudentAnswer ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(answer))
            return false;

        var options = question.AnswerOptions.ToList();
        if (!options.Any())
            return true;

        switch (question.QuestionType)
        {
            case QuestionType.ShortText:
            case QuestionType.FillInBlank:
            {
                var validTexts = options
                    .Where(o => o.IsCorrect)
                    .SelectMany(o => o.OptionText.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    .Select(NormalizeToken)
                    .ToHashSet();

                return validTexts.Contains(NormalizeToken(answer));
            }
            case QuestionType.SingleChoice:
            {
                var correctOption = options.FirstOrDefault(o => o.IsCorrect);
                if (correctOption == null) return false;
                return NormalizeToken(answer) == NormalizeToken(correctOption.OptionText)
                    || answer == correctOption.Id.ToString();
            }
            case QuestionType.MultipleChoice:
            {
                var correctSet = options
                    .Where(o => o.IsCorrect)
                    .Select(o => NormalizeToken(o.OptionText))
                    .ToHashSet();

                var studentSet = answer
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeToken)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToHashSet();

                return correctSet.SetEquals(studentSet);
            }
            case QuestionType.Matching:
            {
                var expectedSet = options
                    .Select(o => $"{NormalizeToken(o.OptionText)}={NormalizeToken(o.MatchPair ?? string.Empty)}")
                    .ToHashSet();

                var studentSet = answer
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair =>
                    {
                        var parts = pair.Split('=', 2);
                        return parts.Length == 2
                            ? $"{NormalizeToken(parts[0])}={NormalizeToken(parts[1])}"
                            : NormalizeToken(pair);
                    })
                    .ToHashSet();

                return expectedSet.SetEquals(studentSet);
            }
            case QuestionType.ExtendedText:
            {
                return answer.Length >= 10;
            }
            default:
                return false;
        }
    }

    private static bool CompareStructuredAnswer(QuestionType type, string studentAnswer, string expectedAnswer)
    {
        switch (type)
        {
            case QuestionType.ShortText:
            case QuestionType.FillInBlank:
            {
                var acceptable = expectedAnswer
                    .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeToken)
                    .ToHashSet();
                return acceptable.Contains(NormalizeToken(studentAnswer));
            }
            case QuestionType.SingleChoice:
            {
                return NormalizeToken(studentAnswer) == NormalizeToken(expectedAnswer);
            }
            case QuestionType.MultipleChoice:
            {
                var expectedSet = expectedAnswer
                    .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeToken)
                    .ToHashSet();
                var actualSet = studentAnswer
                    .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeToken)
                    .ToHashSet();
                return expectedSet.SetEquals(actualSet);
            }
            case QuestionType.Matching:
            {
                var expectedSet = expectedAnswer
                    .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizePair)
                    .ToHashSet();
                var actualSet = studentAnswer
                    .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizePair)
                    .ToHashSet();
                return expectedSet.SetEquals(actualSet);
            }
            default:
                return NormalizeToken(studentAnswer) == NormalizeToken(expectedAnswer);
        }
    }

    private static string NormalizePair(string rawPair)
    {
        var parts = rawPair.Split('=', 2);
        return parts.Length == 2
            ? $"{NormalizeToken(parts[0])}={NormalizeToken(parts[1])}"
            : NormalizeToken(rawPair);
    }

    private static string NormalizeToken(string input)
    {
        return (input ?? string.Empty).Trim().ToLowerInvariant().Replace('ё', 'е');
    }

    private static (bool isCompleted, bool isWaitingReview, bool hasAnyActivity) EvaluateLectureCompletion(
        Lecture lecture,
        List<StudentAnswer> answers,
        List<TestAttempt> attempts)
    {
        bool hasAnyActivity = answers.Any() || attempts.Any();
        bool questionsSatisfied = true;
        bool waitingTeacherReview = false;

        if (lecture.RequireControlQuestions && lecture.ControlQuestions.Any())
        {
            foreach (var q in lecture.ControlQuestions)
            {
                var ans = answers.FirstOrDefault(a => a.QuestionId == q.Id);
                if (ans == null)
                {
                    questionsSatisfied = false;
                    continue;
                }

                if (q.QuestionType == QuestionType.ExtendedText)
                {
                    if (lecture.WaitForTeacherApproval)
                    {
                        if (!ans.CheckedByTeacher || ans.IsApproved == null)
                        {
                            waitingTeacherReview = true;
                            questionsSatisfied = false;
                        }
                        else if (ans.IsApproved == false)
                        {
                            questionsSatisfied = false;
                        }
                    }
                    else if (ans.IsApproved == false)
                    {
                        questionsSatisfied = false;
                    }
                }
                else
                {
                    if (ans.IsApproved != true)
                    {
                        questionsSatisfied = false;
                    }
                }
            }
        }

        bool testsSatisfied = true;
        if (lecture.RequireTestPass && lecture.Tests.Any())
        {
            foreach (var test in lecture.Tests)
            {
                bool passedThisTest = attempts.Any(a =>
                    a.TestId == test.Id &&
                    a.Status == TestAttemptStatus.Passed &&
                    a.ScorePercentage >= test.PassingPercentage);

                if (!passedThisTest)
                {
                    testsSatisfied = false;
                }
            }
        }

        bool isCompleted = questionsSatisfied && testsSatisfied;
        return (isCompleted, !isCompleted && waitingTeacherReview, hasAnyActivity);
    }
}

