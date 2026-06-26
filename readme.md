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
| `rmode=pad` / `rmode=boxpad` with `bgcolor` | _(no fit/width/height in CF)_ | Cloudflare handles format+quality; ImageSharp handles the pad+bgcolor in the source image |
| `rmode=stretch` | _(no fit param)_ | Stretches to exact dimensions |

### Remaining with ImageSharp.Web (not offloaded)

| ImageSharp command | Notes |
|---|---|
| `bgcolor` with `rmode=pad`/`boxpad` | Pad sizing (rmode/width/height/bgcolor) stays with ImageSharp to preserve the custom colour; Cloudflare still handles format and quality |
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

https://developers.cloudflare.com/images/optimization/transformations/

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

By default the provider offloads conversion of webp and avif file types, you can configure further types, check they are supported output types https://developers.cloudflare.com/images/get-started/limits/

e.g.

```json
"CloudflareImageUrlGenerator": {
	"Enabled": true,
	"CloudFlareSupportedImageFileTypes": ["webp", "avif", "jpg", "png"]
}
```

By default `OffloadAllResizing` is `false` and Cloudflare offloading only activates when a `format` parameter is present. When set to `true`, Cloudflare handles width/height/quality/crop for any request, even without a format parameter. If format is present but not in `CloudFlareSupportedImageFileTypes`, it stays with ImageSharp while the resize is still offloaded to Cloudflare.

```json
"CloudflareImageUrlGenerator": {
	"Enabled": true,
	"OffloadAllResizing": true
}
```

## Usage Without Slimsy

While the Cloudflare Image URL Generator works best with Slimsy for modern responsive image patterns, you can use it directly without Slimsy by calling `GetCropUrl()` directly in your Razor views or controllers.

### Basic Usage with Format

```csharp
// In your Razor view - format triggers Cloudflare for resize and conversion
var imageUrl = Url.GetCropUrl(mediaItem, 323, 300, furtherOptions: "format=webp", htmlEncode: false).ToString();
<img src="@imageUrl" alt="Description" />
```

When you include a `format` parameter with a supported format (webp, avif, jpg, png), the Cloudflare Image URL Generator automatically handles the resizing and format conversion.

## Understanding sourceWidth and sourceHeight

### What Are They For?

Umbraco stores crop coordinates as **fractions** (values between 0.0 and 1.0), for example:

```
crop.Left = 0.25, crop.Top = 0.1, crop.Right = 0.75, crop.Bottom = 0.9
```

Cloudflare's `trim` parameter requires **pixel values**, for example:

```
trim=120;600;1080;300
```

To convert from fractions to pixels, the generator needs to know the original image dimensions. This is what `sourceWidth` and `sourceHeight` provide. They must be passed alongside a predefined crop alias — without a crop alias, there are no fractional coordinates to convert.

**With sourceWidth and sourceHeight (and a crop alias):**
- Fractional crop coordinates are converted to pixels → Cloudflare handles the crop via `trim`
- Full Cloudflare pipeline: crop + resize + format conversion

**Without sourceWidth and sourceHeight:**
- The `rxy` (fractional crop) parameter stays in the ImageSharp source URL
- ImageSharp handles the crop on the source image
- Cloudflare still handles format conversion and quality
- The image is still cropped correctly, just by ImageSharp rather than Cloudflare

### Secondary Use: Oversized Source Images

`sourceWidth` and `sourceHeight` are also used to detect images over 100 megapixels (100,000,000 pixels). If a source image exceeds this threshold, the generator automatically pre-scales it via ImageSharp before sending it to Cloudflare, preventing processing failures on extremely large images.

### Usage Without Slimsy — Crop Offloaded to Cloudflare

Use named parameters to reach the overload that supports both `cropAlias` and `furtherOptions`. The `useCropDimensions: true` parameter tells `GetCropUrl` to use the output dimensions defined on the crop itself (e.g. 300×300 for a GlobalSquare crop).

```csharp
// Get the media item's actual dimensions so the fractional crop coordinates
// can be converted to pixel values for Cloudflare's trim parameter
var sourceWidth = mediaItem.Value<int>("umbracoWidth");
var sourceHeight = mediaItem.Value<int>("umbracoHeight");

var imageUrl = Url.GetCropUrl(
    mediaItem,
    cropAlias: "GlobalSquare",
    useCropDimensions: true,
    furtherOptions: $"sourceWidth={sourceWidth}&sourceHeight={sourceHeight}&format=webp",
    htmlEncode: false
).ToString();

<img src="@imageUrl" alt="Description" />
```

### Usage Without Slimsy — Fallback (ImageSharp handles the crop)

If you omit `sourceWidth` and `sourceHeight`, the crop stays in the ImageSharp source URL. This still produces the correct output but ImageSharp does the crop work instead of Cloudflare.

```csharp
// Without sourceWidth/sourceHeight the rxy crop parameter stays in the ImageSharp
// source URL. ImageSharp handles the crop; Cloudflare handles format and quality.
var imageUrl = Url.GetCropUrl(
    mediaItem,
    cropAlias: "GlobalSquare",
    useCropDimensions: true,
    furtherOptions: "format=webp",
    htmlEncode: false
).ToString();

<img src="@imageUrl" alt="Description" />
```

### Usage With Slimsy (Automatic sourceWidth & sourceHeight)

Slimsy v4.1+ can be configured to automatically add `sourceWidth` and `sourceHeight` to all generated URLs. This is the recommended approach when using Slimsy, as it ensures crops are always offloaded to Cloudflare without any manual work in your views.

#### Configuration

In your `appsettings.json`:

```json
"Slimsy": {
  "AddSourceDimensions": true
}
```

#### Usage

Once configured, simply use Slimsy tag helpers as normal — `sourceWidth` and `sourceHeight` are added automatically from the media item's metadata:

```html
<!-- Slimsy automatically includes sourceWidth and sourceHeight, enabling Cloudflare crop offloading -->
<slimsy-picture media-item="@person.Photo" width="323" height="300" format="webp"></slimsy-picture>

<!-- Or with SlimsyService -->
<img srcset="@SlimsyService.GetSrcSetUrls(person.Photo, 323, 300)" />
```