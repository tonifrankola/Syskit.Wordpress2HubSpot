using SitemapCheckerApp.Models;

namespace SitemapCheckerApp.Services;

public interface ICacheService
{
    Task<CachedResults?> GetCachedResultsAsync();
    Task<CachedResults?> GetNewPagesCachedResultsAsync();
    Task SaveResultsAsync(List<PageCheckResult> results);
    Task SaveNewPagesResultsAsync(List<PageCheckResult> results);
    Task ClearCacheAsync();
}
