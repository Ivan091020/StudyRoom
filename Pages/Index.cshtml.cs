using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StudyRoom.Data;
using StudyRoom.Models;

namespace StudyRoom.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Discipline> Disciplines { get; set; } = new();
    public int TotalStudents { get; set; }
    public int TotalLectures { get; set; }
    public int TotalTests { get; set; }

    public async Task OnGetAsync()
    {
        Disciplines = await _db.Disciplines
            .Include(d => d.Topics)
            .ThenInclude(t => t.Lectures)
            .OrderBy(d => d.Id)
            .ToListAsync();

        TotalStudents = await _db.Users.CountAsync(u => u.Role == UserRole.Student);
        TotalLectures = await _db.Lectures.CountAsync();
        TotalTests = await _db.Tests.CountAsync();
    }
}

