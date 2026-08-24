import type {
  AnsightDomToolsOptions,
  AnsightOperationResult,
  AnsightToolDefinition,
  AnsightToolHandler,
  AnsightToolRegistration,
  AnsightToolResult,
} from "./definitions";

type RegisterTool = (
  definition: AnsightToolDefinition,
  handler: AnsightToolHandler,
) => AnsightToolRegistration;

interface DomNode {
  id: string;
  typeId: number;
  automationId?: string;
  label?: string;
  role: string;
  supportedActions: string[];
  interactable: boolean;
  visible: boolean;
  enabled: boolean;
  focusable: boolean;
  childCount: number;
  bounds: { x: number; y: number; width: number; height: number };
  visual: {
    foreground?: string;
    background?: string;
    opacity: number;
    text?: string;
    value?: string;
  };
  z?: number;
  properties: Record<string, unknown>;
  children: DomNode[];
}

interface DomTypeRegistry {
  idsByTypeName: Map<string, number>;
  types: string[];
}

interface DomCoordinateSpace {
  x: number;
  y: number;
  width: number;
  height: number;
  source: "dom.viewport";
}

type DomWindowViewport = Pick<Window, "innerWidth" | "innerHeight">;
type DomDocumentViewport = Pick<HTMLElement, "clientWidth" | "clientHeight">;

function createTypeRegistry(): DomTypeRegistry {
  return { idsByTypeName: new Map(), types: [] };
}

function registerType(registry: DomTypeRegistry, typeName: string): number {
  const existingTypeId = registry.idsByTypeName.get(typeName);
  if (existingTypeId !== undefined) return existingTypeId;

  const typeId = registry.types.length;
  registry.types.push(typeName);
  registry.idsByTypeName.set(typeName, typeId);
  return typeId;
}

const nodeIds = new WeakMap<Element, string>();
let nextNodeId = 1;

function nodeId(element: Element): string {
  const existing = nodeIds.get(element);
  if (existing) return existing;
  const id = `dom.${nextNodeId++}`;
  nodeIds.set(element, id);
  return id;
}

function isVisible(
  element: Element,
  style: CSSStyleDeclaration,
  bounds: DOMRect,
): boolean {
  return (
    style.display !== "none" &&
    style.visibility !== "hidden" &&
    style.opacity !== "0" &&
    bounds.width > 0 &&
    bounds.height > 0
  );
}

function accessibleLabel(
  element: Element,
  includeText: boolean,
): string | undefined {
  const labelledBy = element.getAttribute("aria-labelledby");
  const referenced = labelledBy
    ?.split(/\s+/)
    .map((id) => document.getElementById(id)?.textContent?.trim())
    .filter(Boolean)
    .join(" ");
  const label =
    element.getAttribute("aria-label") ??
    referenced ??
    element.getAttribute("alt") ??
    element.getAttribute("title") ??
    (includeText
      ? element.textContent?.replace(/\s+/g, " ").trim().slice(0, 240)
      : undefined);
  return label || undefined;
}

function attributes(element: Element): Record<string, string> {
  return Object.fromEntries(
    Array.from(element.attributes, ({ name, value }) => [
      name,
      value.slice(0, 500),
    ]),
  );
}

function automationId(element: Element): string | undefined {
  return (
    (
      element.getAttribute("data-testid") ??
      element.getAttribute("data-test-id") ??
      element.getAttribute("data-test") ??
      element.id ??
      undefined
    )?.trim() || undefined
  );
}

const tapRoles = new Set([
  "button",
  "checkbox",
  "combobox",
  "link",
  "menuitem",
  "menuitemcheckbox",
  "menuitemradio",
  "option",
  "radio",
  "switch",
  "tab",
]);

function semanticRole(element: Element): string {
  const declared = element.getAttribute("role")?.trim().toLowerCase();
  if (declared) return declared;
  if (element instanceof HTMLButtonElement) return "button";
  if (element instanceof HTMLInputElement) {
    if (element.type === "checkbox") return "checkbox";
    if (element.type === "radio") return "radio";
    if (element.type === "range") return "slider";
    if (["button", "submit", "reset"].includes(element.type)) return "button";
    return "textbox";
  }
  if (element instanceof HTMLTextAreaElement) return "textbox";
  if (element instanceof HTMLSelectElement) return "combobox";
  if (element.tagName === "A") return "link";
  if (/^H[1-6]$/.test(element.tagName)) return "heading";
  return "view";
}

function supportedActions(element: Element, allowActions: boolean): string[] {
  if (!allowActions) return [];
  const actions: string[] = [];
  const htmlElement = element as HTMLElement;
  const role = semanticRole(element);
  if (
    ["A", "BUTTON", "SUMMARY"].includes(element.tagName) ||
    element instanceof HTMLInputElement ||
    tapRoles.has(role) ||
    element.hasAttribute("onclick") ||
    typeof htmlElement.onclick === "function"
  ) {
    actions.push("tap");
  }
  if (
    element instanceof HTMLInputElement ||
    element instanceof HTMLTextAreaElement ||
    element instanceof HTMLSelectElement ||
    htmlElement.isContentEditable
  ) {
    actions.push("typeText", "focus");
  } else if (htmlElement.tabIndex >= 0) {
    actions.push("focus");
  }
  if (
    htmlElement.scrollHeight > htmlElement.clientHeight ||
    htmlElement.scrollWidth > htmlElement.clientWidth
  ) {
    actions.push("scroll", "swipe");
  }
  return [...new Set(actions)];
}

function positiveFinite(value: unknown): number | undefined {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : undefined;
}

export function createDomCoordinateSpace(
  browserWindow: DomWindowViewport | undefined = typeof window === "undefined"
    ? undefined
    : window,
  documentElement: DomDocumentViewport | null = typeof document === "undefined"
    ? null
    : document.documentElement,
): DomCoordinateSpace | undefined {
  const width =
    positiveFinite(browserWindow?.innerWidth) ??
    positiveFinite(documentElement?.clientWidth);
  const height =
    positiveFinite(browserWindow?.innerHeight) ??
    positiveFinite(documentElement?.clientHeight);
  if (width === undefined || height === undefined) return undefined;

  return { x: 0, y: 0, width, height, source: "dom.viewport" };
}

export function normalizeDomAction(action: unknown): string {
  if (action === "tap") return "click";
  if (action === "typeText") return "setValue";
  return String(action ?? "");
}

function computedColorToArgbHex(value: string): string | undefined {
  const normalized = value.trim().toLowerCase();
  if (!normalized) return undefined;
  if (normalized === "transparent") return "#00000000";

  const components = normalized.match(/[\d.]+%?/g);
  if (!components || components.length < 3) return undefined;
  const channel = (component: string): number => {
    const parsed = Number.parseFloat(component);
    const value = component.endsWith("%") ? (parsed * 255) / 100 : parsed;
    return Math.max(0, Math.min(255, Math.round(value)));
  };
  const alpha =
    components.length > 3
      ? Math.max(
          0,
          Math.min(
            255,
            Math.round(
              (components[3].endsWith("%")
                ? Number.parseFloat(components[3]) / 100
                : Number.parseFloat(components[3])) * 255,
            ),
          ),
        )
      : 255;
  return `#${[
    alpha,
    channel(components[0]),
    channel(components[1]),
    channel(components[2]),
  ]
    .map((component) => component.toString(16).padStart(2, "0"))
    .join("")}`.toUpperCase();
}

function displayedText(element: Element): string | undefined {
  if (element instanceof HTMLInputElement) {
    return element.placeholder?.trim() || undefined;
  }
  if (element instanceof HTMLTextAreaElement) {
    return element.placeholder?.trim() || undefined;
  }
  if (element instanceof HTMLSelectElement) {
    return element.selectedOptions[0]?.textContent?.trim() || undefined;
  }

  const shouldReadDescendants = ["BUTTON", "A", "SUMMARY", "OPTION"].includes(
    element.tagName,
  );
  const rawText = shouldReadDescendants
    ? element.textContent
    : Array.from(element.childNodes)
        .filter((child) => child.nodeType === Node.TEXT_NODE)
        .map((child) => child.textContent)
        .join(" ");
  const normalized = rawText?.replace(/\s+/g, " ").trim();
  return normalized ? normalized.slice(0, 240) : undefined;
}

function displayedValue(element: Element): string | undefined {
  let value: string | undefined;
  if (element instanceof HTMLInputElement) {
    if (element.type === "password") return undefined;
    if (element.type === "checkbox" || element.type === "radio") {
      return element.checked.toString();
    }
    value = element.value;
  } else if (
    element instanceof HTMLSelectElement ||
    element instanceof HTMLTextAreaElement
  ) {
    value = element.value;
  }

  const normalized = value?.replace(/\s+/g, " ").trim();
  return normalized ? normalized.slice(0, 240) : undefined;
}

function captureNode(
  element: Element,
  options: Required<
    Pick<
      AnsightDomToolsOptions,
      "includeHidden" | "includeText" | "includeAttributes" | "allowActions"
    >
  >,
  depth: number,
  limits: { maxDepth: number; maxNodes: number; count: number },
  typeRegistry: DomTypeRegistry,
): DomNode | null {
  if (limits.count >= limits.maxNodes || depth > limits.maxDepth) return null;

  const style = getComputedStyle(element);
  const rect = element.getBoundingClientRect();
  const visible = isVisible(element, style, rect);
  if (!visible && !options.includeHidden) return null;
  limits.count += 1;

  const children = Array.from(element.children)
    .map((child) =>
      captureNode(child, options, depth + 1, limits, typeRegistry),
    )
    .filter((child): child is DomNode => child !== null);
  const htmlElement = element as HTMLElement;
  const disabled =
    element.hasAttribute("disabled") ||
    element.getAttribute("aria-disabled") === "true";
  const parsedOpacity = Number.parseFloat(style.opacity);
  const parsedZIndex = Number.parseFloat(style.zIndex);
  const actions = supportedActions(element, options.allowActions);
  const label = accessibleLabel(element, options.includeText);

  const node: DomNode = {
    id: nodeId(element),
    typeId: registerType(typeRegistry, element.tagName.toLowerCase()),
    automationId: automationId(element),
    label,
    role: semanticRole(element),
    supportedActions: actions,
    interactable: visible && !disabled && actions.length > 0,
    visible,
    enabled: !disabled,
    focusable:
      htmlElement.tabIndex >= 0 ||
      ["A", "BUTTON", "INPUT", "SELECT", "TEXTAREA", "SUMMARY"].includes(
        element.tagName,
      ),
    childCount: children.length,
    bounds: {
      x: rect.x,
      y: rect.y,
      width: rect.width,
      height: rect.height,
    },
    visual: {
      foreground: computedColorToArgbHex(style.color),
      background: computedColorToArgbHex(style.backgroundColor),
      opacity: Number.isFinite(parsedOpacity)
        ? Math.max(0, Math.min(1, parsedOpacity))
        : 1,
      text: options.includeText ? displayedText(element) : undefined,
      value: options.includeText ? displayedValue(element) : undefined,
    },
    properties: {
      id: element.id || undefined,
      role: element.getAttribute("role") ?? undefined,
      className: element.getAttribute("class") ?? undefined,
      checked:
        element instanceof HTMLInputElement ? element.checked : undefined,
      attributes: options.includeAttributes ? attributes(element) : undefined,
    },
    children,
  };
  if (Number.isFinite(parsedZIndex) && parsedZIndex !== 0) {
    node.z = parsedZIndex;
  }
  return node;
}

function findElement(id: string): Element | undefined {
  return Array.from(document.querySelectorAll("*")).find(
    (element) => nodeId(element) === id,
  );
}

function successful(result: unknown, message: string): AnsightToolResult {
  return { success: true, message, result };
}

export function installDomTools(
  registerTool: RegisterTool,
  input: AnsightDomToolsOptions = {},
): {
  ids: string[];
  ready: Promise<AnsightOperationResult[]>;
  unregister(): Promise<AnsightOperationResult[]>;
} {
  const options = {
    source: input.source ?? "capacitor-dom",
    includeHidden: input.includeHidden ?? false,
    maxDepth: input.maxDepth ?? 30,
    maxNodes: input.maxNodes ?? 1500,
    includeText: input.includeText ?? true,
    includeAttributes: input.includeAttributes ?? true,
    allowActions: input.allowActions ?? false,
  };

  const registrations = [
    registerTool(
      {
        id: "dom.get_document",
        name: "Get DOM document",
        description:
          "Returns the accessible HTML DOM tree rendered inside the Capacitor WebView.",
        category: "UI",
        scope: "read",
        security: { level: "low", summary: "Reads the current app DOM." },
      },
      async () => {
        const root = document.documentElement;
        const limits = {
          maxDepth: options.maxDepth,
          maxNodes: options.maxNodes,
          count: 0,
        };
        const typeRegistry = createTypeRegistry();
        const tree = captureNode(root, options, 0, limits, typeRegistry);
        return successful(
          {
            format: "ansight.dom.visual-tree.compact.v2",
            platform: "web",
            source: options.source,
            adapter: "@ansight/capacitor",
            coordinateSpace: createDomCoordinateSpace(),
            capturedAtUtc: new Date().toISOString(),
            types: typeRegistry.types,
            truncated: limits.count >= limits.maxNodes,
            root: tree,
          },
          "DOM document captured.",
        );
      },
    ),
    registerTool(
      {
        id: "dom.inspect_node",
        name: "Inspect DOM node",
        description:
          "Returns the current state of a DOM node captured by dom.get_document.",
        category: "UI",
        scope: "read",
        argumentsSchema: {
          type: "object",
          required: ["nodeId"],
          properties: { nodeId: { type: "string" } },
        },
      },
      ({ nodeId: id }) => {
        const element = findElement(id);
        if (!element) {
          return {
            success: false,
            message: `DOM node '${id}' was not found.`,
            errorCode: "dom_node_not_found",
          };
        }
        const limits = { maxDepth: 0, maxNodes: 1, count: 0 };
        const typeRegistry = createTypeRegistry();
        const node = captureNode(element, options, 0, limits, typeRegistry);
        return successful(
          {
            format: "ansight.dom.visual-tree.compact.v2",
            platform: "web",
            source: options.source,
            adapter: "@ansight/capacitor",
            coordinateSpace: createDomCoordinateSpace(),
            capturedAtUtc: new Date().toISOString(),
            types: typeRegistry.types,
            node,
          },
          "DOM node captured.",
        );
      },
    ),
    registerTool(
      {
        id: "dom.query_selector",
        name: "Query DOM",
        description: "Finds DOM nodes using a CSS selector.",
        category: "UI",
        scope: "read",
        argumentsSchema: {
          type: "object",
          required: ["selector"],
          properties: { selector: { type: "string" } },
        },
      },
      ({ selector }) => {
        try {
          const matches = Array.from(document.querySelectorAll(selector)).slice(
            0,
            200,
          );
          return successful(
            matches.map((element) => ({
              id: nodeId(element),
              type: element.tagName.toLowerCase(),
              label: accessibleLabel(element, true),
            })),
            `Matched ${matches.length} DOM node(s).`,
          );
        } catch (error) {
          return {
            success: false,
            message:
              error instanceof Error ? error.message : "Invalid CSS selector.",
            errorCode: "dom_selector_invalid",
          };
        }
      },
    ),
  ];

  if (options.allowActions) {
    registrations.push(
      registerTool(
        {
          id: "dom.invoke_action",
          name: "Invoke DOM action",
          description: "Taps, focuses, blurs, or enters text in a DOM node.",
          category: "UI",
          scope: "write",
          argumentsSchema: {
            type: "object",
            required: ["nodeId", "action"],
            properties: {
              nodeId: { type: "string" },
              action: {
                type: "string",
                enum: ["tap", "typeText", "click", "focus", "blur", "setValue"],
              },
              value: { type: "string" },
            },
          },
          security: {
            level: "high",
            summary: "Can interact with controls inside the app WebView.",
          },
        },
        ({ nodeId: id, action, value }) => {
          const element = findElement(id) as HTMLElement | undefined;
          if (!element) {
            return {
              success: false,
              message: `DOM node '${id}' was not found.`,
              errorCode: "dom_node_not_found",
            };
          }
          const normalizedAction = normalizeDomAction(action);
          if (normalizedAction === "click") element.click();
          else if (normalizedAction === "focus") element.focus();
          else if (normalizedAction === "blur") element.blur();
          else if (
            normalizedAction === "setValue" &&
            (element instanceof HTMLInputElement ||
              element instanceof HTMLTextAreaElement ||
              element instanceof HTMLSelectElement ||
              element.isContentEditable)
          ) {
            if (element.isContentEditable) {
              element.textContent = value ?? "";
            } else {
              (
                element as
                  HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement
              ).value = value ?? "";
            }
            element.dispatchEvent(new Event("input", { bubbles: true }));
            element.dispatchEvent(new Event("change", { bubbles: true }));
          } else {
            return {
              success: false,
              message: `Unsupported action '${action}'.`,
              errorCode: "dom_action_unsupported",
            };
          }
          return successful(
            { nodeId: id, action, performedAction: normalizedAction },
            `DOM action '${action}' invoked.`,
          );
        },
      ),
    );
  }

  return {
    ids: registrations.map(({ id }) => id),
    ready: Promise.all(registrations.map(({ ready }) => ready)),
    unregister: () =>
      Promise.all(
        registrations.map((registration) => registration.unregister()),
      ),
  };
}
