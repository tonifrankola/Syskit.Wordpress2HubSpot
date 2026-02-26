using SitemapCheckerApp.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for longer timeouts and HTTP/1.1 for SSE
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
    
    // Enable HTTP/1.1 to fix SSE issues
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

// Add services
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped<SitemapCheckerService>();

// Register cache service based on configuration
var useAzureBlob = builder.Configuration.GetValue<bool>("CacheService:UseAzureBlob");
if (useAzureBlob)
{
    builder.Services.AddSingleton<ICacheService, BlobCacheService>();
}
else
{
    builder.Services.AddSingleton<ICacheService, FileCacheService>();
}

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure middleware
app.UseCors();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();

// Serve index.html as default
app.MapFallback(() => Results.Redirect("/index.html"));

app.Run();
