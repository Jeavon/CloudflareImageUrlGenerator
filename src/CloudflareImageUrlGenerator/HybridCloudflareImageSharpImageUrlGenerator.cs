using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp.Web;
using SixLabors.ImageSharp.Web.Middleware;
using SixLabors.ImageSharp.Web.Processors;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.Models;
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

            var resizeSourceAction = ResizeSourceAction.None;
            int? sourceResize = null;
            if (sourceWidth != null && sourceHeight != null)
            {
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
            }

            // remove format from ImageSharp and add it to Cloudflare, additionally set ImageSharp quality to 100 (as source) and add quality parameter to Cloudflare 
            if (imageSharpCommands.ContainsKey(FormatWebProcessor.Format))
            {
                var format = imageSharpCommands[FormatWebProcessor.Format];

                if (_cloudflareImageUrlGeneratorOptions.CloudFlareSupportedImageFileTypes.Contains(format[0]))
                {
                    imageSharpCommands.Remove(FormatWebProcessor.Format);
                    var addFit = false;

                    // only offload crop mode resizing for now
                    if (options.ImageCropMode is null or ImageCropMode.Crop)
                    {

                        if (imageSharpCommands.ContainsKey(ResizeWebProcessor.Width))
                        {
                            if (resizeSourceAction == ResizeSourceAction.Width)
                            {
                                var width = imageSharpCommands[ResizeWebProcessor.Width];
                                imageSharpCommands[ResizeWebProcessor.Width] = sourceResize.ToString();
                                cfCommands.Add("w", width);
                                addFit = true;
                            }
                            else
                            {
                                if (imageSharpCommands.Remove(ResizeWebProcessor.Width, out var width))
                                {
                                    cfCommands.Add("w", width);
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
                                cfCommands.Add("h", height);
                                addFit = true;
                            }
                            else
                            {
                                if (imageSharpCommands.Remove(ResizeWebProcessor.Height, out var height))
                                {
                                    var h = Convert.ToInt32(height);
                                    if (h > 0)
                                    {
                                        cfCommands.Add("h", h.ToString());
                                        addFit = true;
                                    }
                                }
                            }
                        }

                        if (options.FocalPoint is not null)
                        {
                            if (imageSharpCommands.Remove(ResizeWebProcessor.Xy))
                            {
                                cfCommands.Add("gravity", FormattableString.Invariant($"{options.FocalPoint.Left}x{options.FocalPoint.Top}"));
                                addFit = true;
                            }
                        }
                    }

                    cfCommands.Add(FormatWebProcessor.Format, format[0]);

                    if (imageSharpCommands.ContainsKey(QualityWebProcessor.Quality))
                    {
                        var quality = imageSharpCommands[QualityWebProcessor.Quality];
                        imageSharpCommands[QualityWebProcessor.Quality] = "100";
                        cfCommands.Add(QualityWebProcessor.Quality, quality);
                    }

                    if (addFit)
                    {
                        cfCommands.Add("fit", "cover");
                    }
                }
                else
                {
                    return QueryHelpers.AddQueryString(options.ImageUrl, imageSharpCommands);
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

            return  QueryHelpers.AddQueryString("/cdn-cgi/image/" + cfCommandString + options.ImageUrl, imageSharpCommands);
        }
    }

    internal enum ResizeSourceAction
    {
        None,
        Width,
        Height
    }
}
