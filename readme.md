CloudflareImageUrlGenerator
============
This package adds a ImageUrlGenerator to Umbraco that offloads image format conversion of avif and webp formats to Cloudflare Image Resizing. When implemented calls to GetCropUrl will generate Urls using this generator.

It works very well with [Slimsy v4.1+](https://github.com/Jeavon/Slimsy) to offer avif format images as the primary source for modern browsers. 

__Release Downloads__

NuGet Package: [![NuGet release](https://img.shields.io/nuget/vpre/Umbraco.Community.CloudflareImageUrlGenerator.svg)](https://www.nuget.org/packages/Umbraco.Community.CloudflareImageUrlGenerator/)

__Prerelease Downloads__

NuGet Package: [![MyGet build](https://img.shields.io/myget/umbraco-packages/vpre/Umbraco.Community.CloudflareImageUrlGenerator.svg)](https://www.myget.org/feed/umbraco-packages/package/nuget/Umbraco.Community.CloudflareImageUrlGenerator)

## Installation

### 1. Install from NuGet

### 2. Add to Startup.cs in the ConfigureServices method

```c#
.AddCloudflareImageUrlGenerator()
```

e.g.

```c#
services.AddUmbraco(_env, _config)
  .AddBackOffice()
  .AddWebsite()
	.AddComposers()
	.AddSlimsy()
	.AddAzureBlobMediaFileSystem()
	.AddCloudflareImageUrlGenerator()
	.Build();
```

### 3. Enable Image Resizing on Cloudflare

https://developers.cloudflare.com/images/image-resizing/enable-image-resizing/

### 4. Optionally disable the generator for local development

In appsettings.json

```json
	"CloudflareImageUrlGenerator": {
		"Enabled": false
	}
```

Then in appsettings.production.json

```json
	"CloudflareImageUrlGenerator": {
		"Enabled": true
	}
```

Or use the environment variable `CloudflareImageUrlGenerator__Enabled` : `true` for environments with Cloudflare