import type { LiveVisualTreeResult, TaskInvocation } from "../tasks/ansight-task.d.ts";

export type FrameworkNode = {
  id?: string;
  label?: string;
  role?: string;
  enabled?: boolean;
  focusable?: boolean;
  childCount?: number;
  accessibility?: { peerType?: string; isControlElement?: boolean; isContentElement?: boolean };
  captureError?: string;
  children?: FrameworkNode[];
};

export async function captureFrameworkTree(
  { app, ansight, expect }: TaskInvocation,
  kind: "logical" | "visual" | "accessibility",
  maximumPages: number
) {
  const pending: (string | undefined)[] = [undefined];
  const capturedRoots = new Set<string | undefined>();
  const nodes = new Map<string, FrameworkNode>();
  const snapshotIds: string[] = [];
  let rootId: string | undefined;

  while (pending.length) {
    if (snapshotIds.length >= maximumPages) {
      throw new Error(`${kind} tree exceeded its ${maximumPages}-page capture budget.`);
    }

    const rootNodeId = pending.shift();
    if (capturedRoots.has(rootNodeId)) continue;
    capturedRoots.add(rootNodeId);

    const capture = await app.callTool<LiveVisualTreeResult>("ui.get_visual_tree", {
      source: `avalonia-${kind}`, maxDepth: 8, maxNodes: 400,
      ...(rootNodeId ? { rootNodeId } : {})
    });
    if (capture.responseType === "tool.error") throw new Error(capture.payload.message);
    const snapshotId = capture.persistedVisualTree?.snapshotId;
    expect(Boolean(snapshotId), { id: `${kind}-page-${snapshotIds.length}-retained` }).toBe(true);
    if (!snapshotId) throw new Error(`The ${kind} tree page was not retained.`);
    snapshotIds.push(snapshotId);

    const snapshot = await ansight.ui.getVisualTreeSnapshot({ snapshotId, maxDepth: 8 });
    const payload = snapshot.visualTree.payload;
    expect(payload?.source, { id: `${kind}-page-${snapshotIds.length}-source` }).toBe(`avalonia-${kind}`);
    if (kind === "accessibility") {
      expect(payload?.hierarchy, { id: `automation-peer-hierarchy-${snapshotIds.length}` })
        .toBe("Avalonia.Automation.Peers.AutomationPeer.GetChildren");
    }

    const root = payload?.root as FrameworkNode | undefined;
    if (!root?.id) throw new Error(`The retained ${kind} page has no root identity.`);
    if (rootNodeId && root.id !== rootNodeId) throw new Error("Subtree capture returned a different root.");
    rootId ??= root.id;

    const stack = [{ node: root, ancestors: [] as FrameworkNode[] }];
    const frontier = new Set<string>();
    while (stack.length) {
      const { node, ancestors } = stack.pop()!;
      if (!node.id) throw new Error(`A ${kind} node has no stable identity.`);
      if (node.captureError) throw new Error(`${kind} node ${node.id}: ${node.captureError}`);
      const previous = nodes.get(node.id);
      if (!previous || (node.children?.length ?? 0) >= (previous.children?.length ?? 0)) {
        nodes.set(node.id, node);
      }

      if ((node.childCount ?? 0) > (node.children?.length ?? 0)) {
        // Restart above the depth boundary so one page can cover several cut branches.
        const continuation = ancestors[4] ?? node;
        if (!continuation.id || capturedRoots.has(continuation.id)) {
          throw new Error(`The ${kind} traversal cannot make progress at ${node.id}.`);
        }
        frontier.add(continuation.id);
      }
      for (const child of node.children ?? []) {
        stack.push({ node: child, ancestors: [...ancestors, node] });
      }
    }
    if (snapshot.visualTree.truncated && frontier.size === 0) {
      throw new Error(`The ${kind} page was truncated without a resolvable continuation.`);
    }
    for (const id of frontier) {
      if (!pending.includes(id)) pending.push(id);
    }
  }

  expect(nodes.size > 1, { id: `${kind}-controls` }).toBe(true);
  expect([...nodes.values()].every(node => (node.childCount ?? 0) === (node.children?.length ?? 0)), {
    id: `${kind}-all-children-captured`
  }).toBe(true);
  return { rootId, nodes: [...nodes.values()], snapshotIds };
}
