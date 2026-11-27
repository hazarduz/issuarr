import {
  BookOpen,
  Building2,
  CalendarClock,
  ChevronDown,
  FolderOpen,
  HardDrive,
  History,
  Link2,
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
import { ItemDeleteDialog, ItemMatchDialog } from '../components/ItemDialogs';
import { MatchDialog } from '../components/MatchDialog';
import { MonitorToggle, ProgressBar, StatusPill } from '../components/Status';
import { DeleteTitleDialog, EditTitleDialog, FilesDialog, HistoryDialog, InteractiveSearchDialog, RenameDialog } from '../components/TitleDialogs';
import { useToast } from '../components/Toast';
import { Grow, Sep, Tool, Toolbar } from '../components/Toolbar';
import { api, isConnected } from '../data/api';
import { itemWord, kindLabel, parseDate, sourceName, stats, titles, type Issue } from '../data/mock';
import { notifyLibrary, runCommand, useLibraryVersion } from '../data/store';

type Dialog = 'edit' | 'delete' | 'rename' | 'files' | 'history' | 'match' | { search: Issue | null } | { matchItem: Issue } | { deleteItem: Issue };

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
      // An author's books read better as one list than split by publication year.
      const y = title.kind === 'book' ? 'Books' : i.releaseDate.slice(0, 4);
      g.set(y, [...(g.get(y) ?? []), i]);
    });
    return [...g.entries()];
  }, [title, title?.issues]);

  // Explicit open/closed choices; the newest year is open unless the user closed it.
  const [open, setOpen] = useState<Record<string, boolean>>({});

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
        <Tool icon={Link2} label={title.metadataSource ? 'Rematch' : 'Match'} onClick={needsLive('match')} />
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
                <BookOpen size={15} /> {title.kind === 'book' ? 'Author' : kindLabel[title.kind]}
              </span>
              {title.publisher && (
                <span>
                  <Building2 size={15} /> {title.publisher}
                </span>
              )}
              <span>
                <CalendarClock size={15} /> {title.kind === 'book' ? `${title.issues.length} books` : `${title.frequency ? `${title.frequency} · ` : ''}since ${title.year}`}
              </span>
              <span>
                <HardDrive size={15} /> {s.size}
              </span>
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginBottom: 16 }}>
              {title.status === 'ended' ? <span className="pill info">Ended</span> : <span className="pill ok">Continuing</span>}
              {title.metadataSource ? (
                <span className="pill clickable" title="Change match" onClick={needsLive('match')}>
                  <Link2 size={11} /> {sourceName[title.metadataSource] ?? title.metadataSource}
                </span>
              ) : (
                <span
                  className={`pill ${title.kind === 'magazine' ? '' : 'warn'} clickable`}
                  title={title.kind === 'book' ? 'Link to Open Library or Google Books for the full book list and covers' : title.kind === 'magazine' ? 'Link to Wikidata or ComicVine for details and cover' : 'Link to ComicVine or Metron for the full issue list, dates and cover'}
                  onClick={needsLive('match')}
                >
                  <Link2 size={11} /> Not matched
                </span>
              )}
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
                <div className="k">Next {title.kind === 'book' ? 'Book' : 'Issue'}</div>
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
              <div style={{ fontWeight: 600, color: 'var(--text)' }}>No {itemWord(title.kind, true)} yet</div>
              <div>{title.metadataSource || title.kind === 'magazine' ? `Click Refresh to load the ${itemWord(title.kind)} list.` : 'Import files, or Match this title to a metadata source.'}</div>
            </div>
          </div>
        )}
        {byYear.map(([year, issues], index) => {
          const have = issues.filter((i) => i.status === 'downloaded').length;
          const aired = issues.filter((i) => i.status !== 'unaired').length;
          const isOpen = open[year] ?? index === 0;
          return (
            <div className="card year-block" key={year}>
              <div className="year-head" onClick={() => setOpen((o) => ({ ...o, [year]: !isOpen }))}>
                <span onClick={(e) => e.stopPropagation()}>
                  <MonitorToggle on={issues.some((i) => i.monitored)} onClick={() => setIssuesMonitored(issues, !issues.some((i) => i.monitored))} />
                </span>
                <h3>{year}</h3>
                <span className={`pill ${have === aired && aired > 0 ? 'ok' : have === 0 ? '' : 'danger'}`}>
                  {have} / {aired}
                </span>
                <span style={{ color: 'var(--text-faint)', fontSize: 12 }}>
                  {issues.length} {itemWord(title.kind, issues.length !== 1)}
                </span>
                <span onClick={(e) => e.stopPropagation()} style={{ marginLeft: 'auto' }}>
                  <button
                    className="icon-btn"
                    style={{ width: 30, height: 30 }}
                    title={`Search all missing ${year} ${itemWord(title.kind, true)}`}
                    onClick={() => {
                      const wanted = issues.filter((i) => i.monitored && i.status === 'missing' && i.serverId).map((i) => i.serverId!);
                      if (wanted.length === 0) return toast(`No missing monitored ${itemWord(title.kind, true)} in ${year}`);
                      run(`year-${year}`, 'IssueSearch', wanted, `Searching for ${wanted.length} ${itemWord(title.kind, wanted.length !== 1)}…`);
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
                        <th>{title.kind === 'book' ? 'Book' : 'Issue'}</th>
                        <th>{title.kind === 'book' ? 'Published' : 'Release Date'}</th>
                        <th>File</th>
                        <th>Size</th>
                        <th>Status</th>
                        <th style={{ width: 150 }} />
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
                            <div style={{ display: 'flex', gap: 2, justifyContent: 'flex-end' }}>
                              <button className="icon-btn" style={{ width: 30, height: 30 }} title={`Match or edit this ${itemWord(title.kind)}`} onClick={needsLive({ matchItem: i })}>
                                <Link2 size={15} />
                              </button>
                              <button className="icon-btn danger-hover" style={{ width: 30, height: 30 }} title={`Delete this ${itemWord(title.kind)}`} onClick={needsLive({ deleteItem: i })}>
                                <Trash2 size={15} />
                              </button>
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
        {(title.serverStats?.hidden ?? 0) > 0 && (
          <div className="help" style={{ textAlign: 'right', marginBottom: 4 }}>
            {title.serverStats!.hidden} hidden {itemWord(title.kind, title.serverStats!.hidden !== 1)} ·{' '}
            <a
              style={{ color: 'var(--accent)', cursor: 'pointer' }}
              onClick={async () => {
                await api.restoreHidden(title.id);
                await api.loadTitle(title.id);
                notifyLibrary();
                toast(`Restored hidden ${itemWord(title.kind, true)}`);
              }}
            >
              Restore
            </a>
          </div>
        )}
        {title.metadataSource && (
          <div className="help" style={{ textAlign: 'right' }}>
            {title.kind === 'book' ? 'Book list' : title.kind === 'magazine' ? 'Details' : 'Issue list'} from {sourceName[title.metadataSource] ?? title.metadataSource}
          </div>
        )}
      </div>

      {dialog === 'edit' && <EditTitleDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'delete' && <DeleteTitleDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'rename' && <RenameDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'files' && <FilesDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'history' && <HistoryDialog title={title} onClose={() => setDialog(null)} />}
      {dialog === 'match' && (
        <MatchDialog
          kind={title.kind}
          initialTerm={title.name}
          currentId={title.metadataSource && title.externalId ? `${title.metadataSource}:${title.externalId}` : undefined}
          onClose={() => setDialog(null)}
          onPick={async (r, useName) => {
            try {
              const { warning } = await api.matchTitle(title.id, r.source, r.externalId, useName);
              notifyLibrary();
              setDialog(null);
              toast(warning ?? `Matched to ${r.name}${r.year ? ` (${r.year})` : ''}`, warning ? 'error' : 'success');
            } catch (e) {
              toast(e instanceof Error ? e.message : 'Match failed', 'error');
            }
          }}
          onUnmatch={async () => {
            try {
              await api.unmatchTitle(title.id);
              await api.loadTitle(title.id);
              notifyLibrary();
              setDialog(null);
              toast('Unmatched. Issues and files are kept.');
            } catch (e) {
              toast(e instanceof Error ? e.message : 'Unmatch failed', 'error');
            }
          }}
        />
      )}
      {dialog && typeof dialog === 'object' && 'matchItem' in dialog && <ItemMatchDialog title={title} item={dialog.matchItem} onClose={() => setDialog(null)} />}
      {dialog && typeof dialog === 'object' && 'deleteItem' in dialog && <ItemDeleteDialog title={title} item={dialog.deleteItem} onClose={() => setDialog(null)} />}
      {dialog && typeof dialog === 'object' && 'search' in dialog && (
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
