namespace SitemapCheckerApp.Models;

public class ProgressUpdate
{
    public int TotalPages { get; set; }
    public int ProcessedPages { get; set; }
    public PageCheckResult? CurrentResult { get; set; }
    public bool IsComplete { get; set; }
}
