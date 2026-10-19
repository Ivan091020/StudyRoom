using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;
using StudyRoom.Services;

namespace StudyRoom.Pages.Teacher;

[Authorize(Policy = "TeacherOnly")]
public class UsersModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;

    public UsersModel(AppDbContext db, IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public List<User> Users { get; set; } = new();
    public List<StudentGroup> Groups { get; set; } = new();

    public async Task OnGetAsync()
    {
        Users = await _db.Users
            .Include(u => u.GroupMemberships)
            .ThenInclude(gm => gm.StudentGroup)
            .OrderBy(u => u.Role)
            .ThenBy(u => u.FullName)
            .ToListAsync();

        Groups = await _db.StudentGroups
            .Include(g => g.GroupMemberships)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateGroupAsync(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "Укажите название учебной группы.";
            return RedirectToPage();
        }

        var normalized = name.Trim();
        if (await _db.StudentGroups.AnyAsync(g => g.Name.ToLower() == normalized.ToLower()))
        {
            TempData["ErrorMessage"] = "Такая учебная группа уже существует.";
            return RedirectToPage();
        }

        _db.StudentGroups.Add(new StudentGroup
        {
            Name = normalized,
            Description = description?.Trim()
        });
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Учебная группа «{normalized}» создана.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateUserAsync(string login, string fullName, string password, UserRole role, int? groupId)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(password))
        {
            TempData["ErrorMessage"] = "Заполните логин, ФИО и пароль пользователя.";
            return RedirectToPage();
        }

        var loginNorm = login.Trim();
        if (await _db.Users.AnyAsync(u => u.Login.ToLower() == loginNorm.ToLower()))
        {
            TempData["ErrorMessage"] = "Пользователь с таким логином уже существует.";
            return RedirectToPage();
        }

        var user = new User
        {
            Login = loginNorm,
            FullName = fullName.Trim(),
            Role = role,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (role == UserRole.Student && groupId.HasValue)
        {
            _db.GroupMemberships.Add(new GroupMembership
            {
                StudentId = user.Id,
                StudentGroupId = groupId.Value
            });
            await _db.SaveChangesAsync();
        }

        _db.AuditLogs.Add(new AuditLog
        {
            TeacherId = User.GetUserId(),
            TargetStudentId = user.Id,
            ActionType = "CreateUser",
            Description = $"Создана учётная запись {user.FullName} ({user.Login}, роль: {user.Role})."
        });
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Пользователь «{user.FullName}» успешно добавлен.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAssignGroupAsync(int studentId, int groupId)
    {
        var existing = await _db.GroupMemberships
            .Where(gm => gm.StudentId == studentId)
            .ToListAsync();

        _db.GroupMemberships.RemoveRange(existing);
        if (groupId > 0)
        {
            _db.GroupMemberships.Add(new GroupMembership
            {
                StudentId = studentId,
                StudentGroupId = groupId
            });
        }
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Привязка студента к учебной группе обновлена.";
        return RedirectToPage();
    }
}
