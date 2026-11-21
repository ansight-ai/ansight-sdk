const { app, BrowserWindow } = require("electron");
const http = require("node:http"),
  fs = require("node:fs"),
  path = require("node:path"),
  os = require("node:os");
const { createAnsight } = require("..");
app.setPath(
  "userData",
  fs.mkdtempSync(path.join(os.tmpdir(), "ansight-recording-fixture-")),
);
const html = `<!doctype html><meta name="viewport" content="width=device-width,initial-scale=1"><style>body{margin:0}button,input{font-size:20px;margin:12px}#scroll{height:180px;overflow:auto;border:1px solid}#inner{height:1800px;background:linear-gradient(white,lightblue)}</style>
<form id="form" onsubmit="event.preventDefault();document.getElementById('submitted').textContent=document.getElementById('name').value"><input id="name" aria-label="Name"><button id="save" type="submit">Save</button></form><div id="submitted"></div><button id="count" onclick="this.textContent=String(Number(this.textContent)+1)">0</button><input id="secret" type="password" value="fixture-secret"><div id="scroll"><div id="inner">Scrollable</div></div><a id="next" href="/next">Next</a>`;
const server = http.createServer((req, res) => {
  if (req.url.startsWith("/redirect")) {
    res.writeHead(302, { Location: "/body" });
    res.end();
  } else if (req.url.startsWith("/body")) {
    res.writeHead(200, { "content-type": "text/plain" });
    res.end("x".repeat(128));
  } else if (req.url.startsWith("/failure")) {
    res.writeHead(503);
    res.end("failure");
  } else if (req.url.startsWith("/drop")) req.socket.destroy();
  else {
    res.writeHead(200, { "content-type": "text/html" });
    res.end(req.url === "/next" ? '<h1 id="next-page">Next page</h1>' : html);
  }
});
app.whenReady().then(async () => {
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const origin = `http://127.0.0.1:${server.address().port}`;
  const sdk = createAnsight({
    appId: "com.ansight.electron.recording-fixture",
    appName: "Electron recording fixture",
    enabled: true,
    allowActions: true,
    captureNetwork: true,
    discoveryPort: Number(process.env.ANSIGHT_DISCOVERY_PORT || 47123),
    sessionJpegCapture: {
      intervalMilliseconds: 500,
      mode: process.env.ANSIGHT_CAPTURE_MODE || "screenshotAndVisualTree",
    },
  });
  const w = new BrowserWindow({
    width: 800,
    height: 650,
    focusable: false,
    webPreferences: {
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
    },
  });
  sdk.attachWindow(w);
  sdk.registerTool(
    {
      id: "fixture.control",
      policy: "write",
      argumentsSchema: {
        type: "object",
        properties: { action: { type: "string" } },
        required: ["action"],
        additionalProperties: false,
      },
    },
    async ({ action }) => {
      if (action === "emit") {
        await sdk.setSessionProperties({
          fixture: { marker: "recording-contract", number: 42, flag: true },
        });
        sdk.event("fixture.event", { value: 42 });
        sdk.log("fixture.warning", { level: 2 });
        await w.webContents.executeJavaScript(
          `console.error('fixture.console.error');fetch('/redirect?token=fixture-secret');fetch('/failure');fetch('/drop').catch(()=>{});setTimeout(()=>{throw new Error('fixture.renderer.exception')},0);void Promise.reject(new Error('fixture.renderer.rejection'));`,
        );
        await sdk.publishArtifact(
          {
            artifactId: "fixture.bytes",
            providerId: "fixture",
            name: "Known bytes",
            kind: "report",
            mimeType: "text/plain",
            fileName: "known.txt",
          },
          Buffer.from("known artifact bytes\n"),
        );
        return { emitted: true };
      }
      if (action === "hide") {
        w.hide();
        return { hidden: true };
      }
      if (action === "show") {
        w.show();
        return { hidden: false };
      }
      if (action === "zoom") {
        w.webContents.setZoomFactor(1.5);
        return { zoom: 1.5 };
      }
      if (action === "resetZoom") {
        w.webContents.setZoomFactor(1);
        return { zoom: 1 };
      }
      if (action === "reload") {
        await w.loadURL(origin);
        return { loaded: true };
      }
      if (action === "busy") {
        const until = Date.now() + 250;
        while (Date.now() < until) {}
        return { busyMs: 250 };
      }
      if (action === "state")
        return await w.webContents.executeJavaScript(
          `({value:document.getElementById('name')?.value,submitted:document.getElementById('submitted')?.textContent,count:document.getElementById('count')?.textContent,scroll:document.getElementById('scroll')?.scrollTop,focused:document.activeElement?.id,scale:visualViewport.scale,url:location.pathname,nodeAccess:typeof require})`,
        );
      if (action === "quit") {
        setTimeout(() => app.quit(), 100);
        return { quitting: true };
      }
      throw new Error("Unknown fixture action");
    },
  );
  sdk.on("diagnostic", (m) => console.error("[SDK]", m));
  await sdk.start();
  await w.loadURL(origin);
  await w.webContents.setVisualZoomLevelLimits(1, 3);
  app.on("before-quit", () => server.close());
  console.log("FIXTURE_READY", origin);
});
