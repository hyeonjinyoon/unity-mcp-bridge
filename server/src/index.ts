import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { callUnity, checkHealth } from "./unity-client.js";

const server = new McpServer({
  name: "unity-mcp",
  version: "1.0.0",
});

const Vec3 = z.object({
  x: z.number(),
  y: z.number(),
  z: z.number(),
});

function formatResult(data: unknown): { content: Array<{ type: "text"; text: string }> } {
  return {
    content: [{ type: "text", text: JSON.stringify(data, null, 2) }],
  };
}

// ── Tree formatter for scene_query ──

interface SceneNode {
  name: string;
  path?: string;
  activeSelf: boolean;
  components?: string[];
  childCount: number;
  children?: SceneNode[] | null;
}

interface SceneQueryResult {
  sceneName: string;
  isPrefabMode: boolean;
  matchCount?: number;
  objects: SceneNode[];
}

const NOISE_COMPONENTS = new Set(["Transform", "RectTransform", "CanvasRenderer"]);

function formatSceneTree(data: SceneQueryResult): string {
  const lines: string[] = [];
  const header = `${data.sceneName} (Scene${data.isPrefabMode ? ", Prefab Mode" : ""})`;
  const matchSuffix = data.matchCount !== undefined
    ? ` [${data.matchCount} match${data.matchCount === 1 ? "" : "es"}]`
    : "";
  lines.push(`${header}${matchSuffix}`);
  if (data.objects) {
    buildTreeLines(data.objects, 1, lines);
  }
  return lines.join("\n");
}

function buildTreeLines(nodes: SceneNode[], depth: number, lines: string[]) {
  const indent = "  ".repeat(depth);
  const groups = groupLeafSiblings(nodes);

  for (const group of groups) {
    const first = group.nodes[0];
    const inactive = first.activeSelf ? "" : " [inactive]";
    const components = (first.components ?? []).filter(c => !NOISE_COMPONENTS.has(c));
    const compStr = components.length > 0 ? `: ${components.join(" ")}` : "";
    const countSuffix = group.count > 1 ? ` ×${group.count}` : "";
    const displayName = depth === 1 && first.path && first.path !== first.name
      ? first.path
      : first.name;

    lines.push(`${indent}${displayName}${inactive}${compStr}${countSuffix}`);

    if (group.count === 1 && first.children && first.children.length > 0) {
      buildTreeLines(first.children, depth + 1, lines);
    }
  }
}

interface SiblingGroup {
  nodes: SceneNode[];
  count: number;
}

function groupLeafSiblings(nodes: SceneNode[]): SiblingGroup[] {
  const groups: SiblingGroup[] = [];
  let i = 0;
  while (i < nodes.length) {
    const current = nodes[i];
    const group: SiblingGroup = { nodes: [current], count: 1 };

    // Only collapse leaf nodes (no children) with identical name base + components + active state.
    const hasChildren = (current.children?.length ?? 0) > 0;
    if (hasChildren) {
      groups.push(group);
      i++;
      continue;
    }

    const baseName = stripCloneSuffix(current.name);
    const currentKey = componentKey(current);

    let j = i + 1;
    while (j < nodes.length) {
      const next = nodes[j];
      if ((next.children?.length ?? 0) > 0) break;
      if (stripCloneSuffix(next.name) !== baseName) break;
      if (componentKey(next) !== currentKey) break;
      if (next.activeSelf !== current.activeSelf) break;

      group.nodes.push(next);
      group.count++;
      j++;
    }

    groups.push(group);
    i = j;
  }
  return groups;
}

function componentKey(node: SceneNode): string {
  return (node.components ?? []).slice().sort().join("|");
}

function stripCloneSuffix(name: string): string {
  return name.replace(/\s*\(\d+\)$/, "");
}

// ── Health ──

server.tool(
  "unity_health",
  "Check if Unity Editor MCP bridge is running",
  {},
  async () => {
    const data = await checkHealth();
    return formatResult(data);
  }
);

// ── Editor ──

server.tool(
  "unity_refresh",
  "Trigger AssetDatabase.Refresh() to recompile scripts and reimport assets",
  {},
  async () => {
    const data = await callUnity("/api/editor/refresh");
    return formatResult(data);
  }
);

server.tool(
  "unity_console_log",
  "Get recent Unity console logs (errors, warnings, messages). By default returns collapsed (first-line only) view like Unity Console.",
  {
    count: z.number().optional().describe("Number of recent logs to return (default: 50)"),
    type: z.string().optional().describe("Filter by type: Log, Warning, Error (default: all). Thrown exceptions (NullReferenceException 등 LogType.Exception) are normalized to Error."),
    collapse: z.boolean().optional().describe("If true (default), return first line only per log. Set false for full message + stackTrace."),
    since: z.string().optional().describe("Only return logs after this time (HH:mm:ss or HH:mm:ss.fff)"),
    sinceSeconds: z.number().optional().describe("Only return logs from the last N seconds"),
    clear: z.boolean().optional().describe("Clear logs after reading"),
  },
  async ({ count, type, collapse, since, sinceSeconds, clear }) => {
    const data = await callUnity("/api/console/get", { count, type, collapse, since, sinceSeconds, clear });
    return formatResult(data);
  }
);

server.tool(
  "unity_console_clear",
  "Clear all buffered console logs",
  {},
  async () => {
    const data = await callUnity("/api/console/clear");
    return formatResult(data);
  }
);

server.tool(
  "unity_editor_log",
  "Read recent lines from Unity Editor.log (Application.consoleLogPath). Captures output that console_log misses — compile errors, domain reload issues, native crashes, Play mode entry failures. Tails the last N bytes of the file so large logs are safe.",
  {
    lines: z.number().optional().describe("Number of lines to return from the tail after filtering (default: 200)"),
    grep: z.string().optional().describe("Regex pattern to filter lines (applied before tail)"),
    tailBytes: z.number().optional().describe("Only read the last N bytes of the file to avoid loading huge logs (default: 1000000)"),
  },
  async ({ lines, grep, tailBytes }) => {
    const data = await callUnity("/api/editor/log", { lines, grep, tailBytes });
    return formatResult(data);
  }
);

server.tool(
  "unity_screenshot",
  "Capture a screenshot from the Game View. Always saved into the project's ScreenShots/ folder (location is fixed and cannot be overridden). Optional fileName overrides the default `ScreenShot_{timestamp}.png`; any path separators are stripped and `.png` extension is enforced. In Play mode, also returns the PNG image inline.",
  {
    fileName: z.string().optional().describe("File name only (no directories). Path separators are stripped. `.png` is appended if missing. Defaults to `ScreenShot_{yyMMdd_HHmmss_fff}.png`."),
  },
  async ({ fileName }) => {
    const data = (await callUnity("/api/editor/screenshot", { fileName })) as {
      path: string;
      imageBase64: string | null;
      mimeType?: string;
      note?: string;
    };

    const content: Array<
      | { type: "image"; data: string; mimeType: string }
      | { type: "text"; text: string }
    > = [];

    if (data.imageBase64) {
      content.push({
        type: "image",
        data: data.imageBase64,
        mimeType: data.mimeType ?? "image/png",
      });
    }

    content.push({
      type: "text",
      text: data.note ? `${data.note}\nSaved to: ${data.path}` : `Saved to: ${data.path}`,
    });

    return { content };
  }
);

// ── Scene ──

server.tool(
  "unity_scene_query",
  "Query the scene hierarchy. Returns an indented tree (2-space) with components after `:`, noise components (Transform/RectTransform/CanvasRenderer) filtered, and identical leaf siblings collapsed as `×N`. Path supports glob wildcards: `*` matches any characters within a segment (e.g., `Canvas/Main*/Button`), `**` matches zero or more path segments (e.g., `Canvas/**/Button*` or `**/Popup`). Wildcard matches are listed as separate roots with full paths.",
  {
    path: z.string().optional().describe("Hierarchy path to query from (empty = all roots)"),
    depth: z.number().optional().describe("Max traversal depth (default: 3)"),
    includeInactive: z.boolean().optional().describe("Include inactive objects"),
  },
  async ({ path, depth, includeInactive }) => {
    const data = await callUnity("/api/scene/query", { path, depth, includeInactive });
    const tree = formatSceneTree(data as SceneQueryResult);
    return { content: [{ type: "text" as const, text: tree }] };
  }
);

server.tool(
  "unity_save_scene",
  "Save the current scene",
  {},
  async () => {
    const data = await callUnity("/api/scene/save");
    return formatResult(data);
  }
);

server.tool(
  "unity_open_scene",
  "Open a scene by asset path",
  {
    scenePath: z.string().describe("Scene asset path (e.g. Assets/Scenes/Lobby.unity)"),
  },
  async ({ scenePath }) => {
    const data = await callUnity("/api/scene/open", { scenePath });
    return formatResult(data);
  }
);

server.tool(
  "unity_new_scene",
  "Create a new empty scene with default camera and light",
  {},
  async () => {
    const data = await callUnity("/api/scene/new");
    return formatResult(data);
  }
);

// ── GameObject ──

server.tool(
  "unity_create_object",
  "Create an empty GameObject in the scene",
  {
    name: z.string().optional().describe("Object name (default: New GameObject)"),
    parentPath: z.string().optional().describe("Parent hierarchy path"),
  },
  async ({ name, parentPath }) => {
    const data = await callUnity("/api/object/create", { name, parentPath });
    return formatResult(data);
  }
);

server.tool(
  "unity_delete_object",
  "Delete a GameObject from the scene (Undo supported)",
  {
    path: z.string().describe("Hierarchy path of the object to delete"),
  },
  async ({ path }) => {
    const data = await callUnity("/api/object/delete", { path });
    return formatResult(data);
  }
);

server.tool(
  "unity_rename_object",
  "Rename a GameObject",
  {
    path: z.string().describe("Hierarchy path of the object"),
    newName: z.string().describe("New name for the object"),
  },
  async ({ path, newName }) => {
    const data = await callUnity("/api/object/rename", { path, newName });
    return formatResult(data);
  }
);

// ── Transform ──

server.tool(
  "unity_get_transform",
  "Get position, rotation, and scale of a GameObject",
  {
    path: z.string().describe("Hierarchy path of the object"),
    local: z.boolean().optional().describe("Use local space (default: true)"),
  },
  async ({ path, local }) => {
    const data = await callUnity("/api/transform/get", { path, local });
    return formatResult(data);
  }
);

server.tool(
  "unity_set_transform",
  "Set position, rotation, and/or scale of a GameObject (Undo supported)",
  {
    path: z.string().describe("Hierarchy path of the object"),
    position: Vec3.optional().describe("Position {x, y, z}"),
    rotation: Vec3.optional().describe("Euler rotation {x, y, z}"),
    scale: Vec3.optional().describe("Scale {x, y, z}"),
    local: z.boolean().optional().describe("Use local space (default: true)"),
  },
  async ({ path, position, rotation, scale, local }) => {
    const data = await callUnity("/api/transform/set", { path, position, rotation, scale, local });
    return formatResult(data);
  }
);

// ── Component ──

server.tool(
  "unity_add_component",
  "Add a component to a GameObject (Undo supported)",
  {
    path: z.string().describe("Hierarchy path of the object"),
    componentType: z.string().describe("Component type name (e.g. BoxCollider2D, Image, AudioSource)"),
  },
  async ({ path, componentType }) => {
    const data = await callUnity("/api/component/add", { path, componentType });
    return formatResult(data);
  }
);

server.tool(
  "unity_remove_component",
  "Remove a component from a GameObject (Undo supported)",
  {
    path: z.string().describe("Hierarchy path of the object"),
    componentType: z.string().describe("Component type name to remove"),
  },
  async ({ path, componentType }) => {
    const data = await callUnity("/api/component/remove", { path, componentType });
    return formatResult(data);
  }
);

server.tool(
  "unity_list_components",
  "List all components on a GameObject",
  {
    path: z.string().describe("Hierarchy path of the object"),
  },
  async ({ path }) => {
    const data = await callUnity("/api/component/list", { path });
    return formatResult(data);
  }
);

// ── Property (SerializedProperty) ──

server.tool(
  "unity_get_property",
  "Get a serialized property value from a component",
  {
    path: z.string().describe("Hierarchy path of the object"),
    component: z.string().describe("Component type name (e.g. Image, Transform)"),
    property: z.string().describe("SerializedProperty path (e.g. m_Color, m_LocalPosition.x)"),
  },
  async ({ path, component, property }) => {
    const data = await callUnity("/api/property/get", { path, component, property });
    return formatResult(data);
  }
);

server.tool(
  "unity_set_property",
  "Set a serialized property value on a component (Undo supported). Supports int, float, bool, string, Color, Vector2/3/4, Rect, Enum, ObjectReference. ObjectReference accepts: int (instanceId), null, asset path string starting with 'Assets/' or 'Packages/', hierarchy path string (e.g. 'Canvas/Panel/Button'), or {path, component?} object for explicit hierarchy lookup. GameObject→Component coercion is automatic when the field expects a Component type.",
  {
    path: z.string().describe("Hierarchy path of the object"),
    component: z.string().describe("Component type name"),
    property: z.string().describe("SerializedProperty path"),
    value: z.any().describe("Value to set (type must match property type). For ObjectReference: instanceId (int), asset path, hierarchy path, or {path, component?}."),
  },
  async ({ path, component, property, value }) => {
    // MCP 프로토콜에서 z.any() 값이 문자열로 도착할 수 있음 → JSON 파싱 시도
    let parsed = value;
    if (typeof value === "string") {
      try {
        parsed = JSON.parse(value);
      } catch {
        // 순수 문자열 값이면 그대로 유지
      }
    }
    const data = await callUnity("/api/property/set", { path, component, property, value: parsed });
    return formatResult(data);
  }
);

// ── Prefab ──

server.tool(
  "unity_open_prefab",
  "Open a prefab in Prefab Mode for editing",
  {
    prefabPath: z.string().describe("Prefab asset path (e.g. Assets/Prefabs/Player.prefab)"),
  },
  async ({ prefabPath }) => {
    const data = await callUnity("/api/prefab/open", { prefabPath });
    return formatResult(data);
  }
);

server.tool(
  "unity_instantiate_prefab",
  "Instantiate a prefab in the scene (Undo supported)",
  {
    prefabPath: z.string().describe("Prefab asset path (e.g. Assets/Prefabs/Player.prefab)"),
    parentPath: z.string().optional().describe("Parent hierarchy path"),
    position: Vec3.optional().describe("Initial position {x, y, z}"),
  },
  async ({ prefabPath, parentPath, position }) => {
    const data = await callUnity("/api/prefab/instantiate", { prefabPath, parentPath, position });
    return formatResult(data);
  }
);

server.tool(
  "unity_save_prefab",
  "Save a scene GameObject as a prefab asset and connect the instance to it.",
  {
    sourcePath: z.string().describe("Hierarchy path of the source GameObject"),
    targetPath: z.string().describe("Target prefab asset path (e.g. Assets/Prefabs/Box.prefab)"),
  },
  async ({ sourcePath, targetPath }) => {
    const data = await callUnity("/api/prefab/save", { sourcePath, targetPath });
    return formatResult(data);
  }
);

// ── Play Mode ──

interface PlayState {
  isPlaying: boolean;
  isPaused: boolean;
  activeScene: string;
}

// Domain reload / scene switch 후 /api/play/state가 안정 상태로 응답할 때까지 polling.
// 도메인 리로드 중에는 callUnity가 throw하므로 try/catch로 흡수하고 다음 iteration 진행.
async function waitForPlayState(
  predicate: (state: PlayState) => boolean,
  timeoutMs = 30000,
  intervalMs = 300
): Promise<PlayState> {
  const start = Date.now();
  let lastError: unknown = null;
  while (Date.now() - start < timeoutMs) {
    try {
      const state = (await callUnity("/api/play/state")) as PlayState;
      if (predicate(state)) return state;
    } catch (e) {
      lastError = e;
    }
    await new Promise((r) => setTimeout(r, intervalMs));
  }
  throw new Error(
    `Timeout waiting for play state${lastError ? ` (last error: ${String(lastError)})` : ""}`
  );
}

server.tool(
  "unity_play",
  "Enter Play mode in Unity Editor. Default: opens the Init scene and records the current scene so unity_stop restores it. Set fromCurrent=true to Play from the current scene without Init entry or restoration. Resolves once Play mode entry has stabilized (isPlaying=true after domain reload).",
  {
    fromCurrent: z
      .boolean()
      .optional()
      .describe(
        "If true, enter Play mode on the current scene without switching to Init. No scene restoration on stop. Default: false."
      ),
  },
  async ({ fromCurrent }) => {
    const start = await callUnity("/api/play/start", { fromCurrent });
    const settled = await waitForPlayState((s) => s.isPlaying);
    return formatResult({ ...(start as object), settled });
  }
);

server.tool(
  "unity_stop",
  "Exit Play mode in Unity Editor. If unity_play was called without fromCurrent, the previously active scene is restored automatically. Resolves once Play mode exit has stabilized (isPlaying=false after domain reload).",
  {},
  async () => {
    const start = await callUnity("/api/play/stop");
    const settled = await waitForPlayState((s) => !s.isPlaying);
    return formatResult({ ...(start as object), settled });
  }
);

server.tool(
  "unity_pause",
  "Toggle pause in Play mode",
  {},
  async () => {
    const data = await callUnity("/api/play/pause");
    return formatResult(data);
  }
);

server.tool(
  "unity_play_state",
  "Get current Play mode state (playing, paused, stopped)",
  {},
  async () => {
    const data = await callUnity("/api/play/state");
    return formatResult(data);
  }
);

// ── Input Simulation ──

server.tool(
  "unity_tap",
  "Simulate a tap/click at screen coordinates. Sends pointer down, click, and up events via EventSystem.",
  {
    x: z.number().describe("X coordinate (left=0, right=Screen.width)"),
    y: z.number().describe("Y coordinate (top=0, bottom=Screen.height) — image coordinate system, top-down"),
  },
  async ({ x, y }) => {
    const data = await callUnity("/api/input/tap", { x, y });
    return formatResult(data);
  }
);

server.tool(
  "unity_hold",
  "Simulate a long press/hold at screen coordinates. Sends pointer down, repeated click events (50 per second for the given duration), then pointer up.",
  {
    x: z.number().describe("X coordinate (left=0, right=Screen.width)"),
    y: z.number().describe("Y coordinate (top=0, bottom=Screen.height) — image coordinate system, top-down"),
    duration: z.number().describe("Hold duration in seconds (e.g. 2.0 = ~100 clicks)"),
  },
  async ({ x, y, duration }) => {
    const data = await callUnity("/api/input/hold", { x, y, duration });
    return formatResult(data);
  }
);

server.tool(
  "unity_swipe",
  "Simulate a swipe/drag gesture from (startX, startY) to (endX, endY). Dispatches beginDrag/drag/endDrag events via EventSystem, so it works with ScrollRect and other IDragHandler targets.",
  {
    startX: z.number().describe("Swipe start X (left=0, right=Screen.width)"),
    startY: z.number().describe("Swipe start Y (top=0, bottom=Screen.height) — image coordinate system, top-down"),
    endX: z.number().describe("Swipe end X (left=0, right=Screen.width)"),
    endY: z.number().describe("Swipe end Y (top=0, bottom=Screen.height) — image coordinate system, top-down"),
    steps: z.number().optional().describe("Number of intermediate drag events (default: 20, min: 2)"),
  },
  async ({ startX, startY, endX, endY, steps }) => {
    const data = await callUnity("/api/input/swipe", { startX, startY, endX, endY, steps });
    return formatResult(data);
  }
);

server.tool(
  "unity_raycast",
  "Raycast at screen coordinates to see what UI elements are there (without clicking)",
  {
    x: z.number().describe("X coordinate (left=0, right=Screen.width)"),
    y: z.number().describe("Y coordinate (top=0, bottom=Screen.height) — image coordinate system, top-down"),
  },
  async ({ x, y }) => {
    const data = await callUnity("/api/input/raycast", { x, y });
    return formatResult(data);
  }
);

server.tool(
  "unity_press_key",
  "Simulate a keyboard key press. Queues a press event this frame and auto-releases next frame. Uses Unity Input System Key enum names (e.g. Escape, Enter, Space, A, B, LeftShift).",
  {
    key: z.string().describe("Key name from Unity Key enum (e.g. Escape, Enter, Space, Tab, Backspace, A-Z)"),
  },
  async ({ key }) => {
    const data = await callUnity("/api/input/press_key", { key });
    return formatResult(data);
  }
);

// ── Start ──

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
}

main().catch((err) => {
  console.error("Fatal:", err);
  process.exit(1);
});
