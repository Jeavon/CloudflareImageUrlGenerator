using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SixLabors.ImageSharp.Web;
using SixLabors.ImageSharp.Web.Middleware;
using SixLabors.ImageSharp.Web.Processors;
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
        public HybridCloudflareImageSharpImageUrlGenerator(SixLabors.ImageSharp.Configuration configuration, IOptionsMonitor<CloudflareImageUrlGeneratorOptions> cloudflareImageUrlGeneratorOptions, RequestAuthorizationUtilities requestAuthorizationUtilities, IOptions<ImageSharpMiddlewareOptions> imageSharpMiddlewareOptions)
        {

            SupportedImageFileTypes = configuration.ImageFormats.SelectMany(f => f.FileExtensions).ToArray();
            _configuration = configuration;
            _requestAuthorizationUtilities = requestAuthorizationUtilities;
            _imageSharpMiddlewareOptions = imageSharpMiddlewareOptions;
            _cloudflareImageUrlGeneratorOptions = cloudflareImageUrlGeneratorOptions.CurrentValue;
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

            // remove format from ImageSharp and add it to Cloudflare, additionally set ImageSharp quality to 100 (as source) and add quality parameter to Cloudflare 
            if (imageSharpCommands.Remove(FormatWebProcessor.Format, out StringValues format))
            {
                if (_cloudflareImageUrlGeneratorOptions.CloudFlareSupportedImageFileTypes.Contains(format[0]))
                {
                    var addFit = false;
                    if (imageSharpCommands.ContainsKey(ResizeWebProcessor.Width))
                    {
                        if (imageSharpCommands.Remove(ResizeWebProcessor.Width, out var width))
                        {
                            cfCommands.Add("w", width);
                            addFit = true;
                        }
                    }
                    if (imageSharpCommands.ContainsKey(ResizeWebProcessor.Height))
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

                    cfCommands.Add(FormatWebProcessor.Format, format[0]);

                    if (imageSharpCommands.ContainsKey(QualityWebProcessor.Quality))
                    {
                        var quality = imageSharpCommands[QualityWebProcessor.Quality];
                        imageSharpCommands[QualityWebProcessor.Quality] = "100";
                        cfCommands.Add(QualityWebProcessor.Quality, quality);
                    }

                    if (options.FocalPoint is not null)
                    {
                        cfCommands.Add("gravity", FormattableString.Invariant($"{options.FocalPoint.Left}x{options.FocalPoint.Top}"));
                        addFit = true;
                    }

                    if (addFit)
                    {
                        cfCommands.Add("fit", "crop");
                    }
                }
                else
                {
                    return imageSharpString;
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
}
