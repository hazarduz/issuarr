import {
  ArrowDownUp,
  Filter,
  LayoutGrid,
  List,
  Plus,
  RefreshCw,
  Rows3,
  Search as SearchIcon,
} from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Cover } from '../components/Cover';
import { MonitorToggle, ProgressBar } from '../components/Status';
import { Grow, Sep, Tool, Toolbar } from '../components/Toolbar';
import { useToast } from '../components/Toast';
import { api, isConnected } from '../data/api';
import { kindLabel, parseDate, stats, titles, type Kind, type Title } from '../data/mock';
import { notifyLibrary, runCommand, useLibraryVersion } from '../data/store';

type View = 'posters' | 'overview' | 'table';
type Sort = 'name' | 'missing' | 'next' | 'added';
type FilterId = 'all' | 'monitored' | 'unmonitored' | 'missing' | 'continuing' | 'ended' | 'unmatched';

const filters: { id: FilterId; label: string; test: (t: Title) => boolean }[] = [
  { id: 'all', label: 'All', test: () => true },
  { id: 'monitored', label: 'Monitored only', test: (t) => t.monitored },
  { id: 'unmonitored', label: 'Unmonitored', test: (t) => !t.monitored },
  { id: 'missing', label: 'Missing issues', test: (t) => stats(t).missing > 0 },
  { id: 'continuing', label: 'Continuing', test: (t) => t.status === 'continuing' },
  { id: 'ended', label: 'Ended', test: (t) => t.status === 'ended' },
  { id: 'unmatched', label: 'Not matched to ComicVine/Metron', test: (t) => t.kind === 'comic' && !t.metadataSource },
];

function readView(): View {
  try {
    return (localStorage.getItem('issuarr-view') as View) || 'posters';
  } catch {
    return 'posters';
  }
}

function fmtDate(d?: string) {
  if (!d) return '—';
  return parseDate(d).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
}

export default function Library() {
  useLibraryVersion();
  const [view, setViewState] = useState<View>(readView);
  const [filter, setFilter] = useState<FilterId>('all');
  const [filterOpen, setFilterOpen] = useState(false);
  const [searching, setSearching] = useState(false);
  const filterRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const close = (e: MouseEvent) => filterRef.current && !filterRef.current.contains(e.target as Node) && setFilterOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);
  const [kind, setKind] = useState<'all' | Kind>('all');
  const [sort, setSort] = useState<Sort>('name');
  const [refreshing, setRefreshing] = useState(false);
  const [posterSize, setPosterSize] = useState(168);
  const toast = useToast();
  const navigate = useNavigate();

  const setView = (v: View) => {
    setViewState(v);
    try {
      localStorage.setItem('issuarr-view', v);
    } catch {
      /* ignore */
    }
  };

  const list = useMemo(() => {
    const test = filters.find((f) => f.id === filter)!.test;
    const l = titles.filter((t) => (kind === 'all' || t.kind === kind) && test(t));
    const s = [...l];
    if (sort === 'name') s.sort((a, b) => a.name.localeCompare(b.name));
    if (sort === 'missing') s.sort((a, b) => stats(b).missing - stats(a).missing);
    if (sort === 'next')
      s.sort((a, b) => (stats(a).next?.releaseDate ?? '9999').localeCompare(stats(b).next?.releaseDate ?? '9999'));
    if (sort === 'added') s.sort((a, b) => b.year - a.year);
    return s;
    // eslint-disable-next-line
  }, [kind, sort, filter, titles.length, titles.map((t) => `${t.monitored}${t.issues.length}`).join()]);

  const sorts: Sort[] = ['name', 'missing', 'next', 'added'];
  const sortLabel: Record<Sort, string> = { name: 'Title', missing: 'Missing', next: 'Next Issue', added: 'Year' };

  const refresh = async () => {
    if (!isConnected()) return toast('Not available with sample data. Run the Issuarr backend to use this.', 'error');
    setRefreshing(true);
    toast('Refreshing every title…');
    try {
      const cmd = await runCommand('RefreshAll');
      toast(cmd.message ?? 'Library refreshed');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Refresh failed', 'error');
    } finally {
      setRefreshing(false);
    }
  };

  const searchMissing = async () => {
    if (!isConnected()) return toast('Not available with sample data. Run the Issuarr backend to use this.', 'error');
    setSearching(true);
    toast(`Searching for ${totals.missing} missing issues…`);
    try {
      const cmd = await runCommand('MissingSearch');
      toast(cmd.message ?? 'Search finished');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Search failed', 'error');
    } finally {
      setSearching(false);
    }
  };

  const toggleMonitored = (t: Title) => {
    t.monitored = !t.monitored;
    notifyLibrary();
    if (isConnected()) {
      api.updateTitle(t, { monitored: t.monitored }).then(() => api.loadTitle(t.id)).then(notifyLibrary).catch(() => {
        t.monitored = !t.monitored;
        notifyLibrary();
        toast('Could not save', 'error');
      });
    }
  };

  const totals = titles.reduce(
    (acc, t) => {
      const s = stats(t);
      acc.have += s.have;
      acc.missing += s.missing;
      return acc;
    },
    { have: 0, missing: 0 },
  );

  return (
    <>
      <Toolbar>
        <Tool icon={RefreshCw} label="Update All" onClick={refresh} spin={refreshing} />
        <Tool icon={SearchIcon} label="Search Missing" spin={searching} onClick={searchMissing} />
        <Tool icon={Plus} label="Add New" onClick={() => navigate('/add')} />
        <Grow />
        <div className="segmented" style={{ marginRight: 8 }}>
          {(['all', 'comic', 'magazine'] as const).map((k) => (
            <button key={k} className={kind === k ? 'on' : ''} onClick={() => setKind(k)}>
              {k === 'all' ? 'All' : `${kindLabel[k]}s`}
            </button>
          ))}
        </div>
        <Sep />
        <Tool icon={LayoutGrid} label="Posters" on={view === 'posters'} onClick={() => setView('posters')} />
        <Tool icon={Rows3} label="Overview" on={view === 'overview'} onClick={() => setView('overview')} />
        <Tool icon={List} label="Table" on={view === 'table'} onClick={() => setView('table')} />
        <Sep />
        <Tool
          icon={ArrowDownUp}
          label={`Sort: ${sortLabel[sort]}`}
          onClick={() => setSort(sorts[(sorts.indexOf(sort) + 1) % sorts.length])}
        />
        <div className="rel-wrap" ref={filterRef}>
          <Tool icon={Filter} label={filter === 'all' ? 'Filter' : filters.find((f) => f.id === filter)!.label} on={filter !== 'all'} onClick={() => setFilterOpen((o) => !o)} />
          {filterOpen && (
            <div className="menu">
              {filters.map((f) => (
                <button
                  key={f.id}
                  className={filter === f.id ? 'on' : ''}
                  onClick={() => {
                    setFilter(f.id);
                    setFilterOpen(false);
                  }}
                >
                  {f.label}
                </button>
              ))}
            </div>
          )}
        </div>
      </Toolbar>

      <div className="page">
        <h1 className="page-title">
          Library
          <small>
            {list.length} titles · {totals.have} issues on disk · {totals.missing} missing
          </small>
          {view === 'posters' && (
            <input
              type="range"
              min={130}
              max={240}
              value={posterSize}
              onChange={(e) => setPosterSize(+e.target.value)}
              style={{ marginLeft: 'auto', accentColor: 'var(--accent)' }}
              aria-label="Poster size"
            />
          )}
        </h1>

        {list.length === 0 && (
          <div className="card empty">
            <div>
              <div style={{ fontWeight: 600, color: 'var(--text)', fontSize: 16 }}>{titles.length === 0 ? 'Your library is empty' : 'Nothing matches this filter'}</div>
              {titles.length === 0 && (
                <>
                  <p>Add a comic series or magazine to start tracking issues, or import a collection you already have.</p>
                  <div style={{ display: 'flex', gap: 10, justifyContent: 'center' }}>
                    <button className="btn primary" onClick={() => navigate('/add')}>
                      <Plus size={16} /> Add New
                    </button>
                    <button className="btn" onClick={() => navigate('/import')}>
                      Library Import
                    </button>
                  </div>
                </>
              )}
            </div>
          </div>
        )}

        {view === 'posters' && (
          <div className="poster-grid" style={{ ['--poster-w' as string]: `${posterSize}px` }}>
            {list.map((t) => {
              const s = stats(t);
              return (
                <Link to={`/title/${t.slug}`} key={t.id} className="poster">
                  <div className="cover-wrap">
                    <Cover title={t} />
                    <div className="badges">
                      <span className="pill glass">{kindLabel[t.kind]}</span>
                      {t.status === 'ended' && <span className="pill glass">Ended</span>}
                    </div>
                  </div>
                  <ProgressBar have={s.have} total={s.total} title={t} />
                  <div className="meta">
                    <div className="name">{t.name}</div>
                    <div className="sub">
                      <span>
                        {s.have}/{s.total}
                      </span>
                      <span>{s.next ? `Next ${fmtDate(s.next.releaseDate)}` : t.publisher}</span>
                    </div>
                  </div>
                </Link>
              );
            })}
          </div>
        )}

        {view === 'overview' && (
          <div className="overview-list">
            {list.map((t) => {
              const s = stats(t);
              return (
                <Link to={`/title/${t.slug}`} key={t.id} className="card overview-row">
                  <div style={{ borderRadius: 6, overflow: 'hidden' }}>
                    <Cover title={t} size="sm" />
                  </div>
                  <div style={{ minWidth: 0 }}>
                    <h3>{t.name}</h3>
                    <div className="row-meta">
                      <span className="pill">{kindLabel[t.kind]}</span>
                      <span className="pill">{t.publisher}</span>
                      <span className="pill">{t.frequency}</span>
                      {t.status === 'ended' ? <span className="pill info">Ended</span> : <span className="pill ok">Continuing</span>}
                      {s.missing > 0 && <span className="pill danger">{s.missing} missing</span>}
                    </div>
                    <p>{t.overview}</p>
                    <div style={{ maxWidth: 360 }}>
                      <ProgressBar have={s.have} total={s.total} title={t} large />
                    </div>
                  </div>
                </Link>
              );
            })}
          </div>
        )}

        {view === 'table' && (
          <div className="card table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th style={{ width: 40 }} />
                  <th>Title</th>
                  <th>Type</th>
                  <th>Publisher</th>
                  <th>Frequency</th>
                  <th>Next Issue</th>
                  <th>Profile</th>
                  <th>Size</th>
                  <th style={{ width: 160 }}>Issues</th>
                </tr>
              </thead>
              <tbody>
                {list.map((t) => {
                  const s = stats(t);
                  return (
                    <tr key={t.id}>
                      <td>
                        <MonitorToggle on={t.monitored} onClick={() => toggleMonitored(t)} />
                      </td>
                      <td>
                        <Link className="link" to={`/title/${t.slug}`}>
                          {t.name}
                        </Link>
                      </td>
                      <td className="muted">{kindLabel[t.kind]}</td>
                      <td className="muted">{t.publisher}</td>
                      <td className="muted">{t.frequency}</td>
                      <td className="num">{fmtDate(s.next?.releaseDate)}</td>
                      <td>
                        <span className="pill">{t.qualityProfile}</span>
                      </td>
                      <td className="num muted">{s.size}</td>
                      <td>
                        <ProgressBar have={s.have} total={s.total} title={t} large />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
