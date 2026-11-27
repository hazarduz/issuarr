import {
  BookOpen,
  Building2,
  CalendarClock,
  ChevronDown,
  FolderOpen,
  HardDrive,
  History,
  Loader2,
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
import { DeleteTitleDialog, EditTitleDialog, FilesDialog, HistoryDialog, InteractiveSearchDialog, RenameDialog } from '../components/TitleDialogs';
import { useToast } from '../components/Toast';
import { Grow, Sep, Tool, Toolbar } from '../components/Toolbar';
import { api, isConnected } from '../data/api';
import { kindLabel, parseDate, stats, titles, type Issue } from '../data/mock';
import { notifyLibrary, runCommand, useLibraryVersion } from '../data/store';

type Dialog = 'edit' | 'delete' | 'rename' | 'files' | 'history' | { search: Issue | null };

export default function TitleDetail() {
  useLibraryVersion();
  const { slug } = useParams();
  const title = titles.find((t) => t.slug === slug);
  const toast = useToast();
  const [, force] = useState(0);
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [busy, setBusy] = useState<Record<string, boolean>>({});

  const byYear = useMemo(() => {
    const g = new Map<string, Issue[]>();
    title?.issues.forEach((i) => {
      const y = i.releaseDate.slice(0, 4);
      g.set(y, [...(g.get(y) ?? []), i]);
    });
    return [...g.entries()];
  }, [title, title?.issues]);

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
  const live = isConnected();

  /** Run a background command with a spinner on the button that started it. */
  const run = async (key: string, name: string, issueIds?: number[], startMsg?: string) => {
    if (!live) return toast('Not available with sample data. Run the Issuarr backend to use this.', 'error');
    setBusy((b) => ({ ...b, [key]: true }));
    if (startMsg) toast(startMsg);
    try {
      const cmd = await runCommand(name, title.id, issueIds);
      toast(cmd.message ?? 'Done');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Failed', 'error');
    } finally {
      setBusy((b) => ({ ...b, [key]: false }));
    }
  };

  // Optimistically update the UI, then persist to the backend.
  const setIssuesMonitored = (list: Issue[], value: boolean) => {
    const previous = Object.fromEntries(list.map((i) => [i.id, i.monitored]));
    list.forEach((i) => (i.monitored = value));
    force((n) => n + 1);
    if (!live) return;
    api.setIssuesMonitored(list.map((i) => i.serverId!).filter(Boolean), value).catch(() => {
      list.forEach((i) => (i.monitored = previous[i.id]));
      force((n) => n + 1);
      toast('Could not save monitoring change', 'error');
    });
  };

  const toggleTitleMonitored = () => {
    const value = !title.monitored;
    title.monitored = value;
    notifyLibrary();
    toast(value ? 'Title monitored' : 'Title unmonitored');
    if (live) {
      api.updateTitle(title, { monitored: value }).then(() => api.loadTitle(title.id)).then(notifyLibrary).catch(() => {
        title.monitored = !value;
        notifyLibrary();
        toast('Could not save monitoring change', 'error');
      });
    }
  };

  const needsLive = (d: Dialog) => () => (live ? setDialog(d) : toast('Not available with sample data. Run the Issuarr backend to use this.', 'error'));

  return (
    <>
      <Toolbar>
        <Tool icon={RefreshCw} label="Refresh" spin={busy.refresh} onClick={() => run('refresh', 'RefreshTitle', undefined)} />
        <Tool icon={Search} label="Search Monitored" spin={busy.search} onClick={() => run('search', 'TitleSearch', undefined, `Searching for ${title.name}…`)} />
        <Tool icon={User} label="Interactive Search" onClick={needsLive({ search: null })} />
        <Sep />
        <Tool icon={Wand2} label="Preview Rename" onClick={needsLive('rename')} />
        <Tool icon={FolderOpen} label="Manage Files" onClick={needsLive('files')} />
        <Tool icon={History} label="History" onClick={needsLive('history')} />
        <Sep />
        <Tool icon={Pencil} label="Edit" onClick={needsLive('edit')} />
        <Tool icon={Trash2} label="Delete" onClick={needsLive('delete')} />
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
              <MonitorToggle on={title.monitored} onClick={toggleTitleMonitored} />
              {title.name}
            </h1>
            <div className="facts">
              <span>
                <BookOpen size={15} /> {kindLabel[title.kind]}
              </span>
              {title.publisher && (
                <span>
                  <Building2 size={15} /> {title.publisher}
                </span>
              )}
              <span>
                <CalendarClock size={15} /> {title.frequency ? `${title.frequency} · ` : ''}since {title.year}
              </span>
              <span>
                <HardDrive size={15} /> {s.size}
              </span>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
              {title.status === 'ended' ? <span className="pill info">Ended</span> : <span className="pill ok">Continuing</span>}
              {title.metadataSource && <span className="pill">{title.metadataSource === 'metron' ? 'Metron' : 'ComicVine'}</span>}
              {title.tags.map((t) => (
                <span className="tag" key={t}>
                  {t}
                </span>
              ))}
            </div>
            {title.overview && <p className="overview">{title.overview}</p>}
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
                <div className="v">{s.next ? parseDate(s.next.releaseDate).toLocaleDateString(undefined, { dateStyle: 'medium' }) : '—'}</div>
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
        {byYear.length === 0 && (
          <div className="card empty">
            <div>
              <div style={{ fontWeight: 600, color: 'var(--text)' }}>No issues yet</div>
              <div>{title.metadataSource || title.kind === 'magazine' ? 'Click Refresh to load the issue list.' : 'Import files or add issues from a metadata source.'}</div>
            </div>
          </div>
        )}
        {byYear.map(([year, issues]) => {
          const have = issues.filter((i) => i.status === 'downloaded').length;
          const aired = issues.filter((i) => i.status !== 'unaired').length;
          const isOpen = !!open[year];
          return (
            <div className="card year-block" key={year}>
              <div className="year-head" onClick={() => setOpen((o) => ({ ...o, [year]: !o[year] }))}>
                <span onClick={(e) => e.stopPropagation()}>
                  <MonitorToggle on={issues.some((i) => i.monitored)} onClick={() => setIssuesMonitored(issues, !issues.some((i) => i.monitored))} />
                </span>
                <h3>{year}</h3>
                <span className={`pill ${have === aired && aired > 0 ? 'ok' : have === 0 ? '' : 'danger'}`}>
                  {have} / {aired}
                </span>
                <span style={{ color: 'var(--text-faint)', fontSize: 12 }}>{issues.length} issues</span>
                <span onClick={(e) => e.stopPropagation()} style={{ marginLeft: 'auto' }}>
                  <button
                    className="icon-btn"
                    style={{ width: 30, height: 30 }}
                    title={`Search all missing ${year} issues`}
                    onClick={() => {
                      const wanted = issues.filter((i) => i.monitored && i.status === 'missing' && i.serverId).map((i) => i.serverId!);
                      if (wanted.length === 0) return toast(`No missing monitored issues in ${year}`);
                      run(`year-${year}`, 'IssueSearch', wanted, `Searching for ${wanted.length} issues…`);
                    }}
                  >
                    {busy[`year-${year}`] ? <Loader2 size={15} className="spin-icon" /> : <Search size={15} />}
                  </button>
                </span>
                <ChevronDown size={20} className={`chev ${isOpen ? 'open' : ''}`} style={{ marginLeft: 0 }} />
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
                            <MonitorToggle on={i.monitored} onClick={() => setIssuesMonitored([i], !i.monitored)} />
                          </td>
                          <td>
                            <span style={{ fontWeight: 600 }}>{i.number}</span>
                            {i.title && <span className="muted"> — {i.title}</span>}
                          </td>
                          <td className="num muted">{parseDate(i.releaseDate).toLocaleDateString(undefined, { dateStyle: 'medium' })}</td>
                          <td className="muted mono" style={{ fontSize: 12 }}>
                            {i.file ? (
                              <>
                                {i.file.name} <span className="pill">{i.file.format}</span>
                              </>
                            ) : (
                              '—'
                            )}
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
                                onClick={() => run(`issue-${i.id}`, 'IssueSearch', i.serverId != null ? [i.serverId] : [], `Searching for ${title.name} ${i.number}…`)}
                              >
                                {busy[`issue-${i.id}`] ? <Loader2 size={15} className="spin-icon" /> : <Search size={15} />}
                              </button>
                              <button className="icon-btn" style={{ width: 30, height: 30 }} title="Interactive search" onClick={needsLive({ search: i })}>
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
        {title.metadataSource && (
          <div className="help" style={{ textAlign: 'right' }}>
            Issue list from {title.metadataSource === 'metron' ? 'Metron' : 'ComicVine'}
          </div>
        )}
      </div>

      {dialog === 'edit' && <EditTitleDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'delete' && <DeleteTitleDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'rename' && <RenameDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'files' && <FilesDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'history' && <HistoryDialog title={title} onClose={() => setDialog(null)} />}
      {dialog && typeof dialog === 'object' && (
        <InteractiveSearchDialog
          title={title}
          issueId={dialog.search?.serverId}
          issueLabel={dialog.search?.number}
          onClose={() => setDialog(null)}
        />
      )}
    </>
  );
}
