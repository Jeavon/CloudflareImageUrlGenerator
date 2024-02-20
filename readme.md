CloudflareImageUrlGenerator
============
This package adds a ImageUrlGenerator to Umbraco that offloads image format conversion of avif and webp formats to Cloudflare Image Resizing. When implemented calls to GetCropUrl will generate Urls using this generator.

It works very well with [Slimsy v4.1+](https://github.com/Jeavon/Slimsy) to offer avif format images as the primary source for modern browsers.

**For Umbraco v10 & v11 please use v1.x**

```
dotnet add package Umbraco.Community.CloudflareImageUrlGenerator --version 1.0.0
```

**For Umbraco v12+ please use v2.x**
This Url Generator will **not work with the HMACSecretKey** due to the path being different so ensure that's not enabled

```
dotnet add package Umbraco.Community.CloudflareImageUrlGenerator --version 2.0.1
```

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

### Further Options (v2.0.1+)

By default the provider offloads conversion of webp and avif file types, you can configure further types, check they are supported output types https://developers.cloudflare.com/images/image-resizing/format-limitations/

e.g.

```json
	"CloudflareImageUrlGenerator": {
		"Enabled": true,
		"CloudFlareSupportedImageFileTypes": ["webp", "avif", "jpg", "png"]
	}
```

