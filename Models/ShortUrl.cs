using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LinkShortenerApp.Models
{
    /// <summary>
    /// Represents a shortened URL in the system
    /// This model maps to the ShortUrls table in the database
    /// </summary>
    public class ShortUrl
    {
        /// <summary>
        /// Primary key - unique identifier for each shortened URL
        /// [Key] attribute indicates this is the primary key
        /// Database will auto-generate this value
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// The original long URL that was shortened
        /// [Required] ensures this field must have a value
        /// [Url] validates that the value is a valid URL format
        /// [MaxLength] limits the size to prevent database bloat
        /// </summary>
        [Required(ErrorMessage = "Original URL is required")]
        [Url(ErrorMessage = "Please provide a valid URL")]
        [MaxLength(2048, ErrorMessage = "URL cannot exceed 2048 characters")]
        public string OriginalUrl { get; set; }

        /// <summary>
        /// The unique short code used in the shortened URL
        /// Example: "abc123" would make the short URL: https://domain.com/s/abc123
        /// This must be unique across all records
        /// </summary>
        [Required]
        [MaxLength(10)]
        [RegularExpression(@"^[a-zA-Z0-9]+$", ErrorMessage = "Short code can only contain letters and numbers")]
        public string ShortCode { get; set; }

        /// <summary>
        /// Timestamp when the URL was created
        /// Automatically set to current UTC time when record is created
        /// UTC is used for consistency across different time zones
        /// </summary>
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tracks how many times this short URL has been accessed
        /// Useful for analytics and monitoring popularity
        /// Incremented each time the /s/{code} endpoint is called
        /// </summary>
        public int ClickCount { get; set; } = 0;

        /// <summary>
        /// Optional field to track when the URL was last accessed
        /// Helps with analytics and identifying unused URLs for cleanup
        /// </summary>
        public DateTime? LastAccessedDate { get; set; }

        /// <summary>
        /// Optional expiration date for temporary URLs
        /// If set, URLs older than this date will no longer redirect
        /// Useful for temporary sharing links
        /// </summary>
        public DateTime? ExpirationDate { get; set; }

        /// <summary>
        /// Check if the URL has expired
        /// Returns true if expiration date is set and has passed
        /// </summary>
        [NotMapped] // This property won't be stored in the database
        public bool IsExpired => ExpirationDate.HasValue && ExpirationDate.Value < DateTime.UtcNow;
    }
}