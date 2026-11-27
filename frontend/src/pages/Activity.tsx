import { Ban, CheckCircle2, CircleAlert, Download, Loader2, Pause, RefreshCw, Trash2, TriangleAlert, XCircle } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Modal } from '../components/Modal';
import { eventMeta } from '../components/TitleDialogs';
import { useToast } from '../components/Toast';
import { Grow, Tool, Toolbar } from '../components/Toolbar';
import { api, formatBytes, formatEta, isConnected, timeAgo, type BlocklistRecord, type HistoryRecord, type QueueRecord } from '../data/api';
import { history as demoHistory, queue as demoQueue, titles } from '../data/mock';
import { runCommand } from '../data/store';

const statusMeta: Record<QueueRecord['status'], { cls: string; label: string; icon: typeof Download }> = {
  downloading: { cls: 'info', label: 'Downloading', icon: Download },
  queued: { cls: 'idle', label: 'Queued', icon: Pause },
  paused: { cls: 'idle', label: 'Paused', icon: Pause },
  completed: { cls: 'ok', label: 'Completed', icon: CheckCircle2 },
  failed: { cls: 'danger', label: 'Failed', icon: CircleAlert },
  warning: { cls: 'warn', label: 'Warning', icon: TriangleAlert },
};

/** Sample data shown when no backend is connected. */
function demoRecords(): QueueRecord[] {
  return demoQueue.map((q) => {
    const t = titles.find((x) => x.id === q.titleId);
    return {
      id: q.id,
      titleId: Number(q.titleId),
      titleName: t?.name,
      titleSlug: t?.slug,
      issues: q.issue,
      release: q.release,
      downloadClient: q.client,
      protocol: q.protocol,
      indexer: q.indexer,
      size: parseFloat(q.size) * 1024 * 1024,
      sizeLeft: parseFloat(q.size) * 1024 * 1024 * (1 - q.progress / 100),
      progress: q.progress,
      status: q.state === 'importing' ? 'completed' : q.state === 'warning' ? 'warning' : q.state,
      trackedState: q.state === 'importing' ? 'Importing' : 'Downloading',
      messages: q.message ? [q.message] : [],
    } as QueueRecord;
  });
}

function Queue({ reloadKey }: { reloadKey: number }) {
  const toast = useToast();
  const [items, setItems] = useState<QueueRecord[] | null>(isConnected() ? null : demoRecords());
  const [removing, setRemoving] = useState<QueueRecord | null>(null);

  const load = useCallback(async () => {
    if (!isConnected()) return;
    try {
      setItems((await api.queue()).records);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not load the queue', 'error');
    }
    // eslint-disable-next-line
  }, []);

  useEffect(() => {
    load();
    const t = setInterval(load, 5000);
    return () => clearInterval(t);
  }, [load, reloadKey]);

  if (items === null) return <div className="card empty"><Loader2 className="spin-icon" /></div>;

  if (items.length === 0) {
    return (
      <div className="card empty">
        <div>
          <Download size={40} />
          <div style={{ fontWeight: 600, color: 'var(--text)' }}>Queue is empty</div>
          <div>Downloads sent to your download clients show up here, then get imported automatically when they finish.</div>
        </div>
      </div>
    );
  }

  return (
    <>
      <div className="card table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th style={{ width: 40 }} />
              <th>Title</th>
              <th>Issue</th>
              <th>Release</th>
              <th>Client</th>
              <th>Indexer</th>
              <th>Size</th>
              <th style={{ width: 180 }}>Progress</th>
              <th>ETA</th>
              <th style={{ width: 50 }} />
            </tr>
          </thead>
          <tbody>
            {items.map((q) => {
              const m = statusMeta[q.status];
              const Icon = m.icon;
              const importIssue = q.trackedState === 'ImportFailed';
              return (
                <tr key={q.id}>
                  <td title={[m.label, q.trackedState, ...q.messages].join('\n')}>
                    {importIssue ? <TriangleAlert size={17} color="var(--warn)" /> : <Icon size={17} color={`var(--${m.cls})`} />}
                  </td>
                  <td>
                    {q.titleSlug ? (
                      <Link className="link" to={`/title/${q.titleSlug}`}>
                        {q.titleName}
                      </Link>
                    ) : (
                      <span className="muted">Unknown</span>
                    )}
                  </td>
                  <td className="num">{q.issues ?? '—'}</td>
                  <td className="muted mono" style={{ fontSize: 12, whiteSpace: 'normal', minWidth: 220 }}>
                    {q.release}
                    {q.messages.length > 0 && (
                      <div style={{ color: importIssue || q.status === 'failed' ? 'var(--danger)' : 'var(--warn)', fontFamily: 'Inter, sans-serif', marginTop: 2 }}>
                        {q.messages.join(' ')}
                      </div>
                    )}
                  </td>
                  <td>
                    <span className={`pill ${q.protocol === 'torrent' ? 'accent' : 'info'}`}>{q.downloadClient}</span>
                  </td>
                  <td className="muted">{q.indexer ?? '—'}</td>
                  <td className="num muted">{formatBytes(q.size)}</td>
                  <td>
                    <div className="progress lg">
                      <div className={`fill ${q.status === 'warning' || q.status === 'failed' ? 'partial' : 'accent'}`} style={{ width: `${q.progress}%` }} />
                      <div className="label">
                        {q.trackedState === 'Importing' ? 'Importing' : q.status === 'completed' ? (importIssue ? 'Import failed' : 'Import pending') : q.status === 'queued' ? 'Queued' : `${Math.round(q.progress)}%`}
                      </div>
                    </div>
                  </td>
                  <td className="num muted">{formatEta(q.eta)}</td>
                  <td>
                    <button className="icon-btn" style={{ width: 30, height: 30 }} title="Remove" onClick={() => (isConnected() ? setRemoving(q) : toast('Not available with sample data.', 'error'))}>
                      <XCircle size={16} />
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {removing && <RemoveDialog item={removing} onClose={() => setRemoving(null)} onDone={load} />}
    </>
  );
}

function RemoveDialog({ item, onClose, onDone }: { item: QueueRecord; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const [fromClient, setFromClient] = useState(true);
  const [blocklist, setBlocklist] = useState(false);
  const [busy, setBusy] = useState(false);
  const remove = async () => {
    setBusy(true);
    try {
      await api.removeFromQueue(item.id, fromClient, blocklist);
      toast('Removed from queue');
      onDone();
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not remove', 'error');
      setBusy(false);
    }
  };
  return (
    <Modal
      title="Remove from queue"
      onClose={onClose}
      footer={
        <>
          <span style={{ flex: 1 }} />
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn danger" onClick={remove} disabled={busy}>
            Remove
          </button>
        </>
      }
    >
      <p className="mono" style={{ marginTop: 0, fontSize: 13 }}>
        {item.release}
      </p>
      <div style={{ display: 'grid', gap: 10 }}>
        <label className="check">
          <input type="checkbox" checked={fromClient} onChange={(e) => setFromClient(e.target.checked)} /> Remove from {item.downloadClient} (and delete its files)
        </label>
        <label className="check">
          <input type="checkbox" checked={blocklist} onChange={(e) => setBlocklist(e.target.checked)} /> Blocklist this release so it isn't grabbed again
        </label>
      </div>
    </Modal>
  );
}

function HistoryTable() {
  const toast = useToast();
  const [page, setPage] = useState(1);
  const [data, setData] = useState<{ totalRecords: number; records: HistoryRecord[] } | null>(null);
  const pageSize = 50;

  const load = useCallback(() => {
    if (!isConnected()) {
      setData({
        totalRecords: demoHistory.length,
        records: demoHistory.map((h, i) => {
          const t = titles.find((x) => x.id === h.titleId);
          const type = ({ grabbed: 'Grabbed', imported: 'Imported', failed: 'DownloadFailed', deleted: 'FileDeleted' } as const)[h.event];
          return { id: i, titleId: Number(h.titleId), titleName: t?.name, titleSlug: t?.slug, issue: h.issue, eventType: type, sourceTitle: h.source, date: new Date(h.date).toISOString(), data: {} };
        }),
      });
      return;
    }
    api.history(page, pageSize).then(setData).catch((e) => toast(e.message, 'error'));
    // eslint-disable-next-line
  }, [page]);

  useEffect(() => {
    load();
  }, [load]);

  if (!data) return <div className="card empty"><Loader2 className="spin-icon" /></div>;
  if (data.records.length === 0) {
    return (
      <div className="card empty">
        <div>
          <CheckCircle2 size={40} />
          <div style={{ fontWeight: 600, color: 'var(--text)' }}>No history yet</div>
          <div>Grabs, imports, renames and failures are recorded here.</div>
        </div>
      </div>
    );
  }

  const pages = Math.max(1, Math.ceil(data.totalRecords / pageSize));
  return (
    <>
      <div className="card table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th>Event</th>
              <th>Title</th>
              <th>Issue</th>
              <th>Source</th>
              <th>Details</th>
              <th>When</th>
            </tr>
          </thead>
          <tbody>
            {data.records.map((h) => (
              <tr key={h.id}>
                <td>
                  <span className={`pill ${eventMeta[h.eventType].cls}`}>{eventMeta[h.eventType].label}</span>
                </td>
                <td>
                  {h.titleSlug ? (
                    <Link className="link" to={`/title/${h.titleSlug}`}>
                      {h.titleName}
                    </Link>
                  ) : (
                    <span className="muted">Deleted title</span>
                  )}
                </td>
                <td className="num">{h.issue ?? '—'}</td>
                <td className="muted mono" style={{ fontSize: 12, whiteSpace: 'normal' }}>
                  {h.sourceTitle}
                </td>
                <td className="muted" style={{ fontSize: 12, whiteSpace: 'normal' }}>
                  {h.data.indexer ? `${h.data.indexer} → ${h.data.downloadClient}` : h.data.message ?? h.data.path ?? ''}
                </td>
                <td className="num muted" title={new Date(h.date).toLocaleString()}>
                  {timeAgo(h.date)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {pages > 1 && (
        <div className="pager">
          <button className="btn sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </button>
          <span className="muted">
            Page {page} of {pages}
          </span>
          <button className="btn sm" disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>
            Next
          </button>
        </div>
      )}
    </>
  );
}

function Blocklist({ reloadKey }: { reloadKey: number }) {
  const toast = useToast();
  const [items, setItems] = useState<BlocklistRecord[] | null>(isConnected() ? null : []);
  const load = useCallback(() => {
    if (isConnected()) api.blocklist().then(setItems).catch((e) => toast(e.message, 'error'));
    // eslint-disable-next-line
  }, []);
  useEffect(() => {
    load();
  }, [load, reloadKey]);

  if (items === null) return <div className="card empty"><Loader2 className="spin-icon" /></div>;
  if (items.length === 0) {
    return (
      <div className="card empty">
        <div>
          <Ban size={40} />
          <div style={{ fontWeight: 600, color: 'var(--text)' }}>Blocklist is empty</div>
          <div>Releases that fail to download, or that you block from the queue, are listed here and never grabbed again.</div>
        </div>
      </div>
    );
  }
  return (
    <div className="card table-wrap">
      <table className="table">
        <thead>
          <tr>
            <th>Release</th>
            <th>Title</th>
            <th>Indexer</th>
            <th>Reason</th>
            <th>When</th>
            <th style={{ width: 50 }} />
          </tr>
        </thead>
        <tbody>
          {items.map((b) => (
            <tr key={b.id}>
              <td className="mono" style={{ fontSize: 12, whiteSpace: 'normal' }}>
                {b.sourceTitle}
              </td>
              <td>
                {b.titleSlug ? (
                  <Link className="link" to={`/title/${b.titleSlug}`}>
                    {b.titleName}
                  </Link>
                ) : (
                  '—'
                )}
              </td>
              <td className="muted">{b.indexer ?? '—'}</td>
              <td className="muted" style={{ whiteSpace: 'normal' }}>
                {b.message}
              </td>
              <td className="muted">{timeAgo(b.date)}</td>
              <td>
                <button
                  className="icon-btn"
                  style={{ width: 30, height: 30 }}
                  title="Remove from blocklist"
                  onClick={() => api.removeBlocklist(b.id).then(load).then(() => toast('Removed from blocklist'))}
                >
                  <Trash2 size={15} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function Activity() {
  const { pathname } = useLocation();
  const toast = useToast();
  const tab = pathname.endsWith('history') ? 'history' : pathname.endsWith('blocklist') ? 'blocklist' : 'queue';
  const [reloadKey, setReloadKey] = useState(0);
  const [busy, setBusy] = useState(false);

  const refresh = async () => {
    if (!isConnected()) return toast('Not available with sample data.', 'error');
    setBusy(true);
    try {
      await runCommand('ProcessDownloads');
      setReloadKey((k) => k + 1);
      toast('Queue checked');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Failed', 'error');
    } finally {
      setBusy(false);
    }
  };

  const clear = async () => {
    if (!window.confirm(tab === 'history' ? 'Clear all history?' : 'Clear the blocklist?')) return;
    await (tab === 'history' ? api.clearHistory() : api.clearBlocklist());
    setReloadKey((k) => k + 1);
    toast(tab === 'history' ? 'History cleared' : 'Blocklist cleared');
  };

  return (
    <>
      <Toolbar>
        {tab === 'queue' && <Tool icon={RefreshCw} label="Check Now" spin={busy} onClick={refresh} />}
        {tab !== 'queue' && isConnected() && <Tool icon={Trash2} label={tab === 'history' ? 'Clear History' : 'Clear Blocklist'} onClick={clear} />}
        <Grow />
      </Toolbar>
      <div className="page">
        <h1 className="page-title">{tab === 'queue' ? 'Queue' : tab === 'history' ? 'History' : 'Blocklist'}</h1>
        {tab === 'queue' && <Queue reloadKey={reloadKey} />}
        {tab === 'history' && <HistoryTable key={reloadKey} />}
        {tab === 'blocklist' && <Blocklist reloadKey={reloadKey} />}
      </div>
    </>
  );
}
