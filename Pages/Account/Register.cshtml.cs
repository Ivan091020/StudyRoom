using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;

namespace StudyRoom.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;

    public RegisterModel(AppDbContext db, IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> GroupOptions { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Укажите логин")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Логин должен содержать от 3 до 100 символов")]
        [Display(Name = "Логин")]
        public string Login { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите ФИО")]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Укажите полное ФИО")]
        [Display(Name = "Фамилия, имя, отчество")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите пароль")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть не менее 6 символов")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Подтвердите пароль")]
        [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают")]
        [DataType(DataType.Password)]
        [Display(Name = "Подтверждение пароля")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "Учебная группа")]
        public int? StudentGroupId { get; set; }
    }

    public async Task OnGetAsync()
    {
        await LoadGroupsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadGroupsAsync();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var loginNormalized = Input.Login.Trim();
        var exists = await _db.Users.AnyAsync(u => u.Login.ToLower() == loginNormalized.ToLower());
        if (exists)
        {
            ModelState.AddModelError("Input.Login", "Пользователь с таким логином уже зарегистрирован.");
            return Page();
        }

        var user = new User
        {
            Login = loginNormalized,
            FullName = Input.FullName.Trim(),
            Role = UserRole.Student,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, Input.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (Input.StudentGroupId.HasValue)
        {
            var groupExists = await _db.StudentGroups.AnyAsync(g => g.Id == Input.StudentGroupId.Value);
            if (groupExists)
            {
                _db.GroupMemberships.Add(new GroupMembership
                {
                    StudentId = user.Id,
                    StudentGroupId = Input.StudentGroupId.Value
                });
                await _db.SaveChangesAsync();
            }
        }

        // Инициализация первой лекции в каждой дисциплине как открытой
        var disciplines = await _db.Disciplines
            .Include(d => d.Topics)
            .ThenInclude(t => t.Lectures)
            .ToListAsync();

        foreach (var disc in disciplines)
        {
            var orderedLectures = disc.Topics
                .OrderBy(t => t.OrderIndex)
                .ThenBy(t => t.Id)
                .SelectMany(t => t.Lectures.OrderBy(l => l.OrderIndex).ThenBy(l => l.Id))
                .ToList();

            for (int i = 0; i < orderedLectures.Count; i++)
            {
                _db.LectureProgresses.Add(new LectureProgress
                {
                    StudentId = user.Id,
                    LectureId = orderedLectures[i].Id,
                    Status = i == 0 ? LectureStatus.Open : LectureStatus.Locked
                });
            }
        }
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Login),
            new("FullName", user.FullName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return RedirectToPage("/Student/Index");
    }

    private async Task LoadGroupsAsync()
    {
        GroupOptions = await _db.StudentGroups
            .OrderBy(g => g.Name)
            .Select(g => new SelectListItem { Value = g.Id.ToString(), Text = g.Name })
            .ToListAsync();
    }
}

