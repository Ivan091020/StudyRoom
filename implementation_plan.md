# План реализации веб-приложения «Электронный учебный кабинет преподавателя и студента»

## 1. Общие сведения и архитектура решения
- **Платформа:** .NET 10.0, ASP.NET Core Razor Pages, веб-сервер Kestrel.
- **СУБД и ORM:** PostgreSQL 18 + Entity Framework Core (подход Code First с миграциями).
- **Аутентификация и безопасность:**
  - Cookie-аутентификация (`CookieAuthenticationDefaults.AuthenticationScheme`).
  - Авторизация по ролям: `Teacher` (Преподаватель) и `Student` (Студент).
  - Хеширование паролей: `PasswordHasher<User>` из `Microsoft.AspNetCore.Identity` (хранение паролей в открытом виде исключено).
  - Защищённое хранение учебных материалов в выделенном серверном каталоге `App_Data/Materials` (вне `wwwroot`) и отдача через защищённый обработчик с серверной проверкой авторизации и открытости лекции.
- **Клиентский интерфейс:** Русскоязычный адаптивный UI на базе HTML5, CSS3, JavaScript и Bootstrap 5.

## 2. Проектирование базы данных (13 обязательных сущностей + журнал аудита)
1. **`Users`**: `Id`, `Login`, `PasswordHash`, `FullName`, `Role`, `CreatedAt`.
2. **`StudentGroups`**: `Id`, `Name`, `Description`.
3. **`GroupMemberships`**: `Id`, `StudentId`, `StudentGroupId`, `JoinedAt`.
4. **`Disciplines`**: `Id`, `Title`, `Description`, `CreatedAt`.
5. **`Topics`**: `Id`, `DisciplineId`, `Title`, `OrderIndex`.
6. **`Lectures`**: `Id`, `TopicId`, `Title`, `Description`, `ContentHtml`, `MaterialFilePath`, `MaterialOriginalFileName`, `OrderIndex`, `RequireControlQuestions`, `WaitForTeacherApproval`, `RequireTestPass`.
7. **`ControlQuestions`**: `Id`, `LectureId`, `QuestionText`, `QuestionType`, `CorrectAnswer`, `OptionsJson`, `OrderIndex`.
8. **`Tests`**: `Id`, `LectureId`, `Title`, `PassingPercentage`, `MaxAttempts`, `TimeLimitMinutes`, `ShuffleQuestions`, `RequireRetryOnFail`.
9. **`TestQuestions`**: `Id`, `TestId`, `QuestionText`, `QuestionType`, `OrderIndex`, `Points`.
10. **`AnswerOptions`**: `Id`, `QuestionId`, `OptionText`, `IsCorrect`, `MatchPair`.
11. **`StudentAnswers`**: `Id`, `StudentId`, `QuestionId`, `AnswerText`, `IsApproved`, `CheckedByTeacher`, `TeacherComment`, `SubmittedAt`, `CheckedAt`.
12. **`TestAttempts`**: `Id`, `StudentId`, `TestId`, `StartedAt`, `AttemptDate`, `ScorePercentage`, `CorrectCount`, `TotalQuestions`, `Status`, `DetailsJson`.
13. **`LectureProgress`**: `Id`, `StudentId`, `LectureId`, `Status`, `IsManuallyUnlocked`, `ExtraAttemptsGranted`, `UpdatedAt`.
14. **`AuditLogs`**: `Id`, `TeacherId`, `TargetStudentId`, `LectureId`, `ActionType`, `Description`, `CreatedAt`.

## 3. Этапы выполнения
- **Шаг 1:** Установка NuGet-пакетов EF Core и Npgsql, создание моделей всех 14 сущностей, `AppDbContext`, настройка подключения к PostgreSQL и выполнение первой миграции БД.
- **Шаг 2:** Реализация Cookie-аутентификации, регистрации, хеширования паролей через `PasswordHasher<User>`, разграничения ролей (`Teacher` / `Student`) и `DbSeeder` с начальными данными.
- **Шаг 3:** Реализация панели преподавателя (`/Teacher/...`): управление дисциплинами, темами, лекциями, загрузка и замена файлов PDF/DOCX, конструктор вопросов 6 типов и тестов, проверка развёрнутых ответов, сводные отчёты по группам и ручное открытие лекций с логированием в `AuditLogs`.
- **Шаг 4:** Реализация личного кабинета студента (`/Student/...`), сервиса расчёта прогресса и строгой серверной блокировки последовательного открытия лекций и скачивания файлов материалов.
- **Шаг 5:** Проверка сборки проекта (`dotnet build`), тестирование по всем 12 сценариям из раздела 7 ТЗ и подготовка комплекта документации (описание внедрения, инструкция пользователя, регламент сопровождения и резервного копирования).

