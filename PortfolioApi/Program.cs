using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PortfolioApi.Data;
using PortfolioApi.Models;
using System.Text.Json;
using System.Text.Json.Serialization;



var builder = WebApplication.CreateBuilder(args);

// Admin credentials from appsettings.json
var adminUsername = builder.Configuration["Admin:Username"] ?? "admin";
var adminPassword = builder.Configuration["Admin:Password"] ?? "admin123";
var adminPath = builder.Configuration["Admin:Path"] ?? "/admin";

// Ensure backups directory exists
var backupDir = Path.Combine(builder.Environment.ContentRootPath, "backups");
Directory.CreateDirectory(backupDir);

builder.Services.AddDbContext<PortfolioDb>(options =>
    options.UseSqlite("Data Source=portfolio.db"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = $"{adminPath}/login";
        options.AccessDeniedPath = $"{adminPath}/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();


var app = builder.Build();

// Ensure DB is created and seeded
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortfolioDb>();
    db.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

// ─── Simple in-memory rate limiter for login ───
var _failedLogins = new System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime FirstAttempt)>();
var _rateLimitLock = new object();

bool IsRateLimited(string ip)
{
    if (_failedLogins.TryGetValue(ip, out var entry))
    {
        // Reset after 15 minutes
        if (DateTime.UtcNow - entry.FirstAttempt > TimeSpan.FromMinutes(15))
        {
            _failedLogins.TryRemove(ip, out _);
            return false;
        }
        // Block after 5 failed attempts
        if (entry.Count >= 5)
            return true;
    }
    return false;
}

void RecordFailedLogin(string ip)
{
    _failedLogins.AddOrUpdate(ip,
        _ => (1, DateTime.UtcNow),
        (_, entry) => (entry.Count + 1, entry.FirstAttempt));
}

void ClearRateLimit(string ip)
{
    _failedLogins.TryRemove(ip, out _);
}

// ──────────────────────────────────────
// Auth endpoints
// ──────────────────────────────────────

app.MapGet($"{adminPath}/login", async (HttpContext context) =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync("wwwroot/admin/login.html");
});

app.MapPost("/api/auth/login", async (HttpContext context, PortfolioDb db) =>
{
    try
    {
        // Rate limiting by client IP
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (IsRateLimited(clientIp))
        {
            Console.Error.WriteLine($"[Security] Rate limit hit from {clientIp}");
            return Results.Redirect($"{adminPath}/login?error=5");
        }

        var form = await context.Request.ReadFormAsync();
        var username = form["username"].ToString();
        var password = form["password"].ToString();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return Results.Redirect($"{adminPath}/login?error=3");

        var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (admin == null)
        {
            // Create first admin if none exists
            if (!await db.AdminUsers.AnyAsync())
            {
                db.AdminUsers.Add(new AdminUser
                {
                    Username = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword)
                });
                await db.SaveChangesAsync();
                admin = await db.AdminUsers.FirstAsync(u => u.Username == "admin");
            }
            else
            {
                RecordFailedLogin(clientIp);
            return Results.Redirect($"{adminPath}/login?error=1");
            }
        }

        if (!BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
        {
            RecordFailedLogin(clientIp);
            return Results.Redirect($"{adminPath}/login?error=2");
        }

        var claims = new[] { new System.Security.Claims.Claim("username", username) };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        ClearRateLimit(clientIp);
        await context.SignInAsync(new System.Security.Claims.ClaimsPrincipal(identity));
        return Results.Redirect(adminPath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Login Error] {ex.Message}");
        return Results.Redirect($"{adminPath}/login?error=4");
    }
});

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect($"{adminPath}/login");
});

// ──────────────────────────────────────
// Admin dashboard
// ──────────────────────────────────────

app.MapGet($"{adminPath}", async (HttpContext context) =>
{
    if (!context.User.Identity?.IsAuthenticated ?? true)
    {
        context.Response.Redirect($"{adminPath}/login");
        return;
    }
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync("wwwroot/admin/index.html");
});

// ──────────────────────────────────────
// Site Content API
// ──────────────────────────────────────

var adminGroup = app.MapGroup("/api/admin");
adminGroup.RequireAuthorization();

adminGroup.MapGet("/site-content", async (HttpContext context, PortfolioDb db) =>
{
    try
    {
        var content = await db.SiteContents.FirstOrDefaultAsync();
        return Results.Ok(content);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] GET /api/admin/site-content: {ex.Message}");
        return Results.Json(new { error = "Failed to load site content" }, statusCode: 500);
    }
});

adminGroup.MapPut("/site-content", async (HttpContext context, PortfolioDb db, SiteContent updated) =>
{
    try
    {
        var content = await db.SiteContents.FirstOrDefaultAsync();
        if (content == null) return Results.NotFound();

        content.HeroTitle = updated.HeroTitle;
        content.HeroSubtitle = updated.HeroSubtitle;
        content.AboutText = updated.AboutText;
        content.AboutImageUrl = updated.AboutImageUrl;
        content.ContactEmail = updated.ContactEmail;
        content.ContactSubtitle = updated.ContactSubtitle;
        await db.SaveChangesAsync();
        return Results.Ok(content);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] PUT /api/admin/site-content: {ex.Message}");
        return Results.Json(new { error = "Failed to save site content" }, statusCode: 500);
    }
});

// ──────────────────────────────────────
// Expertise API
// ──────────────────────────────────────

adminGroup.MapGet("/expertise", async (HttpContext context, PortfolioDb db) =>
{
    try
    {
        var items = await db.Expertises.OrderBy(e => e.SortOrder).ToListAsync();
        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] GET /api/admin/expertise: {ex.Message}");
        return Results.Json(new { error = "Failed to load expertise" }, statusCode: 500);
    }
});

adminGroup.MapPut("/expertise/{id:int}", async (HttpContext context, PortfolioDb db, int id, Expertise updated) =>
{
    try
    {
        var item = await db.Expertises.FindAsync(id);
        if (item == null) return Results.NotFound();
        item.Number = updated.Number;
        item.Title = updated.Title;
        item.Description = updated.Description;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] PUT /api/admin/expertise/{id}: {ex.Message}");
        return Results.Json(new { error = "Failed to update expertise" }, statusCode: 500);
    }
});

adminGroup.MapPost("/expertise", async (HttpContext context, PortfolioDb db, Expertise newItem) =>
{
    try
    {
        var maxOrder = await db.Expertises.MaxAsync(e => (int?)e.SortOrder) ?? 0;
        newItem.SortOrder = maxOrder + 1;
        db.Expertises.Add(newItem);
        await db.SaveChangesAsync();
        return Results.Ok(newItem);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] POST /api/admin/expertise: {ex.Message}");
        return Results.Json(new { error = "Failed to add expertise" }, statusCode: 500);
    }
});

adminGroup.MapDelete("/expertise/{id:int}", async (HttpContext context, PortfolioDb db, int id) =>
{
    try
    {
        var item = await db.Expertises.FindAsync(id);
        if (item == null) return Results.NotFound();
        db.Expertises.Remove(item);
        await db.SaveChangesAsync();
        return Results.Ok();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] DELETE /api/admin/expertise/{id}: {ex.Message}");
        return Results.Json(new { error = "Failed to delete expertise" }, statusCode: 500);
    }
});

// ──────────────────────────────────────
// Projects API
// ──────────────────────────────────────

adminGroup.MapGet("/projects", async (HttpContext context, PortfolioDb db) =>
{
    try
    {
        var items = await db.Projects.OrderBy(p => p.SortOrder).ToListAsync();
        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] GET /api/admin/projects: {ex.Message}");
        return Results.Json(new { error = "Failed to load projects" }, statusCode: 500);
    }
});

adminGroup.MapPut("/projects/{id:int}", async (HttpContext context, PortfolioDb db, int id, Project updated) =>
{
    try
    {
        var item = await db.Projects.FindAsync(id);
        if (item == null) return Results.NotFound();
        item.Title = updated.Title;
        item.Category = updated.Category;
        item.ImageUrl = updated.ImageUrl;
        item.ImageAlt = updated.ImageAlt;
        item.ImageBase64 = updated.ImageBase64;
        await db.SaveChangesAsync();
        return Results.Ok(item);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] PUT /api/admin/projects/{id}: {ex.Message}");
        return Results.Json(new { error = "Failed to update project" }, statusCode: 500);
    }
});

adminGroup.MapPost("/projects", async (HttpContext context, PortfolioDb db, Project newItem) =>
{
    try
    {
        var maxOrder = await db.Projects.MaxAsync(e => (int?)e.SortOrder) ?? 0;
        newItem.SortOrder = maxOrder + 1;
        db.Projects.Add(newItem);
        await db.SaveChangesAsync();
        return Results.Ok(newItem);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] POST /api/admin/projects: {ex.Message}");
        return Results.Json(new { error = "Failed to add project" }, statusCode: 500);
    }
});

adminGroup.MapDelete("/projects/{id:int}", async (HttpContext context, PortfolioDb db, int id) =>
{
    try
    {
        var item = await db.Projects.FindAsync(id);
        if (item == null) return Results.NotFound();
        db.Projects.Remove(item);
        await db.SaveChangesAsync();
        return Results.Ok();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] DELETE /api/admin/projects/{id}: {ex.Message}");
        return Results.Json(new { error = "Failed to delete project" }, statusCode: 500);
    }
});


// ──────────────────────────────────────
// Backup API
// ──────────────────────────────────────

adminGroup.MapPost("/backup", async (HttpContext context, PortfolioDb db) =>
{
    try
    {
        var content = await db.SiteContents.FirstOrDefaultAsync();
        var expertise = await db.Expertises.OrderBy(e => e.SortOrder).ToListAsync();
        var projects = await db.Projects.OrderBy(p => p.SortOrder).ToListAsync();

        var backup = new
        {
            createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            siteContent = content,
            expertise,
            projects
        };

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"backup-{timestamp}.json";
        var filePath = Path.Combine(backupDir, fileName);

        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        await File.WriteAllTextAsync(filePath, json);

        return Results.Ok(new { message = "Backup created", fileName });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] POST /api/admin/backup: {ex.Message}");
        return Results.Json(new { error = "Failed to create backup" }, statusCode: 500);
    }
});

adminGroup.MapGet("/backup/list", async (HttpContext context) =>
{
    try
    {
        if (!Directory.Exists(backupDir))
            return Results.Ok(new List<object>());

        var files = Directory.GetFiles(backupDir, "backup-*.json")
            .Select(f => new
            {
                name = Path.GetFileName(f),
                size = new FileInfo(f).Length,
                createdAt = new FileInfo(f).LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss")
            })
            .OrderByDescending(f => f.createdAt)
            .ToList();

        return Results.Ok(files);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] GET /api/admin/backup/list: {ex.Message}");
        return Results.Json(new { error = "Failed to list backups" }, statusCode: 500);
    }
});

adminGroup.MapGet("/backup/download/{name}", async (HttpContext context, string name) =>
{
    try
    {
        name = Path.GetFileName(name);
        var filePath = Path.Combine(backupDir, name);
        if (!File.Exists(filePath))
            return Results.NotFound();

        var bytes = await File.ReadAllBytesAsync(filePath);
        return Results.File(bytes, "application/json", name);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] GET /api/admin/backup/download/{name}: {ex.Message}");
        return Results.Json(new { error = "Failed to download backup" }, statusCode: 500);
    }
});

adminGroup.MapPost("/backup/restore/{name}", async (HttpContext context, PortfolioDb db, string name) =>
{
    try
    {
        name = Path.GetFileName(name);
        var filePath = Path.Combine(backupDir, name);
        if (!File.Exists(filePath))
            return Results.NotFound();

        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Restore site content
        if (root.TryGetProperty("siteContent", out var sc) && sc.ValueKind == JsonValueKind.Object && sc.TryGetProperty("heroTitle", out _))
        {
            var existing = await db.SiteContents.FirstOrDefaultAsync();
            if (existing != null)
            {
                var restored = JsonSerializer.Deserialize<SiteContent>(sc.GetRawText(), options);
                if (restored != null)
                {
                    existing.HeroTitle = restored.HeroTitle ?? "";
                    existing.HeroSubtitle = restored.HeroSubtitle ?? "";
                    existing.AboutText = restored.AboutText ?? "";
                    existing.AboutImageUrl = restored.AboutImageUrl ?? "";
                    existing.ContactEmail = restored.ContactEmail ?? "";
                    existing.ContactSubtitle = restored.ContactSubtitle ?? "";
                }
            }
        }

        // Restore expertise
        if (root.TryGetProperty("expertise", out var exp) && exp.ValueKind == JsonValueKind.Array)
        {
            var restored = JsonSerializer.Deserialize<List<ExpertiseBackup>>(exp.GetRawText(), options);
            if (restored != null && restored.Count > 0)
            {
                db.Expertises.RemoveRange(db.Expertises);
                int order = 1;
                foreach (var item in restored)
                {
                    db.Expertises.Add(new Expertise
                    {
                        SortOrder = order++,
                        Number = item.Number ?? "",
                        Title = item.Title ?? "",
                        Description = item.Description ?? ""
                    });
                }
            }
        }

        // Restore projects
        if (root.TryGetProperty("projects", out var prj) && prj.ValueKind == JsonValueKind.Array)
        {
            var restored = JsonSerializer.Deserialize<List<ProjectBackup>>(prj.GetRawText(), options);
            if (restored != null && restored.Count > 0)
            {
                db.Projects.RemoveRange(db.Projects);
                int order = 1;
                foreach (var item in restored)
                {
                    db.Projects.Add(new Project
                    {
                        SortOrder = order++,
                        Title = item.Title ?? "",
                        Category = item.Category ?? "",
                        ImageUrl = item.ImageUrl ?? "",
                        ImageAlt = item.ImageAlt ?? "",
                        ImageBase64 = item.ImageBase64 ?? ""
                    });
                }
            }
        }

        await db.SaveChangesAsync();
        return Results.Ok(new { message = "Backup restored successfully" });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] POST /api/admin/backup/restore/{name}: {ex.Message}");
        return Results.Json(new { error = "Failed to restore backup" }, statusCode: 500);
    }
});

// ──────────────────────────────────────
// Public API (for front-end dynamic rendering)
// ──────────────────────────────────────

app.MapGet("/api/content", async (HttpContext context, PortfolioDb db) =>
{
    try
    {
        var content = await db.SiteContents.FirstOrDefaultAsync();
        var expertise = await db.Expertises.OrderBy(e => e.SortOrder).ToListAsync();
        var projects = await db.Projects.OrderBy(p => p.SortOrder).ToListAsync();
        return Results.Ok(new { content, expertise, projects });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[API Error] GET /api/content: {ex.Message}");
        return Results.Json(new { error = "Failed to load site data" }, statusCode: 500);
    }
});

// ──────────────────────────────────────
// Serve the main site
// ──────────────────────────────────────

app.MapFallback(async (HttpContext context) =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync("wwwroot/index.html");
});



app.Run();
// ─── Backup data models for deserialization ───
public class ExpertiseBackup
{
    public int SortOrder { get; set; }
    public string? Number { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
}

public class ProjectBackup
{
    public int SortOrder { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageAlt { get; set; }
    public string? ImageBase64 { get; set; }
}



