using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materialStorage = scope.ServiceProvider.GetRequiredService<MaterialStorageService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        await context.Database.MigrateAsync();

        if (await context.Users.AnyAsync())
        {
            return;
        }

        // 1. Создание пользователей (Преподаватель и 2 студента) с хешированием паролей
        var teacher = new User
        {
            Login = "teacher",
            FullName = "Соколов Александр Викторович",
            Role = UserRole.Teacher,
            CreatedAt = DateTime.UtcNow
        };
        teacher.PasswordHash = passwordHasher.HashPassword(teacher, "Teacher123!");

        var student1 = new User
        {
            Login = "student1",
            FullName = "Иванов Иван Сергеевич",
            Role = UserRole.Student,
            CreatedAt = DateTime.UtcNow
        };
        student1.PasswordHash = passwordHasher.HashPassword(student1, "Student123!");

        var student2 = new User
        {
            Login = "student2",
            FullName = "Петрова Мария Алексеевна",
            Role = UserRole.Student,
            CreatedAt = DateTime.UtcNow
        };
        student2.PasswordHash = passwordHasher.HashPassword(student2, "Student123!");

        context.Users.AddRange(teacher, student1, student2);
        await context.SaveChangesAsync();

        // 2. Создание учебных групп и привязка студентов
        var groupIsp41 = new StudentGroup
        {
            Name = "ИСП-41",
            Description = "Информационные системы и программирование, 4 курс (1 подгруппа)"
        };
        var groupIsp42 = new StudentGroup
        {
            Name = "ИСП-42",
            Description = "Информационные системы и программирование, 4 курс (2 подгруппа)"
        };
        context.StudentGroups.AddRange(groupIsp41, groupIsp42);
        await context.SaveChangesAsync();

        context.GroupMemberships.AddRange(
            new GroupMembership { StudentId = student1.Id, StudentGroupId = groupIsp41.Id },
            new GroupMembership { StudentId = student2.Id, StudentGroupId = groupIsp41.Id }
        );
        await context.SaveChangesAsync();

        // 3. Демонстрационная дисциплина и тема
        var discipline = new Discipline
        {
            Title = "Внедрение и поддержка компьютерных систем",
            Description = "Учебный курс по методам тестирования, развёртывания, ввода в эксплуатацию, сопровождения и резервного копирования программных комплексов и информационных систем."
        };
        context.Disciplines.Add(discipline);
        await context.SaveChangesAsync();

        var topic1 = new Topic
        {
            DisciplineId = discipline.Id,
            Title = "Тестирование программного обеспечения",
            OrderIndex = 1
        };
        context.Topics.Add(topic1);
        await context.SaveChangesAsync();

        // 4. Генерация демонстрационных файлов материалов (PDF и DOCX) в защищённом хранилище
        var pdf1 = materialStorage.EnsureSeedPdfFile(
            "seed_lecture1_testing_basics.pdf",
            "Lecture 1: Software Testing Fundamentals & Classification",
            "Discipline: Implementation and Support of Computer Systems");

        var docx2 = materialStorage.EnsureSeedDocxFile(
            "seed_lecture2_test_design.docx",
            "Лекция 2. Проектирование тест-кейсов и техники тест-дизайна",
            new[]
            {
                "1. Понятие тест-дизайна и жизненного цикла дефекта.",
                "2. Эквивалентное разделение (Equivalence Partitioning) и анализ граничных значений (Boundary Value Analysis).",
                "3. Таблицы принятия решений (Decision Tables) и диаграммы переходов состояний.",
                "4. Структура тест-кейса: ID, Название, Предусловия, Шаги, Ожидаемый результат, Фактический результат, Статус."
            });

        var pdf3 = materialStorage.EnsureSeedPdfFile(
            "seed_lecture3_deployment_support.pdf",
            "Lecture 3: Test Automation, Deployment & System Maintenance",
            "Discipline: Implementation and Support of Computer Systems");

        // 5. Создание 3 последовательных лекций
        var lecture1 = new Lecture
        {
            TopicId = topic1.Id,
            OrderIndex = 1,
            Title = "Лекция 1. Основы, цели и виды тестирования ПО",
            Description = "Базовые понятия контроля качества ПО, верификация и валидация, классификация тестирования по уровням и знанию внутренней структуры кода.",
            MaterialFilePath = pdf1,
            MaterialOriginalFileName = "Лекция_1_Основы_тестирования_ПО.pdf",
            RequireControlQuestions = true,
            WaitForTeacherApproval = false,
            RequireTestPass = true,
            ContentHtml = """
                <h4>1. Понятие и цели тестирования программного обеспечения</h4>
                <p><strong>Тестирование программного обеспечения (Software Testing)</strong> — это процесс исследования и анализа программного средства с целью выявления дефектов (ошибок) и проверки соответствия между реальным поведением программы и её ожидаемым поведением на конечном наборе тестов.</p>
                <div class="alert alert-info">
                    <strong>Ключевой принцип (Г. Майерс, ISTQB):</strong> Тестирование может доказать наличие дефектов, но не может доказать их полное отсутствие.
                </div>
                <h4>2. Верификация и валидация</h4>
                <ul>
                    <li><strong>Верификация (Verification):</strong> проверка того, что продукт разрабатывается в соответствии со спецификацией и техническим заданием («Правильно ли мы создаём продукт?»).</li>
                    <li><strong>Валидация (Validation):</strong> проверка того, что разработанный продукт действительно решает задачи конечного пользователя («Тот ли продукт мы создаём?»).</li>
                </ul>
                <h4>3. Классификация по запуску кода и доступу к структуре</h4>
                <ul>
                    <li><strong>Статическое тестирование:</strong> проводится <em>без запуска</em> исполняемого кода (рецензирование требований, инспекция исходного кода, статический анализ).</li>
                    <li><strong>Динамическое тестирование:</strong> проводится <em>с запуском</em> программного кода на выполнение с тестовыми входными данными.</li>
                    <li><strong>Метод «чёрного ящика» (Black Box):</strong> тестировщик проверяет внешнее поведение системы <em>без знания внутренней структуры кода</em>.</li>
                    <li><strong>Метод «белого ящика» (White Box):</strong> проверка основана на <em>полном знании внутренней структуры и алгоритмов исходного кода</em>.</li>
                    <li><strong>Метод «серого ящика» (Gray Box):</strong> комбинация подходов с <em>частичным знанием внутренней архитектуры</em> (например, структуры БД и API).</li>
                </ul>
                <h4>4. Уровни тестирования и регрессионный контроль</h4>
                <ol>
                    <li><strong>Модульное (Unit) тестирование</strong> — проверка отдельных функций, методов или классов.</li>
                    <li><strong>Интеграционное тестирование</strong> — проверка взаимодействия между несколькими модулями или подсистемами (например, приложение + СУБД PostgreSQL).</li>
                    <li><strong>Системное тестирование</strong> — проверка всей интегрированной системы целиком на соответствие функциональным и нефункциональным требованиям.</li>
                    <li><strong>Приёмочное (Acceptance) тестирование</strong> — финальная проверка перед вводом в эксплуатацию.</li>
                </ol>
                <p><strong>Регрессионное тестирование</strong> — повторное тестирование уже проверенной функциональности после внесения изменений или исправления ошибок с целью убедиться, что изменения не вызвали новых дефектов в ранее работавших модулях.</p>
                """
        };

        var lecture2 = new Lecture
        {
            TopicId = topic1.Id,
            OrderIndex = 2,
            Title = "Лекция 2. Проектирование тест-кейсов и техники тест-дизайна",
            Description = "Основные техники проектирования проверок: классы эквивалентности, граничные значения, структура тест-кейса и отчёта о дефекте (баг-репорта).",
            MaterialFilePath = docx2,
            MaterialOriginalFileName = "Лекция_2_Техники_тест_дизайна.docx",
            RequireControlQuestions = true,
            WaitForTeacherApproval = true,
            RequireTestPass = true,
            ContentHtml = """
                <h4>1. Зачем нужен тест-дизайн</h4>
                <p>Полный перебор всех возможных комбинаций входных данных невозможен даже для простых форм ввода. <strong>Тест-дизайн</strong> — это этап процесса тестирования, на котором проектируются и создаются тестовые сценарии (тест-кейсы) с максимальным покрытием требований при минимальном числе проверок.</p>
                <h4>2. Основные техники тест-дизайна</h4>
                <ul>
                    <li><strong>Классы эквивалентности (Equivalence Partitioning):</strong> множество входных значений разбивается на диапазоны (классы), внутри которых система ведёт себя одинаково (валидные и невалидные классы).</li>
                    <li><strong>Анализ граничных значений (Boundary Value Analysis):</strong> проверка поведения системы на границах классов эквивалентности (минимум, минимум − 1, максимум, максимум + 1), где чаще всего возникают ошибки разработчиков.</li>
                    <li><strong>Таблицы принятия решений (Decision Tables):</strong> используются для проверки сложной бизнес-логики, зависящей от комбинации нескольких условий (например, условия открытия следующей лекции).</li>
                    <li><strong>Диаграмма переходов состояний (State Transition):</strong> моделирует допустимые и недопустимые переходы объекта между статусами (например: <code>Заблокировано -> Открыто -> В процессе -> Изучено</code>).</li>
                </ul>
                <h4>3. Структура тест-кейса и отчёта о дефекте</h4>
                <p>Каждый тест-кейс включает: уникальный идентификатор (ID), заголовок, предусловия (Preconditions), последовательность шагов (Steps), входные данные и <strong>ожидаемый результат (Expected Result)</strong>.</p>
                """
        };

        var lecture3 = new Lecture
        {
            TopicId = topic1.Id,
            OrderIndex = 3,
            Title = "Лекция 3. Внедрение, сопровождение и резервное копирование ИС",
            Description = "Порядок ввода программного продукта в эксплуатацию, настройка серверной среды, резервное копирование PostgreSQL и регламент технической поддержки.",
            MaterialFilePath = pdf3,
            MaterialOriginalFileName = "Лекция_3_Внедрение_и_сопровождение.pdf",
            RequireControlQuestions = true,
            WaitForTeacherApproval = false,
            RequireTestPass = true,
            ContentHtml = """
                <h4>1. Внедрение информационной системы</h4>
                <p>Внедрение программного средства включает подготовку аппаратно-программной среды, установку среды выполнения (.NET Runtime и СУБД PostgreSQL), сборку проекта в конфигурации <code>Release</code>, применение миграций базы данных и настройку сетевого экрана.</p>
                <h4>2. Резервное копирование и аварийное восстановление</h4>
                <p>Для защиты учебных данных используется комбинированное резервное копирование:</p>
                <ul>
                    <li>Дамп базы данных PostgreSQL с помощью утилиты <code>pg_dump</code>;</li>
                    <li>Архивирование серверного каталога учебных материалов <code>App_Data/Materials</code>;</li>
                    <li>Регулярная проверка восстановления на тестовом контуре с помощью <code>pg_restore</code> / <code>psql</code>.</li>
                </ul>
                <h4>3. Регламент сопровождения</h4>
                <p>Сопровождение включает мониторинг доступности веб-сервера Kestrel, анализ журнала событий (<code>AuditLogs</code>), контроль разграничения ролей и установку обновлений безопасности.</p>
                """
        };

        context.Lectures.AddRange(lecture1, lecture2, lecture3);
        await context.SaveChangesAsync();

        // 6. Контрольные вопросы к Лекции 1 (все 5 типов с автопроверкой)
        context.ControlQuestions.AddRange(
            new ControlQuestion
            {
                LectureId = lecture1.Id,
                OrderIndex = 1,
                QuestionType = QuestionType.ShortText,
                QuestionText = "Как называется процесс исследования и анализа программного средства с целью выявления дефектов и проверки соответствия требованиям?",
                CorrectAnswer = "тестирование"
            },
            new ControlQuestion
            {
                LectureId = lecture1.Id,
                OrderIndex = 2,
                QuestionType = QuestionType.SingleChoice,
                QuestionText = "Какой вид тестирования выполняется без запуска исполняемого кода программы?",
                OptionsJson = "Динамическое тестирование|Статическое тестирование|Нагрузочное тестирование|Стресс-тестирование",
                CorrectAnswer = "Статическое тестирование"
            },
            new ControlQuestion
            {
                LectureId = lecture1.Id,
                OrderIndex = 3,
                QuestionType = QuestionType.MultipleChoice,
                QuestionText = "Выберите все существующие уровни тестирования программного обеспечения:",
                OptionsJson = "Модульное (Unit)|Интеграционное|Системное|Компиляционное",
                CorrectAnswer = "Модульное (Unit);Интеграционное;Системное"
            },
            new ControlQuestion
            {
                LectureId = lecture1.Id,
                OrderIndex = 4,
                QuestionType = QuestionType.Matching,
                QuestionText = "Установите соответствие между методом тестирования и степенью знания внутренней структуры кода:",
                OptionsJson = "Белый ящик=Полное знание кода|Чёрный ящик=Без знания кода|Серый ящик=Частичное знание кода",
                CorrectAnswer = "Белый ящик=Полное знание кода;Чёрный ящик=Без знания кода;Серый ящик=Частичное знание кода"
            },
            new ControlQuestion
            {
                LectureId = lecture1.Id,
                OrderIndex = 5,
                QuestionType = QuestionType.FillInBlank,
                QuestionText = "Вставьте пропущенный термин: Повторная проверка ранее работавшей функциональности после внесения изменений в код называется ___ тестированием.",
                CorrectAnswer = "регрессионным"
            }
        );

        // Контрольные вопросы к Лекции 2 (включая развёрнутый ответ ExtendedText с проверкой преподавателем)
        context.ControlQuestions.AddRange(
            new ControlQuestion
            {
                LectureId = lecture2.Id,
                OrderIndex = 1,
                QuestionType = QuestionType.SingleChoice,
                QuestionText = "Какая техника тест-дизайна разбивает множество входных данных на диапазоны с одинаковым ожидаемым поведением системы?",
                OptionsJson = "Классы эквивалентности|Метод белого ящика|Мутационное тестирование|Профилирование памяти",
                CorrectAnswer = "Классы эквивалентности"
            },
            new ControlQuestion
            {
                LectureId = lecture2.Id,
                OrderIndex = 2,
                QuestionType = QuestionType.FillInBlank,
                QuestionText = "Вставьте пропущенное слово: Проверка поведения системы на границах допустимых диапазонов называется анализом ___ значений.",
                CorrectAnswer = "граничных"
            },
            new ControlQuestion
            {
                LectureId = lecture2.Id,
                OrderIndex = 3,
                QuestionType = QuestionType.ExtendedText,
                QuestionText = "Развёрнутый ответ (проверяется преподавателем): Перечислите обязательные поля тест-кейса и приведите пример одного тест-кейса для проверки входа в учебный кабинет.",
                CorrectAnswer = "Должны быть указаны ID, название, предусловия, шаги, ожидаемый результат."
            }
        );

        // Контрольные вопросы к Лекции 3
        context.ControlQuestions.AddRange(
            new ControlQuestion
            {
                LectureId = lecture3.Id,
                OrderIndex = 1,
                QuestionType = QuestionType.ShortText,
                QuestionText = "Какая штатная утилита PostgreSQL используется для создания логической резервной копии базы данных?",
                CorrectAnswer = "pg_dump"
            },
            new ControlQuestion
            {
                LectureId = lecture3.Id,
                OrderIndex = 2,
                QuestionType = QuestionType.SingleChoice,
                QuestionText = "В какой конфигурации необходимо собирать приложение ASP.NET Core перед развёртыванием на компьютере преподавателя?",
                OptionsJson = "Debug|Release|Trace|Draft",
                CorrectAnswer = "Release"
            }
        );
        await context.SaveChangesAsync();

        // 7. Тесты и тестовые вопросы к лекциям
        var test1 = new Test
        {
            LectureId = lecture1.Id,
            Title = "Тест №1. Основы и классификация тестирования ПО",
            PassingPercentage = 70.0,
            MaxAttempts = 5,
            TimeLimitMinutes = 15,
            ShuffleQuestions = true,
            RequireRetryOnFail = true
        };
        var test2 = new Test
        {
            LectureId = lecture2.Id,
            Title = "Тест №2. Техники тест-дизайна и тестовая документация",
            PassingPercentage = 70.0,
            MaxAttempts = 5,
            TimeLimitMinutes = 15,
            ShuffleQuestions = true,
            RequireRetryOnFail = true
        };
        var test3 = new Test
        {
            LectureId = lecture3.Id,
            Title = "Тест №3. Внедрение, сопровождение и резервное копирование",
            PassingPercentage = 70.0,
            MaxAttempts = 5,
            TimeLimitMinutes = 15,
            ShuffleQuestions = true,
            RequireRetryOnFail = true
        };
        context.Tests.AddRange(test1, test2, test3);
        await context.SaveChangesAsync();

        // Вопросы к Тесту 1 (5 вопросов всех типов)
        var t1q1 = new TestQuestion
        {
            TestId = test1.Id,
            OrderIndex = 1,
            QuestionType = QuestionType.SingleChoice,
            QuestionText = "Какая проверка отвечает на вопрос «Правильно ли мы создаём продукт в соответствии со спецификацией»?"
        };
        var t1q2 = new TestQuestion
        {
            TestId = test1.Id,
            OrderIndex = 2,
            QuestionType = QuestionType.MultipleChoice,
            QuestionText = "Какие два утверждения о тестировании методом «чёрного ящика» являются верными?"
        };
        var t1q3 = new TestQuestion
        {
            TestId = test1.Id,
            OrderIndex = 3,
            QuestionType = QuestionType.ShortText,
            QuestionText = "Как называется минимальный уровень тестирования, проверяющий отдельные методы или классы (одним словом по-русски или по-английски: модульное / unit)?"
        };
        var t1q4 = new TestQuestion
        {
            TestId = test1.Id,
            OrderIndex = 4,
            QuestionType = QuestionType.Matching,
            QuestionText = "Соотнесите уровень тестирования и его назначение:"
        };
        var t1q5 = new TestQuestion
        {
            TestId = test1.Id,
            OrderIndex = 5,
            QuestionType = QuestionType.FillInBlank,
            QuestionText = "Тестирование без запуска программного кода на выполнение называется ___."
        };
        context.TestQuestions.AddRange(t1q1, t1q2, t1q3, t1q4, t1q5);
        await context.SaveChangesAsync();

        context.AnswerOptions.AddRange(
            // t1q1 (SingleChoice)
            new AnswerOption { QuestionId = t1q1.Id, OptionText = "Верификация", IsCorrect = true },
            new AnswerOption { QuestionId = t1q1.Id, OptionText = "Валидация", IsCorrect = false },
            new AnswerOption { QuestionId = t1q1.Id, OptionText = "Компиляция", IsCorrect = false },
            new AnswerOption { QuestionId = t1q1.Id, OptionText = "Декомпозиция", IsCorrect = false },

            // t1q2 (MultipleChoice)
            new AnswerOption { QuestionId = t1q2.Id, OptionText = "Не требует доступа к исходному коду программы", IsCorrect = true },
            new AnswerOption { QuestionId = t1q2.Id, OptionText = "Основано на проверке требований и пользовательского интерфейса", IsCorrect = true },
            new AnswerOption { QuestionId = t1q2.Id, OptionText = "Проверяет только покрытие ветвей условных операторов в C#", IsCorrect = false },
            new AnswerOption { QuestionId = t1q2.Id, OptionText = "Выполняется исключительно компилятором", IsCorrect = false },

            // t1q3 (ShortText - допустимые ответы)
            new AnswerOption { QuestionId = t1q3.Id, OptionText = "модульное", IsCorrect = true },
            new AnswerOption { QuestionId = t1q3.Id, OptionText = "unit", IsCorrect = true },

            // t1q4 (Matching)
            new AnswerOption { QuestionId = t1q4.Id, OptionText = "Модульное", MatchPair = "Проверка отдельного класса или метода", IsCorrect = true },
            new AnswerOption { QuestionId = t1q4.Id, OptionText = "Интеграционное", MatchPair = "Проверка взаимодействия модулей и БД", IsCorrect = true },
            new AnswerOption { QuestionId = t1q4.Id, OptionText = "Приёмочное", MatchPair = "Финальная проверка готовности к вводу в эксплуатацию", IsCorrect = true },

            // t1q5 (FillInBlank)
            new AnswerOption { QuestionId = t1q5.Id, OptionText = "статическим", IsCorrect = true },
            new AnswerOption { QuestionId = t1q5.Id, OptionText = "статическое", IsCorrect = true }
        );

        // Вопросы к Тесту 2
        var t2q1 = new TestQuestion
        {
            TestId = test2.Id,
            OrderIndex = 1,
            QuestionType = QuestionType.SingleChoice,
            QuestionText = "Поле принимает целые числа от 1 до 100 включительно. Какой набор значений относится к анализу граничных значений?"
        };
        var t2q2 = new TestQuestion
        {
            TestId = test2.Id,
            OrderIndex = 2,
            QuestionType = QuestionType.SingleChoice,
            QuestionText = "Какая техника тест-дизайна лучше всего подходит для проверки сложных бизнес-правил с комбинацией нескольких логических условий?"
        };
        context.TestQuestions.AddRange(t2q1, t2q2);
        await context.SaveChangesAsync();

        context.AnswerOptions.AddRange(
            new AnswerOption { QuestionId = t2q1.Id, OptionText = "0, 1, 100, 101", IsCorrect = true },
            new AnswerOption { QuestionId = t2q1.Id, OptionText = "25, 50, 75", IsCorrect = false },
            new AnswerOption { QuestionId = t2q1.Id, OptionText = "-500, 500", IsCorrect = false },

            new AnswerOption { QuestionId = t2q2.Id, OptionText = "Таблица принятия решений (Decision Table)", IsCorrect = true },
            new AnswerOption { QuestionId = t2q2.Id, OptionText = "Стресс-тестирование", IsCorrect = false },
            new AnswerOption { QuestionId = t2q2.Id, OptionText = "Статический анализ кода", IsCorrect = false }
        );

        // Вопросы к Тесту 3
        var t3q1 = new TestQuestion
        {
            TestId = test3.Id,
            OrderIndex = 1,
            QuestionType = QuestionType.SingleChoice,
            QuestionText = "Какой HTTP-код или действие должен вернуть сервер при попытке студента открыть закрытую лекцию по прямой ссылке?"
        };
        var t3q2 = new TestQuestion
        {
            TestId = test3.Id,
            OrderIndex = 2,
            QuestionType = QuestionType.MultipleChoice,
            QuestionText = "Что обязательно входит в регламент резервного копирования разработанного учебного кабинета?"
        };
        context.TestQuestions.AddRange(t3q1, t3q2);
        await context.SaveChangesAsync();

        context.AnswerOptions.AddRange(
            new AnswerOption { QuestionId = t3q1.Id, OptionText = "403 Forbidden (Отказ в доступе на стороне сервера)", IsCorrect = true },
            new AnswerOption { QuestionId = t3q1.Id, OptionText = "200 OK с пустым текстом", IsCorrect = false },
            new AnswerOption { QuestionId = t3q1.Id, OptionText = "Скрытие кнопки только средствами CSS", IsCorrect = false },

            new AnswerOption { QuestionId = t3q2.Id, OptionText = "Резервная копия базы данных PostgreSQL", IsCorrect = true },
            new AnswerOption { QuestionId = t3q2.Id, OptionText = "Резервная копия каталога загруженных учебных материалов", IsCorrect = true },
            new AnswerOption { QuestionId = t3q2.Id, OptionText = "Удаление журнала аудита", IsCorrect = false }
        );
        await context.SaveChangesAsync();

        // 8. Начальный прогресс для обоих студентов: Лекция 1 открыта, Лекции 2 и 3 заблокированы
        foreach (var st in new[] { student1, student2 })
        {
            context.LectureProgresses.AddRange(
                new LectureProgress
                {
                    StudentId = st.Id,
                    LectureId = lecture1.Id,
                    Status = LectureStatus.Open,
                    IsManuallyUnlocked = false
                },
                new LectureProgress
                {
                    StudentId = st.Id,
                    LectureId = lecture2.Id,
                    Status = LectureStatus.Locked,
                    IsManuallyUnlocked = false
                },
                new LectureProgress
                {
                    StudentId = st.Id,
                    LectureId = lecture3.Id,
                    Status = LectureStatus.Locked,
                    IsManuallyUnlocked = false
                }
            );
        }

        context.AuditLogs.Add(new AuditLog
        {
            TeacherId = teacher.Id,
            ActionType = "SystemInit",
            Description = "Первичная инициализация БД (DbSeeder): созданы тестовые пользователи, группа ИСП-41, дисциплина «Внедрение и поддержка компьютерных систем» и 3 последовательные лекции.",
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }
}

