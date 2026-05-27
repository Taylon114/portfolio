using System.ComponentModel.DataAnnotations;

namespace PortfolioApi.Models;

public class SiteContent
{
    [Key]
    public int Id { get; set; }
    public string HeroTitle { get; set; } = "";
    public string HeroSubtitle { get; set; } = "";
    public string AboutText { get; set; } = "";
    public string AboutImageUrl { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string ContactSubtitle { get; set; } = "";
}

public class Expertise
{
    [Key]
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

public class Project
{
    [Key]
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string ImageAlt { get; set; } = "";
    public string ImageBase64 { get; set; } = "";
}

public class AdminUser
{
    [Key]
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
