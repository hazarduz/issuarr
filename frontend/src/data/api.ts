/**
 * Connects the UI to the Issuarr backend.
 *
 * On startup we ask the server for /initialize.json (API root + key, the same
 * handshake Sonarr/Radarr use). If that works, the library is loaded from the
 * API and replaces the built-in sample data. If not (e.g. the UI is being
 * previewed on its own), the sample data stays and the app runs in demo mode.
 */
import { history, queue, setToday, titles, type Issue, type IssueStatus, type Title } from './mock';

interface Init {
  apiRoot: string;
  apiKey: string;
  urlBase: string;
  instanceName: string;
  version: string;
  isDemo: boolean;
}

interface ApiIssue {
  id: number;
  titleId: number;
  number: string;
  title?: string;
  releaseDate: string;
  monitored: boolean;
  status: IssueStatus;
  file?: { id: number; name: string; size: number; format: string };
}

interface ApiTitle {
  id: number;
  slug: string;
  name: string;
  kind: Title['kind'];
  publisher?: string;
  year: number;
  frequency?: string;
  status: Title['status'];
  monitored: boolean;
  qualityProfile: string;
  path: string;
  overview?: string;
  tags: string[];
  palette: string[];
  statistics: { issueCount: number; issueFileCount: number; missingCount: number; sizeOnDisk: number; nextIssue?: string };
  issues?: ApiIssue[];
}

export interface HealthItem {
  source: string;
  type: 'notice' | 'warning' | 'error';
  message: string;
}

let init: Init | null = null;

export const isConnected = () => init !== null;
export const serverInfo = () => init;

export function formatBytes(bytes: number) {
  if (bytes >= 1024 ** 4) return `${(bytes / 1024 ** 4).toFixed(1)} TB`;
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
  if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${Math.round(bytes / 1024)} KB`;
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  if (!init) throw new Error('Not connected to Issuarr backend');
  const res = await fetch(`${init.apiRoot}${path}`, {
    ...options,
    headers: { 'Content-Type': 'application/json', 'X-Api-Key': init.apiKey, ...(options.headers ?? {}) },
  });
  if (!res.ok) throw new Error(`${options.method ?? 'GET'} ${path} failed: ${res.status}`);
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

const fallbackPalette: [string, string] = ['#334155', '#64748b'];

function toTitle(t: ApiTitle): Title {
  const issues: Issue[] = (t.issues ?? []).map((i) => ({
    id: String(i.id),
    serverId: i.id,
    number: i.number,
    title: i.title,
    releaseDate: i.releaseDate,
    status: i.status,
    monitored: i.monitored,
    file: i.file ? { name: i.file.name, size: formatBytes(i.file.size), format: i.file.format } : undefined,
  }));
  return {
    id: String(t.id),
    slug: t.slug,
    name: t.name,
    kind: t.kind,
    publisher: t.publisher ?? '',
    year: t.year,
    frequency: t.frequency ?? '',
    status: t.status,
    monitored: t.monitored,
    qualityProfile: t.qualityProfile,
    path: t.path,
    overview: t.overview ?? '',
    tags: t.tags,
    palette: t.palette.length === 2 ? [t.palette[0], t.palette[1]] : fallbackPalette,
    issues,
    serverStats: {
      have: t.statistics.issueFileCount,
      total: t.statistics.issueCount,
      missing: t.statistics.missingCount,
      sizeBytes: t.statistics.sizeOnDisk,
      next: t.statistics.nextIssue,
    },
  };
}

/** Try to reach the backend. Resolves either way; check isConnected() afterwards. */
export async function bootstrap() {
  try {
    const res = await fetch('/initialize.json', { headers: { Accept: 'application/json' } });
    if (!res.ok || !res.headers.get('content-type')?.includes('json')) return;
    init = (await res.json()) as Init;
    const list = await request<ApiTitle[]>('/title?includeIssues=true');
    titles.splice(0, titles.length, ...list.map(toTitle));
    // Download clients aren't wired up yet, so live mode starts with an empty queue/history.
    queue.splice(0, queue.length);
    history.splice(0, history.length);
    const now = new Date();
    setToday(new Date(now.getFullYear(), now.getMonth(), now.getDate()));
  } catch (e) {
    console.warn('Issuarr backend not reachable, running with sample data.', e);
    init = null;
  }
}

export const api = {
  setIssuesMonitored: (issueIds: number[], monitored: boolean) =>
    request('/issue/monitor', { method: 'PUT', body: JSON.stringify({ issueIds, monitored }) }),

  updateTitle: async (title: Title) => {
    const updated = await request<ApiTitle>(`/title/${title.id}`, {
      method: 'PUT',
      body: JSON.stringify({ ...title, id: Number(title.id), issues: undefined }),
    });
    return updated;
  },

  addTitle: async (t: Pick<Title, 'name' | 'kind' | 'publisher' | 'year' | 'palette'>, rootFolderPath: string) => {
    const created = await request<ApiTitle>('/title', {
      method: 'POST',
      body: JSON.stringify({
        ...t,
        frequency: 'Monthly',
        status: 'continuing',
        monitored: true,
        qualityProfile: t.kind === 'comic' ? 'CBZ / CBR' : 'Any',
        tags: [],
        rootFolderPath,
      }),
    });
    const title = toTitle(created);
    titles.push(title);
    return title;
  },

  health: () => request<HealthItem[]>('/health'),
  rootFolders: () => request<{ id: number; path: string; freeSpace?: number; accessible: boolean }[]>('/rootfolder'),
  addRootFolder: (path: string) => request('/rootfolder', { method: 'POST', body: JSON.stringify({ path }) }),
  status: () => request<Record<string, string | boolean>>('/system/status'),
};
