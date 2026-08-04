using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp.Web;
using SixLabors.ImageSharp.Web.Middleware;
using SixLabors.ImageSharp.Web.Processors;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Imaging.ImageSharp.ImageProcessors;
using Umbraco.Cms.Imaging.ImageSharp.Media;

namespace CloudflareImageUrlGenerator
{
    public sealed class HybridCloudflareImageSharpImageUrlGenerator : IImageUrlGenerator
    {
        public IEnumerable<string> SupportedImageFileTypes { get; }
        private SixLabors.ImageSharp.Configuration _configuration { get; }
        private RequestAuthorizationUtilities _requestAuthorizationUtilities { get; }
        private IOptions<ImageSharpMiddlewareOptions> _imageSharpMiddlewareOptions { get; }
        private readonly CloudflareImageUrlGeneratorOptions _cloudflareImageUrlGeneratorOptions;
        private readonly ImagingSettings _imagingSettings;
        const int MaxSourcePixels = 100000000;
        public HybridCloudflareImageSharpImageUrlGenerator(SixLabors.ImageSharp.Configuration configuration, IOptionsMonitor<CloudflareImageUrlGeneratorOptions> cloudflareImageUrlGeneratorOptions, RequestAuthorizationUtilities requestAuthorizationUtilities, IOptions<ImageSharpMiddlewareOptions> imageSharpMiddlewareOptions, IOptions<ImagingSettings> imagingSettings)
        {

            SupportedImageFileTypes = configuration.ImageFormats.SelectMany(f => f.FileExtensions).ToArray();
            _configuration = configuration;
            _requestAuthorizationUtilities = requestAuthorizationUtilities;
            _imageSharpMiddlewareOptions = imageSharpMiddlewareOptions;
            _cloudflareImageUrlGeneratorOptions = cloudflareImageUrlGeneratorOptions.CurrentValue;
            _imagingSettings = imagingSettings.Value;
        }

        public string? GetImageUrl(ImageUrlGenerationOptions? options)
        {
            if (options?.ImageUrl == null)
            {
                return null;
            }

            var cfCommands = new Dictionary<string, string?>();
            Uri fakeBaseUri = new Uri("https://localhost/");
            var imageSharpString = new ImageSharpImageUrlGenerator(_configuration, _requestAuthorizationUtilities, _imageSharpMiddlewareOptions).GetImageUrl(options);

            Dictionary<string, StringValues> imageSharpCommands = QueryHelpers.ParseQuery(new Uri(fakeBaseUri, imageSharpString).Query);

            // Strip the HMAC token - it was computed for the original params and will be invalid
            // after we move commands to Cloudflare. We recompute it at each return point.
            imageSharpCommands.Remove(RequestAuthorizationUtilities.TokenCommand);

            int? sourceWidth = null;
            int? sourceHeight = null;
            if (imageSharpCommands.Remove("sourceWidth", out StringValues sourceWidthValue))
            {
                sourceWidth = Convert.ToInt32(sourceWidthValue);
            }
            if (imageSharpCommands.Remove("sourceHeight", out StringValues sourceHeightValue))
            {
                sourceHeight = Convert.ToInt32(sourceHeightValue);
            }

            // Cloudflare handles EXIF auto orient by default
            imageSharpCommands.Remove(AutoOrientWebProcessor.AutoOrient);

            var resizeSourceAction = ResizeSourceAction.None;
            int? sourceResize = null;
            if (sourceWidth != null && sourceHeight != null)
            {
                // check if source image is over 100 mega pixels
                if (sourceWidth * sourceHeight > MaxSourcePixels)
                {
                    if (sourceWidth > sourceHeight)
                    {
                        resizeSourceAction = ResizeSourceAction.Width;

                        var ratio = (decimal)_imagingSettings.Resize.MaxWidth / (decimal)sourceWidth;
                        var calculatedHeight = (int)Math.Round((decimal)sourceHeight * ratio, 0);

                        if (_imagingSettings.Resize.MaxWidth * calculatedHeight > MaxSourcePixels)
                        {
                            // output image is still going to be over 100 mega pixels, let's play it safe and set size to 3k
                            sourceResize = 3000;
                        }
                        else
                        {
                            sourceResize = _imagingSettings.Resize.MaxWidth;
                        }
                    }
                    else
                    {
                        resizeSourceAction = ResizeSourceAction.Height;

                        var ratio = (decimal)_imagingSettings.Resize.MaxHeight / (decimal)sourceHeight;
                        var calculatedWidth = (int)Math.Round((decimal)sourceWidth * ratio, 0);

                        if (_imagingSettings.Resize.MaxHeight * calculatedWidth > MaxSourcePixels)
                        {
                            // output image is still going to be over 100 mega pixels, let's play it safe and set size to 3k
                            sourceResize = 3000;
                        }
                        else
                        {
                            sourceResize = _imagingSettings.Resize.MaxHeight;
                        }
                    }
                }

                if (options.Crop is not null)
                {
                    ImageUrlGenerationOptions.CropCoordinates? crop = options.Crop;

                    if (imageSharpCommands.Remove(CropWebProcessor.Coordinates))
                    {
                        var top = Math.Round((decimal)(crop.Top * sourceHeight));
                        var left = Math.Round((decimal)(crop.Left * sourceWidth));
                        var bottom = Math.Round((decimal)(crop.Bottom * sourceHeight));
                        var right = Math.Round((decimal)(crop.Right * sourceWidth));
                        cfCommands.Add(CloudflareCommands.Trim, $"{top};{right};{bottom};{left}");

                    }
                }
            }

            // remove format from ImageSharp and add it to Cloudflare, additionally set ImageSharp quality to 100 (as source) and add quality parameter to Cloudflare
            if (imageSharpCommands.ContainsKey(FormatWebProcessor.Format) || _cloudflareImageUrlGeneratorOptions.OffloadAllResizing)
            {
                string? cfFormat = null;
                if (imageSharpCommands.ContainsKey(FormatWebProcessor.Format))
                {
                    var format = imageSharpCommands[FormatWebProcessor.Format];
                    if (_cloudflareImageUrlGeneratorOptions.CloudFlareSupportedImageFileTypes.Contains(format[0]))
                    {
                        cfFormat = format[0];
                        imageSharpCommands.Remove(FormatWebProcessor.Format);
                    }
                    else if (!_cloudflareImageUrlGeneratorOptions.OffloadAllResizing)
                    {
                        // Format not in CF list and OffloadAllResizing disabled — stay with ImageSharp
                        if (_cloudflareImageUrlGeneratorOptions.UseImageSharpFallback)
                        {
                            AddHmacIfEnabled(options.ImageUrl, imageSharpCommands);
                            return QueryHelpers.AddQueryString(options.ImageUrl, imageSharpCommands);
                        }

                        return options.ImageUrl;
                    }
                    // OffloadAllResizing=true: format stays in imageSharpCommands for the ImageSharp source URL
                }

                var addFit = false;
                string? fitOverride = null;

                if (options.ImageCropMode is null or ImageCropMode.Crop)
                {
                    // Remove mode and anchor — Cloudflare handles crop via cover fit + gravity
                    imageSharpCommands.Remove(ResizeWebProcessor.Mode);
                    imageSharpCommands.Remove(ResizeWebProcessor.Anchor);

                    if (imageSharpCommands.ContainsKey(ResizeWebProcessor.Width))
                    {
                        if (resizeSourceAction == ResizeSourceAction.Width)
                        {
                            var width = imageSharpCommands[ResizeWebProcessor.Width];
                            imageSharpCommands[ResizeWebProcessor.Width] = sourceResize.ToString();
                            cfCommands.Add(CloudflareCommands.Width, width);
                            addFit = true;
                        }
                        else
                        {
                            if (imageSharpCommands.Remove(ResizeWebProcessor.Width, out var width))
                            {
                                cfCommands.Add(CloudflareCommands.Width, width);
                                addFit = true;
                            }
                        }
                    }

                    if (imageSharpCommands.ContainsKey(ResizeWebProcessor.Height))
                    {
                        if (resizeSourceAction == ResizeSourceAction.Height)
                        {
                            var height = imageSharpCommands[ResizeWebProcessor.Height];
                            imageSharpCommands[ResizeWebProcessor.Height] = sourceResize.ToString();
                            cfCommands.Add(CloudflareCommands.Height, height);
                            addFit = true;
                        }
                        else
                        {
                            if (imageSharpCommands.Remove(ResizeWebProcessor.Height, out var height))
                            {
                                var h = Convert.ToInt32(height);
                                if (h > 0)
                                {
                                    cfCommands.Add(CloudflareCommands.Height, h.ToString());
                                    addFit = true;
                                }
                            }
                        }
                    }

                    if (options.FocalPoint is not null)
                    {
                        if (imageSharpCommands.Remove(ResizeWebProcessor.Xy))
                        {
                            cfCommands.Add(CloudflareCommands.Gravity, FormattableString.Invariant($"{options.FocalPoint.Left}x{options.FocalPoint.Top}"));
                            addFit = true;
                        }
                    }
                }
                else if (options.ImageCropMode is ImageCropMode.Max)
                {
                    imageSharpCommands.Remove(ResizeWebProcessor.Mode);
                    imageSharpCommands.Remove(ResizeWebProcessor.Anchor);

                    if (imageSharpCommands.Remove(ResizeWebProcessor.Width, out var width))
                    {
                        cfCommands.Add(CloudflareCommands.Width, width);
                        addFit = true;
                    }

                    if (imageSharpCommands.Remove(ResizeWebProcessor.Height, out var height))
                    {
                        var h = Convert.ToInt32(height);
                        if (h > 0)
                        {
                            cfCommands.Add(CloudflareCommands.Height, h.ToString());
                            addFit = true;
                        }
                    }

                    fitOverride = CloudflareCommands.ScaleDown;
                }
                else if (options.ImageCropMode is ImageCropMode.Min)
                {
                    imageSharpCommands.Remove(ResizeWebProcessor.Mode);
                    imageSharpCommands.Remove(ResizeWebProcessor.Anchor);

                    if (imageSharpCommands.Remove(ResizeWebProcessor.Width, out var width))
                    {
                        cfCommands.Add(CloudflareCommands.Width, width);
                        addFit = true;
                    }

                    if (imageSharpCommands.Remove(ResizeWebProcessor.Height, out var height))
                    {
                        var h = Convert.ToInt32(height);
                        if (h > 0)
                        {
                            cfCommands.Add(CloudflareCommands.Height, h.ToString());
                            addFit = true;
                        }
                    }

                    fitOverride = CloudflareCommands.Contain;
                }
                else if (options.ImageCropMode is ImageCropMode.Pad or ImageCropMode.BoxPad)
                {
                    if (imageSharpCommands.ContainsKey(BackgroundColorWebProcessor.Color))
                    {
                        // bgcolor present: keep rmode/width/height/bgcolor in imageSharpCommands so ImageSharp
                        // handles the pad+colour. Format and quality are still offloaded to Cloudflare below.
                    }
                    else
                    {
                        imageSharpCommands.Remove(ResizeWebProcessor.Mode);
                        imageSharpCommands.Remove(ResizeWebProcessor.Anchor);

                        if (imageSharpCommands.Remove(ResizeWebProcessor.Width, out var width))
                        {
                            cfCommands.Add(CloudflareCommands.Width, width);
                            addFit = true;
                        }

                        if (imageSharpCommands.Remove(ResizeWebProcessor.Height, out var height))
                        {
                            var h = Convert.ToInt32(height);
                            if (h > 0)
                            {
                                cfCommands.Add(CloudflareCommands.Height, h.ToString());
                                addFit = true;
                            }
                        }

                        fitOverride = CloudflareCommands.Pad;
                    }
                }
                else if (options.ImageCropMode is ImageCropMode.Stretch)
                {
                    imageSharpCommands.Remove(ResizeWebProcessor.Mode);
                    imageSharpCommands.Remove(ResizeWebProcessor.Anchor);

                    if (imageSharpCommands.Remove(ResizeWebProcessor.Width, out var width))
                    {
                        cfCommands.Add(CloudflareCommands.Width, width);
                    }

                    if (imageSharpCommands.Remove(ResizeWebProcessor.Height, out var height))
                    {
                        var h = Convert.ToInt32(height);
                        if (h > 0)
                        {
                            cfCommands.Add(CloudflareCommands.Height, h.ToString());
                        }
                    }
                    // No fit parameter — Cloudflare stretches when both w and h are set without fit
                }

                if (cfFormat != null)
                {
                    cfCommands.Add(FormatWebProcessor.Format, cfFormat);
                }

                if (imageSharpCommands.Remove(QualityWebProcessor.Quality, out var quality))
                {
                    cfCommands.Add(CloudflareCommands.Quality, quality);
                }

                if (addFit)
                {
                    if (fitOverride is not null)
                    {
                        cfCommands.Add(CloudflareCommands.Fit, fitOverride);
                    }
                    else if (cfCommands.ContainsKey(CloudflareCommands.Width) && cfCommands.ContainsKey(CloudflareCommands.Height))
                    {
                        cfCommands.Add(CloudflareCommands.Fit, CloudflareCommands.Cover);
                    }
                    else
                    {
                        cfCommands.Add(CloudflareCommands.Fit, CloudflareCommands.Contain);
                    }
                }
            }

            string cfCommandString = string.Empty;
            foreach (KeyValuePair<string, string?> command in cfCommands)
            {
                cfCommandString += command.Key + "=" + command.Value;
                if (!command.Equals(cfCommands.Last()))
                {
                    cfCommandString += ",";
                }
            }

            if (cfCommandString == string.Empty)
            {
                return imageSharpString;
            }

            var cloudflarePathPrefix = EnsureTrailingSlash(NormalizePathPrefix(_cloudflareImageUrlGeneratorOptions.CloudflarePathPrefix));

            var cloudflareBasePath = string.IsNullOrWhiteSpace(_cloudflareImageUrlGeneratorOptions.AbsoluteCdnPrefix)
                ? cloudflarePathPrefix
                : JoinUrlSegments(NormalizeUrlPrefix(_cloudflareImageUrlGeneratorOptions.AbsoluteCdnPrefix), cloudflarePathPrefix);

            var sourceUrl = options.ImageUrl;
            var sourceQueryParameters = new Dictionary<string, StringValues>();
            if (_cloudflareImageUrlGeneratorOptions.UseImageSharpFallback)
            {
                sourceQueryParameters = imageSharpCommands;
            }
            else if (imageSharpCommands.TryGetValue("v", out var versionValue))
            {
                sourceQueryParameters["v"] = versionValue;
            }
            if (!string.IsNullOrWhiteSpace(_cloudflareImageUrlGeneratorOptions.AbsoluteOriginPrefix))
            {
                var absoluteOriginPrefix = NormalizeUrlPrefix(_cloudflareImageUrlGeneratorOptions.AbsoluteOriginPrefix);
                if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out _) &&
                    !sourceUrl.StartsWith(absoluteOriginPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    sourceUrl = JoinUrlSegments(absoluteOriginPrefix, sourceUrl.TrimStart('/'));
                }
            }

            var normalizedSourceUrl = sourceUrl;

            var cloudflareUrlSuffix = string.IsNullOrEmpty(cfCommandString)
                ? normalizedSourceUrl
                : JoinUrlSegments(cfCommandString, normalizedSourceUrl);

            if (imageSharpCommands.Count == 0 || !imageSharpCommands.Keys.Any(k => k != "v"))
            {
                // No actual ImageSharp processing needed — pass v (if present) for Cloudflare cache busting
                return BuildCloudflareUrl(cloudflareBasePath + cloudflareUrlSuffix, sourceQueryParameters);
            }

            // ImageSharp processing is needed — set quality=100 to prevent double compression
            if (cfCommands.ContainsKey(CloudflareCommands.Quality))
            {
                imageSharpCommands[QualityWebProcessor.Quality] = "100";
            }

            if (_cloudflareImageUrlGeneratorOptions.UseImageSharpFallback)
            {
                AddHmacIfEnabled(sourceUrl, imageSharpCommands);
            }

            return BuildCloudflareUrl(cloudflareBasePath + cloudflareUrlSuffix, sourceQueryParameters);
        }

        private static string EnsureTrailingSlash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "/";
            }

            return value.EndsWith('/') ? value : value + "/";
        }

        private static string NormalizePathPrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "/cdn-cgi/image";
            }

            var trimmed = value.Trim().Replace("\\", "/");
            var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return "/" + string.Join("/", segments);
        }

        private static string NormalizeUrlPrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim().Replace("\\", "/");
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            {
                return $"{absoluteUri.GetLeftPart(UriPartial.Authority)}{absoluteUri.AbsolutePath.TrimEnd('/')}";
            }

            return trimmed.TrimEnd('/');
        }

        private static string JoinUrlSegments(string left, string right)
        {
            var leftTrimmed = left?.TrimEnd('/') ?? string.Empty;
            var rightTrimmed = right?.TrimStart('/') ?? string.Empty;

            if (string.IsNullOrEmpty(leftTrimmed))
            {
                return rightTrimmed;
            }

            if (string.IsNullOrEmpty(rightTrimmed))
            {
                return leftTrimmed;
            }

            return $"{leftTrimmed}/{rightTrimmed}";
        }

        private string BuildCloudflareUrl(string cloudflareUrl, Dictionary<string, StringValues> sourceQueryParameters)
        {
            var urlWithQuery = QueryHelpers.AddQueryString(cloudflareUrl, sourceQueryParameters);

            if (!_cloudflareImageUrlGeneratorOptions.EnableSignedUrls ||
                string.IsNullOrWhiteSpace(_cloudflareImageUrlGeneratorOptions.SignedUrlSecret))
            {
                return urlWithQuery;
            }

            var uri = new Uri(urlWithQuery, UriKind.RelativeOrAbsolute);
            var pathAndQuery = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
            var payloadParts = new List<string> { pathAndQuery };
            string? expires = null;

            if (_cloudflareImageUrlGeneratorOptions.SignedUrlTtlSeconds > 0)
            {
                expires = DateTimeOffset.UtcNow.AddSeconds(_cloudflareImageUrlGeneratorOptions.SignedUrlTtlSeconds).ToUnixTimeSeconds().ToString();
                payloadParts.Insert(0, expires);
            }

            var payload = string.Join(":", payloadParts);
            var signature = CreateSignature(_cloudflareImageUrlGeneratorOptions.SignedUrlSecret, payload);
            var signedUrl = QueryHelpers.AddQueryString(urlWithQuery, _cloudflareImageUrlGeneratorOptions.SignedUrlQueryParameterName, signature);

            if (!string.IsNullOrWhiteSpace(expires))
            {
                signedUrl = QueryHelpers.AddQueryString(signedUrl, _cloudflareImageUrlGeneratorOptions.SignedUrlExpiryQueryParameterName, expires);
            }

            return signedUrl;
        }

        private static string CreateSignature(string secret, string payload)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private void AddHmacIfEnabled(string imageUrl, Dictionary<string, StringValues> imageSharpCommands)
        {
            if (_imageSharpMiddlewareOptions.Value.HMACSecretKey.Length != 0)
            {
                var uri = QueryHelpers.AddQueryString(imageUrl, imageSharpCommands);
                var token = _requestAuthorizationUtilities.ComputeHMAC(uri, CommandHandling.Sanitize);
                if (!string.IsNullOrEmpty(token))
                {
                    imageSharpCommands[RequestAuthorizationUtilities.TokenCommand] = token;
                }
            }
        }
    }

    internal enum ResizeSourceAction
    {
        None,
        Width,
        Height
    }
}
