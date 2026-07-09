const PUBLIC_PATH_PREFIX = "cdn-test/image/";
const PRIVATE_ORIGIN_BASE_URL = "https://mysite.blob.core.windows.net";
const PRIVATE_ORIGIN_CONTAINER_PATH = "/mycontainer";
const ENABLE_WORKER_LOGGING = true;
const ENABLE_SIGNED_URLS = true;
const SIGNED_URL_SECRET = "replace-with-a-long-random-secret";
const SIGNED_URL_QUERY_PARAMETER_NAME = "sig";
const SIGNED_URL_EXPIRY_QUERY_PARAMETER_NAME = "expires";
const SIGNED_URL_TTL_SECONDS = 0;

async function createSignature(secret, payload) {
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey(
    "raw",
    encoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );

  const signature = await crypto.subtle.sign("HMAC", key, encoder.encode(payload));
  return Array.from(new Uint8Array(signature))
    .map((byte) => byte.toString(16).padStart(2, "0"))
    .join("");
}

// Removing keys via `URLSearchParams` (e.g. `searchParams.delete(...)`) forces the
// *entire* query string to be re-serialized using the application/x-www-form-urlencoded
// encoder, which escapes characters (like `,`) differently than the raw query string
// that was originally signed. That silently breaks signature verification for any URL
// containing such characters (e.g. focal-point crop coordinates). Instead, strip the
// named parameters using plain string manipulation so every other parameter's encoding
// is preserved byte-for-byte.
function removeQueryParams(rawSearch, paramNames) {
  if (!rawSearch || rawSearch === "?") {
    return "";
  }

  const query = rawSearch.startsWith("?") ? rawSearch.slice(1) : rawSearch;
  const kept = query
    .split("&")
    .filter((pair) => pair && !paramNames.includes(pair.split("=", 1)[0]));

  return kept.length > 0 ? `?${kept.join("&")}` : "";
}

async function verifySignedRequest(url, secret) {
  if (!ENABLE_SIGNED_URLS || !secret) {
    return true;
  }

  const signature = url.searchParams.get(SIGNED_URL_QUERY_PARAMETER_NAME);
  const expiresValue = url.searchParams.get(SIGNED_URL_EXPIRY_QUERY_PARAMETER_NAME);
  if (!signature) {
    return false;
  }

  const remainingSearch = removeQueryParams(url.search, [
    SIGNED_URL_QUERY_PARAMETER_NAME,
    SIGNED_URL_EXPIRY_QUERY_PARAMETER_NAME
  ]);

  let payload = `${url.pathname}${remainingSearch}`;

  if (SIGNED_URL_TTL_SECONDS > 0) {
    if (!expiresValue) {
      return false;
    }

    const expires = Number(expiresValue);
    if (!Number.isFinite(expires) || expires <= Math.floor(Date.now() / 1000)) {
      return false;
    }

    payload = `${expires}:${payload}`;
  }
  const expectedSignature = await createSignature(secret, payload);

  const encoder = new TextEncoder();
  const signatureBytes = encoder.encode(signature);
  const expectedSignatureBytes = encoder.encode(expectedSignature);

  return timingSafeEqual(signatureBytes, expectedSignatureBytes);
}

// The global `crypto` object in Cloudflare Workers is the standard Web Crypto
// API, which does not expose Node's `crypto.timingSafeEqual`. Implement a
// constant-time comparison manually so signature checks aren't vulnerable to
// timing attacks.
function timingSafeEqual(a, b) {
  if (a.length !== b.length) {
    return false;
  }

  let mismatch = 0;
  for (let i = 0; i < a.length; i++) {
    mismatch |= a[i] ^ b[i];
  }

  return mismatch === 0;
}

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

    const signedUrlSecret = env.SIGNED_URL_SECRET || SIGNED_URL_SECRET;
    if (!(await verifySignedRequest(url, signedUrlSecret))) {
      logWorker("worker rejected unsigned or expired request");
      return new Response("Forbidden", { status: 403 });
    }

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

    // Map the public path to your private origin. If no private origin is configured
    // (e.g. the worker is only used to verify signed URLs), fall back to the
    // requesting host so the worker can still resize images from the same site.
    const originBaseUrl = PRIVATE_ORIGIN_BASE_URL || url.origin;
    const originPath = PRIVATE_ORIGIN_BASE_URL
      ? `${PRIVATE_ORIGIN_CONTAINER_PATH}/${sourcePath}`
      : `/${sourcePath}`;
    const originUrl = new URL(`${originPath}${url.search}`, originBaseUrl);

    logWorker("worker origin base url", originBaseUrl);
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
