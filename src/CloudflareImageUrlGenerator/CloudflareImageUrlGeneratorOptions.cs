namespace CloudflareImageUrlGenerator
{
    public sealed class CloudflareImageUrlGeneratorOptions
    {
        public const string CloudflareImageUrlGeneratorSection = "CloudflareImageUrlGenerator";

        public bool Enabled { get; set; } = true;
        public string[] CloudFlareSupportedImageFileTypes { get; set; } = new string[] { "webp", "avif" };

    }
}
