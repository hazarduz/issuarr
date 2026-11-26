import {
  ArrowDownUp,
  Filter,
  LayoutGrid,
  List,
  Plus,
  RefreshCw,
  Rows3,
  Search as SearchIcon,
  SlidersHorizontal,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Cover } from '../components/Cover';
import { MonitorToggle, ProgressBar } from '../components/Status';
import { Grow, Sep, Tool, Toolbar } from '../components/Toolbar';
import { useToast } from '../components/Toast';
import { kindLabel, parseDate, stats, titles, type Kind } from '../data/mock';

type View = 'posters' | 'overview' | 'table';
type Sort = 'name' | 'missing' | 'next' | 'added';

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
  const [view, setViewState] = useState<View>(readView);
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
    const l = titles.filter((t) => kind === 'all' || t.kind === kind);
    const s = [...l];
    if (sort === 'name') s.sort((a, b) => a.name.localeCompare(b.name));
    if (sort === 'missing') s.sort((a, b) => stats(b).missing - stats(a).missing);
    if (sort === 'next')
      s.sort((a, b) => (stats(a).next?.releaseDate ?? '9999').localeCompare(stats(b).next?.releaseDate ?? '9999'));
    if (sort === 'added') s.sort((a, b) => b.year - a.year);
    return s;
  }, [kind, sort]);

  const sorts: Sort[] = ['name', 'missing', 'next', 'added'];
  const sortLabel: Record<Sort, string> = { name: 'Title', missing: 'Missing', next: 'Next Issue', added: 'Year' };

  const refresh = () => {
    setRefreshing(true);
    setTimeout(() => {
      setRefreshing(false);
      toast('Library refreshed — 3 new issues found');
    }, 1400);
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
        <Tool icon={SearchIcon} label="Search Missing" onClick={() => toast(`Searching for ${totals.missing} missing issues`)} />
        <Tool icon={Plus} label="Add New" onClick={() => navigate('/add')} />
        <Sep />
        <Tool icon={SlidersHorizontal} label="Options" />
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
        <Tool icon={Filter} label="Filter" />
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
                        <MonitorToggle on={t.monitored} />
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
