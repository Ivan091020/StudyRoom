using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Подключение к PostgreSQL через Entity Framework Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Сервисы безопасности и бизнес-логики
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<MaterialStorageService>();
builder.Services.AddScoped<LearningProgressService>();

// 3. Настройка Cookie-аутентификации и авторизации по ролям
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "StudyRoom.Auth";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TeacherOnly", policy => policy.RequireRole(nameof(UserRole.Teacher)));
    options.AddPolicy("StudentOrTeacher", policy =>
        policy.RequireRole(nameof(UserRole.Student), nameof(UserRole.Teacher)));
});

// 4. Razor Pages с защитой папок /Teacher и /Student
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Teacher", "TeacherOnly");
    options.Conventions.AuthorizeFolder("/Student", "StudentOrTeacher");
});

var app = builder.Build();

// 5. Первичная инициализация БД и демонстрационных данных (DbSeeder)
await DbSeeder.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

// 6. Защищённый серверный обработчик отдачи файлов лекций с проверкой доступа к лекции
app.MapGet("/Materials/{mode}/{lectureId:int}", async (
    string mode,
    int lectureId,
    HttpContext httpContext,
    AppDbContext db,
    LearningProgressService progressService,
    MaterialStorageService storageService) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true)
    {
        return Results.Challenge();
    }

    var lecture = await db.Lectures.FirstOrDefaultAsync(l => l.Id == lectureId);
    if (lecture == null || string.IsNullOrWhiteSpace(lecture.MaterialFilePath))
    {
        return Results.NotFound("Учебный материал для данной лекции не найден.");
    }

    // Если запрос от студента — строгая серверная проверка открытости лекции
    if (!httpContext.User.IsTeacher())
    {
        var studentId = httpContext.User.GetUserId();
        var access = await progressService.CheckStudentAccessToLectureAsync(studentId, lectureId);
        if (!access.Allowed)
        {
            return Results.Redirect($"/Account/AccessDenied?Reason={Uri.EscapeDataString(access.DenialReason ?? "Доступ к материалу закрытой лекции запрещён.")}");
        }
    }

    var fullPath = storageService.GetFullFilePath(lecture.MaterialFilePath);
    if (fullPath == null)
    {
        return Results.NotFound("Файл материала отсутствует в хранилище сервера.");
    }

    var downloadName = lecture.MaterialOriginalFileName ?? Path.GetFileName(fullPath);
    var contentType = storageService.GetContentType(downloadName);

    if (string.Equals(mode, "Download", StringComparison.OrdinalIgnoreCase))
    {
        return Results.File(fullPath, contentType, fileDownloadName: downloadName);
    }

    // Режим View — открытие непосредственно в браузере (inline)
    httpContext.Response.Headers.Append("Content-Disposition", $"inline; filename=\"{Uri.EscapeDataString(downloadName)}\"");
    return Results.File(fullPath, contentType);
}).RequireAuthorization();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
