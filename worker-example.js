const PUBLIC_PATH_PREFIX = "cdn-mysite/image/";
const PRIVATE_ORIGIN_BASE_URL = "https://mysite.blob.core.windows.net";
const PRIVATE_ORIGIN_MEDIA_PATH = "/mycontainer/media";

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    // Map the public path to your private origin.
    const originPath = PRIVATE_ORIGIN_MEDIA_PATH + url.pathname;
    const originUrl = new URL(originPath, PRIVATE_ORIGIN_BASE_URL);

    // Parse the command segment from the public URL.
    const path = url.pathname.replace(/^\/+/, '');
    const commandSection = path.startsWith(PUBLIC_PATH_PREFIX)
      ? path.replace(new RegExp(`^${PUBLIC_PATH_PREFIX}`), '').split('/')[0]
      : null;

    if (!commandSection) {
      return new Response("Not Found", { status: 404 });
    }

    const commands = new Map();
    for (const part of commandSection.split(',').filter(Boolean)) {
      const [key, value] = part.split('=', 2);
      if (key && value !== undefined) {
        commands.set(key, value);
      }
    }

    const imageOptions = {};
    for (const [key, value] of commands.entries()) {
      const normalizedKey = key.toLowerCase();
      if (normalizedKey === "w") {
        imageOptions.width = Number(value);
      } else if (normalizedKey === "h") {
        imageOptions.height = Number(value);
      } else if (normalizedKey === "fit") {
        imageOptions.fit = value;
      } else if (normalizedKey === "format") {
        imageOptions.format = value;
      } else if (normalizedKey === "quality") {
        imageOptions.quality = Number(value);
      } else if (normalizedKey === "trim") {
        imageOptions.trim = value;
      } else if (normalizedKey === "gravity") {
        imageOptions.gravity = value;
      } else {
        imageOptions[normalizedKey] = value;
      }
    }

    return fetch(originUrl, {
      cf: {
        image: imageOptions
      }
    });
  }
};
