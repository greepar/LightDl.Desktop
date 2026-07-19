const nativeHostName = "com.lightdl.browser";
const captureDelayMs = 0;
const completedStateLifetimeMs = 5 * 60 * 1000;
const downloadStates = new Map();

browser.downloads.onCreated.addListener((download) => {
  if (downloadStates.has(download.id)) return;

  const timer = setTimeout(() => captureDownload(download.id, download), captureDelayMs);
  downloadStates.set(download.id, { status: "waiting", timer });
});

if (browser.downloads.onDeterminingFilename) {
  browser.downloads.onDeterminingFilename.addListener((download, suggest) => {
    suggest();
    const state = downloadStates.get(download.id);
    if (state?.timer) clearTimeout(state.timer);
    captureDownload(download.id, download);
  });
}

async function captureDownload(downloadId, initialDownload) {
  const state = downloadStates.get(downloadId);
  if (state?.status === "pending" || state?.status === "completed") return;

  if (state?.timer) clearTimeout(state.timer);
  downloadStates.set(downloadId, { status: "pending" });

  let pausedByLightDl = false;
  let handedOff = false;

  try {
    const matches = await browser.downloads.search({ id: downloadId });
    const download = { ...initialDownload, ...(matches[0] || {}) };

    if (!download.paused && download.state === "in_progress") {
      try {
        await browser.downloads.pause(downloadId);
        pausedByLightDl = true;
      } catch {
        // Some browser-managed downloads cannot be paused. Continue safely.
      }
    }

    const request = await buildRequest(download);
    const response = await browser.runtime.sendNativeMessage(nativeHostName, request);

    if (response?.accepted === true && response?.cancelBrowserDownload === true) {
      try {
        await browser.downloads.cancel(downloadId);
        await browser.downloads.erase({ id: downloadId });
        handedOff = true;
      } catch {
        // If cancellation fails, resume below so the browser task is not stranded.
      }
    }
  } catch {
    // Native host failures must leave the browser download untouched.
  } finally {
    if (pausedByLightDl && !handedOff) {
      try {
        await browser.downloads.resume(downloadId);
      } catch {
        // The task may already have completed or been removed by the user.
      }
    }

    markCompleted(downloadId);
  }
}

async function buildRequest(download) {
  const finalUrl = download.finalUrl || download.url || "";
  const headers = [];
  const cookies = await getCookies(finalUrl || download.url);

  if (cookies.length > 0) {
    headers.push({
      name: "Cookie",
      value: cookies.map((cookie) => `${cookie.name}=${cookie.value}`).join("; "),
      sensitive: true
    });
  }

  return {
    protocolVersion: 1,
    requestId: createUuid(),
    type: "capture-download",
    browser: "firefox",
    browserDownloadId: String(download.id),
    url: download.url || finalUrl,
    finalUrl,
    referrer: download.referrer || "",
    suggestedFileName: getBaseName(download.filename || ""),
    mimeType: download.mime || "",
    totalBytes: Number.isFinite(download.totalBytes) && download.totalBytes >= 0
      ? download.totalBytes
      : null,
    headers
  };
}

async function getCookies(url) {
  if (!url) return [];

  try {
    return await browser.cookies.getAll({ url });
  } catch {
    return [];
  }
}

function getBaseName(fileName) {
  return fileName.split(/[\\/]/).pop() || "";
}

function createUuid() {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();

  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0"));
  return `${hex.slice(0, 4).join("")}-${hex.slice(4, 6).join("")}-${hex.slice(6, 8).join("")}-${hex.slice(8, 10).join("")}-${hex.slice(10).join("")}`;
}

function markCompleted(downloadId) {
  downloadStates.set(downloadId, { status: "completed" });
  setTimeout(() => {
    if (downloadStates.get(downloadId)?.status === "completed") {
      downloadStates.delete(downloadId);
    }
  }, completedStateLifetimeMs);
}
