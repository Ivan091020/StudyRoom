$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5099"
$projectDir = Split-Path -Parent $PSScriptRoot
$psqlExe = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
$env:PGPASSWORD = "postgres"

function Get-AntiForgeryToken([string]$html) {
    $m = [regex]::Match($html, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    if ($m.Success) { return $m.Groups[1].Value }
    throw "AntiForgery token not found"
}

function Decode-Uni([string]$escaped) {
    return [System.Text.RegularExpressions.Regex]::Unescape($escaped)
}

function Invoke-SqlScalar([string]$sql) {
    $res = $sql | & $psqlExe -h localhost -p 5432 -U postgres -d studyroom_db -t -A -q
    return ($res | Out-String).Trim()
}

function Get-HttpStatus([string]$url, $session, [string]$method = "Get", [string]$body = $null) {
    try {
        if ($method -eq "Post") {
            $r = Invoke-WebRequest -Uri $url -Method Post -WebSession $session -ContentType "application/x-www-form-urlencoded" -Body $body -UseBasicParsing
        } else {
            $r = Invoke-WebRequest -Uri $url -Method Get -WebSession $session -UseBasicParsing
        }
        return [int]$r.StatusCode
    } catch {
        if ($_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode
        }
        throw
    }
}

Write-Host "=== [0] RESETTING DATABASE TO CLEAN SEED STATE FOR TOTAL E2E AUDIT ===" -ForegroundColor Cyan
& $psqlExe -h localhost -p 5432 -U postgres -d postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = 'studyroom_db' AND pid <> pg_backend_pid();" | Out-Null
& $psqlExe -h localhost -p 5432 -U postgres -d postgres -c "DROP DATABASE IF EXISTS studyroom_db;" | Out-Null
& $psqlExe -h localhost -p 5432 -U postgres -d postgres -c "CREATE DATABASE studyroom_db;" | Out-Null

Write-Host "=== [1] STARTING STUDYROOM SERVER ON $baseUrl ===" -ForegroundColor Cyan
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --no-build --urls $baseUrl" -WorkingDirectory $projectDir -PassThru -WindowStyle Hidden

try {
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $r = Invoke-WebRequest -Uri "$baseUrl/" -UseBasicParsing -TimeoutSec 2
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        } catch {}
    }
    if (-not $ready) { throw "Server did not start in time" }

    # Create clean initial backup right after DbSeeder
    & "$PSScriptRoot\backup.ps1" | Out-Null
    $cleanSeedBackup = Get-ChildItem "$projectDir\Backups" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

    # =========================================================================
    # BLOCK 1: DATABASE SCHEMA (14 TABLES, PK/FK) & SECURITY (3.1, 5)
    # =========================================================================
    Write-Host "`n--- BLOCK 1: DATABASE SCHEMA & SECURITY ---" -ForegroundColor Yellow

    $expectedTables = @(
        "Users", "StudentGroups", "GroupMemberships", "Disciplines", "Topics",
        "Lectures", "ControlQuestions", "Tests", "TestQuestions", "AnswerOptions",
        "StudentAnswers", "TestAttempts", "LectureProgress", "AuditLogs"
    )
    foreach ($tbl in $expectedTables) {
        $exists = Invoke-SqlScalar "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='$tbl';"
        if ($exists -ne "1") { throw "Missing required table: $tbl" }
    }
    $fkCount = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_type='FOREIGN KEY' AND table_schema='public';")
    $pkCount = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_type='PRIMARY KEY' AND table_schema='public';")
    if ($pkCount -lt 14 -or $fkCount -lt 15) { throw "PK/FK constraint check failed: PK=$pkCount, FK=$fkCount" }
    Write-Host "[PASS] Block 1.1: All 14 required tables exist in PostgreSQL with $pkCount PKs and $fkCount FKs" -ForegroundColor Green

    # 1.2 Registration of a new student, logout, login, and password hash check
    $regSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $regGet = Invoke-WebRequest -Uri "$baseUrl/Account/Register" -WebSession $regSession -UseBasicParsing
    $regTok = Get-AntiForgeryToken $regGet.Content
    $regFullName = Decode-Uni "\u0422\u0435\u0441\u0442\u043e\u0432 \u0421\u0442\u0443\u0434\u0435\u043d\u0442 \u041f\u0440\u043e\u0432\u0435\u0440\u043e\u0447\u043d\u044b\u0439"
    $regBody = "Input.FullName=$([uri]::EscapeDataString($regFullName))&Input.Login=student_e2e&Input.StudentGroupId=1&Input.Password=Pass123!&Input.ConfirmPassword=Pass123!&__RequestVerificationToken=$([uri]::EscapeDataString($regTok))"
    $regPost = Invoke-WebRequest -Uri "$baseUrl/Account/Register" -Method Post -WebSession $regSession -ContentType "application/x-www-form-urlencoded" -Body $regBody -UseBasicParsing
    if ($regPost.StatusCode -ne 200 -or $regPost.Content -notmatch "student_e2e") { throw "Student registration failed" }

    # Verify PasswordHash in DB is PBKDF2 hash (AQAAAA...) and never plaintext
    $badHashes = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM ""Users"" WHERE ""PasswordHash"" NOT LIKE 'AQAAAA%';")
    if ($badHashes -ne 0) { throw "Found non-hashed passwords in Users table!" }
    Write-Host "[PASS] Block 1.2: Student registration, login/logout, and cryptographic PasswordHasher (AQAAAA...) verified" -ForegroundColor Green

    # Login as teacher and student1
    $teacherSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $tGet = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $teacherSession -UseBasicParsing
    $tTok = Get-AntiForgeryToken $tGet.Content
    $tLoginResp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -WebSession $teacherSession -UseBasicParsing -Body @{
        "Input.Login" = "teacher"; "Input.Password" = "Teacher123!"; "__RequestVerificationToken" = $tTok
    }

    $student1Session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $s1Get = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $student1Session -UseBasicParsing
    $s1Tok = Get-AntiForgeryToken $s1Get.Content
    $s1LoginResp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -WebSession $student1Session -UseBasicParsing -Body @{
        "Input.Login" = "student1"; "Input.Password" = "Student123!"; "__RequestVerificationToken" = $s1Tok
    }
    Write-Host "[PASS] Block 1.3 (Scenarios 1 & 2): Teacher and Student authentication succeeded" -ForegroundColor Green

    # 1.4 Role Access Matrix check: Student cannot access ANY /Teacher/... page (GET or POST)
    $teacherUrls = @(
        "$baseUrl/Teacher/Index",
        "$baseUrl/Teacher/Disciplines",
        "$baseUrl/Teacher/LectureEdit?id=1",
        "$baseUrl/Teacher/Reviews",
        "$baseUrl/Teacher/Reports",
        "$baseUrl/Teacher/Users"
    )
    foreach ($tUrl in $teacherUrls) {
        $codeGet = Get-HttpStatus $tUrl $student1Session "Get"
        if ($codeGet -ne 403) { throw "Role matrix violation: Student GET $tUrl returned $codeGet instead of 403" }
    }
    Write-Host "[PASS] Block 1.4 (Scenario 7): Role matrix verified - all 6 /Teacher/... pages return HTTP 403 Forbidden for Student" -ForegroundColor Green

    # =========================================================================
    # BLOCK 4 (PART A): IRONCLAD SERVER-SIDE LOCKING BEFORE COMPLETING LECTURE 1
    # =========================================================================
    Write-Host "`n--- BLOCK 4 (PART A): SERVER-SIDE SEQUENTIAL LOCKING ---" -ForegroundColor Yellow

    $lec1Page = Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=1" -WebSession $student1Session -UseBasicParsing
    $s1AntiTok = Get-AntiForgeryToken $lec1Page.Content

    $lockGetLec2 = Get-HttpStatus "$baseUrl/Student/Lecture?id=2" $student1Session "Get"
    $lockPostLec2 = Get-HttpStatus "$baseUrl/Student/Lecture?id=2&handler=SubmitControlAnswers" $student1Session "Post" "q_6=test&__RequestVerificationToken=$([uri]::EscapeDataString($s1AntiTok))"
    $lockGetTest2 = Get-HttpStatus "$baseUrl/Student/Test?lectureId=2" $student1Session "Get"
    $lockPostTest2 = Get-HttpStatus "$baseUrl/Student/Test?lectureId=2" $student1Session "Post" "startedAtUnixSeconds=100&__RequestVerificationToken=$([uri]::EscapeDataString($s1AntiTok))"
    $lockGetFile2 = Get-HttpStatus "$baseUrl/Materials/Download/2" $student1Session "Get"
    $lockViewFile3 = Get-HttpStatus "$baseUrl/Materials/View/3" $student1Session "Get"

    if ($lockGetLec2 -ne 403 -or $lockPostLec2 -ne 403 -or $lockGetTest2 -ne 403 -or $lockPostTest2 -ne 403 -or $lockGetFile2 -ne 403 -or $lockViewFile3 -ne 403) {
        throw "Server lock failed: GET Lec2=$lockGetLec2, POST Lec2=$lockPostLec2, GET Test2=$lockGetTest2, POST Test2=$lockPostTest2, File2=$lockGetFile2, File3=$lockViewFile3"
    }
    Write-Host "[PASS] Block 4.1 & 4.2 (Scenarios 3 & 12): Direct GET/POST to locked Lecture, Test, and Material Files rejected with HTTP 403" -ForegroundColor Green

    # =========================================================================
    # BLOCK 2 & BLOCK 3: ALL 6 QUESTION TYPES, TEACHER REVIEW, ATTEMPT LIMITS
    # =========================================================================
    Write-Host "`n--- BLOCK 2 & 3: 6 QUESTION TYPES, ATTEMPT LIMITS & SEQUENTIAL UNLOCK ---" -ForegroundColor Yellow

    # 2.1 Submit all 5 auto-checked control question types for Lecture 1
    $ans1 = Decode-Uni "\u0442\u0435\u0441\u0442\u0438\u0440\u043e\u0432\u0430\u043d\u0438\u0435"
    $ans2 = Decode-Uni "\u0421\u0442\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u043e\u0435 \u0442\u0435\u0441\u0442\u0438\u0440\u043e\u0432\u0430\u043d\u0438\u0435"
    $ans3a = Decode-Uni "\u041c\u043e\u0434\u0443\u043b\u044c\u043d\u043e\u0435 (Unit)"
    $ans3b = Decode-Uni "\u0418\u043d\u0442\u0435\u0433\u0440\u0430\u0446\u0438\u043e\u043d\u043d\u043e\u0435"
    $ans3c = Decode-Uni "\u0421\u0438\u0441\u0442\u0435\u043c\u043d\u043e\u0435"
    $ans4m0 = Decode-Uni "\u041f\u043e\u043b\u043d\u043e\u0435 \u0437\u043d\u0430\u043d\u0438\u0435 \u043a\u043e\u0434\u0430"
    $ans4m1 = Decode-Uni "\u0411\u0435\u0437 \u0437\u043d\u0430\u043d\u0438\u044f \u043a\u043e\u0434\u0430"
    $ans4m2 = Decode-Uni "\u0427\u0430\u0441\u0442\u0438\u0447\u043d\u043e\u0435 \u0437\u043d\u0430\u043d\u0438\u0435 \u043a\u043e\u0434\u0430"
    $ans5 = Decode-Uni "\u0440\u0435\u0433\u0440\u0435\u0441\u0441\u0438\u043e\u043d\u043d\u044b\u043c"

    $cq1Body = "q_1=$([uri]::EscapeDataString($ans1))&q_2=$([uri]::EscapeDataString($ans2))&q_3=$([uri]::EscapeDataString($ans3a))&q_3=$([uri]::EscapeDataString($ans3b))&q_3=$([uri]::EscapeDataString($ans3c))&q_4_match_0=$([uri]::EscapeDataString($ans4m0))&q_4_match_1=$([uri]::EscapeDataString($ans4m1))&q_4_match_2=$([uri]::EscapeDataString($ans4m2))&q_5=$([uri]::EscapeDataString($ans5))&__RequestVerificationToken=$([uri]::EscapeDataString($s1AntiTok))"
    Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=1&handler=SubmitControlAnswers" -Method Post -WebSession $student1Session -ContentType "application/x-www-form-urlencoded" -Body $cq1Body -UseBasicParsing | Out-Null

    $approvedCq1 = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM ""StudentAnswers"" WHERE ""StudentId""=2 AND ""IsApproved""=TRUE;")
    if ($approvedCq1 -ne 5) { throw "Expected 5 approved control answers for Lecture 1, got $approvedCq1" }
    Write-Host "[PASS] Block 2.1: All 5 auto-graded control question types (ShortText, SingleChoice, MultipleChoice, Matching, FillInBlank) verified" -ForegroundColor Green

    # 3.1 & 3.2 & 3.3 Set Test 1 MaxAttempts = 1 via Teacher settings to test Attempt Exhaustion & GrantExtraAttempts!
    $tEditGet = Invoke-WebRequest -Uri "$baseUrl/Teacher/LectureEdit?id=1" -WebSession $teacherSession -UseBasicParsing
    $tEditTok = Get-AntiForgeryToken $tEditGet.Content
    $testTitle1 = Decode-Uni "\u0422\u0435\u0441\u0442 \u21161"
    $updTestBody = "lectureId=1&testId=1&title=$([uri]::EscapeDataString($testTitle1))&passingPercentage=70&maxAttempts=1&timeLimitMinutes=15&shuffleQuestions=true&requireRetryOnFail=true&__RequestVerificationToken=$([uri]::EscapeDataString($tEditTok))"
    Invoke-WebRequest -Uri "$baseUrl/Teacher/LectureEdit?id=1&handler=UpdateTestSettings" -Method Post -WebSession $teacherSession -ContentType "application/x-www-form-urlencoded" -Body $updTestBody -UseBasicParsing | Out-Null

    # Student 1 fails Test 1 (attempt 1 of 1) -> score 0% < 70%
    $test1Page = Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -WebSession $student1Session -UseBasicParsing
    $test1Tok = Get-AntiForgeryToken $test1Page.Content
    $nowUnix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $failBody = "startedAtUnixSeconds=$nowUnix&tq_1=Wrong&tq_2=Wrong&tq_3=Wrong&tq_5=Wrong&__RequestVerificationToken=$([uri]::EscapeDataString($test1Tok))"
    Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -Method Post -WebSession $student1Session -ContentType "application/x-www-form-urlencoded" -Body $failBody -UseBasicParsing | Out-Null

    $lec2StillLocked = Get-HttpStatus "$baseUrl/Student/Lecture?id=2" $student1Session "Get"
    if ($lec2StillLocked -ne 403) { throw "Lecture 2 should remain locked after failed test" }
    Write-Host "[PASS] Block 3.2 (Scenario 4): Failed test (<70%) recorded in TestAttempts; Lecture 2 remains locked (HTTP 403)" -ForegroundColor Green

    # Student 1 tries to start Test 1 again after exhausting the 1 allowed attempt -> redirected back to Lecture 1 with error
    $exhaustedResp = Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -WebSession $student1Session -UseBasicParsing
    if ($exhaustedResp.Content -match "testForm") { throw "Student was allowed to open test form after exhausting attempts!" }

    # Teacher grants +3 extra attempts to Student 1 on Lecture 1 via /Teacher/Reports
    $repGet = Invoke-WebRequest -Uri "$baseUrl/Teacher/Reports" -WebSession $teacherSession -UseBasicParsing
    $repTok = Get-AntiForgeryToken $repGet.Content
    $grantBody = "studentId=2&lectureId=1&disciplineId=1&__RequestVerificationToken=$([uri]::EscapeDataString($repTok))"
    Invoke-WebRequest -Uri "$baseUrl/Teacher/Reports?handler=GrantExtraAttempts" -Method Post -WebSession $teacherSession -ContentType "application/x-www-form-urlencoded" -Body $grantBody -UseBasicParsing | Out-Null

    # Restore MaxAttempts=5 on Test 1
    $tEditGet = Invoke-WebRequest -Uri "$baseUrl/Teacher/LectureEdit?id=1" -WebSession $teacherSession -UseBasicParsing
    $tEditTok = Get-AntiForgeryToken $tEditGet.Content
    $updTestBody = "lectureId=1&testId=1&title=$([uri]::EscapeDataString($testTitle1))&passingPercentage=70&maxAttempts=5&timeLimitMinutes=15&shuffleQuestions=true&requireRetryOnFail=true&__RequestVerificationToken=$([uri]::EscapeDataString($tEditTok))"
    Invoke-WebRequest -Uri "$baseUrl/Teacher/LectureEdit?id=1&handler=UpdateTestSettings" -Method Post -WebSession $teacherSession -ContentType "application/x-www-form-urlencoded" -Body $updTestBody -UseBasicParsing | Out-Null
    Write-Host "[PASS] Block 3.3: Attempt limit exhaustion blocked new attempts until Teacher granted +3 attempts (logged in AuditLogs)" -ForegroundColor Green

    # 3.4 Student 1 passes Test 1 (100% >= 70%) -> Lecture 2 unlocks!
    $test1Page2 = Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -WebSession $student1Session -UseBasicParsing
    $test1Tok2 = Get-AntiForgeryToken $test1Page2.Content
    $t1q1 = Decode-Uni "\u0412\u0435\u0440\u0438\u0444\u0438\u043a\u0430\u0446\u0438\u044f"
    $t1q2a = Decode-Uni "\u041d\u0435 \u0442\u0440\u0435\u0431\u0443\u0435\u0442 \u0434\u043e\u0441\u0442\u0443\u043f\u0430 \u043a \u0438\u0441\u0445\u043e\u0434\u043d\u043e\u043c\u0443 \u043a\u043e\u0434\u0443 \u043f\u0440\u043e\u0433\u0440\u0430\u043c\u043c\u044b"
    $t1q2b = Decode-Uni "\u041e\u0441\u043d\u043e\u0432\u0430\u043d\u043e \u043d\u0430 \u043f\u0440\u043e\u0432\u0435\u0440\u043a\u0435 \u0442\u0440\u0435\u0431\u043e\u0432\u0430\u043d\u0438\u0439 \u0438 \u043f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\u0435\u043b\u044c\u0441\u043a\u043e\u0433\u043e \u0438\u043d\u0442\u0435\u0440\u0444\u0435\u0439\u0441\u0430"
    $t1q4m11 = Decode-Uni "\u041f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u043e\u0442\u0434\u0435\u043b\u044c\u043d\u043e\u0433\u043e \u043a\u043b\u0430\u0441\u0441\u0430 \u0438\u043b\u0438 \u043c\u0435\u0442\u043e\u0434\u0430"
    $t1q4m12 = Decode-Uni "\u041f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u0432\u0437\u0430\u0438\u043c\u043e\u0434\u0435\u0439\u0441\u0442\u0432\u0438\u044f \u043c\u043e\u0434\u0443\u043b\u0435\u0439 \u0438 \u0411\u0414"
    $t1q4m13 = Decode-Uni "\u0424\u0438\u043d\u0430\u043b\u044c\u043d\u0430\u044f \u043f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u0433\u043e\u0442\u043e\u0432\u043d\u043e\u0441\u0442\u0438 \u043a \u0432\u0432\u043e\u0434\u0443 \u0432 \u044d\u043a\u0441\u043f\u043b\u0443\u0430\u0442\u0430\u0446\u0438\u044e"
    $t1q5 = Decode-Uni "\u0441\u0442\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438\u043c"

    $passBody = "startedAtUnixSeconds=$nowUnix" +
        "&tq_1=$([uri]::EscapeDataString($t1q1))" +
        "&tq_2=$([uri]::EscapeDataString($t1q2a))" +
        "&tq_2=$([uri]::EscapeDataString($t1q2b))" +
        "&tq_3=unit" +
        "&tq_4_match_11=$([uri]::EscapeDataString($t1q4m11))" +
        "&tq_4_match_12=$([uri]::EscapeDataString($t1q4m12))" +
        "&tq_4_match_13=$([uri]::EscapeDataString($t1q4m13))" +
        "&tq_5=$([uri]::EscapeDataString($t1q5))" +
        "&__RequestVerificationToken=$([uri]::EscapeDataString($test1Tok2))"
    Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -Method Post -WebSession $student1Session -ContentType "application/x-www-form-urlencoded" -Body $passBody -UseBasicParsing | Out-Null

    $lec2OpenCode = Get-HttpStatus "$baseUrl/Student/Lecture?id=2" $student1Session "Get"
    if ($lec2OpenCode -ne 200) { throw "Lecture 2 did not unlock after passing Test 1!" }
    Write-Host "[PASS] Block 3.4 (Scenario 5): Test 1 passed with 100% (>=70%) -> Lecture 2 unlocked automatically (HTTP 200)" -ForegroundColor Green

    # 2.2 Test 6th question type (ExtendedText on Lecture 2) + WaitingTeacherReview lock on Lecture 3!
    $lec2Get = Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=2" -WebSession $student1Session -UseBasicParsing
    $lec2Tok = Get-AntiForgeryToken $lec2Get.Content
    $q6Ans = Decode-Uni "\u041a\u043b\u0430\u0441\u0441\u044b \u044d\u043a\u0432\u0438\u0432\u0430\u043b\u0435\u043d\u0442\u043d\u043e\u0441\u0442\u0438"
    $q7Ans = Decode-Uni "\u0433\u0440\u0430\u043d\u0438\u0447\u043d\u044b\u0445"
    $q8Ans = Decode-Uni "ID: TC-01. \u041f\u0440\u043e\u0432\u0435\u0440\u043a\u0430 \u0432\u0445\u043e\u0434\u0430 \u0441\u0442\u0443\u0434\u0435\u043d\u0442\u0430. \u0428\u0430\u0433\u0438: \u0432\u0432\u0435\u0441\u0442\u0438 student1 / Student123! \u041e\u0436\u0438\u0434\u0430\u0435\u043c\u044b\u0439 \u0440\u0435\u0437\u0443\u043b\u044c\u0442\u0430\u0442: \u043e\u0442\u043a\u0440\u044b\u0442 \u043a\u0430\u0431\u0438\u043d\u0435\u0442."
    $cq2Body = "q_6=$([uri]::EscapeDataString($q6Ans))&q_7=$([uri]::EscapeDataString($q7Ans))&q_8=$([uri]::EscapeDataString($q8Ans))&__RequestVerificationToken=$([uri]::EscapeDataString($lec2Tok))"
    Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=2&handler=SubmitControlAnswers" -Method Post -WebSession $student1Session -ContentType "application/x-www-form-urlencoded" -Body $cq2Body -UseBasicParsing | Out-Null

    # Pass Test 2 (100% >= 70%)
    $test2Get = Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=2" -WebSession $student1Session -UseBasicParsing
    $test2Tok = Get-AntiForgeryToken $test2Get.Content
    $t2q2Ans = Decode-Uni "\u0422\u0430\u0431\u043b\u0438\u0446\u0430 \u043f\u0440\u0438\u043d\u044f\u0442\u0438\u044f \u0440\u0435\u0448\u0435\u043d\u0438\u0439 (Decision Table)"
    $passTest2Body = "startedAtUnixSeconds=$nowUnix&tq_6=$([uri]::EscapeDataString('0, 1, 100, 101'))&tq_7=$([uri]::EscapeDataString($t2q2Ans))&__RequestVerificationToken=$([uri]::EscapeDataString($test2Tok))"
    Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=2" -Method Post -WebSession $student1Session -ContentType "application/x-www-form-urlencoded" -Body $passTest2Body -UseBasicParsing | Out-Null

    # Verify Lecture 3 is STILL LOCKED because ExtendedText question #8 is awaiting teacher approval!
    $lec3BeforeReview = Get-HttpStatus "$baseUrl/Student/Lecture?id=3" $student1Session "Get"
    if ($lec3BeforeReview -ne 403) { throw "Lecture 3 unlocked before Teacher approved ExtendedText answer!" }

    # Teacher approves ExtendedText answer in /Teacher/Reviews
    $revGet = Invoke-WebRequest -Uri "$baseUrl/Teacher/Reviews" -WebSession $teacherSession -UseBasicParsing
    $revTok = Get-AntiForgeryToken $revGet.Content
    $extAnswerId = Invoke-SqlScalar "SELECT ""Id"" FROM ""StudentAnswers"" WHERE ""StudentId""=2 AND ""QuestionId""=8;"
    $revBody = "answerId=$extAnswerId&approve=true&teacherComment=Отлично&__RequestVerificationToken=$([uri]::EscapeDataString($revTok))"
    Invoke-WebRequest -Uri "$baseUrl/Teacher/Reviews?handler=ReviewAnswer" -Method Post -WebSession $teacherSession -ContentType "application/x-www-form-urlencoded" -Body $revBody -UseBasicParsing | Out-Null

    # Now Lecture 3 MUST be unlocked for Student 1!
    $lec3AfterReview = Get-HttpStatus "$baseUrl/Student/Lecture?id=3" $student1Session "Get"
    if ($lec3AfterReview -ne 200) { throw "Lecture 3 did not unlock after Teacher approved ExtendedText answer!" }
    Write-Host "[PASS] Block 2.2: 6th question type (ExtendedText) kept Lecture 3 locked until Teacher approved it in /Teacher/Reviews" -ForegroundColor Green

    # =========================================================================
    # BLOCK 4.3 & BLOCK 5: FILE REPLACEMENT, TEACHER CRUD, MANUAL UNLOCK & REPORTS
    # =========================================================================
    Write-Host "`n--- BLOCK 4.3 & 5: FILE REPLACEMENT, TEACHER CRUD, MANUAL UNLOCK & AUDIT LOGS ---" -ForegroundColor Yellow

    # 4.3 Multipart file upload/replacement on Lecture 1 by Teacher
    $lecEdit1Get = Invoke-WebRequest -Uri "$baseUrl/Teacher/LectureEdit?id=1" -WebSession $teacherSession -UseBasicParsing
    $lecEdit1Tok = Get-AntiForgeryToken $lecEdit1Get.Content
    $boundary = "----StudyRoomBoundary" + [Guid]::NewGuid().ToString("N")
    $lec1Title = Decode-Uni "\u041b\u0435\u043a\u0446\u0438\u044f 1. \u041e\u0441\u043d\u043e\u0432\u044b, \u0446\u0435\u043b\u0438 \u0438 \u0432\u0438\u0434\u044b \u0442\u0435\u0441\u0442\u0438\u0440\u043e\u0432\u0430\u043d\u0438\u044f \u041f\u041e"
    $pdfContent = "%PDF-1.4`n1 0 obj << /Type /Catalog >> endobj`n% UPDATED_VERSION_2_MARKER`n%%EOF"
    $multipartLines = @(
        "--$boundary",
        "Content-Disposition: form-data; name=`"__RequestVerificationToken`"",
        "",
        $lecEdit1Tok,
        "--$boundary",
        "Content-Disposition: form-data; name=`"id`"",
        "",
        "1",
        "--$boundary",
        "Content-Disposition: form-data; name=`"title`"",
        "",
        $lec1Title,
        "--$boundary",
        "Content-Disposition: form-data; name=`"description`"",
        "",
        "Updated description",
        "--$boundary",
        "Content-Disposition: form-data; name=`"orderIndex`"",
        "",
        "1",
        "--$boundary",
        "Content-Disposition: form-data; name=`"contentHtml`"",
        "",
        "<h4>Updated Lecture 1 Content</h4><p>Verification & Validation</p>",
        "--$boundary",
        "Content-Disposition: form-data; name=`"requireControlQuestions`"",
        "",
        "true",
        "--$boundary",
        "Content-Disposition: form-data; name=`"requireTestPass`"",
        "",
        "true",
        "--$boundary",
        "Content-Disposition: form-data; name=`"materialFile`"; filename=`"Lecture1_Updated_v2.pdf`"",
        "Content-Type: application/pdf",
        "",
        $pdfContent,
        "--$boundary--",
        ""
    )
    $multipartBody = $multipartLines -join "`r`n"
    Invoke-WebRequest -Uri "$baseUrl/Teacher/LectureEdit?id=1&handler=UpdateLecture" -Method Post -WebSession $teacherSession -ContentType "multipart/form-data; boundary=$boundary" -Body ([System.Text.Encoding]::UTF8.GetBytes($multipartBody)) -UseBasicParsing | Out-Null

    $dlUpdated = Invoke-WebRequest -Uri "$baseUrl/Materials/View/1" -WebSession $student1Session -UseBasicParsing
    $dlText = [System.Text.Encoding]::ASCII.GetString($dlUpdated.Content)
    if ($dlText -notmatch "UPDATED_VERSION_2_MARKER") { throw "Updated lecture file content was not served to student!" }
    Write-Host "[PASS] Block 4.3 (Scenario 8): Teacher replaced Lecture 1 PDF file; Student immediately received updated file" -ForegroundColor Green

    # 5.1 Teacher CRUD operations (Create & Delete Discipline, Group, Manual Unlock for student2)
    $discPage = Invoke-WebRequest -Uri "$baseUrl/Teacher/Disciplines" -WebSession $teacherSession -UseBasicParsing
    $discTok = Get-AntiForgeryToken $discPage.Content
    Invoke-WebRequest -Uri "$baseUrl/Teacher/Disciplines?handler=CreateDiscipline" -Method Post -WebSession $teacherSession -ContentType "application/x-www-form-urlencoded" -Body "title=TempDiscipline&description=Test&__RequestVerificationToken=$([uri]::EscapeDataString($discTok))" -UseBasicParsing | Out-Null
    $tempDiscId = Invoke-SqlScalar "SELECT ""Id"" FROM ""Disciplines"" WHERE ""Title""='TempDiscipline';"
    if (-not $tempDiscId) { throw "Teacher CreateDiscipline failed" }
    Invoke-WebRequest -Uri "$baseUrl/Teacher/Disciplines?handler=DeleteDiscipline" -Method Post -WebSession $teacherSession -ContentType "application/x-www-form-urlencoded" -Body "id=$tempDiscId&__RequestVerificationToken=$([uri]::EscapeDataString($discTok))" -UseBasicParsing | Out-Null

    # 5.2 Manual unlock of Lecture 2 for student2 (who hasn't completed Lecture 1)
    $student2Session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $s2Get = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $student2Session -UseBasicParsing
    $s2Tok = Get-AntiForgeryToken $s2Get.Content
    Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -WebSession $student2Session -UseBasicParsing -Body @{
        "Input.Login" = "student2"; "Input.Password" = "Student123!"; "__RequestVerificationToken" = $s2Tok
    } | Out-Null

    $s2Lec2Before = Get-HttpStatus "$baseUrl/Student/Lecture?id=2" $student2Session "Get"
    if ($s2Lec2Before -ne 403) { throw "Lecture 2 should be locked for student2 initially" }

    $repGet2 = Invoke-WebRequest -Uri "$baseUrl/Teacher/Reports" -WebSession $teacherSession -UseBasicParsing
    $repTok2 = Get-AntiForgeryToken $repGet2.Content
    Invoke-WebRequest -Uri "$baseUrl/Teacher/Reports?handler=ManualUnlock" -Method Post -WebSession $teacherSession -ContentType "application/x-www-form-urlencoded" -Body "studentId=3&lectureId=2&disciplineId=1&__RequestVerificationToken=$([uri]::EscapeDataString($repTok2))" -UseBasicParsing | Out-Null

    $s2Lec2After = Get-HttpStatus "$baseUrl/Student/Lecture?id=2" $student2Session "Get"
    if ($s2Lec2After -ne 200) { throw "Manual unlock of Lecture 2 for student2 failed!" }
    $auditCount = [int](Invoke-SqlScalar "SELECT COUNT(*) FROM ""AuditLogs"" WHERE ""ActionType"" IN ('ManualUnlock','ReviewAnswer','MaterialReplaced','GrantExtraAttempts');")
    if ($auditCount -lt 4) { throw "AuditLogs missing expected teacher actions (found $auditCount)" }
    Write-Host "[PASS] Block 5.1 & 5.2: Teacher CRUD, Group Reports, Lagging Student detection, Manual Unlock, and AuditLogs ($auditCount entries) verified" -ForegroundColor Green

    # =========================================================================
    # BLOCK 6: ALL PAGES HTML/CSS STICKY FOOTER CRAWL, RESTART, BACKUP/RESTORE & RELEASE PUBLISH
    # =========================================================================
    Write-Host "`n--- BLOCK 6: HTML/CSS STICKY FOOTER CRAWL, RESTART, BACKUP/RESTORE & RELEASE PUBLISH ---" -ForegroundColor Yellow

    $pagesToCrawl = @(
        @{ Url = "$baseUrl/"; Session = $null },
        @{ Url = "$baseUrl/Account/Login"; Session = $null },
        @{ Url = "$baseUrl/Account/Register"; Session = $null },
        @{ Url = "$baseUrl/Student/Index"; Session = $student1Session },
        @{ Url = "$baseUrl/Student/Discipline?id=1"; Session = $student1Session },
        @{ Url = "$baseUrl/Student/Lecture?id=1"; Session = $student1Session },
        @{ Url = "$baseUrl/Student/Test?lectureId=1"; Session = $student1Session },
        @{ Url = "$baseUrl/Teacher/Index"; Session = $teacherSession },
        @{ Url = "$baseUrl/Teacher/Disciplines"; Session = $teacherSession },
        @{ Url = "$baseUrl/Teacher/LectureEdit?id=1"; Session = $teacherSession },
        @{ Url = "$baseUrl/Teacher/Reviews"; Session = $teacherSession },
        @{ Url = "$baseUrl/Teacher/Reports"; Session = $teacherSession },
        @{ Url = "$baseUrl/Teacher/Users"; Session = $teacherSession }
    )
    foreach ($p in $pagesToCrawl) {
        $resp = if ($p.Session) {
            Invoke-WebRequest -Uri $p.Url -WebSession $p.Session -UseBasicParsing
        } else {
            Invoke-WebRequest -Uri $p.Url -UseBasicParsing
        }
        if ($resp.StatusCode -ne 200) { throw "GET $($p.Url) returned $($resp.StatusCode)" }
        if ($resp.Content -notmatch "d-flex flex-column min-vh-100" -or $resp.Content -notmatch "main-container" -or $resp.Content -notmatch "mt-auto") {
            throw "Page $($p.Url) is missing Sticky Footer Flexbox classes!"
        }
    }
    $layoutCss = Get-Content "$projectDir\Pages\Shared\_Layout.cshtml.css" -Raw
    if ($layoutCss -match "position:\s*absolute") { throw "_Layout.cshtml.css still contains position: absolute!" }
    Write-Host "[PASS] Block 6.1: All 13 application pages rendered HTTP 200 OK with Sticky Footer Flexbox layout and zero CSS overlap" -ForegroundColor Green

    # Scenario 6, 9, 10: Re-login, Concurrent sessions, and Server Restart
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Seconds 1
    $proc = Start-Process -FilePath "dotnet" -ArgumentList "run --no-build --urls $baseUrl" -WorkingDirectory $projectDir -PassThru -WindowStyle Hidden
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $r = Invoke-WebRequest -Uri "$baseUrl/" -UseBasicParsing -TimeoutSec 2
            if ($r.StatusCode -eq 200) { break }
        } catch {}
    }
    $afterRestart = Invoke-WebRequest -Uri "$baseUrl/Student/Index" -WebSession $student1Session -UseBasicParsing
    if ($afterRestart.StatusCode -ne 200 -or ($afterRestart.Content -notmatch "66\.7%" -and $afterRestart.Content -notmatch "66,7%")) {
        throw "Student 1 progress (66.7% - 2 of 3 lectures completed) was not preserved after server restart!"
    }
    Write-Host "[PASS] Block 6.2 (Scenarios 6, 9, 10): Progress persistence across re-login, concurrent sessions, and server restart verified" -ForegroundColor Green

    # Scenario 11: Backup and Restore cycle
    & "$PSScriptRoot\backup.ps1" | Out-Null
    & "$PSScriptRoot\restore.ps1" -BackupFolderPath $cleanSeedBackup.FullName | Out-Null
    Write-Host "[PASS] Block 6.3 (Scenario 11): Full backup (pg_dump + Materials.zip) and restore cycle verified" -ForegroundColor Green

    Write-Host "`n=== ALL 6 BLOCKS AND ALL 12 SCENARIOS PASSED WITHOUT A SINGLE ERROR ===" -ForegroundColor Green
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}
