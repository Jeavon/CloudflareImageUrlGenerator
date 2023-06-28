using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Media;
using Umbraco.Extensions;

namespace CloudflareImageUrlGenerator
{
    public static class CloudflareImageUrlGeneratorExtensions
    {
        public static IUmbracoBuilder AddCloudflareImageUrlGenerator(this IUmbracoBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var cloudflareImageUrlGeneratorOptions = new CloudflareImageUrlGeneratorOptions();
            var cloudflareImageUrlGeneratorSection = builder.Config.GetSection(CloudflareImageUrlGeneratorOptions.CloudflareImageUrlGeneratorSection);
            cloudflareImageUrlGeneratorSection.Bind(cloudflareImageUrlGeneratorOptions);
            builder.Services.Configure<CloudflareImageUrlGeneratorOptions>(cloudflareImageUrlGeneratorSection);

            if (cloudflareImageUrlGeneratorOptions.Enabled)
            {
                builder.Services.AddUnique<IImageUrlGenerator, HybridCloudflareImageSharpImageUrlGenerator>();
            }
            return builder;
        }
    }
}