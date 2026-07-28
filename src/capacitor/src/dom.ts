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
  type: string;
  label?: string;
  visible: boolean;
  enabled: boolean;
  focusable: boolean;
  childCount: number;
  bounds: { x: number; y: number; width: number; height: number };
  properties: Record<string, unknown>;
  children: DomNode[];
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

function captureNode(
  element: Element,
  options: Required<
    Pick<
      AnsightDomToolsOptions,
      "includeHidden" | "includeText" | "includeAttributes"
    >
  >,
  depth: number,
  limits: { maxDepth: number; maxNodes: number; count: number },
): DomNode | null {
  if (limits.count >= limits.maxNodes || depth > limits.maxDepth) return null;

  const style = getComputedStyle(element);
  const rect = element.getBoundingClientRect();
  const visible = isVisible(element, style, rect);
  if (!visible && !options.includeHidden) return null;
  limits.count += 1;

  const children = Array.from(element.children)
    .map((child) => captureNode(child, options, depth + 1, limits))
    .filter((child): child is DomNode => child !== null);
  const htmlElement = element as HTMLElement;
  const disabled =
    element.hasAttribute("disabled") ||
    element.getAttribute("aria-disabled") === "true";

  return {
    id: nodeId(element),
    type: element.tagName.toLowerCase(),
    label: accessibleLabel(element, options.includeText),
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
    properties: {
      id: element.id || undefined,
      role: element.getAttribute("role") ?? undefined,
      className: element.getAttribute("class") ?? undefined,
      value:
        element instanceof HTMLInputElement ||
        element instanceof HTMLSelectElement ||
        element instanceof HTMLTextAreaElement
          ? element.value
          : undefined,
      checked:
        element instanceof HTMLInputElement ? element.checked : undefined,
      attributes: options.includeAttributes ? attributes(element) : undefined,
    },
    children,
  };
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
        const tree = captureNode(root, options, 0, limits);
        return successful(
          {
            platform: "web",
            source: options.source,
            adapter: "@ansight/capacitor",
            capturedAtUtc: new Date().toISOString(),
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
        return successful(
          captureNode(element, options, 0, limits),
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
          description: "Clicks, focuses, blurs, or changes a DOM node.",
          category: "UI",
          scope: "write",
          argumentsSchema: {
            type: "object",
            required: ["nodeId", "action"],
            properties: {
              nodeId: { type: "string" },
              action: { enum: ["click", "focus", "blur", "setValue"] },
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
          if (action === "click") element.click();
          else if (action === "focus") element.focus();
          else if (action === "blur") element.blur();
          else if (
            action === "setValue" &&
            (element instanceof HTMLInputElement ||
              element instanceof HTMLTextAreaElement ||
              element instanceof HTMLSelectElement)
          ) {
            element.value = value ?? "";
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
            { nodeId: id, action },
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
