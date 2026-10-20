"""
Автоматическое снятие реальных скриншотов интерфейса «Электронного учебного кабинета»
для итогового отчёта (docs/screenshots/01..12).

Требования: pip install playwright pillow; установленный Google Chrome (channel="chrome").
Приложение должно быть запущено на чистой демонстрационной БД (после DbSeeder):
    dotnet run --no-launch-profile --urls http://localhost:5099

Сценарий:
  1. Аноним: главная страница + страница входа (01).
  2. student1: изучает Лекцию 1 (10), отвечает на 5 контрольных вопросов, сдаёт тест №1
     (неудачная попытка 40% + успешная 100%), видит структуру дисциплины (09),
     открывает тест по Лекции 2 с таймером (11), отправляет развёрнутый ответ по Лекции 2,
     открывает личный кабинет (08).
  3. student2: пытается открыть закрытую Лекцию 2 по прямой ссылке -> 403 (12).
  4. teacher: добавляет попытки student2 (запись в AuditLogs), снимает 02–07.
"""
import os
import sys
from playwright.sync_api import sync_playwright, Page
from PIL import Image

BASE = os.environ.get("STUDYROOM_URL", "http://localhost:5099")
OUT = os.path.join(os.path.dirname(__file__), "..", "docs", "screenshots")
OUT = os.path.abspath(OUT)
os.makedirs(OUT, exist_ok=True)

VIEWPORT = {"width": 1440, "height": 900}
MAX_HEIGHT = 1900  # обрезка очень длинных страниц, чтобы рисунок помещался на лист A4


def shot(page: Page, name: str, full: bool = True, max_height: int = MAX_HEIGHT, top: int = 0):
    path = os.path.join(OUT, name)
    page.wait_for_load_state("networkidle")
    page.wait_for_timeout(400)
    if full:
        total = page.evaluate("document.documentElement.scrollHeight")
        height = min(total - top, max_height)
        page.screenshot(path=path, full_page=True,
                        clip={"x": 0, "y": top, "width": VIEWPORT["width"], "height": height})
    else:
        page.screenshot(path=path)
    print(f"  [OK] {name} ({os.path.getsize(path)} bytes)")


def login(page: Page, login_name: str, password: str):
    page.goto(f"{BASE}/Account/Logout")
    page.goto(f"{BASE}/Account/Login")
    page.fill("input[name='Input.Login']", login_name)
    page.fill("input[name='Input.Password']", password)
    page.click("button:has-text('Войти в систему')")
    page.wait_for_load_state("networkidle")


def logout(page: Page):
    # Выход выполняется POST-формой в шапке
    btn = page.locator("form[action*='Logout'] button")
    if btn.count() > 0:
        btn.first.click()
        page.wait_for_load_state("networkidle")


def fill_matching(page: Page, prefix: str, mapping: dict):
    """Заполняет выпадающие списки вопроса на соответствие по тексту левой части."""
    page.evaluate(
        """([prefix, mapping]) => {
            document.querySelectorAll(`select[name^='${prefix}']`).forEach(sel => {
                const left = sel.parentElement.querySelector('.input-group-text').innerText.trim();
                const right = mapping[left] ?? sel.options[1].value;
                sel.value = right;
                sel.dispatchEvent(new Event('change', { bubbles: true }));
            });
        }""",
        [prefix, mapping],
    )


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="chrome", headless=True)
        ctx = browser.new_context(viewport=VIEWPORT, locale="ru-RU", device_scale_factor=1)
        page = ctx.new_page()

        # ---------- 01. Главная + вход ----------
        print("[1] Аноним: главная и вход")
        page.goto(f"{BASE}/")
        home = os.path.join(OUT, "_home.png")
        page.wait_for_load_state("networkidle")
        page.screenshot(path=home)
        page.goto(f"{BASE}/Account/Login")
        page.wait_for_load_state("networkidle")
        loginp = os.path.join(OUT, "_login.png")
        page.screenshot(path=loginp)
        a, b = Image.open(home), Image.open(loginp)
        combo = Image.new("RGB", (a.width, a.height + b.height + 12), (180, 180, 180))
        combo.paste(a, (0, 0))
        combo.paste(b, (0, a.height + 12))
        combo.save(os.path.join(OUT, "01_home_login.png"), optimize=True)
        a.close(); b.close()
        os.remove(home); os.remove(loginp)
        print("  [OK] 01_home_login.png")

        # ---------- student1 ----------
        print("[2] student1")
        login(page, "student1", "Student123!")

        page.goto(f"{BASE}/Student/Lecture?id=1")
        page.wait_for_load_state("networkidle")
        # Headless Chrome не отрисовывает встроенный PDF-плагин (чёрный прямоугольник),
        # поэтому сворачиваем PDF-плеер штатной кнопкой — ссылки «Открыть»/«Скачать» остаются видны.
        toggle = page.locator("button[data-bs-target='#pdfPreviewCollapse']")
        if toggle.count() > 0:
            toggle.first.click()
            page.wait_for_timeout(700)
        form_bottom = page.evaluate(
            "(() => { const f = document.querySelector(\"form[action*='SubmitControlAnswers']\");"
            " return f ? Math.ceil(f.getBoundingClientRect().bottom + window.scrollY + 24) : 2400; })()")
        shot(page, "10_student1_lecture.png", top=0, max_height=form_bottom)

        # Контрольные вопросы Лекции 1 (5 типов с автопроверкой)
        page.fill("input[name='q_1']", "тестирование")
        page.check("input[name='q_2'][value='Статическое тестирование']")
        for v in ["Модульное (Unit)", "Интеграционное", "Системное"]:
            page.check(f"input[name='q_3'][value='{v}']")
        fill_matching(page, "q_4_match_", {
            "Белый ящик": "Полное знание кода",
            "Чёрный ящик": "Без знания кода",
            "Серый ящик": "Частичное знание кода",
        })
        page.fill("input[name='q_5']", "регрессионным")
        page.click("form[action*='SubmitControlAnswers'] button[type='submit']")
        page.wait_for_load_state("networkidle")

        # Тест №1: неудачная попытка (40%)
        page.goto(f"{BASE}/Student/Test?lectureId=1")
        page.check("input[name='tq_1'][value='Верификация']")
        page.check("input[name='tq_2'][value='Выполняется исключительно компилятором']")
        page.fill("input[name='tq_3']", "модульное")
        fill_matching(page, "tq_4_match_", {})
        page.fill("input[name='tq_5']", "динамическим")
        page.click("#testForm button[type='submit']")
        page.wait_for_load_state("networkidle")

        # Тест №1: успешная попытка (100%)
        page.goto(f"{BASE}/Student/Test?lectureId=1")
        page.check("input[name='tq_1'][value='Верификация']")
        page.check("input[name='tq_2'][value='Не требует доступа к исходному коду программы']")
        page.check("input[name='tq_2'][value='Основано на проверке требований и пользовательского интерфейса']")
        page.fill("input[name='tq_3']", "модульное")
        fill_matching(page, "tq_4_match_", {
            "Модульное": "Проверка отдельного класса или метода",
            "Интеграционное": "Проверка взаимодействия модулей и БД",
            "Приёмочное": "Финальная проверка готовности к вводу в эксплуатацию",
        })
        page.fill("input[name='tq_5']", "статическим")
        page.click("#testForm button[type='submit']")
        page.wait_for_load_state("networkidle")

        # 09. Структура дисциплины: Л1 изучена, Л2 открыта, Л3 заблокирована
        page.goto(f"{BASE}/Student/Discipline?id=1")
        shot(page, "09_student1_discipline.png")

        # 11. Тест по Лекции 2 с таймером
        page.goto(f"{BASE}/Student/Test?lectureId=2")
        page.wait_for_timeout(2500)  # дать таймеру обратного отсчёта начать отсчёт
        shot(page, "11_student1_test.png")

        # Развёрнутый ответ по Лекции 2 (уходит на проверку преподавателю)
        page.goto(f"{BASE}/Student/Lecture?id=2")
        page.check("input[name='q_6'][value='Классы эквивалентности']")
        page.fill("input[name='q_7']", "граничных")
        page.fill("textarea[name='q_8']",
                  "Обязательные поля тест-кейса: ID, название, предусловия, шаги, входные данные, "
                  "ожидаемый результат, фактический результат и статус. Пример: TC-01 «Вход в учебный кабинет "
                  "с корректными данными». Предусловие: создан пользователь student1. Шаги: открыть /Account/Login, "
                  "ввести логин student1 и пароль, нажать «Войти». Ожидаемый результат: открыт личный кабинет студента.")
        page.click("form[action*='SubmitControlAnswers'] button[type='submit']")
        page.wait_for_load_state("networkidle")

        # 08. Личный кабинет
        page.goto(f"{BASE}/Student/Index")
        shot(page, "08_student1_cabinet.png")
        logout(page)

        # ---------- student2: блокировка ----------
        print("[3] student2")
        login(page, "student2", "Student123!")
        page.goto(f"{BASE}/Student/Lecture?id=2")
        shot(page, "12_student2_forbidden.png", full=False)
        logout(page)

        # ---------- teacher ----------
        print("[4] teacher")
        login(page, "teacher", "Teacher123!")
        page.goto(f"{BASE}/Teacher/Reports?DisciplineId=1&GroupId=1")
        # Добавление попыток student2 по Лекции 1 (фиксируется в AuditLogs)
        forms = page.locator("form[action*='GrantExtraAttempts']")
        if forms.count() > 0:
            # последняя строка ведомости — student2 (Петрова), первая лекция
            target = page.locator("tr", has_text="Петрова").locator("form[action*='GrantExtraAttempts'] button").first
            if target.count() > 0:
                target.click()
                page.wait_for_load_state("networkidle")

        page.goto(f"{BASE}/Teacher/Index")
        shot(page, "02_teacher_dashboard.png")
        page.goto(f"{BASE}/Teacher/Disciplines")
        shot(page, "03_teacher_disciplines.png")
        page.goto(f"{BASE}/Teacher/LectureEdit?id=1")
        shot(page, "04_teacher_lecture_edit.png", max_height=2300)
        page.goto(f"{BASE}/Teacher/Reviews")
        shot(page, "05_teacher_reviews.png")
        page.goto(f"{BASE}/Teacher/Reports?DisciplineId=1&GroupId=1")
        shot(page, "06_teacher_reports.png")
        page.goto(f"{BASE}/Teacher/Users")
        shot(page, "07_teacher_users.png")
        logout(page)

        browser.close()

    expected = [
        "01_home_login.png", "02_teacher_dashboard.png", "03_teacher_disciplines.png",
        "04_teacher_lecture_edit.png", "05_teacher_reviews.png", "06_teacher_reports.png",
        "07_teacher_users.png", "08_student1_cabinet.png", "09_student1_discipline.png",
        "10_student1_lecture.png", "11_student1_test.png", "12_student2_forbidden.png",
    ]
    missing = [f for f in expected if not os.path.exists(os.path.join(OUT, f))
               or os.path.getsize(os.path.join(OUT, f)) < 10_000]
    if missing:
        print("MISSING/EMPTY:", missing)
        sys.exit(1)
    print(f"ALL 12 SCREENSHOTS CREATED in {OUT}")


if __name__ == "__main__":
    main()
