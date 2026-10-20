"""
Mermaid-диаграммы для итогового отчёта (ER-схема БД и алгоритм открытия лекций)
и их рендеринг в PNG через Playwright + Google Chrome (mermaid.js загружается с CDN).

Использование:  python scripts/report_diagrams.py
Результат:      docs/diagrams/er_diagram.png, docs/diagrams/lecture_unlock_flow.png
"""
import os
from playwright.sync_api import sync_playwright

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "docs", "diagrams")

ER_DIAGRAM = """erDiagram
    Users ||--o{ GroupMemberships : "StudentId"
    StudentGroups ||--o{ GroupMemberships : "StudentGroupId"
    Disciplines ||--o{ Topics : "DisciplineId"
    Topics ||--o{ Lectures : "TopicId"
    Lectures ||--o{ ControlQuestions : "LectureId"
    Lectures ||--o{ Tests : "LectureId"
    Tests ||--o{ TestQuestions : "TestId"
    TestQuestions ||--o{ AnswerOptions : "QuestionId"
    Users ||--o{ StudentAnswers : "StudentId"
    ControlQuestions ||--o{ StudentAnswers : "QuestionId"
    Users ||--o{ TestAttempts : "StudentId"
    Tests ||--o{ TestAttempts : "TestId"
    Users ||--o{ LectureProgress : "StudentId"
    Lectures ||--o{ LectureProgress : "LectureId"
    Users |o--o{ AuditLogs : "TeacherId, TargetStudentId"
    Lectures |o--o{ AuditLogs : "LectureId"

    Users {
        int Id PK
        varchar Login UK
        text PasswordHash
        varchar FullName
        varchar Role
    }
    StudentGroups {
        int Id PK
        varchar Name UK
    }
    GroupMemberships {
        int Id PK
        int StudentId FK
        int StudentGroupId FK
    }
    Disciplines {
        int Id PK
        varchar Title
        varchar Description
    }
    Topics {
        int Id PK
        int DisciplineId FK
        varchar Title
        int OrderIndex
    }
    Lectures {
        int Id PK
        int TopicId FK
        varchar Title
        varchar MaterialFilePath
        int OrderIndex
        bool RequireControlQuestions
        bool WaitForTeacherApproval
        bool RequireTestPass
    }
    ControlQuestions {
        int Id PK
        int LectureId FK
        varchar QuestionText
        varchar QuestionType
        varchar CorrectAnswer
    }
    Tests {
        int Id PK
        int LectureId FK
        float PassingPercentage
        int MaxAttempts
        int TimeLimitMinutes
        bool ShuffleQuestions
        bool RequireRetryOnFail
    }
    TestQuestions {
        int Id PK
        int TestId FK
        varchar QuestionText
        varchar QuestionType
    }
    AnswerOptions {
        int Id PK
        int QuestionId FK
        varchar OptionText
        bool IsCorrect
        varchar MatchPair
    }
    StudentAnswers {
        int Id PK
        int StudentId FK
        int QuestionId FK
        text AnswerText
        bool IsApproved
        bool CheckedByTeacher
    }
    TestAttempts {
        int Id PK
        int StudentId FK
        int TestId FK
        timestamptz AttemptDate
        float ScorePercentage
        int CorrectCount
        varchar Status
    }
    LectureProgress {
        int Id PK
        int StudentId FK
        int LectureId FK
        varchar Status
        bool IsManuallyUnlocked
        int ExtraAttemptsGranted
    }
    AuditLogs {
        int Id PK
        int TeacherId FK
        int TargetStudentId FK
        int LectureId FK
        varchar ActionType
        timestamptz CreatedAt
    }
"""

FLOW_DIAGRAM = """flowchart TD
    A["1. Авторизация студента (Cookie, роль Student)"] --> B["2. Выбор дисциплины в личном кабинете"]
    B --> C["3. Сервер вычисляет первую доступную лекцию (LearningProgressService)"]
    C --> D["4. Изучение теоретического материала (HTML-конспект, PDF/DOCX)"]
    D --> E["5. Ответы на контрольные вопросы (6 типов)"]
    E --> F["6. Прохождение итогового теста (таймер, перемешивание, лимит попыток)"]
    F --> G{"7. Все условия выполнены? Вопросы зачтены, развёрнутый ответ одобрен, тест не ниже 70%"}
    G -- "Да" --> H["8. Статус лекции Completed, следующая лекция переходит в Open"]
    G -- "Развёрнутый ответ не проверен" --> W["Статус WaitingTeacherReview, следующая лекция закрыта"]
    W -- "Преподаватель зачёл ответ" --> G
    G -- "Нет" --> I{"9. Остались попытки?"}
    I -- "Да" --> D
    I -- "Нет" --> J["Преподаватель добавляет попытки или открывает лекцию вручную (AuditLogs)"]
    J --> D
    X["Прямой URL к закрытой лекции, тесту или файлу"] --> Y["CheckStudentAccessToLectureAsync: Allowed = false"]
    Y --> Z["Перенаправление на /Account/AccessDenied, HTTP 403 Forbidden"]
"""

HTML = """<!DOCTYPE html><html><head><meta charset="utf-8">
<script src="https://cdn.jsdelivr.net/npm/mermaid@10.9.1/dist/mermaid.min.js"></script>
<style>body{margin:0;padding:24px;background:#fff;font-family:'Times New Roman',serif}</style>
</head><body><pre class="mermaid">%s</pre>
<script>
mermaid.initialize({startOnLoad:false, theme:'neutral', fontFamily:'Arial',
  er:{useMaxWidth:false, fontSize:14}, flowchart:{useMaxWidth:false, htmlLabels:true, wrappingWidth:260}});
mermaid.run().then(()=>{document.body.setAttribute('data-done','1')});
</script></body></html>"""


def render(page, source: str, out_name: str):
    page.set_content(HTML % source)
    page.wait_for_selector("body[data-done='1']", timeout=60000)
    el = page.locator("pre.mermaid svg")
    path = os.path.join(OUT, out_name)
    el.screenshot(path=path)
    print(f"  [OK] {out_name} ({os.path.getsize(path)} bytes)")


def main():
    os.makedirs(OUT, exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="chrome", headless=True)
        page = browser.new_page(viewport={"width": 2000, "height": 1200}, device_scale_factor=2)
        render(page, ER_DIAGRAM, "er_diagram.png")
        render(page, FLOW_DIAGRAM, "lecture_unlock_flow.png")
        browser.close()


if __name__ == "__main__":
    main()
