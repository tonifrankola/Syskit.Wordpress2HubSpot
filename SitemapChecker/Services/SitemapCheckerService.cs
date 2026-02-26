using System.Xml.Linq;
using HtmlAgilityPack;
using SitemapCheckerApp.Models;

namespace SitemapCheckerApp.Services;

public class SitemapCheckerService
{
    private readonly HttpClient _httpClient;
    private const string OldSiteUrl = "https://www.syskit.com";
    private const string NewSiteUrl = "https://145896435.hs-sites-eu1.com";
    private const string SitemapIndexUrl = "https://www.syskit.com/sitemap_index.xml";
    private const string NewSitemapIndexUrl = "https://145896435.hs-sites-eu1.com/sitemap.xml";

    public SitemapCheckerService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(2); // Increased timeout
    }

    public async Task<List<string>> GetAllSitemapUrlsAsync()
    {
        var sitemapUrls = new List<string>();
        
        try
        {
            var response = await _httpClient.GetStringAsync(SitemapIndexUrl);
            var xml = XDocument.Parse(response);
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            
            foreach (var sitemap in xml.Descendants(ns + "sitemap"))
            {
                var loc = sitemap.Element(ns + "loc")?.Value;
                if (!string.IsNullOrEmpty(loc) && !loc.Contains("local-sitemap.xml"))
                {
                    sitemapUrls.Add(loc);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching sitemap index: {ex.Message}");
        }

        return sitemapUrls;
    }

    public async Task<List<(string url, DateTime? lastModified)>> GetPagesFromSitemapAsync(string sitemapUrl)
    {
        var pages = new List<(string url, DateTime? lastModified)>();
        
        try
        {
            var response = await _httpClient.GetStringAsync(sitemapUrl);
            var xml = XDocument.Parse(response);
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            
            foreach (var url in xml.Descendants(ns + "url"))
            {
                var loc = url.Element(ns + "loc")?.Value;
                if (!string.IsNullOrEmpty(loc))
                {
                    var lastModStr = url.Element(ns + "lastmod")?.Value;
                    DateTime? lastMod = null;
                    if (!string.IsNullOrEmpty(lastModStr) && DateTime.TryParse(lastModStr, out var parsedDate))
                    {
                        lastMod = parsedDate;
                    }
                    pages.Add((loc, lastMod));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching sitemap {sitemapUrl}: {ex.Message}");
        }

        return pages;
    }

    public async IAsyncEnumerable<PageCheckResult> CheckPagesAsync(List<(string url, DateTime? lastModified)> pages)
    {
        foreach (var (pageUrl, lastModified) in pages)
        {
            var result = await CheckPageAsync(pageUrl, lastModified);
            yield return result;
        }
    }

    private async Task<PageCheckResult> CheckPageAsync(string existingPageUrl, DateTime? lastModified)
    {
        var relativePath = existingPageUrl.Replace(OldSiteUrl, "");
        if (string.IsNullOrEmpty(relativePath))
        {
            relativePath = "/";
        }

        var result = new PageCheckResult
        {
            ExistingPage = relativePath,
            ExistingPageUrl = existingPageUrl,
            LastModified = lastModified
        };

        // Get title and og:title from existing page
        var (existingTitle, existingOgTitle) = await GetPageMetadataAsync(existingPageUrl);
        result.ExistingTitle = existingTitle;
        result.ExistingOgTitle = existingOgTitle;

        // Check new page
        var newPageUrl = NewSiteUrl + relativePath;
        var (status, finalUrl, redirectCount) = await CheckUrlAsync(newPageUrl);

        if (status == 404 || status == 0)
        {
            result.Status = PageStatus.Missing;
            result.NewPage = "Missing";
            result.NewPageUrl = string.Empty;
        }
        else if (status == 200)
        {
            if (redirectCount == 0)
            {
                result.Status = PageStatus.OK;
                result.NewPage = relativePath;
                result.NewPageUrl = newPageUrl;
            }
            else if (redirectCount == 1)
            {
                result.Status = PageStatus.Redirect301;
                var finalRelativePath = finalUrl.Replace(NewSiteUrl, "");
                result.NewPage = finalRelativePath;
                result.NewPageUrl = finalUrl;
            }
            else
            {
                result.Status = PageStatus.Redirect301Chain;
                var finalRelativePath = finalUrl.Replace(NewSiteUrl, "");
                result.NewPage = finalRelativePath;
                result.NewPageUrl = finalUrl;
            }

            // Get title and og:title from final page
            var (newTitle, newOgTitle) = await GetPageMetadataAsync(finalUrl);
            result.NewTitle = newTitle;
            result.NewOgTitle = newOgTitle;

            // Compare titles
            result.TitleComparison = string.Equals(result.ExistingTitle, result.NewTitle, StringComparison.OrdinalIgnoreCase) 
                ? "Identical" : "Different";
            result.OgTitleComparison = string.Equals(result.ExistingOgTitle, result.NewOgTitle, StringComparison.OrdinalIgnoreCase) 
                ? "Identical" : "Different";

            // Change status to Different if page exists but titles don't match
            if (result.Status == PageStatus.OK && 
                (result.TitleComparison == "Different" || result.OgTitleComparison == "Different"))
            {
                result.Status = PageStatus.Different;
            }
        }
        else
        {
            result.Status = PageStatus.Missing;
            result.NewPage = "Error";
            result.NewPageUrl = string.Empty;
        }

        return result;
    }

    private async Task<(int statusCode, string finalUrl, int redirectCount)> CheckUrlAsync(string url)
    {
        var redirectCount = 0;
        var currentUrl = url;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, currentUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            while ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400 && redirectCount < 10)
            {
                redirectCount++;
                var location = response.Headers.Location;
                if (location == null) break;

                currentUrl = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(currentUrl), location).ToString();
                
                using var redirectRequest = new HttpRequestMessage(HttpMethod.Head, currentUrl);
                using var redirectResponse = await _httpClient.SendAsync(redirectRequest, HttpCompletionOption.ResponseHeadersRead);
                
                if ((int)redirectResponse.StatusCode < 300 || (int)redirectResponse.StatusCode >= 400)
                {
                    return ((int)redirectResponse.StatusCode, currentUrl, redirectCount);
                }
            }

            return ((int)response.StatusCode, currentUrl, redirectCount);
        }
        catch
        {
            return (0, url, 0);
        }
    }

    private async Task<(string title, string ogTitle)> GetPageMetadataAsync(string url)
    {
        try
        {
            var html = await _httpClient.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? string.Empty;
            var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty) ?? string.Empty;

            return (title, ogTitle);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    // Methods for checking NEW pages (reverse check)
    public async Task<List<string>> GetAllSitemapUrlsFromNewSiteAsync()
    {
        var sitemapUrls = new List<string>();
        
        try
        {
            var response = await _httpClient.GetStringAsync(NewSitemapIndexUrl);
            var xml = XDocument.Parse(response);
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            
            // Check if it's a sitemap index or a single sitemap
            var sitemapElements = xml.Descendants(ns + "sitemap").ToList();
            if (sitemapElements.Any())
            {
                // It's a sitemap index
                foreach (var sitemap in sitemapElements)
                {
                    var loc = sitemap.Element(ns + "loc")?.Value;
                    if (!string.IsNullOrEmpty(loc))
                    {
                        sitemapUrls.Add(loc);
                    }
                }
            }
            else
            {
                // It's a single sitemap
                sitemapUrls.Add(NewSitemapIndexUrl);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching new site sitemap: {ex.Message}");
        }

        return sitemapUrls;
    }

    public async Task<List<(string url, DateTime? lastModified)>> GetPagesFromNewSitemapAsync(string sitemapUrl)
    {
        var pages = new List<(string url, DateTime? lastModified)>();
        
        try
        {
            var response = await _httpClient.GetStringAsync(sitemapUrl);
            var xml = XDocument.Parse(response);
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            
            foreach (var url in xml.Descendants(ns + "url"))
            {
                var loc = url.Element(ns + "loc")?.Value;
                var lastMod = url.Element(ns + "lastmod")?.Value;
                
                DateTime? lastModified = null;
                if (!string.IsNullOrEmpty(lastMod) && DateTime.TryParse(lastMod, out var parsedDate))
                {
                    lastModified = parsedDate;
                }
                
                if (!string.IsNullOrEmpty(loc))
                {
                    pages.Add((loc, lastModified));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching pages from new sitemap {sitemapUrl}: {ex.Message}");
        }

        return pages;
    }

    public async IAsyncEnumerable<PageCheckResult> CheckNewPagesAsync(List<(string url, DateTime? lastModified)> pages)
    {
        foreach (var (newPageUrl, lastModified) in pages)
        {
            var result = await CheckNewPageAsync(newPageUrl, lastModified);
            yield return result;
        }
    }

    private async Task<PageCheckResult> CheckNewPageAsync(string newPageUrl, DateTime? lastModified)
    {
        var relativePath = newPageUrl.Replace(NewSiteUrl, "");
        var oldPageUrl = OldSiteUrl + relativePath;

        var result = new PageCheckResult
        {
            ExistingPage = relativePath,
            ExistingPageUrl = oldPageUrl,
            NewPage = relativePath,
            NewPageUrl = newPageUrl,
            LastModified = lastModified
        };

        // Get metadata from new page
        var (newTitle, newOgTitle) = await GetPageMetadataAsync(newPageUrl);
        result.NewTitle = newTitle;
        result.NewOgTitle = newOgTitle;

        // Check if page exists on old site
        var (statusCode, finalUrl, redirectCount) = await CheckUrlAsync(oldPageUrl);

        if (statusCode == 200)
        {
            // Page exists on old site
            result.Status = PageStatus.OK;
            
            // Get metadata from old page
            var (oldTitle, oldOgTitle) = await GetPageMetadataAsync(finalUrl);
            result.ExistingTitle = oldTitle;
            result.ExistingOgTitle = oldOgTitle;

            // Compare titles
            result.TitleComparison = string.Equals(result.ExistingTitle, result.NewTitle, StringComparison.OrdinalIgnoreCase) 
                ? "Identical" : "Different";
            result.OgTitleComparison = string.Equals(result.ExistingOgTitle, result.NewOgTitle, StringComparison.OrdinalIgnoreCase) 
                ? "Identical" : "Different";
        }
        else
        {
            // Page doesn't exist on old site - this is a NEW page!
            result.Status = PageStatus.Missing;
            result.ExistingPage = "NEW";
            result.ExistingPageUrl = string.Empty;
        }

        return result;
    }
}
