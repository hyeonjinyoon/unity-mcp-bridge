import crypto from "crypto";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const BASE_PORT = 29400;
const PORT_RANGE = 100;
const MAX_SCAN_RETRIES = 5;
const LOCAL_HOST = "127.0.0.1";
const URL_OVERRIDE_FILENAME = ".unity-mcp-url";
const PROJECT_HINT_FILENAME = ".unity-mcp-project";

// Workspace root는 프로세스 cwd가 아닌 스크립트 위치에서 파생한다.
// 이 파일은 build 후 `<workspace>/server/build/unity-client.js`에 위치하므로
// 두 단계 상위가 workspace root이다. cwd-의존을 제거하여 어떤 실행 방식에서도 동일하게 동작.
const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const SERVER_ROOT = path.resolve(SCRIPT_DIR, "..");
const WORKSPACE_ROOT = path.resolve(SERVER_ROOT, "..");

interface Endpoint {
  host: string;
  port: number;
}

let activeEndpoint: Endpoint | null = null;
let cachedProjectPath: string | null = null;

interface ProjectLookup {
  path: string | null;
  enforceMatch: boolean;
}

// Unity 프로젝트 폴더 자동 탐색: `Assets/`와 `ProjectSettings/`를 동시에 가진 폴더.
function isUnityProject(candidate: string): boolean {
  try {
    return (
      fs.statSync(path.join(candidate, "Assets")).isDirectory() &&
      fs.statSync(path.join(candidate, "ProjectSettings")).isDirectory()
    );
  } catch {
    return false;
  }
}

function findUnityProjectIn(root: string, maxDepth: number): string | null {
  if (isUnityProject(root)) return root;
  if (maxDepth <= 0) return null;

  let entries: fs.Dirent[];
  try {
    entries = fs.readdirSync(root, { withFileTypes: true });
  } catch {
    return null;
  }

  for (const entry of entries) {
    if (!entry.isDirectory()) continue;
    if (entry.name.startsWith(".")) continue;
    if (entry.name === "node_modules") continue;
    const sub = path.join(root, entry.name);
    const found = findUnityProjectIn(sub, maxDepth - 1);
    if (found) return found;
  }
  return null;
}

// Allows the server to be cloned inside a Unity project (e.g. <UnityProject>/unity-mcp-bridge/
// shipping the server). After failing the child scan, walk up a few ancestors so that the
// containing Unity project is picked up instead of falling back to an unrelated Editor.
function findUnityProjectInOrAncestors(root: string): string | null {
  const fromSelf = findUnityProjectIn(root, 3);
  if (fromSelf) return fromSelf;

  let current = root;
  for (let i = 0; i < 4; i++) {
    const parent = path.dirname(current);
    if (parent === current) break;
    if (isUnityProject(parent)) return parent;
    current = parent;
  }
  return null;
}

function readProjectHint(): string | null {
  const filePath = path.resolve(WORKSPACE_ROOT, PROJECT_HINT_FILENAME);
  try {
    const raw = fs.readFileSync(filePath, "utf8").trim();
    if (!raw) return null;
    return path.isAbsolute(raw) ? raw : path.resolve(WORKSPACE_ROOT, raw);
  } catch {
    return null;
  }
}

// 1순위: UNITY_PROJECT_PATH env, 2순위: .unity-mcp-project 파일, 3순위: workspace root 하위 자동 탐색.
// 자동 탐색 결과는 fallback이므로 health.projectPath 미일치 시에도 강제하지 않는다.
function resolveExpectedProjectPath(): ProjectLookup {
  const fromEnv = process.env.UNITY_PROJECT_PATH;
  if (fromEnv) {
    return { path: path.resolve(WORKSPACE_ROOT, fromEnv), enforceMatch: true };
  }

  const hint = readProjectHint();
  if (hint) {
    return { path: hint, enforceMatch: true };
  }

  if (cachedProjectPath && isUnityProject(cachedProjectPath)) {
    return { path: cachedProjectPath, enforceMatch: false };
  }

  const found = findUnityProjectInOrAncestors(WORKSPACE_ROOT);
  if (found) {
    cachedProjectPath = found;
    return { path: found, enforceMatch: false };
  }

  return { path: null, enforceMatch: false };
}

function computePreferredPort(seed: string): number {
  const hash = crypto.createHash("md5").update(seed).digest();
  return BASE_PORT + (hash.readUInt16LE(0) % PORT_RANGE);
}

function parseEndpointUrl(raw: string): Endpoint | null {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  try {
    const normalized = trimmed.includes("://") ? trimmed : `http://${trimmed}`;
    const url = new URL(normalized);
    if (!url.hostname || !url.port) return null;
    return { host: url.hostname, port: parseInt(url.port, 10) };
  } catch {
    return null;
  }
}

function readUrlOverrideFile(): string | null {
  const filePath = path.resolve(WORKSPACE_ROOT, URL_OVERRIDE_FILENAME);
  try {
    return fs.readFileSync(filePath, "utf8");
  } catch {
    return null;
  }
}

function getExplicitEndpoint(): Endpoint | null {
  const envUrl = process.env.UNITY_MCP_URL;
  if (envUrl) {
    const fromEnv = parseEndpointUrl(envUrl);
    if (fromEnv) return fromEnv;
  }

  const fileContent = readUrlOverrideFile();
  if (fileContent) {
    const fromFile = parseEndpointUrl(fileContent);
    if (fromFile) return fromFile;
  }

  return null;
}

async function checkEndpointHealth(endpoint: Endpoint): Promise<boolean> {
  try {
    const res = await fetch(`http://${endpoint.host}:${endpoint.port}/api/health`, {
      signal: AbortSignal.timeout(300),
    });
    return res.ok;
  } catch {
    return false;
  }
}

async function checkEndpointMatchesProject(endpoint: Endpoint, lookup: ProjectLookup): Promise<boolean> {
  try {
    const res = await fetch(`http://${endpoint.host}:${endpoint.port}/api/health`, {
      signal: AbortSignal.timeout(300),
    });
    if (!res.ok) return false;
    const data = (await res.json()) as { data?: { projectPath?: string } };
    const actual = data.data?.projectPath;
    if (!actual) return true;
    if (!lookup.path) return true;
    const matches = path.resolve(actual) === path.resolve(lookup.path);
    return matches || !lookup.enforceMatch;
  } catch {
    return false;
  }
}

async function discoverEndpoint(): Promise<Endpoint> {
  // 1) Explicit URL override — trust fully, skip scanning & project path check
  const explicit = getExplicitEndpoint();
  if (explicit) return explicit;

  const lookup = resolveExpectedProjectPath();
  const seed = lookup.path ?? WORKSPACE_ROOT;
  const preferred = computePreferredPort(seed);

  // 2) Hash-based preferred port on localhost
  for (let i = 0; i < MAX_SCAN_RETRIES; i++) {
    const port = BASE_PORT + ((preferred - BASE_PORT + i) % PORT_RANGE);
    const candidate: Endpoint = { host: LOCAL_HOST, port };
    if (await checkEndpointMatchesProject(candidate, lookup)) return candidate;
  }

  // 3) Fallback: full scan on localhost
  for (let port = BASE_PORT; port < BASE_PORT + PORT_RANGE; port++) {
    const candidate: Endpoint = { host: LOCAL_HOST, port };
    if (await checkEndpointMatchesProject(candidate, lookup)) return candidate;
  }

  return { host: LOCAL_HOST, port: preferred };
}

async function getEndpoint(): Promise<Endpoint> {
  if (activeEndpoint !== null) {
    const isExplicit = getExplicitEndpoint() !== null;
    const valid = isExplicit
      ? await checkEndpointHealth(activeEndpoint)
      : await checkEndpointMatchesProject(activeEndpoint, resolveExpectedProjectPath());
    if (valid) return activeEndpoint;
    activeEndpoint = null;
  }

  activeEndpoint = await discoverEndpoint();
  return activeEndpoint;
}

export async function callUnity(
  endpoint: string,
  body: Record<string, unknown> = {}
): Promise<unknown> {
  const ep = await getEndpoint();
  const url = `http://${ep.host}:${ep.port}${endpoint}`;

  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(10000),
  });

  const data = (await res.json()) as {
    success: boolean;
    data?: unknown;
    error?: string;
  };

  if (!data.success) {
    throw new Error(data.error ?? "Unknown Unity error");
  }

  return data.data;
}

export async function checkHealth(): Promise<unknown> {
  const ep = await getEndpoint();
  const res = await fetch(`http://${ep.host}:${ep.port}/api/health`, {
    signal: AbortSignal.timeout(5000),
  });
  return (await res.json()) as unknown;
}
