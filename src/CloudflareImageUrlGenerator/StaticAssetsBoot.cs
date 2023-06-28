using System.Diagnostics;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Manifest;

namespace CloudflareImageUrlGenerator
{
    public class StaticAssetsBoot : IComposer
    {
        public void Compose(IUmbracoBuilder builder)
        {
#if NET6_0
            builder.AddCloudflareImageUrlGeneratorStaticAssets();
#endif
        }
    }

    public static class uSyncStaticAssetsExtensions
    {
        internal static IUmbracoBuilder AddCloudflareImageUrlGeneratorStaticAssets(this IUmbracoBuilder builder)
        {
            if (builder.ManifestFilters().Has<CloudflareImageUrlGeneratorAssetManifestFilter>())
                return builder;

            builder.ManifestFilters().Append<CloudflareImageUrlGeneratorAssetManifestFilter>();

            return builder;
        }
    }

    internal class CloudflareImageUrlGeneratorAssetManifestFilter : IManifestFilter
    {
        public void Filter(List<PackageManifest> manifests)
        {
            var assembly = typeof(CloudflareImageUrlGeneratorAssetManifestFilter).Assembly;
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fileVersionInfo.ProductVersion;

            manifests.Add(new PackageManifest
            {
                PackageName = "CloudflareImageUrlGenerator",
                Version = version,
                AllowPackageTelemetry = true,
            });
        }
    }
}
