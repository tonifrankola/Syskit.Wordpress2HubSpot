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

    public SitemapController(SitemapCheckerService sitemapChecker)
    {
        _sitemapChecker = sitemapChecker;
    }

    [HttpGet("check")]
    public async Task CheckSitemaps()
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            // Get all sitemaps
            var sitemapUrls = await _sitemapChecker.GetAllSitemapUrlsAsync();
            
            // Get all pages from all sitemaps
            var allPages = new List<(string url, DateTime? lastModified)>();
            foreach (var sitemapUrl in sitemapUrls)
            {
                var pages = await _sitemapChecker.GetPagesFromSitemapAsync(sitemapUrl);
                allPages.AddRange(pages);
            }

            var totalPages = allPages.Count;
            var processedPages = 0;

            // Send initial progress
            await SendProgressUpdate(new ProgressUpdate
            {
                TotalPages = totalPages,
                ProcessedPages = 0,
                IsComplete = false
            });

            // Check each page and stream results
            await foreach (var result in _sitemapChecker.CheckPagesAsync(allPages))
            {
                processedPages++;
                
                await SendProgressUpdate(new ProgressUpdate
                {
                    TotalPages = totalPages,
                    ProcessedPages = processedPages,
                    CurrentResult = result,
                    IsComplete = false
                });

                await Response.Body.FlushAsync();
            }

            // Send completion
            await SendProgressUpdate(new ProgressUpdate
            {
                TotalPages = totalPages,
                ProcessedPages = processedPages,
                IsComplete = true
            });
        }
        catch (Exception ex)
        {
            await Response.WriteAsync($"data: {{\"error\": \"{ex.Message}\"}}\n\n");
        }
    }

    private async Task SendProgressUpdate(ProgressUpdate update)
    {
        var json = JsonSerializer.Serialize(update);
        await Response.WriteAsync($"data: {json}\n\n");
    }
}
