import {
  Activity,
  BookOpen,
  CalendarDays,
  Cog,
  HeartPulse,
  Menu,
  Moon,
  Search,
  Sun,
  TriangleAlert,
  Bell,
  House,
  CheckCircle2,
  CircleAlert,
  Info,
} from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { queue, titles, kindLabel, stats } from '../data/mock';
import { Cover } from './Cover';
import { api, isConnected, serverInfo, type HealthItem } from '../data/api';
import { useLibraryVersion } from '../data/store';


const buildNav = (missingCount: number, queueCount: number) => [
  { to: '/', label: 'Home', icon: House, home: true },
  {
    to: '/library',
    label: 'Library',
    icon: BookOpen,
    end: true,
    sub: [
      { to: '/add', label: 'Add New' },
      { to: '/import', label: 'Library Import' },
    ],
  },
  { to: '/calendar', label: 'Calendar', icon: CalendarDays },
  {
    to: '/activity',
    label: 'Activity',
    icon: Activity,
    count: queueCount,
    sub: [
      { to: '/activity', label: 'Queue' },
      { to: '/activity/history', label: 'History' },
      { to: '/activity/blocklist', label: 'Blocklist' },
    ],
  },
  {
    to: '/wanted',
    label: 'Wanted',
    icon: TriangleAlert,
    count: missingCount,
    sub: [
      { to: '/wanted', label: 'Missing' },
      { to: '/wanted/cutoff', label: 'Cutoff Unmet' },
    ],
  },
  {
    to: '/settings',
    label: 'Settings',
    icon: Cog,
    sub: [
      { to: '/settings/media', label: 'Media Management' },
      { to: '/settings/profiles', label: 'Profiles' },
      { to: '/settings/indexers', label: 'Indexers' },
      { to: '/settings/downloadclients', label: 'Download Clients' },
      { to: '/settings/connect', label: 'Connect' },
      { to: '/settings/general', label: 'General' },
    ],
  },
  { to: '/system', label: 'System', icon: HeartPulse },
];

function useTheme() {
  const [theme, setTheme] = useState<'dark' | 'light'>(() => {
    try {
      return (localStorage.getItem('issuarr-theme') as 'dark' | 'light') || 'dark';
    } catch {
      return 'dark';
    }
  });
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      localStorage.setItem('issuarr-theme', theme);
    } catch {
      /* private mode */
    }
  }, [theme]);
  return [theme, () => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))] as const;
}

function GlobalSearch() {
  const [q, setQ] = useState('');
  const [open, setOpen] = useState(false);
  const [sel, setSel] = useState(0);
  const ref = useRef<HTMLInputElement>(null);
  const navigate = useNavigate();

  const results = useMemo(() => {
    const s = q.trim().toLowerCase();
    if (!s) return [];
    return titles.filter((t) => t.name.toLowerCase().includes(s) || t.publisher.toLowerCase().includes(s)).slice(0, 6);
  }, [q]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === '/' && document.activeElement?.tagName !== 'INPUT') {
        e.preventDefault();
        ref.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const go = (slug: string) => {
    navigate(`/title/${slug}`);
    setQ('');
    setOpen(false);
    ref.current?.blur();
  };

  return (
    <div className="search">
      <Search size={16} />
      <input
        ref={ref}
        value={q}
        placeholder="Search your library"
        onChange={(e) => {
          setQ(e.target.value);
          setSel(0);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 120)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') setSel((s) => Math.min(s + 1, results.length - 1));
          if (e.key === 'ArrowUp') setSel((s) => Math.max(s - 1, 0));
          if (e.key === 'Enter' && results[sel]) go(results[sel].slug);
          if (e.key === 'Escape') ref.current?.blur();
        }}
      />
      {!q && <kbd>/</kbd>}
      {open && q && (
        <div className="search-results">
          {results.length === 0 && (
            <Link to={`/add?term=${encodeURIComponent(q)}`} style={{ padding: 8, display: 'block' }}>
              No matches. <span style={{ color: 'var(--accent)' }}>Search for "{q}" to add it →</span>
            </Link>
          )}
          {results.map((t, i) => (
            <a
              key={t.id}
              className={i === sel ? 'active' : ''}
              onMouseDown={(e) => {
                e.preventDefault();
                go(t.slug);
              }}
              href={`/title/${t.slug}`}
            >
              <div className="thumb">
                <Cover title={t} size="sm" />
              </div>
              <div>
                <div style={{ fontWeight: 600 }}>{t.name}</div>
                <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>
                  {kindLabel[t.kind]} · {t.publisher} · {t.year}
                </div>
              </div>
            </a>
          ))}
        </div>
      )}
    </div>
  );
}

export function Layout() {
  const [theme, toggleTheme] = useTheme();
  const [menuOpen, setMenuOpen] = useState(false);
  const location = useLocation();
  useLibraryVersion();
  const [queueCount, setQueueCount] = useState(isConnected() ? 0 : queue.length);
  const [health, setHealth] = useState<HealthItem[]>([]);
  const [healthOpen, setHealthOpen] = useState(false);
  const healthRef = useRef<HTMLDivElement>(null);

  // Poll the queue size and health every 30s for the sidebar badge and the bell.
  useEffect(() => {
    if (!isConnected()) return;
    const poll = () => {
      api.queue().then((q) => setQueueCount(q.records.length)).catch(() => {});
      api.health().then(setHealth).catch(() => {});
    };
    poll();
    const t = setInterval(poll, 30000);
    return () => clearInterval(t);
  }, [location.pathname]);

  useEffect(() => {
    const close = (e: MouseEvent) => healthRef.current && !healthRef.current.contains(e.target as Node) && setHealthOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);

  const nav = buildNav(titles.reduce((n, t) => n + (t.monitored ? stats(t).missing : 0), 0), queueCount);
  const worst = health.some((h) => h.type === 'error') ? 'var(--danger)' : health.some((h) => h.type === 'warning') ? 'var(--warn)' : health.length ? 'var(--info)' : null;

  useEffect(() => setMenuOpen(false), [location.pathname]);

  return (
    <div className="shell">
      <Link to="/" className="brand">
        <img src="/logo.svg" alt="" />
        <span>
          Issu<span className="arr">arr</span>
        </span>
      </Link>

      <header className="header">
        <button className="icon-btn mobile-only" onClick={() => setMenuOpen((o) => !o)} aria-label="Menu">
          <Menu size={20} />
        </button>
        <GlobalSearch />
        <span className="header-spacer" />
        {!isConnected() && (
          <span className="pill warn" title="The Issuarr backend isn't reachable, so the UI is showing sample data.">
            Demo data
          </span>
        )}
        <div className="rel-wrap" ref={healthRef}>
          <button className="icon-btn" aria-label="Health" onClick={() => setHealthOpen((o) => !o)}>
            <Bell size={19} />
            {worst && <span className="dot" style={{ background: worst }} />}
          </button>
          {healthOpen && (
            <div className="menu health-pop">
              <div style={{ fontWeight: 600, padding: '6px 10px' }}>Health</div>
              {!isConnected() && <div className="muted" style={{ padding: 10 }}>Showing sample data. Run the Issuarr backend for live status.</div>}
              {isConnected() && health.length === 0 && (
                <div className="health">
                  <CheckCircle2 size={18} color="var(--ok)" /> Everything looks good
                </div>
              )}
              {health.map((h) => (
                <Link key={h.source + h.message} to={h.link ?? '/system'} className="health" onClick={() => setHealthOpen(false)} style={{ display: 'flex' }}>
                  {h.type === 'error' ? <CircleAlert size={18} color="var(--danger)" /> : h.type === 'warning' ? <TriangleAlert size={18} color="var(--warn)" /> : <Info size={18} color="var(--info)" />}
                  <span style={{ fontSize: 13 }}>{h.message}</span>
                </Link>
              ))}
            </div>
          )}
        </div>
        <button className="icon-btn" onClick={toggleTheme} aria-label="Toggle theme">
          {theme === 'dark' ? <Sun size={19} /> : <Moon size={19} />}
        </button>
      </header>

      <nav className={`sidebar ${menuOpen ? 'open' : ''}`}>
        {nav.map((item) => {
          const active = 'home' in item
            ? location.pathname === '/'
            : item.end
              ? location.pathname.startsWith('/library') || location.pathname.startsWith('/title') || ['/add', '/import'].includes(location.pathname)
              : location.pathname.startsWith(item.to);
          const Icon = item.icon;
          return (
            <div className="nav-group" key={item.to}>
              <NavLink to={item.to} className={() => `nav-link ${active ? 'active' : ''}`} end={item.end}>
                <Icon size={19} strokeWidth={1.8} />
                {item.label}
                {item.count ? <span className="count">{item.count}</span> : null}
              </NavLink>
              {active && item.sub && (
                <div className="nav-sub">
                  {item.sub.map((s) => (
                    <NavLink key={s.to} to={s.to} end>
                      {s.label}
                    </NavLink>
                  ))}
                </div>
              )}
            </div>
          );
        })}

        <div className="sidebar-footer">
          <div style={{ fontWeight: 600, color: 'var(--text)' }}>Issuarr v{serverInfo()?.version ?? '0.1.0'}</div>
          <div className="row">
            <span>Library</span>
            <span>{titles.length} titles</span>
          </div>
          <div className="row">
            <span>Mode</span>
            <span>{isConnected() ? 'Live' : 'Demo'}</span>
          </div>
        </div>
      </nav>

      <main className="main">
        <Outlet />
      </main>
    </div>
  );
}
