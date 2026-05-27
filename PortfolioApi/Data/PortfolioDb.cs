using Microsoft.EntityFrameworkCore;
using PortfolioApi.Models;

namespace PortfolioApi.Data;

public class PortfolioDb : DbContext
{
    public PortfolioDb(DbContextOptions<PortfolioDb> options) : base(options) { }

    public DbSet<SiteContent> SiteContents => Set<SiteContent>();
    public DbSet<Expertise> Expertises => Set<Expertise>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SiteContent>().HasData(new SiteContent
        {
            Id = 1,
            HeroTitle = "Z.S — Digital Craftsman & Aesthete",
            HeroSubtitle = "Where minimalism meets meaning.",
            AboutText = @"I don't just design — I craft identities that breathe. Every pixel, every word, every silence between sections is intentional.",
            AboutImageUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=600&h=800&fit=crop&crop=face&q=85&auto=format",
            ContactEmail = "zimo@example.com",
            ContactSubtitle = "Get in Touch"
        });

        modelBuilder.Entity<Expertise>().HasData(
            new Expertise { Id = 1, SortOrder = 1, Number = "01", Title = "Brand Identity", Description = "Visual systems, typography, and tone that make brands unmistakable" },
            new Expertise { Id = 2, SortOrder = 2, Number = "02", Title = "Digital Design", Description = "Web & mobile interfaces that feel like second nature" },
            new Expertise { Id = 3, SortOrder = 3, Number = "03", Title = "Art Direction", Description = "Cohesive visual narratives across every touchpoint" },
            new Expertise { Id = 4, SortOrder = 4, Number = "04", Title = "Creative Strategy", Description = "Framing the right problems before solving them beautifully" }
        );

        modelBuilder.Entity<Project>().HasData(
            new Project { Id = 1, SortOrder = 1, Title = "Aether", Category = "Brand & Web", ImageUrl = "", ImageAlt = "Aether", ImageBase64 = "PHN2ZyB4bWxucz0naHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmcnIHZpZXdCb3g9JzAgMCA4MDAgNDUwJz4gPGRlZnM+IDxsaW5lYXJHcmFkaWVudCBpZD0nZzEnIHgxPScwJScgeTE9JzAlJyB4Mj0nMTAwJScgeTI9JzEwMCUnPiA8c3RvcCBvZmZzZXQ9JzAlJyBzdHlsZT0nc3RvcC1jb2xvcjojMWExYTJlO3N0b3Atb3BhY2l0eToxJy8+IDxzdG9wIG9mZnNldD0nNTAlJyBzdHlsZT0nc3RvcC1jb2xvcjojMTYyMTNlO3N0b3Atb3BhY2l0eToxJy8+IDxzdG9wIG9mZnNldD0nMTAwJScgc3R5bGU9J3N0b3AtY29sb3I6IzBmMzQ2MDtzdG9wLW9wYWNpdHk6MScvPiA8L2xpbmVhckdyYWRpZW50PiA8L2RlZnM+IDxyZWN0IHdpZHRoPSc4MDAnIGhlaWdodD0nNDUwJyBmaWxsPSd1cmwoI2cxKScvPiA8dGV4dCB4PSc0MDAnIHk9JzIyNScgZm9udC1mYW1pbHk9J0dlb3JnaWEsIHNlcmlmJyBmb250LXNpemU9JzcyJyBmaWxsPScjZDRhZDU3JyB0ZXh0LWFuY2hvcj0nbWlkZGxlJyBkeT0nLjNlbSc+QWV0aGVyPC90ZXh0Pjwvc3ZnPg==" },
            new Project { Id = 2, SortOrder = 2, Title = "Noir Studio", Category = "Identity", ImageUrl = "", ImageAlt = "Noir Studio", ImageBase64 = "PHN2ZyB4bWxucz0naHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmcnIHZpZXdCb3g9JzAgMCA4MDAgNDUwJz4gPHJlY3Qgd2lkdGg9JzgwMCcgaGVpZ2h0PSc0NTAnIGZpbGw9JyMxMTExMTEnIC8+IDxjaXJjbGUgY3g9JzQwMCcgY3k9JzIyNScgcj0nODAnIGZpbGw9J25vbmUnIHN0cm9rZT0nI2Q0YWQ1Nycgc3Ryb2tlLXdpZHRoPScxJyAvPiA8dGV4dCB4PSc0MDAnIHk9JzIzNScgZm9udC1mYW1pbHk9J0dlb3JnaWEsIHNlcmlmJyBmb250LXNpemU9JzQ4JyBmaWxsPScjZDRhZDU3JyB0ZXh0LWFuY2hvcj0nbWlkZGxlJz5Ob2lyPC90ZXh0Pjwvc3ZnPg==" },
            new Project { Id = 3, SortOrder = 3, Title = "Lumina", Category = "Editorial", ImageUrl = "", ImageAlt = "Lumina", ImageBase64 = "PHN2ZyB4bWxucz0naHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmcnIHZpZXdCb3g9JzAgMCA4MDAgNDUwJz4gPHJlY3Qgd2lkdGg9JzgwMCcgaGVpZ2h0PSc0NTAnIGZpbGw9JyMxYTFhMmUnIC8+IDxsaW5lIHgxPScxMDAnIHkyPScyMjUnIHgyPSc3MDAnIHkyPScyMjUnIHN0cm9rZT0nI2Q0YWQ1Nycgc3Ryb2tlLXdpZHRoPScwLjUnIC8+IDx0ZXh0IHg9JzQwMCcgeT0nMjM1JyBmb250LWZhbWlseT0nR2VvcmdpYSwgc2VyaWYnIGZvbnQtc2l6ZT0nNjQnIGZpbGw9JyNkYjlmNjMnIHRleHQtYW5jaG9yPSdtaWRkbGUnPkx1bWluYTwvdGV4dD48L3N2Zz4=" }
        );
    }
}
