/**
 * Connects the UI to the Issuarr backend.
 *
 * On startup we ask the server for /initialize.json (API root + key, the same
 * handshake Sonarr/Radarr use). If that works, the library is loaded from the
 * API and replaces the built-in sample data. If not (e.g. the UI is previewed
 * on its own), the sample data stays and the app runs in demo mode.
 */
import { history, queue, setToday, titles, type Issue, type IssueStatus, type Title } from './mock';

export interface CurrentUser {
  id: number;
  username: string;
  role: 'admin' | 'reader';
  isOwner: boolean;
  /** False when the owner got in without logging in (login off, or on the local network). */
  loggedIn: boolean;
}

interface Init {
  apiRoot: string;
  /** Only admins get the master API key; readers use their login cookie. */
  apiKey: string | null;
  user: CurrentUser | null;
  urlBase: string;
  instanceName: string;
  version: string;
  isDemo: boolean;
  authentication: string;
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
  coverUrl?: string;
  readPercent?: number;
  releaseDateLocked?: boolean;
}

export interface ApiTitle {
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
  cover?: string;
  metadataSourceName?: string;
  externalId?: string;
  statistics: { issueCount: number; issueFileCount: number; missingCount: number; sizeOnDisk: number; nextIssue?: string; hiddenCount?: number };
  issues?: ApiIssue[];
}

export interface FileSystemListing {
  path: string;
  parent?: string;
  directories: { name: string; path: string }[];
}

export interface ImportCandidate {
  path: string;
  folderName: string;
  name: string;
  year?: number;
  kind: Title['kind'];
  fileCount: number;
  matchedFileCount: number;
  size: number;
  existingTitleId?: number;
  existingTitleName?: string;
  sampleFiles: string[];
}

export interface ImportResult {
  path: string;
  titleId?: number;
  slug?: string;
  issuesImported: number;
  unmatchedFiles: string[];
  error?: string;
  warning?: string;
}

export interface HealthItem {
  source: string;
  type: 'notice' | 'warning' | 'error';
  message: string;
  link?: string;
}

export type FieldType = 'text' | 'password' | 'number' | 'checkbox' | 'select' | 'url';

export interface ProviderField {
  name: string;
  label: string;
  type: FieldType;
  helpText?: string;
  default?: string;
  required: boolean;
  options?: string[];
  advanced: boolean;
}

export interface ProviderSchema {
  implementation: string;
  displayName: string;
  description: string;
  protocol?: string;
  fields: ProviderField[];
}

export interface Provider {
  id: number;
  name: string;
  implementation: string;
  implementationName?: string;
  enable: boolean;
  priority: number;
  protocol?: string;
  settings: Record<string, string>;
}

export type ProviderKind = 'indexer' | 'downloadclient' | 'notification';

export interface Command {
  id: number;
  name: string;
  titleId?: number;
  status: 'queued' | 'started' | 'completed' | 'failed';
  message?: string;
  queued: string;
  started?: string;
  ended?: string;
  manual: boolean;
}

export interface QueueRecord {
  id: string;
  titleId?: number;
  titleName?: string;
  titleSlug?: string;
  issues?: string;
  release: string;
  downloadClient: string;
  protocol: string;
  indexer?: string;
  size: number;
  sizeLeft: number;
  progress: number;
  eta?: number;
  status: 'queued' | 'downloading' | 'paused' | 'completed' | 'failed' | 'warning';
  trackedState: string;
  messages: string[];
}

export interface HistoryRecord {
  id: number;
  titleId: number;
  titleName?: string;
  titleSlug?: string;
  issueId?: number;
  issue?: string;
  eventType: 'Grabbed' | 'Imported' | 'DownloadFailed' | 'FileDeleted' | 'FileRenamed' | 'ImportFailed';
  sourceTitle: string;
  date: string;
  data: Record<string, string>;
}

export interface BlocklistRecord {
  id: number;
  titleId: number;
  titleName?: string;
  titleSlug?: string;
  sourceTitle: string;
  protocol: string;
  indexer?: string;
  message?: string;
  date: string;
}

export interface Release {
  guid: string;
  title: string;
  indexer: string;
  protocol: string;
  size: number;
  seeders?: number;
  peers?: number;
  publishDate?: string;
  infoUrl?: string;
  titleId?: number;
  titleName?: string;
  issueId?: number;
  issueNumber?: string;
  format?: string;
  approved: boolean;
  rejections: string[];
}

export interface LookupResult {
  source: string;
  externalId: string;
  name: string;
  year?: number;
  publisher?: string;
  issueCount: number;
  overview?: string;
  coverUrl?: string;
  siteUrl?: string;
  existingTitleId?: number;
  existingSlug?: string;
  frequency?: string;
  kind?: Title['kind'];
}

export interface BookResult {
  source: string;
  workId: string;
  title: string;
  author?: string;
  authorId?: string;
  year?: number;
  coverUrl?: string;
  overview?: string;
  siteUrl?: string;
  authorTitleId?: number;
  authorSlug?: string;
  inLibrary: boolean;
}

export interface QualityProfile {
  name: string;
  formats: string[];
  cutoff: string;
  upgradesAllowed: boolean;
  description: string;
}

export interface HostConfig {
  bindAddress: string;
  port: number;
  urlBase: string;
  instanceName: string;
  apiKey: string;
  authenticationMethod: 'none' | 'forms' | 'basic' | 'external';
  authenticationRequired: 'enabled' | 'disabledForLocalAddresses';
  username: string;
  password?: string;
  hasPassword: boolean;
}

export interface RootFolder {
  id: number;
  path: string;
  freeSpace?: number;
  accessible: boolean;
}

export interface ScheduledTask {
  name: string;
  label: string;
  interval: number;
  lastRun?: string;
  nextRun: string;
}

export interface RenamePreview {
  issueFileId: number;
  issueId: number;
  issue: string;
  existingPath: string;
  newPath: string;
}

export interface MetadataCandidate {
  externalId: string;
  number: string;
  name?: string;
  releaseDate?: string;
  coverUrl?: string;
  localIssueId?: number;
  localHasFile: boolean;
  localHidden: boolean;
}

export interface IssueFileInfo {
  id: number;
  relativePath: string;
  size: number;
  format: string;
  dateAdded: string;
  exists: boolean;
}

let init: Init | null = null;

export const isConnected = () => init !== null;
export const serverInfo = () => init;
export const currentUser = () => init?.user ?? null;
/** Admins (and sample-data mode) can change things; readers can only browse and read. */
export const canManage = () => !init || init.user?.role !== 'reader';
const keyHeader = (): Record<string, string> => (init?.apiKey ? { 'X-Api-Key': init.apiKey } : {});
const withKey = (url: string) => (init?.apiKey ? `${url}${url.includes('?') ? '&' : '?'}apikey=${init.apiKey}` : url);

export function formatBytes(bytes: number) {
  if (bytes >= 1024 ** 4) return `${(bytes / 1024 ** 4).toFixed(1)} TB`;
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`;
  if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
  return `${Math.max(0, Math.round(bytes / 1024))} KB`;
}

export function formatEta(seconds?: number) {
  if (seconds == null) return '—';
  if (seconds < 60) return `${Math.round(seconds)}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s`;
  return `${Math.floor(seconds / 3600)}h ${Math.floor((seconds % 3600) / 60)}m`;
}

export function timeAgo(iso?: string) {
  if (!iso) return '—';
  const diff = (Date.now() - new Date(iso).getTime()) / 1000;
  const future = diff < 0;
  const s = Math.abs(diff);
  const v = s < 60 ? `${Math.round(s)}s` : s < 3600 ? `${Math.round(s / 60)}m` : s < 86400 ? `${Math.round(s / 3600)}h` : `${Math.round(s / 86400)}d`;
  return future ? `in ${v}` : `${v} ago`;
}

export class DemoModeError extends Error {
  constructor() {
    super('Not available with sample data. Run the Issuarr backend to use this.');
  }
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  if (!init) throw new DemoModeError();
  const res = await fetch(`${init.apiRoot}${path}`, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...keyHeader(), ...((options.headers as Record<string, string>) ?? {}) },
  });
  if (res.status === 401 && init.authentication !== 'none') {
    window.location.href = `${init.urlBase}/login?returnUrl=${encodeURIComponent(window.location.pathname)}`;
  }
  if (!res.ok) {
    // Surface the server's explanation (validation problems use "detail", others "message").
    let message = `${options.method ?? 'GET'} ${path} failed: ${res.status}`;
    try {
      const body = await res.json();
      message = body.detail ?? body.message ?? body.title ?? message;
    } catch {
      /* not JSON */
    }
    throw new Error(message);
  }
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

const json = (method: string, body: unknown): RequestInit => ({ method, body: JSON.stringify(body) });

const fallbackPalette: [string, string] = ['#334155', '#64748b'];

export function toTitle(t: ApiTitle): Title {
  const issues: Issue[] = (t.issues ?? []).map((i) => ({
    id: String(i.id),
    serverId: i.id,
    number: i.number,
    title: i.title,
    releaseDate: i.releaseDate,
    status: i.status,
    monitored: i.monitored,
    coverUrl: i.coverUrl,
    readPercent: i.readPercent,
    releaseDateLocked: i.releaseDateLocked,
    file: i.file ? { id: i.file.id, name: i.file.name, size: formatBytes(i.file.size), format: i.file.format } : undefined,
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
    coverUrl: t.cover,
    metadataSource: t.metadataSourceName,
    externalId: t.externalId,
    issues,
    serverStats: {
      have: t.statistics.issueFileCount,
      total: t.statistics.issueCount,
      missing: t.statistics.missingCount,
      sizeBytes: t.statistics.sizeOnDisk,
      next: t.statistics.nextIssue,
      hidden: t.statistics.hiddenCount ?? 0,
    },
  };
}

/** Try to reach the backend. Resolves either way; check isConnected() afterwards. */
export async function bootstrap() {
  try {
    const base = document.querySelector('base')?.getAttribute('href')?.replace(/\/$/, '') ?? '';
    const res = await fetch(`${base}/initialize.json`, { headers: { Accept: 'application/json' } });
    if (res.status === 401) {
      const body = await res.json().catch(() => ({}));
      window.location.href = body.loginUrl ?? `${base}/login`;
      await new Promise(() => {}); // stop rendering while the browser navigates
    }
    if (!res.ok || !res.headers.get('content-type')?.includes('json')) return;
    init = (await res.json()) as Init;
    await reloadTitles();
    // The queue and history live on the Activity pages in live mode.
    queue.splice(0, queue.length);
    history.splice(0, history.length);
    const now = new Date();
    setToday(new Date(now.getFullYear(), now.getMonth(), now.getDate()));
  } catch (e) {
    console.warn('Issuarr backend not reachable, running with sample data.', e);
    init = null;
  }
}

/** Reload the whole library from the server. */
export async function reloadTitles() {
  const list = await request<ApiTitle[]>('/title?includeIssues=true');
  titles.splice(0, titles.length, ...list.map(toTitle));
}

export const api = {
  // Library
  setIssuesMonitored: (issueIds: number[], monitored: boolean) => request('/issue/monitor', json('PUT', { issueIds, monitored })),
  updateTitle: (title: Title, changes: Partial<Title> = {}) => {
    const t = { ...title, ...changes };
    return request<ApiTitle>(`/title/${t.id}`, json('PUT', { ...t, id: Number(t.id), issues: undefined, serverStats: undefined }));
  },
  deleteTitle: (id: string | number, deleteFiles: boolean) => request(`/title/${id}?deleteFiles=${deleteFiles}`, { method: 'DELETE' }),
  lookup: (term: string, source?: string, kind: Title['kind'] = 'comic') =>
    request<LookupResult[]>(`/title/lookup?term=${encodeURIComponent(term)}&kind=${kind}${source ? `&source=${source}` : ''}`),
  lookupBook: (term: string, source?: string) => request<BookResult[]>(`/title/lookup/book?term=${encodeURIComponent(term)}${source ? `&source=${source}` : ''}`),
  matchTitle: async (id: string | number, source: string, externalId: string, useMetadataName: boolean) => {
    if (!init) throw new DemoModeError();
    const res = await fetch(`${init.apiRoot}/title/${id}/match`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...keyHeader() },
      body: JSON.stringify({ source, externalId, useMetadataName }),
    });
    if (!res.ok) {
      const b = await res.json().catch(() => ({}));
      throw new Error(b.detail ?? b.message ?? `Match failed: ${res.status}`);
    }
    const title = toTitle((await res.json()) as ApiTitle);
    const i = titles.findIndex((x) => x.id === title.id);
    if (i >= 0) titles[i] = title;
    return { title, warning: res.headers.get('X-Issuarr-Warning') || undefined };
  },
  unmatchTitle: (id: string | number) => request(`/title/${id}/match`, { method: 'DELETE' }),
  addTitle: async (body: Record<string, unknown>) => {
    if (!init) throw new DemoModeError();
    const res = await fetch(`${init.apiRoot}/title`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...keyHeader() },
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      const b = await res.json().catch(() => ({}));
      throw new Error(b.detail ?? b.message ?? b.title ?? `Add failed: ${res.status}`);
    }
    const title = toTitle((await res.json()) as ApiTitle);
    // Adding a book by an author we already have returns that author: replace, don't duplicate.
    const existing = titles.findIndex((t) => t.id === title.id);
    if (existing >= 0) titles[existing] = title;
    else titles.push(title);
    return { title, warning: res.headers.get('X-Issuarr-Warning') || undefined };
  },
  /** Fetch a title and add (or refresh) it in the in-memory library. */
  loadTitle: async (id: number | string) => {
    const t = toTitle(await request<ApiTitle>(`/title/${id}`));
    const i = titles.findIndex((x) => x.id === t.id);
    if (i >= 0) titles[i] = t;
    else titles.push(t);
    return t;
  },
  candidates: (titleId: string | number) => request<MetadataCandidate[]>(`/title/${titleId}/candidates`),
  matchIssue: (issueId: number, externalId: string) => request('/issue/' + issueId + '/match', json('POST', { externalId })),
  editIssue: (issueId: number, number: string, title: string | undefined, releaseDate: string) =>
    request('/issue/' + issueId, json('PUT', { number, title, releaseDate })),
  setReleaseDate: (issueId: number, releaseDate: string | null) => request(`/issue/${issueId}/releasedate`, json('PUT', { releaseDate })),
  deleteIssue: (issueId: number, deleteFile: boolean) => request(`/issue/${issueId}?deleteFile=${deleteFile}`, { method: 'DELETE' }),
  restoreHidden: (titleId: string | number) => request(`/title/${titleId}/restorehidden`, { method: 'POST' }),
  titleFiles: (id: string | number) => request<IssueFileInfo[]>(`/title/${id}/files`),
  deleteIssueFile: (id: number) => request(`/issuefile/${id}`, { method: 'DELETE' }),
  renamePreview: (id: string | number) => request<RenamePreview[]>(`/title/${id}/rename`),
  missing: () => request<ApiIssue[]>('/wanted/missing'),
  cutoffUnmet: () => request<ApiIssue[]>('/wanted/cutoff'),

  // Searching / activity
  command: (name: string, titleId?: number | string, issueIds?: number[]) =>
    request<Command>('/command', json('POST', { name, titleId: titleId != null ? Number(titleId) : undefined, issueIds })),
  getCommand: (id: number) => request<Command>(`/command/${id}`),
  commands: () => request<Command[]>('/command'),
  releases: (titleId: string | number, issueId?: number) =>
    request<Release[]>(`/release?titleId=${titleId}${issueId != null ? `&issueId=${issueId}` : ''}`),
  grab: (guid: string, titleId: string | number, issueId?: number) =>
    request('/release', json('POST', { guid, titleId: Number(titleId), issueId })),
  queue: () => request<{ totalRecords: number; records: QueueRecord[] }>('/queue'),
  removeFromQueue: (id: string, removeFromClient: boolean, blocklist: boolean) =>
    request(`/queue/${encodeURIComponent(id)}?removeFromClient=${removeFromClient}&blocklist=${blocklist}`, { method: 'DELETE' }),
  history: (page = 1, pageSize = 50, titleId?: string | number) =>
    request<{ totalRecords: number; records: HistoryRecord[] }>(`/history?page=${page}&pageSize=${pageSize}${titleId != null ? `&titleId=${titleId}` : ''}`),
  clearHistory: () => request('/history', { method: 'DELETE' }),
  blocklist: () => request<BlocklistRecord[]>('/blocklist'),
  removeBlocklist: (id: number) => request(`/blocklist/${id}`, { method: 'DELETE' }),
  clearBlocklist: () => request('/blocklist', { method: 'DELETE' }),

  // Settings
  providers: (kind: ProviderKind) => request<Provider[]>(`/${kind}`),
  providerSchema: (kind: ProviderKind) => request<ProviderSchema[]>(`/${kind}/schema`),
  saveProvider: (kind: ProviderKind, p: Partial<Provider>) =>
    p.id ? request<Provider>(`/${kind}/${p.id}`, json('PUT', p)) : request<Provider>(`/${kind}`, json('POST', p)),
  deleteProvider: (kind: ProviderKind, id: number) => request(`/${kind}/${id}`, { method: 'DELETE' }),
  testProvider: (kind: ProviderKind, p: Partial<Provider>) => request<{ message: string }>(`/${kind}/test`, json('POST', p)),
  config: () => request<Record<string, string>>('/config'),
  saveConfig: (values: Record<string, string>) => request<Record<string, string>>('/config', json('PUT', values)),
  hostConfig: () => request<HostConfig>('/config/host'),
  saveHostConfig: (c: HostConfig) => request<{ config: HostConfig; restartNeeded: boolean }>('/config/host', json('PUT', c)),
  regenerateApiKey: () => request<{ apiKey: string }>('/config/host/apikey', { method: 'POST' }),
  qualityProfiles: () => request<QualityProfile[]>('/qualityprofile'),
  rootFolders: () => request<RootFolder[]>('/rootfolder'),
  addRootFolder: (path: string) => request('/rootfolder', json('POST', { path })),
  deleteRootFolder: (id: number) => request(`/rootfolder/${id}`, { method: 'DELETE' }),
  browse: (path: string) => request<FileSystemListing>(`/filesystem?path=${encodeURIComponent(path)}`),
  libraryScan: (path: string) => request<ImportCandidate[]>(`/libraryimport?path=${encodeURIComponent(path)}`),
  libraryImport: (items: { path: string; name: string; year?: number; kind: Title['kind']; metadataSource?: string; externalId?: string; coverUrl?: string }[]) =>
    request<ImportResult[]>('/libraryimport', json('POST', items)),

  // System
  health: () => request<HealthItem[]>('/health'),
  status: () => request<Record<string, string | boolean>>('/system/status'),
  tasks: () => request<ScheduledTask[]>('/system/task'),
};

export interface ReadInfo {
  issueId: number;
  titleId: number;
  titleName: string;
  titleSlug: string;
  kind: Title['kind'];
  number: string;
  issueTitle?: string;
  format: string;
  mode: 'comic' | 'pdf' | 'epub' | 'download';
  pages?: number;
  size: number;
  fileName: string;
  error?: string;
  progress?: { position: string; percent: number; updated: string };
  previousId?: number;
  nextId?: number;
}

export const reader = {
  info: (issueId: number | string) => request<ReadInfo>(`/read/${issueId}`),
  pageUrl: (issueId: number | string, page: number) => (init ? withKey(`${init.apiRoot}/read/${issueId}/page/${page}`) : ''),
  fileUrl: (issueId: number | string, download = false) => (init ? withKey(`${init.apiRoot}/read/${issueId}/file${download ? '?download=true' : ''}`) : ''),
  saveProgress: (issueId: number | string, position: string, percent: number) =>
    request(`/read/${issueId}/progress`, json('PUT', { position, percent })),
  clearProgress: (issueId: number | string) => request(`/read/${issueId}/progress`, { method: 'DELETE' }),
};

/** Images served by Issuarr itself (custom covers, file pages) need the API key in the URL. */
export function mediaUrl(url?: string) {
  if (!url || !init || !url.startsWith('/api/')) return url;
  return withKey(`${init.urlBase}${url}`);
}

export interface CoverCandidate {
  source: 'metadata' | 'issue' | 'file' | 'pdf';
  url: string;
  label: string;
}

export interface ShelfItem {
  issueId: number;
  titleId: number;
  titleName: string;
  titleSlug: string;
  kind: Title['kind'];
  number: string;
  issueTitle?: string;
  format: string;
  percent?: number;
  when?: string;
}

export const covers = {
  candidates: (titleId: string | number) => request<CoverCandidate[]>(`/title/${titleId}/covers`),
  fromUrl: (titleId: string | number, url: string) => request(`/title/${titleId}/cover`, json('PUT', { url })),
  reset: (titleId: string | number) => request(`/title/${titleId}/cover`, { method: 'DELETE' }),
  upload: async (titleId: string | number, blob: Blob) => {
    if (!init) throw new DemoModeError();
    const form = new FormData();
    form.append('file', blob, 'cover');
    const res = await fetch(`${init.apiRoot}/title/${titleId}/cover`, { method: 'POST', headers: keyHeader(), body: form });
    if (!res.ok) {
      const b = await res.json().catch(() => ({}));
      throw new Error(b.message ?? b.title ?? `Upload failed: ${res.status}`);
    }
  },
  issueCoverUrl: (issueId: number) => mediaUrl(`/api/v1/read/${issueId}/cover`)!,
};

export const reading = {
  home: () =>
    request<{ continueReading: ShelfItem[]; upNext: ShelfItem[]; recentlyAdded: ShelfItem[]; latestComics: ShelfItem[]; latestMagazines: ShelfItem[]; latestBooks: ShelfItem[] }>('/reading/home'),
  markRead: (issueId: number) => request(`/read/${issueId}/progress`, json('PUT', { position: 'end', percent: 100 })),
  clear: (issueId: number) => request(`/read/${issueId}/progress`, { method: 'DELETE' }),
};

export function kosyncUrl() {
  return init ? `${window.location.origin}${init.urlBase}/kosync` : '';
}

export function opdsUrl(withKey: boolean) {
  if (!init) return '';
  return `${window.location.origin}${init.urlBase}/opds${withKey && init.apiKey ? `?apikey=${init.apiKey}` : ''}`;
}

/** URL of the iCal feed (includes the API key so calendar apps can fetch it). */
export function calendarFeedUrl() {
  if (!init) return '';
  return `${window.location.origin}${init.urlBase}/feed/v1/calendar/issuarr.ics?apikey=${init.apiKey}`;
}

export interface UserInfo {
  id: number;
  username: string;
  role: 'admin' | 'reader';
  isOwner: boolean;
  created: string;
  lastSeen?: string;
  koSync: boolean;
}

export interface InviteInfo {
  token: string;
  role: 'admin' | 'reader';
  note?: string;
  created: string;
  expires: string;
}

export const users = {
  list: () => request<UserInfo[]>('/user'),
  update: (id: number, change: { role?: string; password?: string }) => request<UserInfo>(`/user/${id}`, json('PUT', change)),
  remove: (id: number) => request(`/user/${id}`, { method: 'DELETE' }),
  invites: () => request<InviteInfo[]>('/user/invite'),
  invite: (role: string, note: string, days: number) => request<InviteInfo>('/user/invite', json('POST', { role, note, days })),
  revoke: (token: string) => request(`/user/invite/${token}`, { method: 'DELETE' }),
  inviteUrl: (token: string) => `${window.location.origin}${init?.urlBase ?? ''}/invite/${token}`,
  me: () => request<{ user: UserInfo; koSyncEnabled: boolean }>('/me'),
  changePassword: (current: string, password: string) => request('/me/password', json('PUT', { current, password })),
  setKoSyncPassword: (password: string | null) => request('/me/kosync', json('PUT', { password })),
};
