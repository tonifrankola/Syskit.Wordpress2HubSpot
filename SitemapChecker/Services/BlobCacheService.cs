using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using SitemapCheckerApp.Models;

namespace SitemapCheckerApp.Services;

public class BlobCacheService : ICacheService
{
    private readonly BlobContainerClient _containerClient;
    private readonly string _cacheFileName = "sitemap-results.json";
    private readonly string _newPagesCacheFileName = "new-pages-results.json";
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public BlobCacheService(IConfiguration configuration)
    {
        var connectionString = configuration["AzureBlobStorage:ConnectionString"];
        var containerName = configuration["AzureBlobStorage:ContainerName"] ?? "sitemap-cache";

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "Azure Blob Storage connection string is not configured. " +
                "Please set AzureBlobStorage:ConnectionString in your configuration.");
        }

        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        
        // Create container if it doesn't exist
        _containerClient.CreateIfNotExists();
    }

    public async Task<CachedResults?> GetCachedResultsAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var blobClient = _containerClient.GetBlobClient(_cacheFileName);
            
            if (!await blobClient.ExistsAsync())
                return null;

            var response = await blobClient.DownloadContentAsync();
            var json = response.Value.Content.ToString();
            return JsonSerializer.Deserialize<CachedResults>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading cache from blob storage: {ex.Message}");
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
            var blobClient = _containerClient.GetBlobClient(_newPagesCacheFileName);
            
            if (!await blobClient.ExistsAsync())
                return null;

            var response = await blobClient.DownloadContentAsync();
            var json = response.Value.Content.ToString();
            return JsonSerializer.Deserialize<CachedResults>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading new pages cache from blob storage: {ex.Message}");
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
            var blobClient = _containerClient.GetBlobClient(_cacheFileName);
            
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blobClient.UploadAsync(stream, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving cache to blob storage: {ex.Message}");
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
            var blobClient = _containerClient.GetBlobClient(_newPagesCacheFileName);
            
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await blobClient.UploadAsync(stream, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving new pages cache to blob storage: {ex.Message}");
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
            var cacheBlob = _containerClient.GetBlobClient(_cacheFileName);
            var newPagesBlob = _containerClient.GetBlobClient(_newPagesCacheFileName);
            
            await cacheBlob.DeleteIfExistsAsync();
            await newPagesBlob.DeleteIfExistsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing cache from blob storage: {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
