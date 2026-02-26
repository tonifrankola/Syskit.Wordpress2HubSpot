namespace SitemapCheckerApp.Models;

public class StatusSummary
{
    public int Total { get; set; }
    public int OK { get; set; }
    public int Missing { get; set; }
    public int Redirect301 { get; set; }
    public int Redirect301Chain { get; set; }
}
