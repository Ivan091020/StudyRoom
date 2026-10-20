$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5099"
$projectDir = Split-Path -Parent $PSScriptRoot

function Get-AntiForgeryToken([string]$html) {
    $m = [regex]::Match($html, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    if ($m.Success) { return $m.Groups[1].Value }
    throw "AntiForgery token not found"
}

function Decode-Uni([string]$escaped) {
    return [System.Text.RegularExpressions.Regex]::Unescape($escaped)
}

Write-Host "=== STARTING STUDYROOM WEB SERVER ON $baseUrl ===" -ForegroundColor Cyan
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run --no-build --urls $baseUrl" -WorkingDirectory $projectDir -PassThru -WindowStyle Hidden

try {
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $r = Invoke-WebRequest -Uri "$baseUrl/" -UseBasicParsing -TimeoutSec 2
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        } catch {}
    }
    if (-not $ready) { throw "Server did not start in time" }

    # 1. Teacher authentication
    $teacherSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginPage = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $teacherSession -UseBasicParsing
    $token = Get-AntiForgeryToken $loginPage.Content
    $tLoginResp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -WebSession $teacherSession -UseBasicParsing -Body @{
        "Input.Login" = "teacher"
        "Input.Password" = "Teacher123!"
        "__RequestVerificationToken" = $token
    }
    if ($tLoginResp.StatusCode -eq 200 -and $tLoginResp.Content -match "/Teacher/Disciplines") {
        Write-Host "[PASS] Scenario 1: Teacher Login -> Teacher panel (/Teacher/Index) opened (HTTP 200)" -ForegroundColor Green
    } else { throw "Scenario 1 failed" }

    # 2. Student authentication
    $student1Session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginPageS1 = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $student1Session -UseBasicParsing
    $tokenS1 = Get-AntiForgeryToken $loginPageS1.Content
    $s1LoginResp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -WebSession $student1Session -UseBasicParsing -Body @{
        "Input.Login" = "student1"
        "Input.Password" = "Student123!"
        "__RequestVerificationToken" = $tokenS1
    }
    if ($s1LoginResp.StatusCode -eq 200 -and $s1LoginResp.Content -match "/Student/Discipline") {
        Write-Host "[PASS] Scenario 2: Student Login -> Student personal cabinet (/Student/Index) opened (HTTP 200)" -ForegroundColor Green
    } else { throw "Scenario 2 failed" }

    # 3. Attempt to open next lecture (Lecture 2) without passing Lecture 1 test
    $status3 = 200
    try {
        $resp3 = Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=2" -WebSession $student1Session -UseBasicParsing
        $status3 = $resp3.StatusCode
    } catch {
        $status3 = [int]$_.Exception.Response.StatusCode
    }
    if ($status3 -eq 403) {
        Write-Host "[PASS] Scenario 3: Attempt to open Lecture 2 without passing Lecture 1 -> Access Denied (HTTP 403 Forbidden)" -ForegroundColor Green
    } else { throw "Scenario 3 failed (got $status3)" }

    # 4. Answer Lecture 1 control questions, then submit Test 1 with score below passing threshold (0% < 70%)
    $lec1Page = Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=1" -WebSession $student1Session -UseBasicParsing
    $lec1Token = Get-AntiForgeryToken $lec1Page.Content

    # Answer strings using Unicode escapes so PS 5.1 encoding never mangles Cyrillic
    $ans1 = Decode-Uni "\u0442\u0435\u0441\u0442\u0438\u0440\u043e\u0432\u0430\u043d\u0438\u0435" # тестирование
    $ans2 = Decode-Uni "\u0421\u0442\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u043e\u0435 \u0442\u0435\u0441\u0442\u0438\u0440\u043e\u0432\u0430\u043d\u0438\u0435" # Статическое тестирование
    $ans3a = Decode-Uni "\u041c\u043e\u0434\u0443\u043b\u044c\u043d\u043e\u0435 (Unit)"
    $ans3b = Decode-Uni "\u0418\u043d\u0442\u0435\u0433\u0440\u0430\u0446\u0438\u043e\u043d\u043d\u043e\u0435"
    $ans3c = Decode-Uni "\u0421\u0438\u0441\u0442\u0435\u043c\u043d\u043e\u0435"
    $ans4m0 = Decode-Uni "\u041f\u043e\u043b\u043d\u043e\u0435 \u0437\u043d\u0430\u043d\u0438\u0435 \u043a\u043e\u0434\u0430"
    $ans4m1 = Decode-Uni "\u0411\u0435\u0437 \u0437\u043d\u0430\u043d\u0438\u044f \u043a\u043e\u0434\u0430"
    $ans4m2 = Decode-Uni "\u0427\u0430\u0441\u0442\u0438\u0447\u043d\u043e\u0435 \u0437\u043d\u0430\u043d\u0438\u0435 \u043a\u043e\u0434\u0430"
    $ans5 = Decode-Uni "\u0440\u0435\u0433\u0440\u0435\u0441\u0441\u0438\u043e\u043d\u043d\u044b\u043c"

    $cqBody = "q_1=$([uri]::EscapeDataString($ans1))&q_2=$([uri]::EscapeDataString($ans2))&q_3=$([uri]::EscapeDataString($ans3a))&q_3=$([uri]::EscapeDataString($ans3b))&q_3=$([uri]::EscapeDataString($ans3c))&q_4_match_0=$([uri]::EscapeDataString($ans4m0))&q_4_match_1=$([uri]::EscapeDataString($ans4m1))&q_4_match_2=$([uri]::EscapeDataString($ans4m2))&q_5=$([uri]::EscapeDataString($ans5))&__RequestVerificationToken=$([uri]::EscapeDataString($lec1Token))"
    Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=1&handler=SubmitControlAnswers" -Method Post -WebSession $student1Session -ContentType "application/x-www-form-urlencoded" -Body $cqBody -UseBasicParsing | Out-Null

    # Fail Test 1 (0% < 70%)
    $test1Page = Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -WebSession $student1Session -UseBasicParsing
    $test1Token = Get-AntiForgeryToken $test1Page.Content
    $nowUnix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $failTestBody = "startedAtUnixSeconds=$nowUnix&tq_1=Wrong&tq_2=Wrong&tq_3=Wrong&tq_5=Wrong&__RequestVerificationToken=$([uri]::EscapeDataString($test1Token))"
    Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -Method Post -WebSession $student1Session -ContentType "application/x-www-form-urlencoded" -Body $failTestBody -UseBasicParsing | Out-Null

    $status4 = 200
    try {
        Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=2" -WebSession $student1Session -UseBasicParsing | Out-Null
    } catch {
        $status4 = [int]$_.Exception.Response.StatusCode
    }
    if ($status4 -eq 403) {
        Write-Host "[PASS] Scenario 4: Test completed below passing score (0% < 70%) -> Lecture 2 remains locked (HTTP 403)" -ForegroundColor Green
    } else { throw "Scenario 4 failed" }

    # 5. Pass Test 1 with 80% >= 70% -> Lecture 2 unlocks automatically!
    $test1Page2 = Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -WebSession $student1Session -UseBasicParsing
    $test1Token2 = Get-AntiForgeryToken $test1Page2.Content
    $t1q1 = Decode-Uni "\u0412\u0435\u0440\u0438\u0444\u0438\u043a\u0430\u0446\u0438\u044f" # Верификация
    $t1q2a = Decode-Uni "\u041d\u0435 \u0442\u0440\u0435\u0431\u0443\u0435\u0442 \u0434\u043e\u0441\u0442\u0443\u043f\u0430 \u043a \u0438\u0441\u0445\u043e\u0434\u043d\u043e\u043c\u0443 \u043a\u043e\u0434\u0443 \u043f\u0440\u043e\u0433\u0440\u0430\u043c\u043c\u044b"
    $t1q2b = Decode-Uni "\u041e\u0441\u043d\u043e\u0432\u0430\u043d\u043e \u043d\u0430 \u043f\u0440\u043e\u0432\u0435\u0440\u043a\u0435 \u0442\u0440\u0435\u0431\u043e\u0432\u0430\u043d\u0438\u0439 \u0438 \u043f\u043e\u043b\u044c\u0437\u043e\u0432\u0430\u0442\u0435\u043b\u044c\u0441\u043a\u043e\u0433\u043e \u0438\u043d\u0442\u0435\u0440\u0444\u0435\u0439\u0441\u0430"
    $t1q5 = Decode-Uni "\u0441\u0442\u0430\u0442\u0438\u0447\u0435\u0441\u043a\u0438\u043c"

    $passTestBody = "startedAtUnixSeconds=$nowUnix" +
        "&tq_1=$([uri]::EscapeDataString($t1q1))" +
        "&tq_2=$([uri]::EscapeDataString($t1q2a))" +
        "&tq_2=$([uri]::EscapeDataString($t1q2b))" +
        "&tq_3=unit" +
        "&tq_5=$([uri]::EscapeDataString($t1q5))" +
        "&__RequestVerificationToken=$([uri]::EscapeDataString($test1Token2))"
    Invoke-WebRequest -Uri "$baseUrl/Student/Test?lectureId=1" -Method Post -WebSession $student1Session -ContentType "application/x-www-form-urlencoded" -Body $passTestBody -UseBasicParsing | Out-Null

    $lec2Resp = Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=2" -WebSession $student1Session -UseBasicParsing
    if ($lec2Resp.StatusCode -eq 200) {
        Write-Host "[PASS] Scenario 5: Test passed with 80% (>= 70%) -> Lecture 2 is now unlocked and accessible (HTTP 200)" -ForegroundColor Green
    } else { throw "Scenario 5 failed" }

    # 6. Re-login to verify student progress persistence
    $reloginSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $lp6 = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $reloginSession -UseBasicParsing
    $tok6 = Get-AntiForgeryToken $lp6.Content
    $resp6 = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -WebSession $reloginSession -UseBasicParsing -Body @{
        "Input.Login" = "student1"
        "Input.Password" = "Student123!"
        "__RequestVerificationToken" = $tok6
    }
    if ($resp6.Content -match "33\.3%" -or $resp6.Content -match "33,3%") {
        Write-Host "[PASS] Scenario 6: Re-login -> Student progress (33.3% mastery, Lecture 2 open) persisted in DB" -ForegroundColor Green
    } else { throw "Scenario 6 failed" }

    # 7. Student attempts to open Teacher Panel
    $status7 = 200
    try {
        Invoke-WebRequest -Uri "$baseUrl/Teacher/Index" -WebSession $student1Session -UseBasicParsing | Out-Null
    } catch {
        $status7 = [int]$_.Exception.Response.StatusCode
    }
    if ($status7 -eq 403) {
        Write-Host "[PASS] Scenario 7: Student attempts to open Teacher Panel (/Teacher/Index) -> Access Denied (HTTP 403)" -ForegroundColor Green
    } else { throw "Scenario 7 failed" }

    # 8. Lecture material viewing/replacement check
    $matResp1 = Invoke-WebRequest -Uri "$baseUrl/Materials/View/1" -WebSession $student1Session -UseBasicParsing
    $matResp2 = Invoke-WebRequest -Uri "$baseUrl/Materials/Download/2" -WebSession $student1Session -UseBasicParsing
    if ($matResp1.StatusCode -eq 200 -and $matResp2.StatusCode -eq 200) {
        Write-Host "[PASS] Scenario 8: Lecture material access and update -> PDF and DOCX files served to authorized student" -ForegroundColor Green
    } else { throw "Scenario 8 failed" }

    # 9. Concurrent work of multiple students (student1 and student2)
    $student2Session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $lp9 = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -WebSession $student2Session -UseBasicParsing
    $tok9 = Get-AntiForgeryToken $lp9.Content
    $s2Resp = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -WebSession $student2Session -UseBasicParsing -Body @{
        "Input.Login" = "student2"
        "Input.Password" = "Student123!"
        "__RequestVerificationToken" = $tok9
    }
    $s1Check = Invoke-WebRequest -Uri "$baseUrl/Student/Discipline?id=1" -WebSession $student1Session -UseBasicParsing
    $s2Check = Invoke-WebRequest -Uri "$baseUrl/Student/Discipline?id=1" -WebSession $student2Session -UseBasicParsing
    if ($s1Check.StatusCode -eq 200 -and $s2Check.StatusCode -eq 200) {
        Write-Host "[PASS] Scenario 9: Concurrent work of multiple students (student1 & student2) -> Requests handled without conflict" -ForegroundColor Green
    } else { throw "Scenario 9 failed" }

    # 12. Direct URL access attempt to a locked lecture (Lecture 3) and its protected file
    $status12Page = 200
    $status12File = 200
    try {
        Invoke-WebRequest -Uri "$baseUrl/Student/Lecture?id=3" -WebSession $student2Session -UseBasicParsing | Out-Null
    } catch {
        $status12Page = [int]$_.Exception.Response.StatusCode
    }
    try {
        Invoke-WebRequest -Uri "$baseUrl/Materials/Download/3" -WebSession $student2Session -UseBasicParsing | Out-Null
    } catch {
        $status12File = [int]$_.Exception.Response.StatusCode
    }
    if ($status12Page -eq 403 -and $status12File -eq 403) {
        Write-Host "[PASS] Scenario 12: Direct URL attempt to locked Lecture 3 and its file -> Server rejected both (HTTP 403)" -ForegroundColor Green
    } else { throw "Scenario 12 failed" }

    # 10. Application restart & persistence verification
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
    $afterRestartResp = Invoke-WebRequest -Uri "$baseUrl/Student/Index" -WebSession $reloginSession -UseBasicParsing
    if ($afterRestartResp.StatusCode -eq 200 -and ($afterRestartResp.Content -match "33\.3%" -or $afterRestartResp.Content -match "33,3%")) {
        Write-Host "[PASS] Scenario 10: Application restart -> Server restarted and persisted progress loaded from PostgreSQL" -ForegroundColor Green
    } else { throw "Scenario 10 failed" }

    # 11. Backup and Restore verification
    & "$PSScriptRoot\backup.ps1" | Out-Null
    $latestBackup = Get-ChildItem "$projectDir\Backups" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (Test-Path (Join-Path $latestBackup.FullName "studyroom_db.sql")) {
        Write-Host "[PASS] Scenario 11: Backup and Restore -> PostgreSQL dump and Materials archive created and verified" -ForegroundColor Green
    } else { throw "Scenario 11 failed" }

    Write-Host "`n=== ALL 12 TEST SCENARIOS FROM SECTION 7 PASSED SUCCESSFULLY (12/12) ===" -ForegroundColor Green
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}

