const PUBLIC_PATH_PREFIX = "cdn-mysite/image/";
const PRIVATE_ORIGIN_BASE_URL = "https://mysite.blob.core.windows.net";
const PRIVATE_ORIGIN_CONTAINER_PATH = "/mycontainer";
const ENABLE_WORKER_LOGGING = true;

function logWorker(message, details) {
  if (!ENABLE_WORKER_LOGGING) {
    return;
  }

  if (details === undefined) {
    console.log(message);
  } else {
    console.log(message, details);
  }
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    logWorker("worker request url", url.toString());

    // Parse the command segment from the public URL.
    const path = url.pathname.replace(/^\/+/, '');
    const relativePath = path.startsWith(PUBLIC_PATH_PREFIX)
      ? path.slice(PUBLIC_PATH_PREFIX.length)
      : path;

    const relativeParts = relativePath.split('/').filter(Boolean);
    const commandSection = relativeParts[0] ?? null;
    const sourcePath = relativeParts.slice(1).join('/');

    logWorker("worker path", path);
    logWorker("worker relative path", relativePath);
    logWorker("worker command section", commandSection);
    logWorker("worker source path", sourcePath);

    if (!commandSection || !sourcePath) {
      logWorker("worker no command section or source path found");
      return new Response("Not Found", { status: 404 });
    }

    // Map the public path to your private origin.
    const originPath = `${PRIVATE_ORIGIN_CONTAINER_PATH}/${sourcePath}`;
    const originUrl = new URL(`${originPath}${url.search}`, PRIVATE_ORIGIN_BASE_URL);

    logWorker("worker origin path", originPath);
    logWorker("worker origin url", originUrl.toString());

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
        const [x, y] = value.split('x', 2);
        imageOptions.gravity = {
          x: Number(x),
          y: Number(y)
        };
      } else {
        imageOptions[normalizedKey] = value;
      }
    }

    logWorker("worker parsed commands", JSON.stringify(Object.fromEntries(commands.entries())));
    logWorker("worker image options", JSON.stringify(imageOptions));

    try {
      const response = await fetch(originUrl, {
        cf: {
          image: imageOptions
        }
      });

      logWorker("worker upstream status", `${response.status} ${response.statusText}`);
      return response;
    } catch (error) {
      logWorker("worker fetch error", error);
      throw error;
    }
  }
};
