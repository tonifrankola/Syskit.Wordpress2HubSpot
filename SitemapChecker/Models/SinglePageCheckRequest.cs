namespace SitemapCheckerApp.Models;

public class SinglePageCheckRequest
{
    public string PageUrl { get; set; } = string.Empty;
    public DateTime? LastModified { get; set; }
}
