using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Extensions;
using Xunit;

namespace CloudflareImageUrlGenerator.Tests
{
    /// <summary>
    /// Documents how Umbraco core's GetCropUrl hands crop mode/anchor to the IImageUrlGenerator.
    /// These describe upstream behaviour (Umbraco.Web.Common 17.3.0), not this package.
    /// </summary>
    public class UmbracoGetCropUrlTests
    {
        private sealed class CapturingGenerator : IImageUrlGenerator
        {
            public ImageUrlGenerationOptions? Options { get; private set; }
            public IEnumerable<string> SupportedImageFileTypes => new[] { "png" };
            public string? GetImageUrl(ImageUrlGenerationOptions? options) { Options = options; return options?.ImageUrl; }
        }

        private static ImageUrlGenerationOptions Capture(ImageCropperValue? cropDataSet, ImageCropMode? mode, ImageCropAnchor? anchor)
        {
            var generator = new CapturingGenerator();
            "/media/abc/storageking.png".GetCropUrl(generator, cropDataSet, width: 400, height: 300, imageCropMode: mode, imageCropAnchor: anchor);
            return generator.Options!;
        }

        [Fact]
        public void With_cropper_value_and_Crop_mode_the_anchor_is_dropped_by_Umbraco()
        {
            // This is the Slimsy case: Slimsy defaults to Crop and the media always has an umbracoFile value.
            var options = Capture(new ImageCropperValue { Src = "/media/abc/storageking.png" }, ImageCropMode.Crop, ImageCropAnchor.TopLeft);
            Assert.Null(options.ImageCropAnchor);
        }

        [Fact]
        public void With_cropper_value_and_null_mode_the_anchor_is_dropped_by_Umbraco()
        {
            Assert.Null(Capture(new ImageCropperValue { Src = "/media/abc/storageking.png" }, null, ImageCropAnchor.TopLeft).ImageCropAnchor);
        }

        [Fact]
        public void Without_cropper_value_the_anchor_reaches_the_generator()
        {
            Assert.Equal(ImageCropAnchor.TopLeft, Capture(null, ImageCropMode.Crop, ImageCropAnchor.TopLeft).ImageCropAnchor);
        }

        [Fact]
        public void With_cropper_value_and_a_non_Crop_mode_the_anchor_reaches_the_generator()
        {
            Assert.Equal(ImageCropAnchor.TopLeft, Capture(new ImageCropperValue { Src = "/media/abc/storageking.png" }, ImageCropMode.Min, ImageCropAnchor.TopLeft).ImageCropAnchor);
        }
    }
}
