using Microsoft.AspNetCore.Mvc;
using SitemapCheckerApp.Models;
using SitemapCheckerApp.Services;
using System.Text.Json;

namespace SitemapCheckerApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SitemapController : ControllerBase
{
    private readonly SitemapCheckerService _sitemapChecker;
    private readonly ICacheService _cacheService;

    public SitemapController(SitemapCheckerService sitemapChecker, ICacheService cacheService)
    {
        _sitemapChecker = sitemapChecker;
        _cacheService = cacheService;
    }

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { message = "API is working", timestamp = DateTime.UtcNow });
    }

    [HttpGet("cached")]
    public async Task<IActionResult> GetCachedResults()
    {
        var cached = await _cacheService.GetCachedResultsAsync();
        if (cached is null)
        {
            return Ok(new { cached = false });
        }

        return Ok(new
        {
            cached = true,
            cachedAt = cached.CachedAt,
            results = cached.Results
        });
    }

    [HttpPost("clear-cache")]
    public async Task<IActionResult> ClearCache()
    {
        await _cacheService.ClearCacheAsync();
        return Ok(new { message = "Cache cleared" });
    }

    [HttpPost("check-single")]
    public async Task<IActionResult> CheckSinglePage([FromBody] SinglePageCheckRequest request)
    {
        try
        {
            // Create a single-item list with the page URL and its last modified date
            var pages = new List<(string url, DateTime? lastModified)>
            {
                (request.PageUrl, request.LastModified)
            };

            // Check the page
            PageCheckResult? result = null;
            await foreach (var pageResult in _sitemapChecker.CheckPagesAsync(pages))
            {
                result = pageResult;
                break;
            }
            
            if (result == null)
            {
                return BadRequest(new { error = "Failed to check page" });
            }

            // Update cache with the new result
            var cached = await _cacheService.GetCachedResultsAsync();
            if (cached is not null)
            {
                // Find and update the existing result
                var existingIndex = cached.Results.FindIndex(r => r.ExistingPageUrl == request.PageUrl);
                if (existingIndex >= 0)
                {
                    cached.Results[existingIndex] = result;
                    await _cacheService.SaveResultsAsync(cached.Results);
                }
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("check-new")]
    public async Task CheckNewPages()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            // Send initial comment to establish connection
            await Response.WriteAsync(": connected\n\n");
            await Response.Body.FlushAsync();
            await Task.Delay(100);

            // Load existing cached results from MAIN cache
            var existingResults = new List<PageCheckResult>();
            var cached = await _cacheService.GetCachedResultsAsync();
            if (cached != null)
            {
                existingResults = cached.Results;
            }

            // Get all sitemaps from ORIGINAL site
            var sitemapUrls = await _sitemapChecker.GetAllSitemapUrlsAsync();
            
            await SendProgressUpdate(new ProgressUpdate
            {
                TotalPages = -1,
                ProcessedPages = 0,
                IsComplete = false
            });

            // Get all pages from original site sitemaps
            var allPages = new List<(string url, DateTime? lastModified)>();
            foreach (var sitemapUrl in sitemapUrls)
            {
                var pages = await _sitemapChecker.GetPagesFromSitemapAsync(sitemapUrl);
                allPages.AddRange(pages);
            }

            // Filter out already checked pages (pages that are in cache)
            var existingUrls = new HashSet<string>(existingResults.Select(r => r.ExistingPageUrl));
            var newPages = allPages.Where(p => !existingUrls.Contains(p.url)).ToList();

            var totalPages = existingResults.Count + newPages.Count;

            // Send updated total
            await SendProgressUpdate(new ProgressUpdate
            {
                TotalPages = totalPages,
                ProcessedPages = existingResults.Count,
                IsComplete = false
            });

            // Start with existing results
            var allResults = new List<PageCheckResult>(existingResults);

            // Only check newly discovered pages from sitemap
            await foreach (var result in _sitemapChecker.CheckPagesAsync(newPages))
            {
                allResults.Add(result);
                
                // Save to MAIN cache immediately after each page
                await _cacheService.SaveResultsAsync(allResults);
                
                await SendProgressUpdate(new ProgressUpdate
                {
                    TotalPages = totalPages,
                    ProcessedPages = allResults.Count,
                    CurrentResult = result,
                    IsComplete = false
                });
                await Response.Body.FlushAsync();
            }

            // Send completion
            await SendProgressUpdate(new ProgressUpdate
            {
                TotalPages = totalPages,
                ProcessedPages = allResults.Count,
                IsComplete = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR in check-new: {ex.Message}");
            await Response.WriteAsync($"data: {{\"error\": \"{ex.Message.Replace("\"", "\\\"")}\"}}\n\n");
            await Response.Body.FlushAsync();
        }
    }

    [HttpGet("check")]
    public async Task CheckSitemaps([FromQuery] bool useCache = true)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            // Send initial comment to establish connection
            await Response.WriteAsync(": connected\n\n");
            await Response.Body.FlushAsync();
            await Task.Delay(100);
            
            // Try to load from cache first
            if (useCache)
            {
                var cached = await _cacheService.GetCachedResultsAsync();
                if (cached is not null)
                {
                    // Send cached results
                    int totalCached = cached.Results.Count;
                    int processedCached = 0;

                    await SendProgressUpdate(new ProgressUpdate
                    {
                        TotalPages = totalCached,
                        ProcessedPages = 0,
                        IsComplete = false
                    });

                    foreach (var result in cached.Results)
                    {
                        processedCached++;
                        await SendProgressUpdate(new ProgressUpdate
                        {
                            TotalPages = totalCached,
                            ProcessedPages = processedCached,
                            CurrentResult = result,
                            IsComplete = false
                        });
                        await Response.Body.FlushAsync();
                        await Task.Delay(5);
                    }

                    await SendProgressUpdate(new ProgressUpdate
                    {
                        TotalPages = totalCached,
                        ProcessedPages = processedCached,
                        IsComplete = true
                    });
                    
                    return;
                }
            }

            // Fetch fresh data
            var allResults = new List<PageCheckResult>();

            await SendProgressUpdate(new ProgressUpdate
            {
                TotalPages = -1,
                ProcessedPages = 0,
                IsComplete = false
            });

            // STEP 1: Check root page first
            var rootPage = new List<(string url, DateTime? lastModified)>
            {
                ("https://www.syskit.com/", null)
            };

            await foreach (var result in _sitemapChecker.CheckPagesAsync(rootPage))
            {
                allResults.Add(result);
                await _cacheService.SaveResultsAsync(allResults);
                
                await SendProgressUpdate(new ProgressUpdate
                {
                    TotalPages = -1,
                    ProcessedPages = 1,
                    CurrentResult = result,
                    IsComplete = false
                });
                await Response.Body.FlushAsync();
            }

            // STEP 2: Get all sitemaps and pages
            var sitemapUrls = await _sitemapChecker.GetAllSitemapUrlsAsync();

            var allPages = new List<(string url, DateTime? lastModified)>();
            foreach (var sitemapUrl in sitemapUrls)
            {
                var pages = await _sitemapChecker.GetPagesFromSitemapAsync(sitemapUrl);
                allPages.AddRange(pages);
            }

            // Remove duplicates and already processed root
            var processedUrls = new HashSet<string>(allResults.Select(r => r.ExistingPageUrl));
            allPages = allPages.Where(p => !processedUrls.Contains(p.url)).ToList();

            var totalPages = allResults.Count + allPages.Count;

            // Send updated total
            await SendProgressUpdate(new ProgressUpdate
            {
                TotalPages = totalPages,
                ProcessedPages = allResults.Count,
                IsComplete = false
            });

            // STEP 3: Process remaining pages
            await foreach (var result in _sitemapChecker.CheckPagesAsync(allPages))
            {
                allResults.Add(result);
                await _cacheService.SaveResultsAsync(allResults);
                
                await SendProgressUpdate(new ProgressUpdate
                {
                    TotalPages = totalPages,
                    ProcessedPages = allResults.Count,
                    CurrentResult = result,
                    IsComplete = false
                });
                await Response.Body.FlushAsync();
            }

            // Save final cache
            await _cacheService.SaveResultsAsync(allResults);

            // Send completion
            await SendProgressUpdate(new ProgressUpdate
            {
                TotalPages = totalPages,
                ProcessedPages = allResults.Count,
                IsComplete = true
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR in check: {ex.Message}");
            await Response.WriteAsync($"data: {{\"error\": \"{ex.Message.Replace("\"", "\\\"")}\"}}\n\n");
            await Response.Body.FlushAsync();
        }
    }

    private async Task SendProgressUpdate(ProgressUpdate update)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(update, options);
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }
}

