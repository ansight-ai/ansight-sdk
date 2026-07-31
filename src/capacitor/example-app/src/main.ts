import { Capacitor } from "@capacitor/core";
import Ansight, {
  type AnsightArtifactProvider,
  type AnsightArtifactProviderRegistration,
  type AnsightOperationResult,
  type AnsightToolRegistration,
} from "@ansight/capacitor";

import {
  insertGeneratedOrder,
  seedHarnessFixtures,
  type HarnessFixtureSummary,
} from "./fixtures";
import {
  createScene3D,
  type Scene3DController,
  type ScenePalette,
} from "./scene3d";
import "./style.css";

type TestState = "idle" | "running" | "pass" | "fail" | "skip";

interface TestCase {
  id: string;
  group: string;
  name: string;
  description: string;
  pairing?: boolean;
  unsafe?: boolean;
  run: () => Promise<unknown> | unknown;
}

interface TestResult {
  state: TestState;
  durationMs?: number;
  value?: unknown;
  error?: string;
}

interface DomRegistration {
  ids: string[];
  ready: Promise<AnsightOperationResult[]>;
  unregister(): Promise<AnsightOperationResult[]>;
}

type HarnessTab = "runtime" | "navigation" | "data" | "tools";
type ShippingSpeed = "Standard" | "Express" | "Priority";

interface HarnessRoute {
  name: string;
  detail: string;
}

interface HarnessState {
  selectedTab: HarnessTab;
  routeStack: HarnessRoute[];
  drawerOpen: boolean;
  modalVisible: boolean;
  modalPresentations: number;
  modalDismissals: number;
  keyboardText: string;
  shippingSpeed: ShippingSpeed;
  expeditedBilling: boolean;
  quantity: number;
  scenePalette: ScenePalette;
  sceneSpeed: number;
  metricButtonTaps: number;
  eventButtonTaps: number;
  lifecycleTransitions: number;
  customToolInvocations: number;
  lastConnectionMessage: string;
  lastError: string | null;
  lastAction: string | null;
}

const appId = "ai.ansight.capacitor.harness";
const clientName = "Capacitor Harness";
const metricChannel = 201;
const results = new Map<string, TestResult>();
const logs: string[] = [];
let fixtures: HarnessFixtureSummary | undefined;
let harnessTools: AnsightToolRegistration[] = [];
let artifactRegistrations: AnsightArtifactProviderRegistration[] = [];
let domRegistration: DomRegistration | undefined;
let disposeErrors: (() => void) | undefined;
let routeSubscription: { remove(): void | Promise<void> } | undefined;
let scene3D: Scene3DController;
let runtimeSnapshot: Record<string, unknown> | null = null;

const harnessState: HarnessState = {
  selectedTab: "runtime",
  routeStack: [{ name: "Dashboard", detail: "Initial root route" }],
  drawerOpen: false,
  modalVisible: false,
  modalPresentations: 0,
  modalDismissals: 0,
  keyboardText: "Capacitor harness order draft",
  shippingSpeed: "Express",
  expeditedBilling: true,
  quantity: 2,
  scenePalette: "studio",
  sceneSpeed: 42,
  metricButtonTaps: 0,
  eventButtonTaps: 0,
  lifecycleTransitions: 0,
  customToolInvocations: 0,
  lastConnectionMessage: "Not connected",
  lastError: null,
  lastAction: null,
};

const app = document.querySelector<HTMLDivElement>("#app");
if (!app) throw new Error("App root not found.");

app.innerHTML = `
  <main class="shell">
    <header class="hero">
      <button class="icon-button" id="toggle-drawer" aria-label="Open harness drawer">☰</button>
      <div class="hero-title">
        <h1>Ansight Capacitor Harness</h1>
        <p>${appId}</p>
      </div>
      <span class="connection-pill" id="connection-pill">starting</span>
    </header>

    <section class="status-strip" aria-label="Harness status">
      <span id="status-route">tab=runtime route=Dashboard stack=1</span>
      <span id="status-runtime">session=pending tools=? metrics=0 events=0</span>
    </section>

    <section class="scene-band" aria-label="Interactive 3D validation scene" data-testid="scene-3d">
      <div class="scene-stage">
        <canvas
          id="scene-3d"
          aria-label="Interactive WebGL validation cube"
          data-testid="scene-3d-canvas"
        ></canvas>
        <div class="css-cube" id="scene-3d-fallback" aria-label="CSS 3D validation cube" hidden>
          <span class="cube-face cube-front"></span>
          <span class="cube-face cube-back"></span>
          <span class="cube-face cube-right"></span>
          <span class="cube-face cube-left"></span>
          <span class="cube-face cube-top"></span>
          <span class="cube-face cube-bottom"></span>
        </div>
        <span class="scene-hint">drag to rotate</span>
      </div>
      <div class="scene-metrics">
        <div><strong id="scene-palette-value">studio</strong><span>palette</span></div>
        <div><strong id="scene-speed-value">42</strong><span>speed</span></div>
        <div><strong id="scene-renderer-value">webgl</strong><span>renderer</span></div>
      </div>
    </section>

    <nav class="tab-bar" aria-label="Harness sections">
      <button class="tab-button selected" data-tab="runtime" aria-label="Harness tab Runtime">Runtime</button>
      <button class="tab-button" data-tab="navigation" aria-label="Harness tab Flow">Flow</button>
      <button class="tab-button" data-tab="data" aria-label="Harness tab Data">Data</button>
      <button class="tab-button" data-tab="tools" aria-label="Harness tab Tools">Tools</button>
    </nav>

    <section class="content-panel" data-panel="runtime">
      <h2>Runtime Controls</h2>
      <div class="button-grid">
        <button data-action="metric">Metric</button>
        <button data-action="event">Event</button>
        <button data-action="snapshot">Snapshot</button>
        <button data-action="session-properties">Session Props</button>
        <button data-action="clear-properties">Clear Props</button>
        <button data-action="palette">Palette</button>
      </div>
      <dl class="key-values">
        <div><dt>connection</dt><dd id="runtime-connection">Not connected</dd></div>
        <div><dt>lastAction</dt><dd id="runtime-action">&lt;none&gt;</dd></div>
        <div><dt>lastError</dt><dd id="runtime-error">&lt;none&gt;</dd></div>
      </dl>
    </section>

    <section class="content-panel" data-panel="navigation" hidden>
      <h2>Navigation</h2>
      <div class="button-grid">
        <button data-action="push">Push</button>
        <button data-action="checkout">Checkout</button>
        <button data-action="pop">Pop</button>
        <button data-action="replace">Replace</button>
        <button data-action="drawer">Drawer</button>
        <button data-action="modal">Modal</button>
      </div>
      <dl class="key-values" id="route-stack"></dl>
    </section>

    <section class="content-panel" data-panel="data" hidden>
      <h2>Data Fixtures</h2>
      <div class="button-grid">
        <button data-action="seed-fixtures">Seed</button>
        <button data-action="insert-order">Insert Order</button>
        <button data-action="quantity" id="quantity-button">Qty 2</button>
      </div>
      <label class="field-label" for="data-input">Harness text input</label>
      <input id="data-input" aria-label="Harness text input" value="Capacitor harness order draft" />
      <div class="segmented" aria-label="Shipping speed">
        <button data-speed="Standard">Standard</button>
        <button data-speed="Express" class="selected">Express</button>
        <button data-speed="Priority">Priority</button>
      </div>
      <label class="switch-row">
        <span>Expedited</span>
        <input id="expedited" type="checkbox" checked />
      </label>
      <dl class="key-values">
        <div><dt>database</dt><dd id="fixture-database">&lt;pending&gt;</dd></div>
        <div><dt>orders</dt><dd id="fixture-orders">0</dd></div>
        <div><dt>events</dt><dd id="fixture-events">0</dd></div>
        <div><dt>file</dt><dd id="fixture-file">&lt;pending&gt;</dd></div>
        <div><dt>latestOrder</dt><dd id="fixture-latest">&lt;pending&gt;</dd></div>
      </dl>
    </section>

    <section class="content-panel" data-panel="tools" hidden>
      <h2>Tool Contract</h2>
      <div class="button-grid">
        <button data-action="snapshot">Refresh</button>
        <button class="primary" id="run-safe">Run stable SDK surface</button>
        <button id="reset-results">Reset results</button>
        <button id="export-results">Export results</button>
      </div>
      <dl class="key-values">
        <div><dt>harness.state.snapshot</dt><dd>read</dd></div>
        <div><dt>harness.state.advance</dt><dd>write</dd></div>
        <div><dt>harness.validation.expectations</dt><dd>read</dd></div>
        <div><dt>dom.*</dt><dd>Capacitor WebView inspection + actions</dd></div>
        <div><dt>ui/files/prefs/secure/data/reflect</dt><dd>standard native suites</dd></div>
        <div><dt>customInvocations</dt><dd id="custom-invocations">0</dd></div>
        <div><dt>registeredTools</dt><dd id="registered-tools">?</dd></div>
      </dl>

      <details class="tool-details">
        <summary>Pairing configuration</summary>
        <textarea id="pairing-json" aria-label="Pairing JSON" placeholder="Paste an Ansight pairing JSON document."></textarea>
        <div class="pairing-actions">
          <button id="scan-pairing-qr" class="primary">Scan QR</button>
          <button id="save-pairing">Save locally</button>
          <button id="clear-pairing" class="danger">Clear local value</button>
        </div>
      </details>

      <details class="tool-details">
        <summary>DOM action sandbox</summary>
        <input id="dom-input" aria-label="DOM test input" value="Capacitor DOM inspection target" />
        <div class="sandbox">
          <button id="dom-action" aria-label="DOM action target">Action target</button>
          <output id="dom-output">Not clicked</output>
        </div>
      </details>

      <details class="tool-details">
        <summary>SDK diagnostics — manual, stateful probes are marked</summary>
        <section class="summary" aria-label="Test summary">
          <div><strong id="summary-total">0</strong><span>Total</span></div>
          <div><strong id="summary-pass">0</strong><span>Passed</span></div>
          <div><strong id="summary-fail">0</strong><span>Failed</span></div>
          <div><strong id="summary-skip">0</strong><span>Skipped</span></div>
          <div><strong id="summary-time">0 ms</strong><span>Runtime</span></div>
        </section>
        <section class="grid" id="tests"></section>
      </details>

      <details class="tool-details">
        <summary>Harness log</summary>
        <div id="log" class="log">Ready.</div>
      </details>
    </section>

    <aside class="drawer" id="drawer" aria-label="Harness drawer" hidden>
      <div class="drawer-header"><strong>Routes</strong><button data-action="drawer" aria-label="Close harness drawer">×</button></div>
      <button data-route="Dashboard">Dashboard</button>
      <button data-route="Orders">Orders</button>
      <button data-route="Profile">Profile</button>
      <button data-route="Settings">Settings</button>
    </aside>
    <div class="drawer-scrim" id="drawer-scrim" data-action="drawer" hidden></div>

    <div class="modal-scrim" id="modal" role="dialog" aria-modal="true" aria-label="Modal Capture" hidden>
      <section class="modal-surface">
        <p class="eyebrow">CAPACITOR MODAL FIXTURE</p>
        <h2>Modal Capture</h2>
        <p id="modal-counts">presentations=0 dismissals=0</p>
        <div class="button-grid">
          <button data-action="modal-push">Push Route</button>
          <button class="primary" data-action="close-modal">Close</button>
        </div>
      </section>
    </div>
  </main>
`;

const pairingInput =
  document.querySelector<HTMLTextAreaElement>("#pairing-json")!;
const pairingJsonStorageKey = "ansight.pairingJson";
const pairingSourceStorageKey = "ansight.pairingSource";
const qrPairingSource = "native-qr";
const storedPairingSource = "native-stored";
const documentPairingSource = "document";
pairingInput.value = localStorage.getItem(pairingJsonStorageKey) ?? "";
scene3D = createScene3D(
  document.querySelector<HTMLCanvasElement>("#scene-3d")!,
  document.querySelector<HTMLElement>("#scene-3d-fallback")!,
  harnessState.scenePalette,
  harnessState.sceneSpeed,
);

function pairingJson(): string {
  return pairingInput.value.trim();
}

function prefersNativeSavedPairing(): boolean {
  const source = localStorage.getItem(pairingSourceStorageKey);
  return source === qrPairingSource || source === storedPairingSource;
}

function pairingConfigured(): boolean {
  return Boolean(pairingJson()) || prefersNativeSavedPairing();
}

function pairingOrThrow(): string {
  const value = pairingJson();
  if (!value) throw new Error("Pairing JSON is not configured.");
  return value;
}

function log(message: string, value?: unknown): void {
  const line = `${new Date().toISOString()}  ${message}${
    value === undefined ? "" : `\n${format(value)}`
  }`;
  logs.unshift(line);
  logs.splice(150);
  localStorage.setItem("ansight.harnessLogs", JSON.stringify(logs));
  document.querySelector<HTMLDivElement>("#log")!.textContent =
    logs.join("\n\n");
}

function format(value: unknown): string {
  if (typeof value === "string") return value;
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

function asObject(value: unknown): Record<string, unknown> {
  return value && typeof value === "object"
    ? (value as Record<string, unknown>)
    : {};
}

function updateFixtureUi(): void {
  document.querySelector("#fixture-database")!.textContent =
    fixtures?.databasePath ?? "<pending>";
  document.querySelector("#fixture-orders")!.textContent = String(
    fixtures?.databaseOrderCount ?? 0,
  );
  document.querySelector("#fixture-events")!.textContent = String(
    fixtures?.databaseEventCount ?? 0,
  );
  document.querySelector("#fixture-file")!.textContent =
    fixtures?.dataFile.uri ?? "<pending>";
  document.querySelector("#fixture-latest")!.textContent =
    fixtures?.latestOrder ?? "<pending>";
}

function renderHarnessState(): void {
  document.querySelectorAll<HTMLElement>("[data-panel]").forEach((panel) => {
    panel.hidden = panel.dataset.panel !== harnessState.selectedTab;
  });
  document.querySelectorAll<HTMLButtonElement>("[data-tab]").forEach((tab) => {
    const selected = tab.dataset.tab === harnessState.selectedTab;
    tab.classList.toggle("selected", selected);
    tab.setAttribute("aria-selected", String(selected));
  });

  const selectedRoute =
    harnessState.routeStack[harnessState.routeStack.length - 1];
  document.querySelector("#status-route")!.textContent =
    `tab=${harnessState.selectedTab} route=${selectedRoute?.name ?? "Dashboard"} stack=${harnessState.routeStack.length}`;
  document.querySelector("#status-runtime")!.textContent =
    `session=${String(runtimeSnapshot?.sessionOpen ?? "pending")} tools=${String(runtimeSnapshot?.registeredTools ?? "?")} metrics=${harnessState.metricButtonTaps} events=${harnessState.eventButtonTaps}`;
  document.querySelector("#runtime-connection")!.textContent =
    harnessState.lastConnectionMessage;
  document.querySelector("#runtime-action")!.textContent =
    harnessState.lastAction ?? "<none>";
  document.querySelector("#runtime-error")!.textContent =
    harnessState.lastError ?? "<none>";

  document.querySelector("#route-stack")!.innerHTML = harnessState.routeStack
    .map(
      (route, index) =>
        `<div><dt>${index + 1}. ${escapeHtml(route.name)}</dt><dd>${escapeHtml(route.detail)}</dd></div>`,
    )
    .join("");
  document.querySelector("#quantity-button")!.textContent =
    `Qty ${harnessState.quantity}`;
  document.querySelector<HTMLInputElement>("#data-input")!.value =
    harnessState.keyboardText;
  document.querySelector<HTMLInputElement>("#expedited")!.checked =
    harnessState.expeditedBilling;
  document
    .querySelectorAll<HTMLButtonElement>("[data-speed]")
    .forEach((button) =>
      button.classList.toggle(
        "selected",
        button.dataset.speed === harnessState.shippingSpeed,
      ),
    );

  document.querySelector("#scene-palette-value")!.textContent =
    harnessState.scenePalette;
  document.querySelector("#scene-speed-value")!.textContent = String(
    harnessState.sceneSpeed,
  );
  document.querySelector("#scene-renderer-value")!.textContent =
    scene3D.getState().renderer;
  document.querySelector("#custom-invocations")!.textContent = String(
    harnessState.customToolInvocations,
  );
  document.querySelector("#registered-tools")!.textContent = String(
    runtimeSnapshot?.registeredTools ?? "?",
  );

  const drawer = document.querySelector<HTMLElement>("#drawer")!;
  const drawerScrim = document.querySelector<HTMLElement>("#drawer-scrim")!;
  drawer.hidden = !harnessState.drawerOpen;
  drawerScrim.hidden = !harnessState.drawerOpen;
  document.querySelector("#toggle-drawer")!.textContent =
    harnessState.drawerOpen ? "×" : "☰";

  const modal = document.querySelector<HTMLElement>("#modal")!;
  modal.hidden = !harnessState.modalVisible;
  document.querySelector("#modal-counts")!.textContent =
    `presentations=${harnessState.modalPresentations} dismissals=${harnessState.modalDismissals}`;
  updateFixtureUi();
}

async function refreshRuntimeSnapshot(
  action = "runtime.snapshot",
): Promise<void> {
  runtimeSnapshot = asObject(await Ansight.snapshot());
  harnessState.lastAction = action;
  renderHarnessState();
}

async function selectTab(tab: HarnessTab): Promise<void> {
  harnessState.selectedTab = tab;
  harnessState.lastAction = `tab.${tab}`;
  renderHarnessState();
  await Ansight.screenViewed(`Harness.${tab}`, { source: "tab" });
}

function pushRoute(name = "Details"): void {
  harnessState.routeStack.push({
    name,
    detail: `Pushed at ${new Date().toLocaleTimeString()}`,
  });
  harnessState.lastAction = "navigation.push";
  renderHarnessState();
  void Ansight.trackRoute(`Harness.${name}`, { source: "push" });
}

function closeModal(): void {
  harnessState.modalVisible = false;
  harnessState.modalDismissals += 1;
  harnessState.lastAction = "modal.close";
  renderHarnessState();
}

function cyclePalette(): void {
  harnessState.scenePalette =
    harnessState.scenePalette === "studio"
      ? "thermal"
      : harnessState.scenePalette === "thermal"
        ? "mono"
        : "studio";
  harnessState.lastAction = "scene.palette";
  scene3D.setPalette(harnessState.scenePalette);
  renderHarnessState();
}

async function seedFixturesFromUi(action = "fixtures.seed"): Promise<void> {
  fixtures = await seedHarnessFixtures();
  harnessState.lastAction = action;
  renderHarnessState();
  await Ansight.event({
    label: "capacitor-harness.fixtures.seed",
    type: "Event",
    channel: metricChannel,
  });
}

async function performHarnessAction(action: string): Promise<void> {
  try {
    switch (action) {
      case "metric":
        await Ansight.recordMetric(
          42 + harnessState.metricButtonTaps,
          metricChannel,
        );
        harnessState.metricButtonTaps += 1;
        harnessState.lastAction = "metric.record";
        break;
      case "event":
        await Ansight.recordEvent({
          label: "Capacitor harness event",
          type: "Event",
          channel: metricChannel,
          details: JSON.stringify({
            tab: harnessState.selectedTab,
            route: harnessState.routeStack.at(-1)?.name,
          }),
        });
        harnessState.eventButtonTaps += 1;
        harnessState.lastAction = "event.record";
        break;
      case "snapshot":
        await refreshRuntimeSnapshot();
        return;
      case "session-properties":
        await Ansight.updateSessionProperties({
          harness: {
            tab: harnessState.selectedTab,
            route: harnessState.routeStack.at(-1)?.name ?? "Dashboard",
            stackDepth: String(harnessState.routeStack.length),
          },
          scene: {
            palette: harnessState.scenePalette,
            speed: String(harnessState.sceneSpeed),
            renderer: scene3D.getState().renderer,
          },
        });
        harnessState.lastAction = "session.properties.update";
        break;
      case "clear-properties":
        await Ansight.clearSessionProperties();
        harnessState.lastAction = "session.properties.clear";
        break;
      case "palette":
        cyclePalette();
        return;
      case "push":
        pushRoute("Details");
        return;
      case "checkout":
        pushRoute("Checkout");
        return;
      case "pop":
        if (harnessState.routeStack.length > 1) harnessState.routeStack.pop();
        harnessState.lastAction = "navigation.pop";
        break;
      case "replace":
        harnessState.routeStack.splice(-1, 1, {
          name: "Settings",
          detail: `Replaced at ${new Date().toLocaleTimeString()}`,
        });
        harnessState.lastAction = "navigation.replace";
        break;
      case "drawer":
        harnessState.drawerOpen = !harnessState.drawerOpen;
        harnessState.lastAction = "drawer.toggle";
        break;
      case "modal":
        harnessState.modalVisible = true;
        harnessState.modalPresentations += 1;
        harnessState.lastAction = "modal.show";
        break;
      case "close-modal":
        closeModal();
        return;
      case "modal-push":
        pushRoute("Modal Result");
        return;
      case "seed-fixtures":
        await seedFixturesFromUi();
        return;
      case "insert-order": {
        const inserted = await insertGeneratedOrder();
        if (fixtures) Object.assign(fixtures, inserted.summary);
        harnessState.lastAction = "database.insert";
        log(`Inserted ${inserted.label}.`);
        break;
      }
      case "quantity":
        harnessState.quantity =
          harnessState.quantity >= 5 ? 1 : harnessState.quantity + 1;
        harnessState.lastAction = "quantity.change";
        break;
      default:
        throw new Error(`Unknown harness action '${action}'.`);
    }
    renderHarnessState();
  } catch (error) {
    harnessState.lastError =
      error instanceof Error ? error.message : String(error);
    renderHarnessState();
    log(`Harness action '${action}' failed.`, harnessState.lastError);
  }
}

function requireSuccess<T>(value: T): T {
  if (
    value &&
    typeof value === "object" &&
    "success" in value &&
    (value as unknown as AnsightOperationResult).success === false
  ) {
    throw new Error((value as unknown as AnsightOperationResult).message);
  }
  return value;
}

function expectedRemoteContract() {
  return {
    appId,
    framework: "capacitor",
    standardToolPrefixes: [
      "ui.",
      "files.",
      "prefs.",
      "secure.",
      "data.",
      "reflect.",
    ],
    requiredStandardTools: [
      "ui.get_visual_tree",
      "ui.get_screenshot",
      "files.list_directory",
      "files.read_file",
      "prefs.list_keys",
      "prefs.get_value",
      "prefs.set_value",
      "secure.get_value",
      "secure.set_value",
      "data.list_databases",
      "data.describe_schema",
      "data.query",
      "reflect.list_roots",
    ],
    requiredJavaScriptTools: [
      "harness.echo",
      "harness.state.snapshot",
      "harness.state.advance",
      "harness.validation.expectations",
      "harness.sdk.surface",
      "harness.scene3d.snapshot",
      "dom.get_document",
      "dom.inspect_node",
      "dom.query_selector",
      "dom.invoke_action",
      "artifacts.query",
      "artifacts.request",
    ],
    secureStorage: {
      allowedKey: "ansight.harness.secret",
      value: "capacitor-secure-fixture",
    },
    coreBehavior: {
      tabs: ["runtime", "navigation", "data", "tools"],
      navigation: ["push", "pop", "replace", "drawer", "modal"],
      data: ["seed_fixtures", "insert_order"],
      scene: scene3D.getState(),
      startup:
        "single initialization and explicit connection; no autorun suite",
    },
    fixtures,
  };
}

const stateArtifactProvider: AnsightArtifactProvider = {
  descriptor: {
    id: "harness.artifacts",
    name: "Capacitor harness artifacts",
    category: "Harness",
    tags: ["capacitor", "validation"],
  },
  query: () => [
    {
      id: "state",
      name: "Harness state",
      description: "Current harness results as JSON.",
      kind: "diagnostic",
      category: "Harness",
      mimeType: "application/json",
      fileName: "ansight-capacitor-harness.json",
    },
    {
      id: "binary",
      name: "Binary transport probe",
      description:
        "A deterministic binary payload for websocket transfer validation.",
      kind: "diagnostic",
      category: "Harness",
      mimeType: "application/octet-stream",
      fileName: "capacitor-probe.bin",
    },
  ],
  create: ({ artifactId }) => {
    if (artifactId === "binary") {
      return {
        metadata: {
          artifactId,
          providerId: "harness.artifacts",
          name: "Binary transport probe",
          kind: "diagnostic",
          mimeType: "application/octet-stream",
          fileName: "capacitor-probe.bin",
          sizeBytes: 256,
        },
        payload: Uint8Array.from({ length: 256 }, (_, index) => index),
      };
    }
    const payload = JSON.stringify(exportPayload(), null, 2);
    return {
      metadata: {
        artifactId,
        providerId: "harness.artifacts",
        name: "Harness state",
        kind: "diagnostic",
        mimeType: "application/json",
        fileName: "ansight-capacitor-harness.json",
        sizeBytes: payload.length,
      },
      payload,
    };
  },
};

const fixtureArtifactProvider: AnsightArtifactProvider = {
  descriptor: {
    id: "harness.fixtures",
    name: "Capacitor native fixtures",
    category: "Harness",
    tags: ["filesystem", "preferences", "sqlite"],
  },
  query: () => [
    {
      id: "manifest",
      name: "Native fixture manifest",
      description: "Paths and keys seeded for native remote-tool validation.",
      kind: "manifest",
      category: "Harness",
      mimeType: "application/json",
      fileName: "capacitor-native-fixtures.json",
    },
  ],
  create: ({ artifactId }) => {
    const payload = JSON.stringify(
      { artifactId, ...expectedRemoteContract() },
      null,
      2,
    );
    return {
      metadata: {
        artifactId,
        providerId: "harness.fixtures",
        name: "Native fixture manifest",
        kind: "manifest",
        mimeType: "application/json",
        fileName: "capacitor-native-fixtures.json",
        sizeBytes: payload.length,
      },
      payload,
    };
  },
};

function createHarnessOptions() {
  const builder = Ansight.createOptionsBuilder()
    .withAnsightSdk((options) => {
      options
        .withBatteryLevel()
        .withDefaultMemoryChannels({
          managedHeap: true,
          physicalFootprint: true,
          residentSetSize: true,
          javaHeap: true,
          nativeHeap: true,
          rss: true,
        })
        .withAdditionalChannels([
          {
            id: metricChannel,
            name: "Capacitor harness latency",
            unit: "ms",
            type: "timing",
            source: "capacitor",
            group: "Harness",
            colorHex: "#5b8cff",
          },
        ])
        .withLifecycleCapture({
          captureAppLifecycle: true,
          captureScreenViews: true,
          minimumScreenViewIntervalMilliseconds: 50,
        });
    })
    .withVisualTreeTools()
    .withFileSystemTools()
    .withDatabaseTools({ includePlatformRoots: true })
    .withPreferencesTools({
      allowedKeys: [
        "ansight.harness.mode",
        "ansight.harness.seededAtUtc",
        "ansight.harness.launchCount",
      ],
      allowedKeyPrefixes: ["ansight.harness."],
    })
    .withReflectionTools({ includeBuiltInRoots: true })
    .withSecureStorage({
      preferencesName: "ansight_capacitor_harness_secure",
      allowedKeys: ["ansight.harness.secret"],
      allowedPrefixes: ["ansight.harness."],
    })
    .withDomTools({ allowActions: true })
    .withErrorCapture({
      errors: true,
      unhandledRejections: true,
      consoleErrors: false,
    })
    .withHostConnectionProfileRetentionSeconds(3600)
    .withSessionJpegCapture(700, 70, 960, true)
    .withTouchCapture({
      captureMoveEvents: true,
      captureCancelEvents: true,
      moveCaptureDistanceThreshold: 8,
      moveCaptureFramesPerSecond: 20,
    })
    .registerCustomProperty("harness", "framework", "capacitor")
    .registerCustomProperty("harness", "appId", appId)
    .registerCustomProperty("harness", "platform", Capacitor.getPlatform());

  return builder.build();
}

async function ensureHarnessTools(): Promise<string[]> {
  await Promise.all(
    harnessTools.map(({ unregister }) => unregister().catch(() => undefined)),
  );
  harnessTools = [
    Ansight.registerTool(
      {
        id: "harness.echo",
        name: "Capacitor harness echo",
        description: "Echoes arguments through the JavaScript reverse bridge.",
        category: "Harness",
        scope: "read",
        timeoutMilliseconds: 5000,
      },
      async (args, context) => ({
        success: true,
        message: "Echoed by Capacitor JavaScript.",
        result: {
          args,
          requestId: context.requestId,
          sessionId: context.sessionId,
          platform: context.platform,
        },
      }),
    ),
    Ansight.registerTool(
      {
        id: "harness.state.snapshot",
        name: "Read Capacitor harness state",
        description: "Returns test, fixture, platform, and connection state.",
        category: "Harness",
        scope: "read",
      },
      async () => ({
        success: true,
        message: "Harness state captured.",
        result: {
          ...harnessState,
          fixtures,
          runtimeSnapshot,
          scene3D: scene3D.getState(),
          diagnostics: {
            testCount: tests.length,
            results: Object.fromEntries(results),
          },
          connection: await Ansight.hostConnectionStatus(),
        },
      }),
    ),
    Ansight.registerTool(
      {
        id: "harness.state.advance",
        name: "Advance Capacitor harness state",
        description:
          "Mutates navigation, tabs, modal, drawer, scene, or fixture state.",
        category: "Harness",
        scope: "write",
        argumentsSchema: {
          type: "object",
          properties: {
            action: {
              type: "string",
              enum: [
                "push",
                "pop",
                "tab_runtime",
                "tab_navigation",
                "tab_data",
                "tab_tools",
                "modal",
                "drawer",
                "palette",
                "seed_fixtures",
                "insert_order",
              ],
            },
          },
          required: ["action"],
        },
      },
      async (args) => {
        const action = String(args.action || "push");
        harnessState.customToolInvocations += 1;
        if (action.startsWith("tab_")) {
          const tab = action.slice(4) as HarnessTab;
          await selectTab(tab);
        } else {
          const localAction =
            action === "seed_fixtures"
              ? "seed-fixtures"
              : action === "insert_order"
                ? "insert-order"
                : action;
          await performHarnessAction(localAction);
        }
        harnessState.lastAction = action;
        renderHarnessState();
        return {
          success: true,
          message: `Applied ${action}.`,
          result: {
            ...harnessState,
            fixtures,
            scene3D: scene3D.getState(),
          },
        };
      },
    ),
    Ansight.registerTool(
      {
        id: "harness.validation.expectations",
        name: "Read Capacitor validation contract",
        description:
          "Returns required native/JavaScript tools and fixture locations.",
        category: "Harness",
        scope: "read",
      },
      () => ({ success: true, result: expectedRemoteContract() }),
    ),
    Ansight.registerTool(
      {
        id: "harness.sdk.surface",
        name: "Capacitor SDK Surface Probe",
        description:
          "Exercises stable, non-destructive Capacitor SDK bridge methods.",
        category: "Harness",
        scope: "write",
      },
      async () => {
        const registerResult = await Ansight.registerCustomProperty(
          "capacitorHarness",
          "surfaceProbe",
          "registered",
        );
        const removeResult = await Ansight.removeCustomProperty(
          "capacitorHarness",
          "surfaceProbe",
        );
        const currentOptions = await Ansight.currentOptions();
        const telemetrySample = await Ansight.captureBuiltInTelemetrySample();
        const screenFrame = await Ansight.captureScreenFrame({
          quality: 50,
          maxWidth: 640,
        });
        const disableTouch = await Ansight.disableTouchCapture();
        const enableTouch = await Ansight.enableTouchCapture();
        const recordedMetrics = await Ansight.recordedMetrics(5);
        const recordedEvents = await Ansight.recordedEvents(5);
        runtimeSnapshot = asObject(await Ansight.snapshot());
        harnessState.customToolInvocations += 1;
        harnessState.lastAction = "sdk.surface";
        renderHarnessState();
        return {
          success: true,
          message:
            "Capacitor SDK surface probe completed without resetting the session.",
          result: {
            registerCustomProperty: registerResult,
            removeCustomProperty: removeResult,
            currentOptions: {
              hasOptions: Boolean(currentOptions),
              hasLegacyPairingConfigJson: Boolean(
                currentOptions && "pairingConfigJson" in currentOptions,
              ),
            },
            telemetrySample,
            screenFrame,
            disableTouch,
            enableTouch,
            recordedTelemetry: {
              metrics: recordedMetrics.length,
              events: recordedEvents.length,
            },
            snapshot: runtimeSnapshot,
            functions: Object.keys(Ansight)
              .filter(
                (key) =>
                  typeof Ansight[key as keyof typeof Ansight] === "function",
              )
              .sort(),
          },
        };
      },
    ),
    Ansight.registerTool(
      {
        id: "harness.scene3d.snapshot",
        name: "Read Capacitor 3D Scene",
        description:
          "Returns WebGL renderer, palette, speed, rotation, and frame state.",
        category: "Harness",
        scope: "read",
      },
      () => ({
        success: true,
        message: "3D scene state captured.",
        result: scene3D.getState(),
      }),
    ),
  ];
  await Promise.all(
    harnessTools.map(({ ready }) => ready.then(requireSuccess)),
  );
  return harnessTools.map(({ id }) => id);
}

async function ensureDomTools(): Promise<string[]> {
  await Ansight.uninstallDomTools();
  domRegistration = Ansight.installDomTools({
    source: "capacitor-harness",
    includeText: true,
    includeAttributes: true,
    allowActions: true,
  });
  await domRegistration.ready;
  return domRegistration.ids;
}

async function ensureArtifactProviders(): Promise<string[]> {
  await Ansight.clearArtifactProviders();
  artifactRegistrations = Ansight.registerArtifactProviders([
    stateArtifactProvider,
    fixtureArtifactProvider,
  ]);
  await Promise.all(
    artifactRegistrations.map(({ ready }) => ready.then(requireSuccess)),
  );
  return Ansight.listRegisteredArtifactProviders().map(({ id }) => id);
}

async function ensureJavaScriptSurface(): Promise<void> {
  await ensureHarnessTools();
  await ensureDomTools();
  await ensureArtifactProviders();
}

async function ensureLiveConnection(): Promise<unknown> {
  const pairingPayload = pairingJson();
  if (!pairingPayload || prefersNativeSavedPairing()) {
    return requireSuccess(
      await Ansight.connect(undefined, {
        clientName,
        expectedAppId: appId,
      }),
    );
  }

  return requireSuccess(
    await Ansight.connect(pairingPayload, {
      clientName,
      expectedAppId: appId,
    }),
  );
}

async function restoreHarness(connectWhenConfigured = true): Promise<unknown> {
  const initialized = requireSuccess(
    await Ansight.initializeAndActivate(
      createHarnessOptions(),
    ),
  );
  const initialConnectionStatus = asObject(
    await Ansight.hostConnectionStatus(),
  );
  if (
    !localStorage.getItem(pairingSourceStorageKey) &&
    (Boolean(initialConnectionStatus.hasSavedConfig) ||
      Boolean(initialConnectionStatus.hasCachedSession))
  ) {
    localStorage.setItem(pairingSourceStorageKey, storedPairingSource);
  }
  if (
    prefersNativeSavedPairing() &&
    !Boolean(initialConnectionStatus.hasSavedConfig) &&
    !Boolean(initialConnectionStatus.hasCachedSession)
  ) {
    localStorage.removeItem(pairingSourceStorageKey);
  }
  fixtures = await seedHarnessFixtures();
  await ensureJavaScriptSurface();
  await Ansight.updateSessionProperties({
    harness: {
      framework: "capacitor",
      appId,
      platform: Capacitor.getPlatform(),
      fixtureSeededAtUtc: fixtures.seededAtUtc,
    },
    scene: {
      palette: harnessState.scenePalette,
      speed: String(harnessState.sceneSpeed),
      renderer: scene3D.getState().renderer,
    },
  });
  if (connectWhenConfigured) {
    try {
      const connection = asObject(await ensureLiveConnection());
      harnessState.lastConnectionMessage = `success=${String(connection.success ?? true)} ${String(connection.message ?? "Connected")}`;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      harnessState.lastConnectionMessage = message;
      log("Runtime enrollment is waiting for Studio.", message);
    }
  }
  await Ansight.screenViewed("Harness.Runtime", {
    source: "bootstrap.connected",
    platform: Capacitor.getPlatform(),
  });
  await Ansight.sendClientLog("capacitor-harness bootstrap complete");
  runtimeSnapshot = asObject(await Ansight.snapshot());
  harnessState.lastAction = "runtime.connect";
  renderHarnessState();
  return initialized;
}

const tests: TestCase[] = [
  {
    id: "interactive-3d-scene",
    group: "Harness UI",
    name: "Interactive 3D scene",
    description:
      "Validates the inspectable canvas fixture and active WebGL or CSS 3D renderer.",
    run: async () => {
      await new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
      );
      const canvas = document.querySelector<HTMLCanvasElement>("#scene-3d");
      const state = scene3D.getState();
      assert(canvas, "3D scene canvas was not found.");
      assert(
        canvas.getAttribute("data-testid") === "scene-3d-canvas",
        "3D scene does not expose its stable DOM inspection target.",
      );
      assert(
        state.renderer === "css-fallback" || state.frameCount > 0,
        "The WebGL renderer did not draw a frame.",
      );
      return state;
    },
  },
  {
    id: "builder-defaults",
    group: "Options builder",
    name: "Developer defaults",
    description:
      "Validates the cross-SDK all-in-one defaults and isolated build snapshots.",
    run: () => {
      const builder = Ansight.createOptionsBuilder()
        .withAnsightDefaults()
        .registerCustomProperty("harness", "value", "original");
      const first = builder.build();
      first.customProperties!.harness.value = "mutated";
      const second = builder.build();
      assert(
        second.useNativeAllInOneDefaults,
        "Native defaults were not enabled.",
      );
      assert(
        second.toolGuard === "readOnly",
        "Expected read-only default tool guard.",
      );
      assert(
        second.customProperties?.harness.value === "original",
        "Builder snapshot was not isolated.",
      );
      return second;
    },
  },
  {
    id: "builder-capture",
    group: "Options builder",
    name: "Capture overloads and toggles",
    description:
      "Covers memory exclusions, numeric JPEG overload, FPS, touch, and lifecycle options.",
    run: () => {
      const options = Ansight.createOptionsBuilder()
        .withDefaultMemoryChannels({ managedHeap: true, rss: true })
        .withoutDefaultMemoryChannels({ managedHeap: true })
        .withSessionJpegCapture(1200, 75, null, false)
        .withoutFramesPerSecond()
        .withoutTouchCapture()
        .withLifecycleCapture({ captureAppLifecycle: true })
        .build();
      assert(
        options.defaultMemoryChannels?.managedHeap === false,
        "Memory exclusion failed.",
      );
      assert(
        options.sessionJpegCapture !== false,
        "JPEG capture overload failed.",
      );
      assert(
        options.sessionJpegCapture?.maxWidth === null,
        "Nullable JPEG width was lost.",
      );
      return options;
    },
  },
  {
    id: "builder-host",
    group: "Options builder",
    name: "Host connection helpers",
    description:
      "Covers configure, bundled config, discovery port, retention, and auto-probe helpers.",
    run: () => {
      const options = Ansight.createOptionsBuilder()
        .withHostConnection({ savedConfigKey: "harness" })
        .configureHostConnection((host) => {
          host.discoveryPort = 4567;
        })
        .withBundledHostConnection({ bundledConfigJson: '{"kind":"test"}' })
        .withHostConnectionProfileRetentionSeconds(90)
        .withoutHostAutoProbe()
        .build();
      assert(
        options.hostConnection?.discoveryPort === 4567,
        "Discovery port was not retained.",
      );
      assert(
        options.hostConnection?.connectionProfileRetentionSeconds === 90,
        "Profile retention was not retained.",
      );
      return options;
    },
  },
  {
    id: "builder-tools",
    group: "Options builder",
    name: "Tool access matrix",
    description:
      "Covers guard modes, visual-tree enable/disable, and every native tool option.",
    run: () => {
      const options = Ansight.createOptionsBuilder()
        .withToolsDisabled()
        .withReadOnlyToolAccess()
        .withReadWriteToolAccess()
        .withAllToolAccess()
        .withVisualTreeTools()
        .withoutVisualTreeTools()
        .withFileSystemTools()
        .withDatabaseTools({ includePlatformRoots: true })
        .withPreferencesTools({ allowedKeyPrefixes: ["ansight.harness."] })
        .withReflectionTools({ includeBuiltInRoots: true })
        .withSecureStorage({ allowedKeys: ["ansight.harness.secret"] })
        .build();
      assert(
        options.toolGuard === "fullAccess",
        "Full tool access was not selected.",
      );
      assert(
        options.remoteTools?.visualTree === false,
        "Visual-tree disable was not retained.",
      );
      return options;
    },
  },
  {
    id: "initialize",
    group: "Runtime",
    name: "Initialize then activate",
    description:
      "Exercises the two-step lifecycle and restores the complete harness surface.",
    unsafe: true,
    run: async () => {
      const initialized = await Ansight.initialize(
        createHarnessOptions(),
      );
      assert(
        initialized.active === false,
        "initialize unexpectedly activated the runtime.",
      );
      const activated = await Ansight.activate();
      assert(activated.active, "activate did not activate the runtime.");
      await ensureJavaScriptSurface();
      if (pairingConfigured()) await ensureLiveConnection();
      return { initialized, activated };
    },
  },
  {
    id: "initialize-and-activate",
    group: "Runtime",
    name: "Initialize and activate",
    description:
      "Rebuilds the all-in-one runtime in one operation, including JS adapters.",
    unsafe: true,
    run: () => restoreHarness(),
  },
  {
    id: "status",
    group: "Runtime",
    name: "Status",
    description:
      "Reads runtime, telemetry, capture, tools, and connection state.",
    run: () => Ansight.status(),
  },
  {
    id: "snapshot",
    group: "Runtime",
    name: "Debug snapshot alias",
    description:
      "Reads the full native debug snapshot through the dedicated alias.",
    run: () => Ansight.snapshot(),
  },
  {
    id: "current-options",
    group: "Runtime",
    name: "Current options",
    description: "Round-trips the effective native configuration.",
    run: async () => {
      const options = await Ansight.currentOptions();
      assert(
        options.toolGuard === "fullAccess",
        "Native full-access option was not applied.",
      );
      return options;
    },
  },
  {
    id: "deactivate-activate",
    group: "Runtime",
    name: "Deactivate + activate",
    description: "Toggles runtime activity and restores the paired connection.",
    unsafe: true,
    run: async () => {
      const inactive = await Ansight.deactivate();
      assert(!inactive.active, "Runtime remained active after deactivate.");
      const active = await Ansight.activate();
      assert(active.active, "Runtime did not reactivate.");
      Ansight.startAppStateTracking();
      if (pairingConfigured()) await ensureLiveConnection();
      return { inactive, active };
    },
  },
  {
    id: "clear-runtime",
    group: "Runtime",
    name: "Clear + restore",
    description:
      "Clears retained SDK state, then rebuilds fixtures, tools, properties, and connection.",
    unsafe: true,
    run: async () => {
      const cleared = await Ansight.clear();
      const restored = await restoreHarness();
      return { cleared, restored };
    },
  },
  {
    id: "app-state-tracking",
    group: "Runtime",
    name: "App-state tracking aliases",
    description:
      "Exercises both lifecycle-tracking and React Native-compatible app-state names.",
    run: () => {
      Ansight.stopLifecycleTracking();
      Ansight.startLifecycleTracking();
      Ansight.stopAppStateTracking();
      Ansight.startAppStateTracking();
      return { tracking: true };
    },
  },
  {
    id: "register-channel",
    group: "Telemetry",
    name: "Custom channel",
    description: "Registers a custom numeric telemetry channel.",
    run: () =>
      Ansight.registerMetricChannel({
        id: metricChannel,
        name: "Capacitor harness latency",
        unit: "ms",
        type: "timing",
        source: "capacitor",
        group: "Harness",
        colorHex: "#5b8cff",
      }),
  },
  {
    id: "metric",
    group: "Telemetry",
    name: "Metric",
    description:
      "Records a deterministic value through the concise metric API.",
    run: () => Ansight.metric(41, metricChannel),
  },
  {
    id: "record-metric",
    group: "Telemetry",
    name: "Record metric alias",
    description:
      "Records and reads back a value through the React Native-compatible alias.",
    run: async () => {
      await Ansight.recordMetric(42, metricChannel);
      const values = await Ansight.recordedMetrics(20);
      assert(
        values.some(
          ({ channel, value }) => channel === metricChannel && value === 42,
        ),
        "Recorded metric was not returned.",
      );
      return values;
    },
  },
  {
    id: "event",
    group: "Telemetry",
    name: "Event",
    description: "Records both string and structured events.",
    run: async () => {
      await Ansight.event("Capacitor harness string event");
      return Ansight.event({
        label: "Capacitor harness event",
        type: "Info",
        details: JSON.stringify({ framework: "capacitor", value: 42 }),
        channel: metricChannel,
      });
    },
  },
  {
    id: "record-event",
    group: "Telemetry",
    name: "Record event alias",
    description:
      "Records and reads back a structured event through the compatibility alias.",
    run: async () => {
      await Ansight.recordEvent({
        label: "Capacitor harness alias event",
        type: "Diagnostic",
        channel: metricChannel,
      });
      const values = await Ansight.recordedEvents(20);
      assert(
        values.some(({ label }) => label === "Capacitor harness alias event"),
        "Recorded event was not returned.",
      );
      return values;
    },
  },
  {
    id: "screen-viewed",
    group: "Telemetry",
    name: "Screen viewed",
    description: "Records an explicit screen with structured details.",
    run: () => Ansight.screenViewed("CapacitorHarness", { source: "manual" }),
  },
  {
    id: "track-route",
    group: "Telemetry",
    name: "Track route alias",
    description: "Records a route through the React Native-compatible alias.",
    run: () => Ansight.trackRoute("/harness/parity", { source: "alias" }),
  },
  {
    id: "route-tracker",
    group: "Telemetry",
    name: "History route tracker",
    description:
      "Installs the Capacitor History API adapter and restores patched browser methods.",
    run: async () => {
      await routeSubscription?.remove();
      routeSubscription = Ansight.createRouteTracker({
        details: { source: "capacitor-harness" },
      });
      const installed = {
        route: location.pathname + location.search + location.hash,
      };
      await routeSubscription.remove();
      routeSubscription = undefined;
      return installed;
    },
  },
  {
    id: "lifecycle-event",
    group: "Telemetry",
    name: "Lifecycle event",
    description:
      "Exercises foreground/background telemetry and restores foreground.",
    run: async () => {
      await Ansight.setAppLifecycleState("background");
      return Ansight.setAppLifecycleState("foreground");
    },
  },
  {
    id: "built-in-sample",
    group: "Telemetry",
    name: "Built-in sample",
    description: "Captures memory, battery, and configured built-in telemetry.",
    run: () => Ansight.captureBuiltInTelemetrySample(),
  },
  {
    id: "recorded-telemetry",
    group: "Telemetry",
    name: "Retained telemetry",
    description: "Reads bounded metric and event retention buffers.",
    run: async () => ({
      metrics: await Ansight.recordedMetrics(10),
      events: await Ansight.recordedEvents(10),
    }),
  },
  {
    id: "fps",
    group: "Capture",
    name: "Frame-rate capture",
    description:
      "Enables, disables, verifies, and restores native FPS capture.",
    run: async () => {
      await Ansight.enableFramesPerSecond();
      assert(
        await Ansight.isFramesPerSecondEnabled(),
        "FPS capture did not enable.",
      );
      await Ansight.disableFramesPerSecond();
      assert(
        !(await Ansight.isFramesPerSecondEnabled()),
        "FPS capture did not disable.",
      );
      return Ansight.enableFramesPerSecond();
    },
  },
  {
    id: "touch",
    group: "Capture",
    name: "Touch capture",
    description:
      "Toggles native touch collection and restores configured capture.",
    run: async () => {
      requireSuccess(await Ansight.enableTouchCapture());
      requireSuccess(await Ansight.disableTouchCapture());
      return requireSuccess(await Ansight.enableTouchCapture());
    },
  },
  {
    id: "screen-frame",
    group: "Capture",
    name: "Screen frame",
    description: "Captures one JPEG frame from the native application window.",
    run: () => Ansight.captureScreenFrame({ quality: 55, maxWidth: 480 }),
  },
  {
    id: "session-properties",
    group: "Session data",
    name: "Session properties",
    description: "Updates grouped session properties.",
    run: () =>
      Ansight.updateSessionProperties({
        harness: { framework: "capacitor", platform: Capacitor.getPlatform() },
      }),
  },
  {
    id: "custom-properties-alias",
    group: "Session data",
    name: "Custom-property alias",
    description: "Updates properties using the React Native-compatible alias.",
    run: () =>
      Ansight.updateCustomProperties({ harness: { alias: "verified" } }),
  },
  {
    id: "property-lifecycle",
    group: "Session data",
    name: "Property lifecycle",
    description:
      "Registers, removes, clears, and restores custom/session property values.",
    unsafe: true,
    run: async () => {
      requireSuccess(
        await Ansight.registerCustomProperty("harness", "temporary", "true"),
      );
      requireSuccess(
        await Ansight.removeCustomProperty("harness", "temporary"),
      );
      requireSuccess(await Ansight.clearSessionProperties());
      requireSuccess(await Ansight.clearCustomProperties());
      return Ansight.updateSessionProperties({
        harness: { framework: "capacitor", restored: "true" },
      });
    },
  },
  {
    id: "client-log",
    group: "Session data",
    name: "Client log",
    description: "Sends a client log over the active pairing transport.",
    pairing: true,
    run: () => Ansight.sendClientLog("Capacitor harness client log"),
  },
  {
    id: "log-listener",
    group: "Session data",
    name: "Log-listener lifecycle",
    description: "Installs and removes a native log subscription.",
    run: () => {
      const subscription = Ansight.addLogListener(() => undefined);
      subscription.remove();
      return { subscribed: true, removed: true };
    },
  },
  {
    id: "native-fixtures",
    group: "Native tool fixtures",
    name: "Files, preferences, and SQLite",
    description:
      "Seeds app data/cache files, preference keys, and a queryable SQLite database.",
    run: async () => {
      fixtures = await seedHarnessFixtures();
      assert(
        fixtures.preferenceKeys.length >= 3,
        "Preference fixtures were not stored.",
      );
      assert(fixtures.databaseBytes > 0, "SQLite fixture is empty.");
      return fixtures;
    },
  },
  {
    id: "remote-contract",
    group: "Native tool fixtures",
    name: "Remote validation contract",
    description:
      "Publishes the native/JavaScript tool and secure-storage expectations used by Studio.",
    run: () => {
      const contract = expectedRemoteContract();
      assert(
        contract.requiredStandardTools.length >= 10,
        "Native contract is incomplete.",
      );
      return contract;
    },
  },
  {
    id: "custom-tools",
    group: "JavaScript tools",
    name: "Harness tools",
    description:
      "Registers echo, state, mutation, expectation, and SDK-surface tools.",
    run: async () => {
      const ids = await ensureHarnessTools();
      assert(ids.length === 6, "Expected six harness tools.");
      return ids;
    },
  },
  {
    id: "tool-lifecycle",
    group: "JavaScript tools",
    name: "Tool list + unregister",
    description:
      "Registers, lists, unregisters, and verifies a temporary JavaScript tool.",
    run: async () => {
      const temporary = Ansight.registerTool(
        {
          id: "harness.temporary",
          name: "Temporary harness tool",
          category: "Harness",
          scope: "read",
        },
        () => ({ success: true }),
      );
      await temporary.ready;
      assert(
        Ansight.listRegisteredTools().includes("harness.temporary"),
        "Temporary tool was not listed.",
      );
      await Ansight.unregisterTool("harness.temporary");
      assert(
        !Ansight.listRegisteredTools().includes("harness.temporary"),
        "Temporary tool remained registered.",
      );
      return Ansight.listRegisteredTools();
    },
  },
  {
    id: "clear-tools",
    group: "JavaScript tools",
    name: "Clear + restore tools",
    description:
      "Clears every JavaScript tool then restores harness, DOM, and artifact tools.",
    unsafe: true,
    run: async () => {
      requireSuccess(await Ansight.clearRegisteredTools());
      assert(
        Ansight.listRegisteredTools().length === 0,
        "Tool handlers were not cleared.",
      );
      await ensureJavaScriptSurface();
      return Ansight.listRegisteredTools();
    },
  },
  {
    id: "dom-tools",
    group: "JavaScript tools",
    name: "DOM tools",
    description:
      "Registers document, node, selector, and guarded action tools.",
    run: async () => {
      const ids = await ensureDomTools();
      assert(
        ids.includes("dom.invoke_action"),
        "DOM actions were not enabled.",
      );
      return ids;
    },
  },
  {
    id: "dom-tool-lifecycle",
    group: "JavaScript tools",
    name: "DOM uninstall + restore",
    description:
      "Exercises the React-tools-equivalent DOM registration lifecycle.",
    run: async () => {
      const removed = await Ansight.uninstallDomTools();
      const restored = await ensureDomTools();
      return { removed: removed.length, restored };
    },
  },
  {
    id: "artifact-provider",
    group: "JavaScript tools",
    name: "Artifact provider",
    description: "Registers text and binary artifact production paths.",
    run: async () => {
      await ensureArtifactProviders();
      const providers = Ansight.listRegisteredArtifactProviders();
      assert(
        providers.some(({ id }) => id === "harness.artifacts"),
        "Provider was not listed.",
      );
      return providers;
    },
  },
  {
    id: "artifact-lifecycle",
    group: "JavaScript tools",
    name: "Artifact provider lifecycle",
    description:
      "Covers register-many, unregister, list, clear, and restore operations.",
    unsafe: true,
    run: async () => {
      await ensureArtifactProviders();
      await Ansight.unregisterArtifactProvider("harness.fixtures");
      assert(
        !Ansight.listRegisteredArtifactProviders().some(
          ({ id }) => id === "harness.fixtures",
        ),
        "Fixture provider remained registered.",
      );
      requireSuccess(await Ansight.clearArtifactProviders());
      assert(
        Ansight.listRegisteredArtifactProviders().length === 0,
        "Artifact providers were not cleared.",
      );
      return ensureArtifactProviders();
    },
  },
  {
    id: "error-handlers",
    group: "JavaScript tools",
    name: "Error-handler lifecycle",
    description:
      "Installs and disposes global error, rejection, and console capture.",
    run: () => {
      disposeErrors?.();
      disposeErrors = Ansight.installErrorHandlers({
        errors: true,
        unhandledRejections: true,
        consoleErrors: true,
      });
      disposeErrors();
      disposeErrors = Ansight.installErrorHandlers();
      return { installed: true, disposed: true, restored: true };
    },
  },
  {
    id: "host-status",
    group: "Host connection",
    name: "Status + capabilities",
    description:
      "Reads host discovery, saved-config, QR/file-picker, and runtime availability.",
    run: async () => ({
      status: await Ansight.hostConnectionStatus(),
      capabilities: await Ansight.hostConnectionCapabilities(),
    }),
  },
  {
    id: "host-listener",
    group: "Host connection",
    name: "Status-listener lifecycle",
    description:
      "Subscribes to immediate and configuration-triggered host status updates.",
    run: async () => {
      let notifications = 0;
      const subscription = Ansight.addHostConnectionStatusListener(
        () => {
          notifications += 1;
        },
        { emitCurrent: true },
      );
      await Ansight.notifyHostConnectionConfigChanged();
      await new Promise((resolve) => window.setTimeout(resolve, 50));
      await subscription.remove();
      assert(notifications > 0, "Host listener did not emit.");
      return { notifications };
    },
  },
  {
    id: "config-change",
    group: "Host connection",
    name: "Configuration changed",
    description:
      "Notifies the native runtime that saved registration state changed.",
    run: () => Ansight.notifyHostConnectionConfigChanged(),
  },
  {
    id: "save-pairing",
    group: "Pairing and sessions",
    name: "Save registration payload",
    description: "Validates and saves the supplied pairing document.",
    pairing: true,
    run: () =>
      Ansight.savePairingConfig(pairingOrThrow(), { expectedAppId: appId }),
  },
  {
    id: "clear-saved-pairing",
    group: "Pairing and sessions",
    name: "Clear saved pairing",
    description: "Clears and restores saved pairing using the primary API.",
    pairing: true,
    unsafe: true,
    run: async () => {
      const cleared = requireSuccess(await Ansight.clearSavedPairing());
      const saved = requireSuccess(
        await Ansight.savePairingConfig(pairingOrThrow(), {
          expectedAppId: appId,
        }),
      );
      return { cleared, saved };
    },
  },
  {
    id: "clear-saved-pairing-alias",
    group: "Pairing and sessions",
    name: "Clear pairing alias",
    description:
      "Covers the React Native-compatible clearSavedPairingConfig alias and restores it.",
    pairing: true,
    unsafe: true,
    run: async () => {
      const cleared = requireSuccess(await Ansight.clearSavedPairingConfig());
      const saved = requireSuccess(
        await Ansight.savePairingConfig(pairingOrThrow(), {
          expectedAppId: appId,
        }),
      );
      return { cleared, saved };
    },
  },
  {
    id: "clear-cached-session",
    group: "Pairing and sessions",
    name: "Clear cached session",
    description: "Clears cached session state and restores a live connection.",
    pairing: true,
    unsafe: true,
    run: async () => {
      const cleared = requireSuccess(await Ansight.clearCachedSession());
      const connected = await ensureLiveConnection();
      return { cleared, connected };
    },
  },
  {
    id: "connect",
    group: "Pairing and sessions",
    name: "Connect",
    description:
      "Opens the host transport using the supplied pairing document.",
    pairing: true,
    run: () => ensureLiveConnection(),
  },
  {
    id: "open-session",
    group: "Pairing and sessions",
    name: "Open live session",
    description:
      "Opens a live session directly from the supplied pairing document.",
    pairing: true,
    run: () =>
      Ansight.openSession(pairingOrThrow(), {
        clientName,
        expectedAppId: appId,
      }),
  },
  {
    id: "complete-session",
    group: "Pairing and sessions",
    name: "Complete + reconnect",
    description:
      "Completes the active session, then restores a visible live connection.",
    pairing: true,
    unsafe: true,
    run: async () => {
      const completed = requireSuccess(await Ansight.completeSession());
      const connected = await ensureLiveConnection();
      return { completed, connected };
    },
  },
  {
    id: "close-session",
    group: "Pairing and sessions",
    name: "Close + reconnect",
    description:
      "Closes the active session, then restores a visible live connection.",
    pairing: true,
    unsafe: true,
    run: async () => {
      const closed = requireSuccess(await Ansight.closeSession());
      const connected = await ensureLiveConnection();
      return { closed, connected };
    },
  },
  {
    id: "disconnect",
    group: "Pairing and sessions",
    name: "Disconnect + reconnect",
    description:
      "Disconnects the transport, verifies it, then reconnects for Studio visibility.",
    pairing: true,
    unsafe: true,
    run: async () => {
      const disconnected = requireSuccess(await Ansight.disconnect());
      const status = await Ansight.hostConnectionStatus();
      assert(
        !status.isConnected,
        "Transport remained connected after disconnect.",
      );
      const connected = await ensureLiveConnection();
      return { disconnected, connected };
    },
  },
  {
    id: "live-final",
    group: "Pairing and sessions",
    name: "Final live visibility",
    description:
      "Asserts the suite ends connected and emits a final host-visible log.",
    pairing: true,
    run: async () => {
      await ensureLiveConnection();
      requireSuccess(
        await Ansight.sendClientLog("Capacitor parity suite is live."),
      );
      const status = await Ansight.hostConnectionStatus();
      assert(
        status.isConnected,
        `Final connection state is '${status.connectionState}'.`,
      );
      return status;
    },
  },
];

function renderTests(): void {
  const container = document.querySelector<HTMLDivElement>("#tests")!;
  let group = "";
  container.innerHTML = tests
    .map((test) => {
      const heading =
        group === test.group
          ? ""
          : `<h2 class="section-title">${(group = test.group)}</h2>`;
      const result = results.get(test.id) ?? { state: "idle" };
      const flags = [
        test.pairing ? "pairing" : "",
        test.unsafe ? "stateful" : "",
      ].filter(Boolean);
      return `${heading}
        <article class="test ${result.state}" data-test="${test.id}">
          <span class="state" aria-label="${result.state}"></span>
          <div>
            <h3>${test.name}${flags.length ? ` · ${flags.join(" · ")}` : ""}</h3>
            <p>${test.description}</p>
          </div>
          <button data-run="${test.id}">Run</button>
          ${
            result.value !== undefined || result.error
              ? `<pre>${escapeHtml(result.error ?? format(result.value))}</pre>`
              : ""
          }
        </article>`;
    })
    .join("");
  container
    .querySelectorAll<HTMLButtonElement>("[data-run]")
    .forEach((button) => {
      button.addEventListener("click", () => void runTest(button.dataset.run!));
    });
  renderSummary();
}

function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

async function runTest(id: string): Promise<TestResult> {
  const test = tests.find((candidate) => candidate.id === id);
  if (!test) throw new Error(`Unknown test '${id}'.`);
  if (test.pairing && !pairingJson()) {
    const result: TestResult = {
      state: "skip",
      error: "Pairing JSON is not configured.",
    };
    results.set(id, result);
    renderTests();
    return result;
  }
  results.set(id, { state: "running" });
  renderTests();
  const started = performance.now();
  try {
    const value = requireSuccess(await test.run());
    const result: TestResult = {
      state: "pass",
      durationMs: performance.now() - started,
      value,
    };
    results.set(id, result);
    log(`PASS ${test.name}`, value);
    return result;
  } catch (error) {
    const result: TestResult = {
      state: "fail",
      durationMs: performance.now() - started,
      error:
        error instanceof Error
          ? `${error.message}\n${error.stack ?? ""}`
          : String(error),
    };
    results.set(id, result);
    log(`FAIL ${test.name}`, result.error);
    return result;
  } finally {
    renderTests();
  }
}

const stableTestIds = new Set([
  "interactive-3d-scene",
  "builder-defaults",
  "builder-capture",
  "builder-host",
  "builder-tools",
  "status",
  "snapshot",
  "current-options",
  "register-channel",
  "metric",
  "record-metric",
  "event",
  "record-event",
  "screen-viewed",
  "track-route",
  "built-in-sample",
  "recorded-telemetry",
  "fps",
  "touch",
  "screen-frame",
  "session-properties",
  "custom-properties-alias",
  "native-fixtures",
  "remote-contract",
  "host-status",
]);

async function runStableSuite(): Promise<void> {
  document.querySelectorAll<HTMLButtonElement>("button").forEach((button) => {
    button.disabled = true;
  });
  try {
    for (const test of tests) {
      if (!stableTestIds.has(test.id)) continue;
      await runTest(test.id);
    }
  } finally {
    document.querySelectorAll<HTMLButtonElement>("button").forEach((button) => {
      button.disabled = false;
    });
  }
}

function renderSummary(): void {
  const values = [...results.values()];
  document.querySelector("#summary-total")!.textContent = String(tests.length);
  document.querySelector("#summary-pass")!.textContent = String(
    values.filter(({ state }) => state === "pass").length,
  );
  document.querySelector("#summary-fail")!.textContent = String(
    values.filter(({ state }) => state === "fail").length,
  );
  document.querySelector("#summary-skip")!.textContent = String(
    values.filter(({ state }) => state === "skip").length,
  );
  document.querySelector("#summary-time")!.textContent = `${Math.round(
    values.reduce((total, { durationMs = 0 }) => total + durationMs, 0),
  )} ms`;
}

function exportPayload() {
  return {
    schema: "ai.ansight.capacitor-harness.results.v2",
    generatedAtUtc: new Date().toISOString(),
    appId,
    platform: Capacitor.getPlatform(),
    nativePlatform: Capacitor.isNativePlatform(),
    pairingConfigured: pairingConfigured(),
    fixtures,
    testCount: tests.length,
    results: Object.fromEntries(results),
    logs,
  };
}

async function startHarness(): Promise<void> {
  try {
    await restoreHarness(true);
    const connection = await Ansight.hostConnectionStatus();
    log("Harness initialized.", {
      paired: pairingConfigured(),
      pairingSource:
        localStorage.getItem(pairingSourceStorageKey) ?? documentPairingSource,
      connection,
      fixtures,
      tools: Ansight.listRegisteredTools(),
    });
    log("Interactive harness ready; diagnostics run only when requested.");
  } catch (error) {
    harnessState.lastError =
      error instanceof Error ? error.message : String(error);
    renderHarnessState();
    log("Automatic harness startup failed.", harnessState.lastError);
  }
}

let savedQrReconnectPromise: Promise<void> | null = null;

function reconnectSavedQrPairing(trigger: "foreground"): Promise<void> {
  if (!prefersNativeSavedPairing() || document.visibilityState !== "visible") {
    return Promise.resolve();
  }

  savedQrReconnectPromise ??= (async () => {
    const status = asObject(await Ansight.hostConnectionStatus());
    if (Boolean(status.isConnected) || !Boolean(status.hasSavedConfig)) return;

    const connection = asObject(await ensureLiveConnection());
    harnessState.lastAction = `pairing.qr.reconnect.${trigger}`;
    harnessState.lastConnectionMessage = String(
      connection.message ?? "Reconnected from saved QR pairing.",
    );
    harnessState.lastError = null;
    renderHarnessState();
    log("Reconnected from the saved native QR pairing profile.", {
      trigger,
      connection,
    });
  })()
    .catch((error) => {
      const message = error instanceof Error ? error.message : String(error);
      harnessState.lastAction = `pairing.qr.reconnect.${trigger}.failed`;
      harnessState.lastError = message;
      renderHarnessState();
      log("Saved QR reconnect attempt failed.", { trigger, message });
    })
    .finally(() => {
      savedQrReconnectPromise = null;
    });

  return savedQrReconnectPromise;
}

document.querySelectorAll<HTMLButtonElement>("[data-tab]").forEach((button) => {
  button.addEventListener(
    "click",
    () => void selectTab(button.dataset.tab as HarnessTab),
  );
});
document
  .querySelectorAll<HTMLButtonElement>("[data-action]")
  .forEach((button) => {
    button.addEventListener(
      "click",
      () => void performHarnessAction(button.dataset.action!),
    );
  });
document
  .querySelectorAll<HTMLButtonElement>("[data-route]")
  .forEach((button) => {
    button.addEventListener("click", () => {
      pushRoute(button.dataset.route!);
      harnessState.drawerOpen = false;
      renderHarnessState();
    });
  });
document
  .querySelectorAll<HTMLButtonElement>("[data-speed]")
  .forEach((button) => {
    button.addEventListener("click", () => {
      harnessState.shippingSpeed = button.dataset.speed as ShippingSpeed;
      harnessState.lastAction = "shipping.speed";
      renderHarnessState();
    });
  });
document
  .querySelector<HTMLInputElement>("#data-input")!
  .addEventListener("input", (event) => {
    harnessState.keyboardText = (event.currentTarget as HTMLInputElement).value;
    harnessState.lastAction = "keyboard.input";
  });
document
  .querySelector<HTMLInputElement>("#expedited")!
  .addEventListener("change", (event) => {
    harnessState.expeditedBilling = (
      event.currentTarget as HTMLInputElement
    ).checked;
    harnessState.lastAction = "billing.expedited";
    renderHarnessState();
  });
document
  .querySelector("#run-safe")!
  .addEventListener("click", () => void runStableSuite());
document.querySelector("#reset-results")!.addEventListener("click", () => {
  results.clear();
  logs.length = 0;
  renderTests();
  log("Results reset.");
});
document.querySelector("#export-results")!.addEventListener("click", () => {
  const blob = new Blob([JSON.stringify(exportPayload(), null, 2)], {
    type: "application/json",
  });
  const anchor = document.createElement("a");
  anchor.href = URL.createObjectURL(blob);
  anchor.download = `ansight-capacitor-${Capacitor.getPlatform()}-${Date.now()}.json`;
  anchor.click();
  URL.revokeObjectURL(anchor.href);
});
document
  .querySelector("#scan-pairing-qr")!
  .addEventListener("click", async () => {
    harnessState.lastAction = "pairing.qr.open";
    harnessState.lastError = null;
    renderHarnessState();
    log("Opening native QR pairing scanner.");

    try {
      const connection = requireSuccess(
        await Ansight.scanPairingQrCode({
          title: "Scan Ansight Pairing QR",
          clientName,
          expectedAppId: appId,
        }),
      );
      const status = asObject(await Ansight.hostConnectionStatus());
      assert(
        Boolean(status.hasSavedConfig),
        "QR pairing connected but did not persist a native reconnect profile.",
      );
      localStorage.setItem(pairingSourceStorageKey, qrPairingSource);
      localStorage.removeItem(pairingJsonStorageKey);
      pairingInput.value = "";
      harnessState.lastAction = "pairing.qr.connected";
      harnessState.lastConnectionMessage = String(
        connection.message ?? "Connected from QR pairing.",
      );
      runtimeSnapshot = asObject(await Ansight.snapshot());
      log("Native QR pairing completed and saved for automatic reconnect.", {
        connection,
        status,
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      harnessState.lastAction = "pairing.qr.failed";
      harnessState.lastError = message;
      log("Native QR pairing did not connect.", message);
    }

    renderHarnessState();
  });
document.querySelector("#save-pairing")!.addEventListener("click", () => {
  localStorage.setItem(pairingJsonStorageKey, pairingJson());
  localStorage.setItem(pairingSourceStorageKey, documentPairingSource);
  log("Pairing JSON saved to local storage.");
});
document.querySelector("#clear-pairing")!.addEventListener("click", () => {
  pairingInput.value = "";
  localStorage.removeItem(pairingJsonStorageKey);
  localStorage.removeItem(pairingSourceStorageKey);
  log("Local pairing JSON cleared.");
});
document.querySelector("#dom-action")!.addEventListener("click", () => {
  document.querySelector<HTMLOutputElement>("#dom-output")!.value =
    `Clicked at ${new Date().toLocaleTimeString()}`;
});

Ansight.addLogListener((entry) =>
  log(`NATIVE ${entry.level}: ${entry.message}`, entry.error),
);
Ansight.addHostConnectionStatusListener(
  (connectionStatus, capabilities) => {
    const connected = Boolean(connectionStatus.isConnected);
    const pill = document.querySelector("#connection-pill")!;
    pill.textContent = String(connectionStatus.connectionState ?? "unknown");
    pill.classList.toggle("connected", connected);
    harnessState.lastConnectionMessage = `${String(connectionStatus.connectionState ?? "unknown")} · ${connected ? "connected" : "not connected"}`;
    renderHarnessState();
    log("Host connection status changed.", { connectionStatus, capabilities });
  },
  { emitCurrent: false },
);
document.addEventListener("visibilitychange", () => {
  harnessState.lifecycleTransitions += 1;
  harnessState.lastAction = `lifecycle.${document.visibilityState}`;
  renderHarnessState();
  if (document.visibilityState === "visible") {
    void reconnectSavedQrPairing("foreground");
  }
});
window.addEventListener("beforeunload", () => scene3D.dispose());

renderTests();
renderHarnessState();
log("Harness ready.", {
  platform: Capacitor.getPlatform(),
  nativePlatform: Capacitor.isNativePlatform(),
  tests: tests.length,
});
void startHarness();
