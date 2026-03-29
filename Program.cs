using LinkShortenerApp.Components;
using LinkShortenerApp.Data;
using LinkShortenerApp.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register HttpClient for API calls
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5139") });

// Register Entity Framework Core with SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Memory Cache
builder.Services.AddMemoryCache();

// Register UrlService
builder.Services.AddScoped<UrlService>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// URL redirection endpoint
app.MapGet("/s/{code}", async (string code, AppDbContext db, UrlService urlService) =>
{
    var url = await urlService.GetUrlByCode(code);

    if (url == null)
        return Results.NotFound("Invalid short URL");

    // Increment click count
    _ = urlService.IncrementClickCount(code);

    return Results.Redirect(url.OriginalUrl);
});

// API endpoint for creating short URLs - update the base URL part
app.MapPost("/api/shorten", async (ShortenRequest request, UrlService urlService, IConfiguration config) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.OriginalUrl))
        {
            return Results.BadRequest(new { error = "URL is required" });
        }

        string shortCode;

        if (!string.IsNullOrWhiteSpace(request.CustomCode))
        {
            shortCode = await urlService.CreateShortUrlWithCustomCode(request.OriginalUrl, request.CustomCode);
        }
        else
        {
            shortCode = await urlService.CreateShortUrl(request.OriginalUrl);
        }

        // Use the correct base URL from configuration
        var baseUrl = config["UrlShortener:BaseUrl"] ?? "https://localhost:7205";
        var shortUrl = $"{baseUrl}/s/{shortCode}";

        return Results.Ok(new
        {
            shortUrl = shortUrl,
            code = shortCode,
            originalUrl = request.OriginalUrl
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.Run();

public class ShortenRequest
{
    public string OriginalUrl { get; set; }
    public string? CustomCode { get; set; }
}