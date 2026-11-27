export type Kind = 'comic' | 'magazine';
export type IssueStatus = 'downloaded' | 'missing' | 'downloading' | 'unaired' | 'unmonitored';

export interface Issue {
  id: string;
  number: string;
  title?: string;
  releaseDate: string; // ISO date
  status: IssueStatus;
  monitored: boolean;
  serverId?: number;
  coverUrl?: string;
  file?: { id?: number; name: string; size: string; format: string };
}

export interface Title {
  id: string;
  /** Server statistics; when absent they're computed from issues (demo data). */
  serverStats?: { have: number; total: number; missing: number; sizeBytes: number; next?: string };
  coverUrl?: string;
  metadataSource?: string;
  externalId?: string;
  slug: string;
  name: string;
  kind: Kind;
  publisher: string;
  year: number;
  frequency: string;
  status: 'continuing' | 'ended';
  monitored: boolean;
  qualityProfile: string;
  path: string;
  overview: string;
  tags: string[];
  // two colours used to paint the generated placeholder cover
  palette: [string, string];
  issues: Issue[];
}

// Small deterministic PRNG so the mock library is stable between reloads.
function rng(seed: number) {
  return () => {
    seed = (seed * 16807) % 2147483647;
    return (seed - 1) / 2147483646;
  };
}

// Fixed "today" so the demo data always looks the same.
const TODAY = new Date(2026, 10, 26);

function addDays(d: Date, n: number) {
  const x = new Date(d);
  x.setDate(x.getDate() + n);
  return x;
}

export function iso(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Parse a YYYY-MM-DD string as a local date (avoids UTC off-by-one). */
export function parseDate(s: string) {
  const [y, m, d] = s.split('-').map(Number);
  return new Date(y, m - 1, d);
}

interface Seed {
  name: string;
  kind: Kind;
  publisher: string;
  year: number;
  frequency: string;
  cadenceDays: number;
  count: number;
  ended?: boolean;
  monitored?: boolean;
  palette: [string, string];
  overview: string;
  tags?: string[];
  profile?: string;
}

const seeds: Seed[] = [
  {
    name: 'Starfall Rangers',
    kind: 'comic',
    publisher: 'Northlight Comics',
    year: 2023,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 38,
    palette: ['#3a1c71', '#d76d77'],
    overview:
      'A disgraced orbital pilot and a crew of misfit salvagers chase a signal no one else can hear across a collapsing star system.',
    tags: ['sci-fi', 'ongoing'],
  },
  {
    name: 'The Hollow Crown',
    kind: 'comic',
    publisher: 'Gravel & Ink',
    year: 2021,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 24,
    ended: true,
    palette: ['#1f1c2c', '#928dab'],
    overview: 'A gothic fantasy about a kingdom ruled by an empty throne and the court that pretends otherwise.',
    tags: ['fantasy'],
  },
  {
    name: 'Circuit & Solder',
    kind: 'magazine',
    publisher: 'Workbench Media',
    year: 2015,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 30,
    palette: ['#0f2027', '#2c5364'],
    overview: 'Hands-on electronics projects, kit reviews and repair guides for makers of every level.',
    tags: ['tech', 'diy'],
    profile: 'PDF Preferred',
  },
  {
    name: 'Trail Weekly',
    kind: 'magazine',
    publisher: 'Open Country Press',
    year: 2019,
    frequency: 'Weekly',
    cadenceDays: 7,
    count: 52,
    palette: ['#134e5e', '#71b280'],
    overview: 'Routes, gear tests and stories from the hiking and trail running community, every week.',
    tags: ['outdoors'],
    profile: 'PDF Preferred',
  },
  {
    name: 'Neon Alley',
    kind: 'comic',
    publisher: 'Northlight Comics',
    year: 2025,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 14,
    palette: ['#fc466b', '#3f5efb'],
    overview: 'A cyberpunk noir following a courier who delivers memories instead of packages.',
    tags: ['sci-fi', 'noir'],
  },
  {
    name: 'The Kitchen Table',
    kind: 'magazine',
    publisher: 'Hearth Publishing',
    year: 2012,
    frequency: 'Bi-monthly',
    cadenceDays: 60,
    count: 18,
    palette: ['#f12711', '#f5af19'],
    overview: 'Seasonal home cooking, baking and the stories behind family recipes.',
    tags: ['food'],
    profile: 'PDF Preferred',
  },
  {
    name: 'Moth & Lantern',
    kind: 'comic',
    publisher: 'Small Hours',
    year: 2024,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 22,
    monitored: false,
    palette: ['#232526', '#414345'],
    overview: 'A quiet horror anthology. Every issue, a new town and a new light that should not be followed.',
    tags: ['horror', 'anthology'],
  },
  {
    name: 'Pixel Quarterly',
    kind: 'magazine',
    publisher: 'Respawn Media',
    year: 2018,
    frequency: 'Quarterly',
    cadenceDays: 91,
    count: 16,
    palette: ['#8e2de2', '#4a00e0'],
    overview: 'Long-form features on game design, preservation and the people who make games.',
    tags: ['games'],
  },
  {
    name: 'Iron Tide',
    kind: 'comic',
    publisher: 'Gravel & Ink',
    year: 2022,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 46,
    palette: ['#2c3e50', '#4ca1af'],
    overview: 'Naval war epic set on an ocean world where the tides are controlled by a single empire.',
    tags: ['war', 'ongoing'],
  },
  {
    name: 'Field Notes Monthly',
    kind: 'magazine',
    publisher: 'Open Country Press',
    year: 2010,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 26,
    palette: ['#56ab2f', '#a8e063'],
    overview: 'Birding, botany and backyard science for curious naturalists.',
    tags: ['nature'],
    profile: 'PDF Preferred',
  },
  {
    name: 'Paper Ghosts',
    kind: 'comic',
    publisher: 'Small Hours',
    year: 2020,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 12,
    ended: true,
    palette: ['#e0eafc', '#9aa9c4'],
    overview: 'Two sisters inherit a print shop haunted by every story it ever published.',
    tags: ['mystery'],
  },
  {
    name: 'Velocity',
    kind: 'magazine',
    publisher: 'Apex Motor Group',
    year: 2016,
    frequency: 'Monthly',
    cadenceDays: 30,
    count: 28,
    palette: ['#cb2d3e', '#ef473a'],
    overview: 'Track tests, restoration builds and motorsport coverage.',
    tags: ['cars'],
  },
];

function weekOfYear(d: Date) {
  const start = new Date(d.getFullYear(), 0, 1);
  return Math.ceil(((d.getTime() - start.getTime()) / 86400000 + start.getDay() + 1) / 7);
}

const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

function buildIssues(seed: Seed, idx: number): Issue[] {
  const r = rng(idx * 7919 + 13);
  const issues: Issue[] = [];
  // Last issue is ~1 cadence into the future for continuing titles.
  const lastOffset = seed.ended ? -200 : Math.round(seed.cadenceDays * (0.3 + r()));
  for (let i = 0; i < seed.count; i++) {
    const fromEnd = seed.count - 1 - i;
    const date = addDays(TODAY, lastOffset - fromEnd * seed.cadenceDays);
    const future = date > TODAY;
    let status: IssueStatus;
    const roll = r();
    if (future) status = 'unaired';
    else if (seed.monitored === false) status = roll < 0.4 ? 'downloaded' : 'unmonitored';
    else if (fromEnd <= 1 && !seed.ended && roll < 0.5) status = 'downloading';
    else if (roll < 0.82) status = 'downloaded';
    else status = 'missing';

    const number =
      seed.kind === 'comic'
        ? `#${i + 1}`
        : seed.frequency === 'Weekly'
          ? `${date.getFullYear()}-W${String(weekOfYear(date)).padStart(2, '0')}`
          : `${months[date.getMonth()]} ${date.getFullYear()}`;

    const ext = seed.kind === 'comic' ? 'cbz' : 'pdf';
    issues.push({
      id: `${idx}-${i}`,
      number,
      title: seed.kind === 'comic' && r() < 0.35 ? chapterNames[Math.floor(r() * chapterNames.length)] : undefined,
      releaseDate: iso(date),
      status,
      monitored: seed.monitored !== false && status !== 'unmonitored',
      file:
        status === 'downloaded'
          ? {
              name: `${seed.name} ${number.replace('#', '')}.${ext}`,
              size: `${(seed.kind === 'comic' ? 28 + r() * 60 : 40 + r() * 120).toFixed(1)} MB`,
              format: ext.toUpperCase(),
            }
          : undefined,
    });
  }
  return issues.reverse();
}

const chapterNames = [
  'First Light',
  'The Long Dark',
  'Undertow',
  'Signal Lost',
  'Ashes',
  'Homecoming',
  'Fault Lines',
  'Paper Moon',
  'The Turning',
  'Low Tide',
];

export const titles: Title[] = seeds.map((s, i) => {
  const slug = s.name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
  return {
    id: String(i + 1),
    slug,
    name: s.name,
    kind: s.kind,
    publisher: s.publisher,
    year: s.year,
    frequency: s.frequency,
    status: s.ended ? 'ended' : 'continuing',
    monitored: s.monitored !== false,
    qualityProfile: s.profile ?? (s.kind === 'comic' ? 'CBZ / CBR' : 'Any'),
    path: `/data/${s.kind === 'comic' ? 'comics' : 'magazines'}/${s.name}`,
    overview: s.overview,
    tags: s.tags ?? [],
    palette: s.palette,
    issues: buildIssues(s, i + 1),
  };
});

export function stats(t: Title) {
  const aired = t.issues.filter((i) => i.status !== 'unaired');
  const have = aired.filter((i) => i.status === 'downloaded').length;
  const monitoredAired = aired.filter((i) => i.monitored).length;
  const next = [...t.issues].filter((i) => i.status === 'unaired').sort((a, b) => a.releaseDate.localeCompare(b.releaseDate))[0];
  const sizeMb = t.serverStats
    ? t.serverStats.sizeBytes / 1024 ** 2
    : t.issues.reduce((acc, i) => acc + (i.file ? parseFloat(i.file.size) : 0), 0);
  return {
    have,
    total: monitoredAired || aired.length,
    missing: aired.filter((i) => i.status === 'missing').length,
    next,
    size: sizeMb > 1024 ? `${(sizeMb / 1024).toFixed(1)} GB` : `${sizeMb.toFixed(0)} MB`,
  };
}

export interface QueueItem {
  id: string;
  titleId: string;
  issue: string;
  release: string;
  client: string;
  protocol: 'torrent' | 'usenet';
  indexer: string;
  size: string;
  progress: number;
  eta: string;
  state: 'downloading' | 'queued' | 'importing' | 'warning';
  message?: string;
}

export const queue: QueueItem[] = [
  {
    id: 'q1',
    titleId: '1',
    issue: '#37',
    release: 'Starfall.Rangers.037.2026.Digital.cbz',
    client: 'qBittorrent',
    protocol: 'torrent',
    indexer: 'ComicTorrents (Prowlarr)',
    size: '64.2 MB',
    progress: 72,
    eta: '1m 12s',
    state: 'downloading',
  },
  {
    id: 'q2',
    titleId: '4',
    issue: '2026-W48',
    release: 'Trail.Weekly.2026.W48.True.PDF',
    client: 'SABnzbd',
    protocol: 'usenet',
    indexer: 'NZBplanet (Prowlarr)',
    size: '118.4 MB',
    progress: 100,
    eta: '—',
    state: 'importing',
  },
  {
    id: 'q3',
    titleId: '9',
    issue: '#46',
    release: 'Iron.Tide.046.2026.Webrip.cbr',
    client: 'Deluge',
    protocol: 'torrent',
    indexer: 'Torznab Generic',
    size: '81.0 MB',
    progress: 18,
    eta: '6m 40s',
    state: 'downloading',
  },
  {
    id: 'q4',
    titleId: '5',
    issue: '#13',
    release: 'Neon.Alley.013.2026.cbz',
    client: 'Transmission',
    protocol: 'torrent',
    indexer: 'ComicTorrents (Prowlarr)',
    size: '52.7 MB',
    progress: 0,
    eta: 'Stalled',
    state: 'warning',
    message: 'No seeders available. Download has been stalled for 2h.',
  },
  {
    id: 'q5',
    titleId: '3',
    issue: 'Nov 2026',
    release: 'Circuit.and.Solder.November.2026.PDF',
    client: 'SABnzbd',
    protocol: 'usenet',
    indexer: 'NZBgeek (Prowlarr)',
    size: '96.3 MB',
    progress: 0,
    eta: 'Queued',
    state: 'queued',
  },
];

export interface HistoryItem {
  id: string;
  titleId: string;
  issue: string;
  event: 'grabbed' | 'imported' | 'failed' | 'deleted';
  source: string;
  date: string;
}

export const history: HistoryItem[] = [
  { id: 'h1', titleId: '1', issue: '#36', event: 'imported', source: 'qBittorrent', date: '2026-11-25 21:14' },
  { id: 'h2', titleId: '1', issue: '#36', event: 'grabbed', source: 'ComicTorrents', date: '2026-11-25 21:02' },
  { id: 'h3', titleId: '10', issue: 'Nov 2026', event: 'imported', source: 'SABnzbd', date: '2026-11-24 08:40' },
  { id: 'h4', titleId: '12', issue: 'Nov 2026', event: 'failed', source: 'SABnzbd', date: '2026-11-23 17:55' },
  { id: 'h5', titleId: '9', issue: '#45', event: 'imported', source: 'Deluge', date: '2026-11-22 12:31' },
  { id: 'h6', titleId: '8', issue: 'Q3 2026', event: 'deleted', source: 'Upgrade', date: '2026-11-20 09:10' },
];

export const kindLabel: Record<Kind, string> = { comic: 'Comic', magazine: 'Magazine' };
// Live binding: switched to the real date when connected to a backend (see api.ts).
export let today = TODAY;
export function setToday(d: Date) {
  today = d;
}
