using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.Models;

namespace CloudflareImageUrlGenerator
{
    public sealed class CloudflareImageUrlGenerator : IImageUrlGenerator 
    {
        //https://developers.cloudflare.com/images/image-resizing/format-limitations/
        public IEnumerable<string> SupportedImageFileTypes => new[] { "jpeg", "png", "gif", "webp", "svg" };
        private IWebHostEnvironment _webHostEnvironment { get; }
        private readonly IImageDimensionExtractor _imageDimensionExtractor;


        public CloudflareImageUrlGenerator( IWebHostEnvironment webHostEnvironment, IImageDimensionExtractor imageDimensionExtractor) {

            _webHostEnvironment = webHostEnvironment;
            _imageDimensionExtractor = imageDimensionExtractor;

        }
     
        public string? GetImageUrl(ImageUrlGenerationOptions? options)
        {
            if (options?.ImageUrl == null)
            {
                return null;
            }

            var queryString = new Dictionary<string, string?>();
            Dictionary<string, StringValues> furtherOptions = QueryHelpers.ParseQuery(options.FurtherOptions);

            var cfCommands = new Dictionary<string, string?>();

            // MUST CACHE THIS, maybe IDistributedCache 
            System.Drawing.Size? imageSourceDimensions;
            using (Stream imageStream = _webHostEnvironment.WebRootFileProvider.GetFileInfo(options.ImageUrl).CreateReadStream())
            {
                imageSourceDimensions = _imageDimensionExtractor.GetDimensions(imageStream);
            }
            if (options.Crop is not null)
            {
                ImageUrlGenerationOptions.CropCoordinates? crop = options.Crop;

                var top = Math.Round(crop.Top * imageSourceDimensions.Value.Height);
                var left = Math.Round(crop.Left * imageSourceDimensions.Value.Width);
                var bottom = Math.Round(crop.Bottom * imageSourceDimensions.Value.Height);
                var right = Math.Round(crop.Right * imageSourceDimensions.Value.Width);

                cfCommands.Add(CloudflareCommands.Trim, $"{top},{right},{bottom},{left}");
            }

            // Gravity only applies in fit cover and crop
            if (options.FocalPoint is not null && options.ImageCropMode is null)
            {
                options.ImageCropMode = ImageCropMode.Crop;
            }

            var cfCropModeCommandValue = "";
            if (options.ImageCropMode is not null)
            {
                cfCropModeCommandValue = options.ImageCropMode switch
                {
                    ImageCropMode.Crop => CloudflareCommands.Crop,
                    ImageCropMode.Pad => CloudflareCommands.Pad,
                    ImageCropMode.Max => CloudflareCommands.Cover,
                    ImageCropMode.Min => CloudflareCommands.Contain,
                    ImageCropMode.Stretch =>
                        // not supported
                        CloudflareCommands.Cover,
                    ImageCropMode.BoxPad =>
                        // not supported
                        CloudflareCommands.Pad,
                    _ => cfCropModeCommandValue
                };
            }
            else
            {
                cfCropModeCommandValue = CloudflareCommands.Crop;
            }

            cfCommands.Add(CloudflareCommands.Fit, cfCropModeCommandValue.ToLowerInvariant());
            

            if (options.FocalPoint is not null)
            {
                cfCommands.Add(CloudflareCommands.Gravity, FormattableString.Invariant($"{options.FocalPoint.Left}x{options.FocalPoint.Top}"));
            }

            if (options.ImageCropAnchor is not null)
            {
                // TODO
               // queryString.Add(ResizeWebProcessor.Anchor, options.ImageCropAnchor.ToString()?.ToLowerInvariant());
            }

            if (options.Width is not null && options.Width > 0)
            {
               cfCommands.Add(CloudflareCommands.Width, options.Width?.ToString(CultureInfo.InvariantCulture));
            }

            if (options.Height is not null && options.Height > 0)
            {
                cfCommands.Add(CloudflareCommands.Height, options.Height?.ToString(CultureInfo.InvariantCulture));
            }

            if (furtherOptions.Remove("format", out StringValues format))
            {
                cfCommands.Add(CloudflareCommands.Format, format[0]);
            }

            if (options.Quality is not null)
            {
                cfCommands.Add(CloudflareCommands.Quality, options.Quality?.ToString(CultureInfo.InvariantCulture));
            }

            foreach (KeyValuePair<string, StringValues> kvp in furtherOptions)
            {
                cfCommands.Add(kvp.Key, kvp.Value);
            }

            if (options.CacheBusterValue is not null && !string.IsNullOrWhiteSpace(options.CacheBusterValue))
            {
                queryString.Add("rnd", options.CacheBusterValue);
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

            return QueryHelpers.AddQueryString("/cdn-cgi/image/" + cfCommandString + options.ImageUrl, queryString);

        }
    }
}
