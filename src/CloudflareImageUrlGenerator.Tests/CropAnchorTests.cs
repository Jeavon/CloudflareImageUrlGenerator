using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Middleware;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.Models;
using Xunit;

namespace CloudflareImageUrlGenerator.Tests
{
    public class CropAnchorTests
    {
        private const string Image = "/media/abc/storageking.png";

        private static HybridCloudflareImageSharpImageUrlGenerator CreateGenerator()
        {
            var services = new ServiceCollection();
            services.AddImageSharp();
            var provider = services.BuildServiceProvider();
            var configuration = SixLabors.ImageSharp.Configuration.Default;
            var middlewareOptions = provider.GetRequiredService<IOptions<ImageSharpMiddlewareOptions>>();
            var authorization = provider.GetRequiredService<RequestAuthorizationUtilities>();
            var cfOptions = new TestOptionsMonitor(new CloudflareImageUrlGeneratorOptions());
            return new HybridCloudflareImageSharpImageUrlGenerator(
                configuration, cfOptions, authorization, middlewareOptions, Options.Create(new ImagingSettings()));
        }

        private static string Generate(ImageCropMode? mode, ImageCropAnchor? anchor, ImageUrlGenerationOptions.FocalPointPosition? focalPoint = null)
        {
            var options = new ImageUrlGenerationOptions(Image)
            {
                Width = 400,
                Height = 300,
                ImageCropMode = mode,
                ImageCropAnchor = anchor,
                FocalPoint = focalPoint,
                Quality = 70,
                FurtherOptions = "&format=webp",
            };
            return CreateGenerator().GetImageUrl(options)!;
        }

        [Theory]
        [InlineData(ImageCropAnchor.Top, "gravity=top")]
        [InlineData(ImageCropAnchor.Right, "gravity=right")]
        [InlineData(ImageCropAnchor.Bottom, "gravity=bottom")]
        [InlineData(ImageCropAnchor.Left, "gravity=left")]
        [InlineData(ImageCropAnchor.TopLeft, "gravity=0x0")]
        [InlineData(ImageCropAnchor.TopRight, "gravity=1x0")]
        [InlineData(ImageCropAnchor.BottomLeft, "gravity=0x1")]
        [InlineData(ImageCropAnchor.BottomRight, "gravity=1x1")]
        public void Anchor_is_mapped_to_gravity(ImageCropAnchor anchor, string expected)
        {
            var url = Generate(ImageCropMode.Crop, anchor);
            Assert.Contains(expected + ",", url);
            Assert.Contains("fit=cover", url);
        }

        [Fact]
        public void Center_anchor_adds_no_gravity()
        {
            Assert.DoesNotContain("gravity", Generate(ImageCropMode.Crop, ImageCropAnchor.Center));
        }

        [Fact]
        public void No_anchor_is_unchanged()
        {
            Assert.Equal("/cdn-cgi/image/w=400,h=300,format=webp,quality=70,fit=cover/media/abc/storageking.png", Generate(null, null));
        }

        [Fact]
        public void Anchor_applies_when_mode_is_null()
        {
            Assert.Contains("gravity=0x0", Generate(null, ImageCropAnchor.TopLeft));
        }

        [Fact]
        public void Focal_point_wins_over_anchor()
        {
            var url = Generate(ImageCropMode.Crop, ImageCropAnchor.TopLeft, new ImageUrlGenerationOptions.FocalPointPosition(0.9m, 0.51m));
            Assert.Contains("gravity=0.9x0.51", url);
            Assert.DoesNotContain("gravity=0x0", url);
        }

        [Fact]
        public void Anchor_is_ignored_for_non_crop_modes()
        {
            Assert.DoesNotContain("gravity", Generate(ImageCropMode.Max, ImageCropAnchor.TopLeft));
        }
    }

    internal sealed class TestOptionsMonitor(CloudflareImageUrlGeneratorOptions value) : IOptionsMonitor<CloudflareImageUrlGeneratorOptions>
    {
        public CloudflareImageUrlGeneratorOptions CurrentValue => value;
        public CloudflareImageUrlGeneratorOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<CloudflareImageUrlGeneratorOptions, string?> listener) => null;
    }
}
