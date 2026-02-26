using System.Text.Json;
using SitemapCheckerApp.Models;

namespace SitemapCheckerApp.Services;

public class FileCacheService : ICacheService
{
    private readonly string _cacheFilePath;
    private readonly string _newPagesCacheFilePath;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileCacheService(IWebHostEnvironment env)
    {
        var cacheDir = Path.Combine(env.ContentRootPath, "cache");
        Directory.CreateDirectory(cacheDir);
        _cacheFilePath = Path.Combine(cacheDir, "sitemap-results.json");
        _newPagesCacheFilePath = Path.Combine(cacheDir, "new-pages-results.json");
    }

    public async Task<CachedResults?> GetCachedResultsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(_cacheFilePath))
                return null;

            var json = await File.ReadAllTextAsync(_cacheFilePath);
            return JsonSerializer.Deserialize<CachedResults>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading cache: {ex.Message}");
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<CachedResults?> GetNewPagesCachedResultsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(_newPagesCacheFilePath))
                return null;

            var json = await File.ReadAllTextAsync(_newPagesCacheFilePath);
            return JsonSerializer.Deserialize<CachedResults>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading new pages cache: {ex.Message}");
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveResultsAsync(List<PageCheckResult> results)
    {
        await _semaphore.WaitAsync();
        try
        {
            var cache = new CachedResults
            {
                CachedAt = DateTime.UtcNow,
                Results = results
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(cache, options);
            await File.WriteAllTextAsync(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving cache: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ClearCacheAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (File.Exists(_cacheFilePath))
                File.Delete(_cacheFilePath);
            if (File.Exists(_newPagesCacheFilePath))
                File.Delete(_newPagesCacheFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing cache: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveNewPagesResultsAsync(List<PageCheckResult> results)
    {
        await _semaphore.WaitAsync();
        try
        {
            var cache = new CachedResults
            {
                CachedAt = DateTime.UtcNow,
                Results = results
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(cache, options);
            await File.WriteAllTextAsync(_newPagesCacheFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving new pages cache: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
