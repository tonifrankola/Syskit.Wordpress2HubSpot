namespace SitemapCheckerApp.Models;

public class PageCheckResult
{
    public string ExistingPage { get; set; } = string.Empty;
    public string ExistingPageUrl { get; set; } = string.Empty;
    public string NewPage { get; set; } = string.Empty;
    public string NewPageUrl { get; set; } = string.Empty;
    public PageStatus Status { get; set; }
    public string ExistingTitle { get; set; } = string.Empty;
    public string NewTitle { get; set; } = string.Empty;
    public string ExistingOgTitle { get; set; } = string.Empty;
    public string NewOgTitle { get; set; } = string.Empty;
    public string TitleComparison { get; set; } = string.Empty;
    public string OgTitleComparison { get; set; } = string.Empty;
    public DateTime? LastModified { get; set; }
    public string LastModifiedFormatted => LastModified?.ToString("yyyy-MM-dd") ?? string.Empty;
}

public enum PageStatus
{
    OK,
    Missing,
    Redirect301,
    Redirect301Chain
}
