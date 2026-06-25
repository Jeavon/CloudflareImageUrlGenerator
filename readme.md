CloudflareImageUrlGenerator
============
This package adds a ImageUrlGenerator to Umbraco that offloads some image processing to Cloudflare Image Resizing.

Currently the approach is "hybrid" with some commands offloaded to Cloudflare and some remaining with ImageSharp.Web.

When implemented calls to GetCropUrl will generate Urls using this generator when the "format" parameter is present.

### Offloaded to Cloudflare Image Resizing

| ImageSharp command | Cloudflare command | Notes |
|---|---|---|
| `format` | `format` | Only when format is in `CloudFlareSupportedImageFileTypes` |
| `width` | `w` | |
| `height` | `h` | |
| `quality` | `quality` | ImageSharp source is set to `quality=100`; Cloudflare applies the requested quality |
| `xy` (focal point) | `gravity` | Expressed as `{left}x{top}` fractions; only for `crop`/default mode |
| `rxy` (crop coordinates) | `trim` | Pixel-based `top;right;bottom;left` |
| `autoOrient` | _(removed)_ | Cloudflare handles EXIF auto-orientation by default |
| `rmode=crop` (default) | `fit=cover` | Crops to fill both dimensions |
| `rmode=max` | `fit=scale-down` | Shrinks to fit, never enlarges |
| `rmode=min` | `fit=contain` | Fits within dimensions, may be smaller |
| `rmode=pad` / `rmode=boxpad` | `fit=pad` | Letterboxes with white/transparent background **only when no custom bgcolor is set** |
| `rmode=stretch` | _(no fit param)_ | Stretches to exact dimensions |

### Remaining with ImageSharp.Web (not offloaded)

| ImageSharp command | Notes |
|---|---|
| `bgcolor` with `rmode=pad`/`boxpad` | When a custom background color is set for pad modes, offloading is skipped to preserve the color (Cloudflare uses white/transparent)|
| Any other `furtherOptions` | Custom or unrecognised commands stay in the ImageSharp source URL |

When remaining ImageSharp commands are present in the source URL and `HMACSecretKey` is configured, a valid HMAC token is automatically appended to the source URL.

It works very well with [Slimsy v4.1+](https://github.com/Jeavon/Slimsy) to offer avif format images as the primary source for modern browsers.

**For Umbraco v17 & v18 use v4.x**
This Url Generator **supports HMACSecretKey if enabled** 

```
dotnet add package Umbraco.Community.CloudflareImageUrlGenerator --version 4.*
```

**For Umbraco v12 & v13+ please use v2.x**
This Url Generator will **not work with the HMACSecretKey** due to the path being different so ensure that's not enabled

```
dotnet add package Umbraco.Community.CloudflareImageUrlGenerator --version 2.*
```

**For Umbraco v10 & v11 please use v1.x**

```
dotnet add package Umbraco.Community.CloudflareImageUrlGenerator --version 1.0.0
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

