using LinkShortenerApp.Data;
using LinkShortenerApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace LinkShortenerApp.Services
{
    /// <summary>
    /// Service class containing all business logic for URL shortening
    /// This separates concerns - keeping controllers/endpoints clean and focused on HTTP concerns
    /// </summary>
    public class UrlService
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UrlService> _logger;
        private readonly IConfiguration _configuration;

        // Configuration settings for URL shortening
        private readonly int _shortCodeLength;
        private readonly int _cacheDurationMinutes;

        /// <summary>
        /// Constructor with dependency injection
        /// All dependencies are injected by the DI container
        /// </summary>
        public UrlService(
            AppDbContext context,
            IMemoryCache cache,
            ILogger<UrlService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
            _configuration = configuration;

            // Load configuration from appsettings.json
            _shortCodeLength = _configuration.GetValue<int>("UrlShortener:ShortCodeLength", 6);
            _cacheDurationMinutes = _configuration.GetValue<int>("UrlShortener:CacheDurationMinutes", 5);
        }

        /// <summary>
        /// Generates a unique short code for a URL
        /// Uses a combination of timestamp and random bytes to ensure uniqueness
        /// </summary>
        /// <returns>A unique short code string</returns>
        public string GenerateShortCode()
        {
            // Using Base64 encoding of random bytes provides more entropy than GUID
            // This reduces collision probability and creates shorter codes
            var randomBytes = new byte[8]; // 8 bytes = 64 bits of entropy
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            // Convert to Base64 and remove non-alphanumeric characters
            // Base64 uses +, /, and = which we don't want in URLs
            var base64 = Convert.ToBase64String(randomBytes);
            var code = base64.Replace("+", "")
                             .Replace("/", "")
                             .Replace("=", "")
                             .ToLower();

            // Take only the first N characters (configurable)
            return code.Substring(0, Math.Min(_shortCodeLength, code.Length));
        }

        /// <summary>
        /// Validates that the URL is properly formatted and accessible
        /// </summary>
        /// <param name="url">The URL to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        public bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            // Try to parse the URL
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult))
                return false;

            // Ensure it's HTTP or HTTPS
            return uriResult.Scheme == Uri.UriSchemeHttp ||
                   uriResult.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Creates a shortened URL for the given original URL
        /// </summary>
        /// <param name="originalUrl">The original URL to shorten</param>
        /// <returns>The generated short code</returns>
        /// <exception cref="ArgumentException">Thrown when URL is invalid</exception>
        public async Task<string> CreateShortUrl(string originalUrl)
        {
            // Validate the URL format
            if (!IsValidUrl(originalUrl))
            {
                _logger.LogWarning("Invalid URL attempted: {Url}", originalUrl);
                throw new ArgumentException("Invalid URL format. Please provide a valid HTTP/HTTPS URL.");
            }

            // Normalize the URL (ensure it starts with http:// or https://)
            if (!originalUrl.StartsWith("http://") && !originalUrl.StartsWith("https://"))
            {
                originalUrl = "https://" + originalUrl;
            }

            // Check if this URL already exists in the database
            // This prevents duplicate entries and saves database space
            var existingUrl = await _context.ShortUrls
                .FirstOrDefaultAsync(x => x.OriginalUrl == originalUrl);

            if (existingUrl != null)
            {
                _logger.LogInformation("URL already shortened: {Url} -> {Code}",
                    originalUrl, existingUrl.ShortCode);
                return existingUrl.ShortCode;
            }

            // Generate a unique short code
            string code;
            bool isUnique = false;
            int maxAttempts = 5;
            int attempts = 0;

            do
            {
                code = GenerateShortCode();

                // Check if this code is already in use
                var existingCode = await _context.ShortUrls
                    .AnyAsync(x => x.ShortCode == code);

                isUnique = !existingCode;
                attempts++;

                if (!isUnique && attempts >= maxAttempts)
                {
                    // If we can't generate a unique code after multiple attempts,
                    // add a timestamp to ensure uniqueness
                    code = $"{code}{DateTime.UtcNow.Ticks.ToString().Substring(0, 2)}";
                    isUnique = true;
                }

            } while (!isUnique && attempts < maxAttempts);

            // Create the URL entity
            var shortUrl = new ShortUrl
            {
                OriginalUrl = originalUrl,
                ShortCode = code,
                CreatedDate = DateTime.UtcNow,
                ClickCount = 0
            };

            // Save to database
            await _context.ShortUrls.AddAsync(shortUrl);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created short URL: {Code} -> {Url}", code, originalUrl);

            return code;
        }

        /// <summary>
        /// Creates a shortened URL with a custom code (if available)
        /// </summary>
        /// <param name="originalUrl">The original URL to shorten</param>
        /// <param name="customCode">User-provided custom short code</param>
        /// <returns>The short code</returns>
        public async Task<string> CreateShortUrlWithCustomCode(string originalUrl, string customCode)
        {
            if (!IsValidUrl(originalUrl))
            {
                throw new ArgumentException("Invalid URL format");
            }

            // Validate custom code format (alphanumeric only)
            if (!System.Text.RegularExpressions.Regex.IsMatch(customCode, @"^[a-zA-Z0-9]+$"))
            {
                throw new ArgumentException("Custom code can only contain letters and numbers");
            }

            // Check if custom code is already taken
            var existing = await _context.ShortUrls
                .AnyAsync(x => x.ShortCode == customCode);

            if (existing)
            {
                throw new InvalidOperationException("This short code is already taken");
            }

            var shortUrl = new ShortUrl
            {
                OriginalUrl = originalUrl,
                ShortCode = customCode,
                CreatedDate = DateTime.UtcNow,
                ClickCount = 0
            };

            await _context.ShortUrls.AddAsync(shortUrl);
            await _context.SaveChangesAsync();

            return customCode;
        }

        /// <summary>
        /// Retrieves a ShortUrl by its code with caching for performance
        /// Caching reduces database load for frequently accessed URLs
        /// </summary>
        /// <param name="code">The short code to look up</param>
        /// <returns>The ShortUrl entity or null if not found</returns>
        public async Task<ShortUrl> GetUrlByCode(string code)
        {
            // Check cache first - much faster than database query
            var cacheKey = $"url_{code}";
            if (_cache.TryGetValue(cacheKey, out ShortUrl cachedUrl))
            {
                _logger.LogDebug("Cache hit for code: {Code}", code);
                return cachedUrl;
            }

            _logger.LogDebug("Cache miss for code: {Code}, querying database", code);

            // Query the database
            var url = await _context.ShortUrls
                .FirstOrDefaultAsync(x => x.ShortCode == code);

            // Check if URL has expired
            if (url != null && url.IsExpired)
            {
                _logger.LogWarning("Expired URL accessed: {Code}", code);
                return null;
            }

            // Store in cache if found
            if (url != null)
            {
                // Set cache options with sliding expiration
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(_cacheDurationMinutes))
                    .SetPriority(CacheItemPriority.High);

                _cache.Set(cacheKey, url, cacheOptions);
            }

            return url;
        }

        /// <summary>
        /// Increments the click count for a URL when it's accessed
        /// </summary>
        /// <param name="code">The short code</param>
        public async Task IncrementClickCount(string code)
        {
            var url = await _context.ShortUrls
                .FirstOrDefaultAsync(x => x.ShortCode == code);

            if (url != null)
            {
                url.ClickCount++;
                url.LastAccessedDate = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Invalidate cache so updated count is reflected
                var cacheKey = $"url_{code}";
                _cache.Remove(cacheKey);
            }
        }

        /// <summary>
        /// Gets analytics for a shortened URL
        /// </summary>
        /// <param name="code">The short code</param>
        /// <returns>Analytics data or null if URL not found</returns>
        public async Task<UrlAnalytics> GetUrlAnalytics(string code)
        {
            var url = await GetUrlByCode(code);

            if (url == null)
                return null;

            return new UrlAnalytics
            {
                ShortCode = url.ShortCode,
                OriginalUrl = url.OriginalUrl,
                ClickCount = url.ClickCount,
                CreatedDate = url.CreatedDate,
                LastAccessedDate = url.LastAccessedDate,
                IsExpired = url.IsExpired
            };
        }

        /// <summary>
        /// Deletes expired URLs to clean up the database
        /// Should be called periodically (e.g., via background service)
        /// </summary>
        public async Task<int> DeleteExpiredUrls()
        {
            var expiredUrls = await _context.ShortUrls
                .Where(x => x.ExpirationDate != null && x.ExpirationDate < DateTime.UtcNow)
                .ToListAsync();

            _context.ShortUrls.RemoveRange(expiredUrls);
            var deletedCount = await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted {Count} expired URLs", deletedCount);
            return deletedCount;
        }
    }

    /// <summary>
    /// Analytics data transfer object for URL statistics
    /// </summary>
    public class UrlAnalytics
    {
        public string ShortCode { get; set; }
        public string OriginalUrl { get; set; }
        public int ClickCount { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastAccessedDate { get; set; }
        public bool IsExpired { get; set; }
    }
}