using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PortfolioApi.Data;
using PortfolioApi.Models;



var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PortfolioDb>(options =>
    options.UseSqlite("Data Source=portfolio.db"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
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

// ──────────────────────────────────────
// Auth endpoints
// ──────────────────────────────────────

app.MapGet("/admin/login", async (HttpContext context) =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync("wwwroot/admin/login.html");
});

app.MapPost("/api/auth/login", async (HttpContext context, PortfolioDb db) =>
{
    try
    {
        var form = await context.Request.ReadFormAsync();
        var username = form["username"].ToString();
        var password = form["password"].ToString();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return Results.Redirect("/admin/login?error=3");

        var admin = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (admin == null)
        {
            // Create first admin if none exists
            if (!await db.AdminUsers.AnyAsync())
            {
                db.AdminUsers.Add(new AdminUser
                {
                    Username = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123")
                });
                await db.SaveChangesAsync();
                admin = await db.AdminUsers.FirstAsync(u => u.Username == "admin");
            }
            else
            {
                return Results.Redirect("/admin/login?error=1");
            }
        }

        if (!BCrypt.Net.BCrypt.Verify(password, admin.PasswordHash))
        {
            return Results.Redirect("/admin/login?error=2");
        }

        var claims = new[] { new System.Security.Claims.Claim("username", username) };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(new System.Security.Claims.ClaimsPrincipal(identity));
        return Results.Redirect("/admin");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Login Error] {ex.Message}");
        return Results.Redirect("/admin/login?error=4");
    }
});

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/admin/login");
});

// ──────────────────────────────────────
// Admin dashboard
// ──────────────────────────────────────

app.MapGet("/admin", async (HttpContext context) =>
{
    if (!context.User.Identity?.IsAuthenticated ?? true)
    {
        context.Response.Redirect("/admin/login");
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


