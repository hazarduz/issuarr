import {
  BookOpen,
  Building2,
  CalendarClock,
  ChevronDown,
  FolderOpen,
  HardDrive,
  History,
  Pencil,
  RefreshCw,
  Search,
  Trash2,
  User,
  Wand2,
} from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Cover } from '../components/Cover';
import { MonitorToggle, ProgressBar, StatusPill } from '../components/Status';
import { Grow, Sep, Tool, Toolbar } from '../components/Toolbar';
import { useToast } from '../components/Toast';
import { api, isConnected } from '../data/api';
import { kindLabel, parseDate, stats, titles, type Issue } from '../data/mock';

export default function TitleDetail() {
  const { slug } = useParams();
  const title = titles.find((t) => t.slug === slug);
  const toast = useToast();
  const [monitored, setMonitored] = useState<Record<string, boolean>>({});
  const [titleMonitored, setTitleMonitored] = useState(title?.monitored ?? true);

  // Optimistically update the UI, then persist to the backend (when connected).
  const setIssuesMonitored = (list: Issue[], value: boolean) => {
    const previous = Object.fromEntries(list.map((i) => [i.id, i.monitored]));
    list.forEach((i) => (i.monitored = value));
    setMonitored((m) => ({ ...m, ...Object.fromEntries(list.map((i) => [i.id, value])) }));
    if (!isConnected()) return;
    api.setIssuesMonitored(list.map((i) => i.serverId!).filter(Boolean), value).catch(() => {
      list.forEach((i) => (i.monitored = previous[i.id]));
      setMonitored((m) => ({ ...m, ...previous }));
      toast('Could not save monitoring change', 'error');
    });
  };

  const toggleTitleMonitored = () => {
    if (!title) return;
    const value = !titleMonitored;
    title.monitored = value;
    setTitleMonitored(value);
    toast(value ? 'Title monitored' : 'Title unmonitored');
    if (isConnected()) {
      api.updateTitle(title).catch(() => {
        title.monitored = !value;
        setTitleMonitored(!value);
        toast('Could not save monitoring change', 'error');
      });
    }
  };

  const byYear = useMemo(() => {
    const g = new Map<string, Issue[]>();
    title?.issues.forEach((i) => {
      const y = i.releaseDate.slice(0, 4);
      g.set(y, [...(g.get(y) ?? []), i]);
    });
    return [...g.entries()];
  }, [title]);

  const [open, setOpen] = useState<Record<string, boolean>>(() => (byYear[0] ? { [byYear[0][0]]: true } : {}));

  if (!title) {
    return (
      <div className="empty">
        <div>
          <BookOpen size={40} />
          <h2>Title not found</h2>
          <Link className="btn" to="/">
            Back to library
          </Link>
        </div>
      </div>
    );
  }

  const s = stats(title);
  const isMon = (i: Issue) => monitored[i.id] ?? i.monitored;

  return (
    <>
      <Toolbar>
        <Tool icon={RefreshCw} label="Refresh" onClick={() => toast(`Refreshing ${title.name}`)} />
        <Tool icon={Search} label="Search Monitored" onClick={() => toast(`Searching ${s.missing} missing issues`)} />
        <Tool icon={Wand2} label="Preview Rename" />
        <Tool icon={FolderOpen} label="Manage Files" />
        <Tool icon={History} label="History" />
        <Sep />
        <Tool icon={Pencil} label="Edit" />
        <Tool icon={Trash2} label="Delete" />
        <Grow />
      </Toolbar>

      <section className="hero">
        <div className="hero-bg" style={{ background: `linear-gradient(135deg, ${title.palette[0]}, ${title.palette[1]})` }} />
        <div className="hero-inner">
          <div className="hero-cover">
            <Cover title={title} size="lg" />
          </div>
          <div style={{ minWidth: 0 }}>
            <h1>
              <MonitorToggle on={titleMonitored} onClick={toggleTitleMonitored} />
              {title.name}
            </h1>
            <div className="facts">
              <span>
                <BookOpen size={15} /> {kindLabel[title.kind]}
              </span>
              <span>
                <Building2 size={15} /> {title.publisher}
              </span>
              <span>
                <CalendarClock size={15} /> {title.frequency} · since {title.year}
              </span>
              <span>
                <HardDrive size={15} /> {s.size}
              </span>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
              {title.status === 'ended' ? <span className="pill info">Ended</span> : <span className="pill ok">Continuing</span>}
              {title.tags.map((t) => (
                <span className="tag" key={t}>
                  {t}
                </span>
              ))}
            </div>
            <p className="overview">{title.overview}</p>
            <div className="info-grid">
              <div className="info-cell">
                <div className="k">Path</div>
                <div className="v mono" title={title.path}>
                  {title.path}
                </div>
              </div>
              <div className="info-cell">
                <div className="k">Quality Profile</div>
                <div className="v">{title.qualityProfile}</div>
              </div>
              <div className="info-cell">
                <div className="k">Next Issue</div>
                <div className="v">
                  {s.next ? parseDate(s.next.releaseDate).toLocaleDateString(undefined, { dateStyle: 'medium' }) : '—'}
                </div>
              </div>
              <div className="info-cell">
                <div className="k">Collection</div>
                <div className="v" style={{ marginTop: 4 }}>
                  <ProgressBar have={s.have} total={s.total} title={title} large />
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <div className="page">
        {byYear.map(([year, issues]) => {
          const have = issues.filter((i) => i.status === 'downloaded').length;
          const aired = issues.filter((i) => i.status !== 'unaired').length;
          const isOpen = !!open[year];
          return (
            <div className="card year-block" key={year}>
              <div className="year-head" onClick={() => setOpen((o) => ({ ...o, [year]: !o[year] }))}>
                <span onClick={(e) => e.stopPropagation()}>
                  <MonitorToggle on={issues.some(isMon)} onClick={() => setIssuesMonitored(issues, !issues.some(isMon))} />
                </span>
                <h3>{year}</h3>
                <span className={`pill ${have === aired && aired > 0 ? 'ok' : have === 0 ? '' : 'danger'}`}>
                  {have} / {aired}
                </span>
                <span style={{ color: 'var(--text-faint)', fontSize: 12 }}>{issues.length} issues</span>
                <ChevronDown size={20} className={`chev ${isOpen ? 'open' : ''}`} />
              </div>
              {isOpen && (
                <div className="table-wrap" style={{ borderTop: '1px solid var(--border)' }}>
                  <table className="table">
                    <thead>
                      <tr>
                        <th style={{ width: 40 }} />
                        <th>Issue</th>
                        <th>Release Date</th>
                        <th>File</th>
                        <th>Size</th>
                        <th>Status</th>
                        <th style={{ width: 90 }} />
                      </tr>
                    </thead>
                    <tbody>
                      {issues.map((i) => (
                        <tr key={i.id}>
                          <td>
                            <MonitorToggle
                              on={isMon(i)}
                              onClick={() => setIssuesMonitored([i], !isMon(i))}
                            />
                          </td>
                          <td>
                            <span style={{ fontWeight: 600 }}>{i.number}</span>
                            {i.title && <span className="muted"> — {i.title}</span>}
                          </td>
                          <td className="num muted">
                            {parseDate(i.releaseDate).toLocaleDateString(undefined, { dateStyle: 'medium' })}
                          </td>
                          <td className="muted mono" style={{ fontSize: 12 }}>
                            {i.file?.name ?? '—'}
                          </td>
                          <td className="num muted">{i.file?.size ?? ''}</td>
                          <td>
                            <StatusPill status={i.status} />
                          </td>
                          <td>
                            <div style={{ display: 'flex', gap: 2 }}>
                              <button
                                className="icon-btn"
                                style={{ width: 30, height: 30 }}
                                title="Automatic search"
                                onClick={() => toast(`Searching for ${title.name} ${i.number}`)}
                              >
                                <Search size={15} />
                              </button>
                              <button className="icon-btn" style={{ width: 30, height: 30 }} title="Interactive search">
                                <User size={15} />
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </>
  );
}
