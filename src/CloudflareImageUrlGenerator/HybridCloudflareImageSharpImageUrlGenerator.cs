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
        public IEnumerable<string> CloudFlareSupportedImageFileTypes { get; } = (new string[]{"webp", "avif"}).ToList();
 
        public IEnumerable<string> SupportedImageFileTypes { get; }
        private SixLabors.ImageSharp.Configuration _configuration { get; }
        private RequestAuthorizationUtilities _requestAuthorizationUtilities { get; }
        private IOptions<ImageSharpMiddlewareOptions> _imageSharpMiddlewareOptions { get; }
        private ImageSharpImageUrlGenerator _imageSharpImageUrlGenerator { get; }

        public HybridCloudflareImageSharpImageUrlGenerator(ImageSharpImageUrlGenerator imageSharpImageUrlGenerator, SixLabors.ImageSharp.Configuration configuration, RequestAuthorizationUtilities requestAuthorizationUtilities, IOptions<ImageSharpMiddlewareOptions> imageSharpMiddlewareOptions)
        {

            SupportedImageFileTypes = configuration.ImageFormats.SelectMany(f => f.FileExtensions).ToArray();
            _configuration = configuration;
            _requestAuthorizationUtilities = requestAuthorizationUtilities;
            _imageSharpMiddlewareOptions = imageSharpMiddlewareOptions;
            _imageSharpImageUrlGenerator = imageSharpImageUrlGenerator;
        }
     
        public string? GetImageUrl(ImageUrlGenerationOptions? options)
        {
            if (options?.ImageUrl == null)
            {
                return null;
            }

            var cfCommands = new Dictionary<string, string?>();
            Uri fakeBaseUri = new Uri("https://localhost/");
            var imageSharpString = new ImageSharpImageUrlGenerator(_configuration,_requestAuthorizationUtilities,_imageSharpMiddlewareOptions).GetImageUrl(options);
            var test = _imageSharpImageUrlGenerator.GetImageUrl(options);

            Dictionary<string, StringValues> imageSharpCommands = QueryHelpers.ParseQuery(new Uri(fakeBaseUri, imageSharpString).Query);

            // remove format from ImageSharp and add it to Cloudflare, additionally set ImageSharp quality to 100 (as source) and add quality parameter to Cloudflare 
            if (imageSharpCommands.Remove(FormatWebProcessor.Format, out StringValues format))
            {
                if (CloudFlareSupportedImageFileTypes.Contains(format[0]))
                {
                    var quality = imageSharpCommands[QualityWebProcessor.Quality];
                    imageSharpCommands[QualityWebProcessor.Quality] = "100";
                    cfCommands.Add(FormatWebProcessor.Format, format[0]);
                    cfCommands.Add(QualityWebProcessor.Quality, quality);
                } else
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
