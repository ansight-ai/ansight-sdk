"use strict";
function attachNetwork(contents, publish, diagnostic) {
  const api = contents.debugger;
  if (api.isAttached()) {
    diagnostic(new Error("Network capture skipped: debugger already attached"));
    return () => {};
  }
  const requests = new Map();
  let sequence = 0;
  const cleanUrl = (value) => {
    try {
      const u = new URL(value);
      u.username = "";
      u.password = "";
      u.search = "";
      u.hash = "";
      return u.toString();
    } catch {
      return "[invalid URL]";
    }
  };
  function finish(id, timestamp, error, response) {
    const record = requests.get(id);
    if (!record) return;
    requests.delete(id);
    if (response) {
      record.statusCode = response.status;
      record.protocol = response.protocol;
    }
    const duration = Math.max(0, (timestamp - record.start) * 1000);
    record.durationMilliseconds = duration;
    record.completedAtUtc = new Date(
      Date.parse(record.startedAtUtc) + duration,
    ).toISOString();
    delete record.start;
    if (error) {
      record.errorType = "NetworkError";
      record.errorMessage = String(error).slice(0, 2048);
    }
    publish(record);
  }
  function message(_e, method, p) {
    if (method === "Network.requestWillBeSent") {
      if (p.redirectResponse)
        finish(p.requestId, p.timestamp, null, p.redirectResponse);
      if (!/^https?:/.test(p.request.url)) return;
      if (requests.size >= 1000) {
        const oldest = requests.keys().next().value;
        finish(oldest, p.timestamp, "Capture capacity exceeded");
      }
      requests.set(p.requestId, {
        schema: "ansight.network-request.v1",
        id: `electron-${contents.id}-${p.requestId}-${++sequence}`,
        source: "electron.cdp",
        startedAtUtc: new Date(p.wallTime * 1000).toISOString(),
        method: p.request.method,
        url: cleanUrl(p.request.url),
        redactSensitiveData: true,
        requestHeaders: [],
        responseHeaders: [],
        start: p.timestamp,
      });
    } else if (method === "Network.responseReceived") {
      const r = requests.get(p.requestId);
      if (r) {
        r.statusCode = p.response.status;
        r.protocol = p.response.protocol;
      }
    } else if (method === "Network.dataReceived") {
      const r = requests.get(p.requestId);
      if (r)
        r.responseBodySizeBytes =
          (r.responseBodySizeBytes || 0) + Math.max(0, p.dataLength || 0);
    } else if (method === "Network.loadingFinished")
      finish(p.requestId, p.timestamp);
    else if (method === "Network.loadingFailed")
      finish(p.requestId, p.timestamp, p.errorText);
  }
  function detached(_e, reason) {
    requests.clear();
    diagnostic(new Error(`Network debugger detached: ${reason}`));
  }
  try {
    api.attach("1.3");
    api.on("message", message);
    api.on("detach", detached);
    api.sendCommand("Network.enable").catch(diagnostic);
  } catch (e) {
    api.removeListener("message", message);
    api.removeListener("detach", detached);
    diagnostic(e);
    return () => {};
  }
  return () => {
    requests.clear();
    api.removeListener("message", message);
    api.removeListener("detach", detached);
    if (!contents.isDestroyed() && api.isAttached()) api.detach();
  };
}
module.exports = { attachNetwork };
