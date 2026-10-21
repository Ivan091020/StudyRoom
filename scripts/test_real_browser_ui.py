import os
import subprocess
import sys
import time
from playwright.sync_api import sync_playwright

BASE_URL = "http://localhost:5099"
PSQL_PATH = r"C:\Program Files\PostgreSQL\18\bin\psql.exe"


def reset_student1_progress_in_db():
    """Сбрасывает прогресс student1 к начальному состоянию перед полным браузерным прогоном."""
    env = os.environ.copy()
    env["PGPASSWORD"] = "postgres"
    sql = """
    DELETE FROM "StudentAnswers" WHERE "StudentId" = (SELECT "Id" FROM "Users" WHERE "Login" = 'student1');
    DELETE FROM "TestAttempts" WHERE "StudentId" = (SELECT "Id" FROM "Users" WHERE "Login" = 'student1');
    DELETE FROM "LectureProgress" WHERE "StudentId" = (SELECT "Id" FROM "Users" WHERE "Login" = 'student1');
    """
    res = subprocess.run(
        [PSQL_PATH, "-h", "localhost", "-p", "5432", "-U", "postgres", "-d", "studyroom_db", "-c", sql],
        env=env,
        capture_output=True,
        text=True,
        check=True,
    )
    return res.stdout


def fill_control_questions_on_lecture_page(page, lecture_num: int):
    """Заполняет контрольные вопросы на странице /Student/Lecture реальными кликами и вводом в браузере."""
    if lecture_num == 1:
        # 1. ShortText: "тестирование"
        page.locator('#control-questions input[type="text"]').first.fill("тестирование")
        # 2. SingleChoice: "Статическое тестирование"
        page.locator('#control-questions label:has-text("Статическое тестирование")').click()
        # 3. MultipleChoice: "Модульное (Unit)", "Интеграционное", "Системное"
        for cb in page.locator('#control-questions input[type="checkbox"]').all():
            if cb.is_checked():
                cb.uncheck()
        page.locator('#control-questions label:has-text("Модульное (Unit)")').click()
        page.locator('#control-questions label:has-text("Интеграционное")').click()
        page.locator('#control-questions label:has-text("Системное")').click()
        # 4. Matching
        matching_map = {
            "Белый ящик": "Полное знание кода",
            "Чёрный ящик": "Без знания кода",
            "Серый ящик": "Частичное знание кода",
        }
        for left_text, right_text in matching_map.items():
            group = page.locator('#control-questions .input-group').filter(has_text=left_text)
            group.locator('select').select_option(label=right_text)
        # 5. FillInBlank: "регрессионным"
        page.locator('#control-questions input[type="text"]').nth(1).fill("регрессионным")

    elif lecture_num == 2:
        # 1. SingleChoice: "Классы эквивалентности"
        page.locator('#control-questions label:has-text("Классы эквивалентности")').click()
        # 2. FillInBlank: "граничных"
        page.locator('#control-questions input[type="text"]').first.fill("граничных")
        # 3. ExtendedText
        page.locator('#control-questions textarea').first.fill(
            "Обязательные поля тест-кейса: ID, Название, Предусловия, Шаги, Ожидаемый результат. "
            "Пример TC-01: Ввод логина student1 и пароля Student123!, нажатие кнопки Войти -> Открыт кабинет студента."
        )

    elif lecture_num == 3:
        # 1. ShortText: "pg_dump"
        page.locator('#control-questions input[type="text"]').first.fill("pg_dump")
        # 2. SingleChoice: "Release"
        page.locator('#control-questions label:has-text("Release")').click()


def fill_test_questions_on_test_page(page, lecture_num: int):
    """Выбирает все правильные ответы в форме /Student/Test в реальном браузере (независимо от перемешивания)."""
    cards = page.locator('#testForm .card').all()
    assert len(cards) > 0, f"На странице теста лекции {lecture_num} не найдены карточки вопросов!"

    for card in cards:
        q_text = card.inner_text()

        if "Правильно ли мы создаём продукт" in q_text:
            card.locator('label:has-text("Верификация")').click()

        elif "чёрного ящика" in q_text:
            for cb in card.locator('input[type="checkbox"]').all():
                if cb.is_checked():
                    cb.uncheck()
            card.locator('label:has-text("Не требует доступа к исходному коду программы")').click()
            card.locator('label:has-text("Основано на проверке требований и пользовательского интерфейса")').click()

        elif "минимальный уровень тестирования" in q_text:
            card.locator('input[type="text"]').fill("модульное")

        elif "Соотнесите уровень тестирования и его назначение" in q_text:
            pairs = {
                "Модульное": "Проверка отдельного класса или метода",
                "Интеграционное": "Проверка взаимодействия модулей и БД",
                "Приёмочное": "Финальная проверка готовности к вводу в эксплуатацию",
            }
            for left_text, right_text in pairs.items():
                grp = card.locator('.input-group').filter(has_text=left_text)
                grp.locator('select').select_option(label=right_text)

        elif "Тестирование без запуска программного кода" in q_text:
            card.locator('input[type="text"]').fill("статическим")

        elif "от 1 до 100 включительно" in q_text:
            card.locator('label:has-text("0, 1, 100, 101")').click()

        elif "комбинацией нескольких логических условий" in q_text:
            card.locator('label:has-text("Таблица принятия решений (Decision Table)")').click()

        elif "попытке студента открыть закрытую лекцию" in q_text:
            card.locator('label:has-text("403 Forbidden (Отказ в доступе на стороне сервера)")').click()

        elif "регламент резервного копирования" in q_text:
            for cb in card.locator('input[type="checkbox"]').all():
                if cb.is_checked():
                    cb.uncheck()
            card.locator('label:has-text("Резервная копия базы данных PostgreSQL")').click()
            card.locator('label:has-text("Резервная копия каталога загруженных учебных материалов")').click()
        else:
            raise AssertionError(f"Неизвестный вопрос на странице теста: {q_text}")


import urllib.request


def wait_for_server(timeout_sec: int = 30):
    deadline = time.time() + timeout_sec
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(BASE_URL, timeout=2) as resp:
                if resp.status == 200:
                    return
        except Exception:
            time.sleep(0.5)
    raise RuntimeError(f"Сервер {BASE_URL} не ответил в течение {timeout_sec} сек.")


def run_e2e_browser_tests():
    sys.stdout.reconfigure(encoding="utf-8")
    print("=" * 80)
    print("ЗАПУСК РЕАЛЬНОГО БРАУЗЕРНОГО E2E-ТЕСТИРОВАНИЯ (PYTHON + PLAYWRIGHT CHROMIUM)")
    print("=" * 80)

    wait_for_server(30)
    reset_student1_progress_in_db()
    print("[INIT] Прогресс student1 сброшен к исходному состоянию (Лекция 1 открыта, Лекции 2 и 3 закрыты).")

    bad_responses = []
    total_responses = []

    with sync_playwright() as p:
        try:
            browser = p.chromium.launch(channel="chrome", headless=True)
        except Exception:
            browser = p.chromium.launch(channel="msedge", headless=True)
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()

        def handle_response(response):
            status = response.status
            url = response.url
            method = response.request.method
            total_responses.append((status, method, url))
            if status == 400 or status >= 500:
                post_data = response.request.post_data
                err_info = f"HTTP {status} on {method} {url} | post_data={post_data}"
                print(f"[CRITICAL HTTP ERROR] {err_info}")
                bad_responses.append(err_info)

        page.on("response", handle_response)

        def assert_no_bad_responses(step_name: str):
            if bad_responses:
                raise AssertionError(f"На шаге '{step_name}' обнаружены ошибки HTTP 400/500:\n" + "\n".join(bad_responses))

        # -------------------------------------------------------------------------
        # ШАГ 1. Вход студента (student1) через реальную форму входа
        # -------------------------------------------------------------------------
        print("[STEP 1] Вход в систему под аккаунтом student1...")
        page.goto(f"{BASE_URL}/Account/Login")
        page.fill('input[name="Input.Login"]', "student1")
        page.fill('input[name="Input.Password"]', "Student123!")
        page.click('button[type="submit"]')
        page.wait_for_url(f"{BASE_URL}/Student**")
        assert "Кабинет студента" in page.content() or "Иванов Иван Сергеевич" in page.content()
        assert_no_bad_responses("Вход студента student1")
        print("  -> PASS: Успешный вход в /Student/Index")

        # -------------------------------------------------------------------------
        # ШАГ 2. Переход в дисциплину и открытие Лекции 1
        # -------------------------------------------------------------------------
        print("[STEP 2] Открытие дисциплины и переход к Лекции 1...")
        page.click('a:has-text("Открыть структуру тем и лекций")')
        page.wait_for_url(f"{BASE_URL}/Student/Discipline**")
        assert "Лекция 1. Основы, цели и виды тестирования ПО" in page.content()
        assert_no_bad_responses("Открытие дисциплины")

        page.locator('a:has-text("Изучить лекцию")').first.click()
        page.wait_for_url(f"{BASE_URL}/Student/Lecture**")
        assert "1. Теоретический материал лекции" in page.content()
        assert_no_bad_responses("Открытие Лекции 1")
        print(f"  -> PASS: Лекция 1 открыта ({page.url})")

        # -------------------------------------------------------------------------
        # ШАГ 3. Заполнение и отправка всех 5 контрольных вопросов Лекции 1
        # -------------------------------------------------------------------------
        print("[STEP 3] Заполнение и отправка 5 контрольных вопросов Лекции 1...")
        fill_control_questions_on_lecture_page(page, lecture_num=1)
        page.click('#control-questions button[type="submit"]')
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Отправка контрольных вопросов Лекции 1")
        content = page.content()
        assert "Все контрольные вопросы (5 из 5) успешно зачтены!" in content, "Контрольные вопросы Лекции 1 не зачлись!"
        print("  -> PASS: Все 5 контрольных вопросов Лекции 1 зачтены без ошибок 400")

        # -------------------------------------------------------------------------
        # ШАГ 4. Прохождение Теста №1 к Лекции 1 и нажатие «Завершить тест»
        # -------------------------------------------------------------------------
        print("[STEP 4] Переход к Тесту №1, выбор всех правильных ответов и отправка формы...")
        page.click('a:has-text("Начать тестирование")')
        page.wait_for_url(f"{BASE_URL}/Student/Test**")
        assert_no_bad_responses("Открытие Теста №1")

        fill_test_questions_on_test_page(page, lecture_num=1)
        page.click('#testForm button[type="submit"]')
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Завершение Теста №1 (POST /Student/Test?lectureId=1)")

        content = page.content()
        assert "Поздравляем! Вы сдали тест с результатом 100%" in content, f"Ожидалось сообщение об успешной сдаче теста 1! URL={page.url}"
        assert "следующая лекция открыта!" in content
        print(f"  -> PASS: Тест №1 сдан на 100%, редирект на {page.url} выполнен без HTTP 400!")

        # -------------------------------------------------------------------------
        # ШАГ 5. Клик по кнопке перехода к Лекции 2
        # -------------------------------------------------------------------------
        print("[STEP 5] Клик по кнопке «Следующая лекция: Лекция 2...»...")
        next_btn = page.locator('a:has-text("Следующая лекция:")')
        assert next_btn.is_visible(), "Кнопка перехода к следующей лекции (Лекции 2) не отображается!"
        next_btn.click()
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Переход к Лекции 2")
        assert "Лекция 2. Проектирование тест-кейсов и техники тест-дизайна" in page.content()
        print(f"  -> PASS: Успешный переход к Лекции 2 ({page.url})")

        # -------------------------------------------------------------------------
        # ШАГ 6. Заполнение контрольных вопросов Лекции 2 и сдача Теста №2
        # -------------------------------------------------------------------------
        print("[STEP 6] Заполнение контрольных вопросов Лекции 2 (включая развёрнутый ответ) и сдача Теста №2...")
        fill_control_questions_on_lecture_page(page, lecture_num=2)
        page.click('#control-questions button[type="submit"]')
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Отправка контрольных вопросов Лекции 2")
        assert "на проверке у преподавателя: 1" in page.content()

        page.click('a:has-text("Начать тестирование")')
        page.wait_for_url(f"{BASE_URL}/Student/Test**")
        fill_test_questions_on_test_page(page, lecture_num=2)
        page.click('#testForm button[type="submit"]')
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Завершение Теста №2 (POST /Student/Test?lectureId=2)")
        assert "Тест успешно сдан с результатом 100%" in page.content()
        print("  -> PASS: Контрольные вопросы и Тест №2 Лекции 2 успешно отправлены без ошибок 400")

        # -------------------------------------------------------------------------
        # ШАГ 7. Проверка мульти-вкладочной работы (Преподаватель во 2-й вкладке того же браузера!)
        # -------------------------------------------------------------------------
        print("[STEP 7] Тест мульти-вкладочного переключения ролей (Teacher одобряет ответ во 2-й вкладке)...")
        teacher_page = context.new_page()
        teacher_page.on("response", handle_response)

        teacher_page.goto(f"{BASE_URL}/Account/Logout")
        teacher_page.goto(f"{BASE_URL}/Account/Login")
        teacher_page.fill('input[name="Input.Login"]', "teacher")
        teacher_page.fill('input[name="Input.Password"]', "Teacher123!")
        teacher_page.click('button[type="submit"]')
        teacher_page.wait_for_url(f"{BASE_URL}/Teacher**")
        assert_no_bad_responses("Вход преподавателя во 2-й вкладке")

        teacher_page.goto(f"{BASE_URL}/Teacher/Reviews")
        teacher_page.fill('input[name="teacherComment"]', "Отличный тест-кейс, ответ зачтён!")
        teacher_page.click('button[name="approve"][value="true"]')
        teacher_page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Одобрение развёрнутого ответа преподавателем в /Teacher/Reviews")
        assert "зачтён" in teacher_page.content()

        # Проверяем также сохранение настроек в /Teacher/LectureEdit?id=1
        teacher_page.goto(f"{BASE_URL}/Teacher/LectureEdit?id=1")
        teacher_page.click('form[action*="UpdateLecture"] button[type="submit"]')
        teacher_page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Сохранение лекции в /Teacher/LectureEdit?id=1")
        teacher_page.close()
        print("  -> PASS: Преподаватель проверил ответ и сохранил лекцию без ошибок 400")

        # -------------------------------------------------------------------------
        # ШАГ 8. Возврат студента, переход к Лекции 3 и полное завершение курса (100%)
        # -------------------------------------------------------------------------
        print("[STEP 8] Вход student1, переход с Лекции 2 на открывшуюся Лекцию 3 и сдача Теста №3...")
        page.goto(f"{BASE_URL}/Account/Logout")
        page.goto(f"{BASE_URL}/Account/Login")
        page.fill('input[name="Input.Login"]', "student1")
        page.fill('input[name="Input.Password"]', "Student123!")
        page.click('button[type="submit"]')
        page.wait_for_url(f"{BASE_URL}/Student**")

        page.goto(f"{BASE_URL}/Student/Lecture?id=2")
        next_btn_3 = page.locator('a:has-text("Следующая лекция:")')
        assert next_btn_3.is_visible(), "После одобрения ответа преподавателем кнопка перехода к Лекции 3 должна быть открыта!"
        next_btn_3.click()
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Переход к Лекции 3")
        assert "Лекция 3. Внедрение, сопровождение и резервное копирование ИС" in page.content()

        fill_control_questions_on_lecture_page(page, lecture_num=3)
        page.click('#control-questions button[type="submit"]')
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Отправка контрольных вопросов Лекции 3")
        assert "Все контрольные вопросы (2 из 2) успешно зачтены!" in page.content()

        page.click('a:has-text("Начать тестирование")')
        page.wait_for_url(f"{BASE_URL}/Student/Test**")
        fill_test_questions_on_test_page(page, lecture_num=3)
        page.click('#testForm button[type="submit"]')
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Завершение Теста №3 (POST /Student/Test?lectureId=3)")
        assert "Поздравляем! Вы сдали тест с результатом 100%" in page.content()

        page.goto(f"{BASE_URL}/Student/Discipline?id=1")
        assert "3 из 3 лекций (100%)" in page.content(), "Прогресс дисциплины должен составлять 100%!"
        print("  -> PASS: Все 3 лекции и 3 теста полностью пройдены в браузере (прогресс 100%)!")

        # -------------------------------------------------------------------------
        # ШАГ 9. Проверка альтернативных параметров URL (?id=1, ?lectureId=1, ?disciplineId=1)
        # -------------------------------------------------------------------------
        print("[STEP 9] Проверка взаимозаменяемости параметров id / lectureId / disciplineId...")
        for test_url in [
            f"{BASE_URL}/Student/Test?id=1",
            f"{BASE_URL}/Student/Test?lectureId=1",
            f"{BASE_URL}/Student/Test?testId=1",
            f"{BASE_URL}/Student/Lecture?id=1",
            f"{BASE_URL}/Student/Lecture?lectureId=1",
            f"{BASE_URL}/Student/Discipline?id=1",
            f"{BASE_URL}/Student/Discipline?disciplineId=1",
        ]:
            resp = page.goto(test_url)
            assert resp is not None and resp.status == 200, f"Ожидался статус 200 для {test_url}, получен {resp.status if resp else None}"
        assert_no_bad_responses("Проверка альтернативных параметров URL")
        print("  -> PASS: Все варианты URL (?id=1, ?lectureId=1, ?testId=1, ?disciplineId=1) возвращают HTTP 200 OK")

        # -------------------------------------------------------------------------
        # ШАГ 10. Стресс-тест: отправка уже открытой формы /Student/Test?lectureId=1 после смены сессии в соседней вкладке
        # -------------------------------------------------------------------------
        print("[STEP 10] Стресс-тест: отправка открытой формы /Student/Test?lectureId=1 после перезахода во 2-й вкладке...")
        page.goto(f"{BASE_URL}/Student/Test?lectureId=1")
        fill_test_questions_on_test_page(page, lecture_num=1)

        tab2 = context.new_page()
        tab2.on("response", handle_response)
        tab2.goto(f"{BASE_URL}/Account/Logout")
        tab2.goto(f"{BASE_URL}/Account/Login")
        tab2.fill('input[name="Input.Login"]', "teacher")
        tab2.fill('input[name="Input.Password"]', "Teacher123!")
        tab2.click('button[type="submit"]')
        tab2.wait_for_url(f"{BASE_URL}/Teacher**")
        tab2.close()

        # Теперь отправляем открытую ранее в первой вкладке форму теста!
        page.click('#testForm button[type="submit"]')
        page.wait_for_load_state("networkidle")
        assert_no_bad_responses("Отправка открытой формы теста после смены пользователя в соседней вкладке")
        print("  -> PASS: Даже при смене сессии в соседней вкладке форма /Student/Test?lectureId=1 не падает с HTTP 400!")

        # Очищаем тестовую попытку от имени teacher после стресс-теста
        env = os.environ.copy()
        env["PGPASSWORD"] = "postgres"
        subprocess.run(
            [
                PSQL_PATH, "-h", "localhost", "-p", "5432", "-U", "postgres", "-d", "studyroom_db",
                "-c", 'DELETE FROM "TestAttempts" WHERE "StudentId" = (SELECT "Id" FROM "Users" WHERE "Login" = \'teacher\');'
            ],
            env=env,
            capture_output=True,
            text=True,
            check=True,
        )

        browser.close()

    print("=" * 80)
    print(f"ИТОГ: ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ УСПЕШНО! Всего HTTP-ответов: {len(total_responses)}, ошибок 400/500: {len(bad_responses)}")
    print("=" * 80)


if __name__ == "__main__":
    run_e2e_browser_tests()
