# Unity MCP Bridge

Drive the Unity Editor from Claude Code / Claude Desktop (or any other MCP client) over HTTP. Ships as two pieces that talk to each other:

- **`server/`** — Node TypeScript MCP server. Exposes 33 tools (scene query, prefab CRUD, screenshots, input simulation, Play mode, console logs, …) and forwards them to the Editor.
- **`unity-package/`** — UPM package with an `[InitializeOnLoad]` HTTP listener and handlers that run on the Editor main thread.

## Architecture

```
┌──────────────────┐   stdio    ┌────────────────┐   HTTP    ┌──────────────────┐
│ Claude / MCP cli │ ─────────► │ server (Node)  │ ────────► │ Unity Editor     │
│  (.mcp.json)     │            │  index.ts      │ 29400+    │  McpHttpServer   │
└──────────────────┘            └────────────────┘           │  Handlers/*.cs   │
                                                             └──────────────────┘
```

- The server picks a localhost port deterministically from the Unity project path (hash → `29400 + n % 100`).
- The Editor listens on the same hash, plus a temp-folder `unity-mcp-port-{id}.txt` for cross-checking.
- For remote Unity, drop a `.unity-mcp-url` file in the workspace root or set `UNITY_MCP_URL`.

## Install

### 1. Add the Unity package

Open Unity's **Package Manager → ⊕ → Add package from git URL** and paste:

```
https://github.com/<your-fork>/unity-mcp-bridge.git?path=unity-package
```

Or, point Unity at a local checkout (`file:` URL) while developing.

The package brings two dependencies via UPM (`com.unity.nuget.newtonsoft-json`, `com.unity.inputsystem`). Make sure your project is using the Input System (`Edit → Project Settings → Player → Active Input Handling = Input System Package` or `Both`).

Once the package compiles, the HTTP server starts automatically on Editor load. You should see a log like:

```
[MCP Bridge] Server started on port 29406 (preferred: 29406, id: a1b2c3d4)
```

Menu items appear under **Tools → MCP Bridge**: Start/Stop Server, Open Settings.

### 2. Build the MCP server

```sh
cd unity-mcp-bridge/server
npm install
npm run build
```

This produces `build/index.js` which is what your MCP client launches.

### 3. Register the server with your MCP client

Copy `.mcp.json.example` to your workspace root as `.mcp.json` (or add it to Claude Desktop's `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": ["./unity-mcp-bridge/server/build/index.js"]
    }
  }
}
```

Adjust the path so it points at your built `index.js`. Restart your MCP client.

### 4. (Optional) Configure project-specific behavior

Open **Tools → MCP Bridge → Open Settings** in Unity. The first invocation creates `Assets/Editor/UnityMcpBridge/McpBridgeSettings.asset`. Two optional fields:

- **Init Scene** — when `unity_play` is called without `fromCurrent`, the Editor switches to this scene and restores the originally active scene when Play mode exits. Leave empty to always play from the current scene.
- **Cursor Sprite** — sprite shown on tap/hold/swipe positions. Leave empty for a 64×64 procedural fallback.

## Locating the Unity project

The server resolves the expected Unity project path in this order:

1. `UNITY_PROJECT_PATH` env var (absolute or relative to workspace root)
2. `.unity-mcp-project` file in the workspace root (one line, path)
3. Auto-scan: first directory under the workspace root (depth ≤ 3) that contains both `Assets/` and `ProjectSettings/`

When the server runs on the same machine as the Editor, the project path is used as the hash seed for port discovery. When the Editor runs remotely, you can skip discovery entirely by providing `.unity-mcp-url`:

```
http://192.168.1.10:29406
```

`UNITY_MCP_URL` env var works the same way and takes precedence.

## Tools (33)

| Category | Tools |
| --- | --- |
| Health / Editor | `unity_health`, `unity_refresh` |
| Console | `unity_console_log`, `unity_console_clear`, `unity_editor_log` |
| Screenshot | `unity_screenshot` |
| Scene | `unity_scene_query`, `unity_save_scene`, `unity_open_scene`, `unity_new_scene` |
| GameObject | `unity_create_object`, `unity_delete_object`, `unity_rename_object` |
| Transform | `unity_get_transform`, `unity_set_transform` |
| Component | `unity_add_component`, `unity_remove_component`, `unity_list_components` |
| Property | `unity_get_property`, `unity_set_property` |
| Prefab | `unity_open_prefab`, `unity_instantiate_prefab`, `unity_save_prefab` |
| Play Mode | `unity_play`, `unity_stop`, `unity_pause`, `unity_play_state` |
| Input | `unity_tap`, `unity_hold`, `unity_swipe`, `unity_raycast`, `unity_press_key` |

Notable behaviors:

- **`unity_scene_query`** returns an indented tree (`Name: Component1 Component2`), filters `Transform`/`RectTransform`/`CanvasRenderer`, collapses identical leaf siblings as `×N`, supports glob (`*`, `**`) on `path`.
- **`unity_screenshot`** always saves into `<projectRoot>/ScreenShots/`; downscales to 720px height before embedding the base64 image in the response.
- **`unity_tap/hold/swipe/raycast`** use **image coordinates** (top-down, top-left origin). `unity_press_key` queues an Input System StateEvent and releases after three editor frames.
- **`unity_set_property`** accepts ObjectReference values as instanceId / asset path / hierarchy path / `{path, component}`. GameObject→Component coercion is automatic.

## Network binding

The Editor's HTTP listener binds to `localhost` only — remote machines on the LAN cannot reach it. To drive the Editor from another machine, run the MCP server on the same host and expose it through your own authenticated channel (SSH tunnel, reverse proxy with auth, etc.).

## Troubleshooting

- **Tools return `fetch failed`** → check `unity-mcp-server/build/` exists (run `npm run build`). If using remote Unity, verify `.unity-mcp-url`.
- **Editor shows `Failed to start - no available port`** → another instance on the same project path already bound the hash port. Stop the other Editor or change `BasePort/PortRange` in `McpHttpServer.cs`.
- **`unity_screenshot` times out in Edit mode** → Game View needs to be the OS foreground window in Edit mode. Play mode renders regardless of focus.
- **Tool schema changes** require a Claude restart. Handler-only changes are picked up on the next Unity recompile.

## License

MIT.
