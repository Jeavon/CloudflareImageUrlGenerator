namespace CloudflareImageUrlGenerator
{
    public sealed class CloudflareImageUrlGeneratorOptions
    {
        public const string CloudflareImageUrlGeneratorSection = "CloudflareImageUrlGenerator";

        public bool Enabled { get; set; } = true;
        public string[] CloudFlareSupportedImageFileTypes { get; set; } = new string[] { "webp", "avif" };
        public bool OffloadAllResizing { get; set; } = false;
        public string? AbsoluteOriginPrefix { get; set; }
        public string? AbsoluteCdnPrefix { get; set; }
        public string CloudflarePathPrefix { get; set; } = "/cdn-cgi/image/";
        public bool UseImageSharpFallback { get; set; } = true;
        public bool EnableSignedUrls { get; set; } = false;
        public string? SignedUrlSecret { get; set; }
        public string SignedUrlQueryParameterName { get; set; } = "sig";
        public string SignedUrlExpiryQueryParameterName { get; set; } = "expires";
        public int SignedUrlTtlSeconds { get; set; } = 0;

    }
}
