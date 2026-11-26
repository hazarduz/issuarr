import { Ban, CheckCircle2, Download, Pause, RefreshCw, Trash2, TriangleAlert, XCircle } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Grow, Sep, Tool, Toolbar } from '../components/Toolbar';
import { history, queue, titles } from '../data/mock';

const titleById = (id: string) => titles.find((t) => t.id === id)!;

function Queue() {
  // Simulate live progress so the page feels alive.
  const [items, setItems] = useState(queue);
  useEffect(() => {
    const t = setInterval(
      () =>
        setItems((list) =>
          list.map((q) => (q.state === 'downloading' ? { ...q, progress: Math.min(99, q.progress + Math.random() * 3) } : q)),
        ),
      1000,
    );
    return () => clearInterval(t);
  }, []);

  const stateMeta = {
    downloading: { cls: 'info', label: 'Downloading', icon: Download },
    queued: { cls: '', label: 'Queued', icon: Pause },
    importing: { cls: 'ok', label: 'Importing', icon: CheckCircle2 },
    warning: { cls: 'warn', label: 'Stalled', icon: TriangleAlert },
  } as const;

  return (
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
            <th style={{ width: 200 }}>Progress</th>
            <th>ETA</th>
            <th style={{ width: 50 }} />
          </tr>
        </thead>
        <tbody>
          {items.map((q) => {
            const t = titleById(q.titleId);
            const m = stateMeta[q.state];
            const Icon = m.icon;
            return (
              <tr key={q.id}>
                <td title={q.message ?? m.label}>
                  <Icon size={17} color={`var(--${m.cls || 'idle'})`} />
                </td>
                <td>
                  <Link className="link" to={`/title/${t.slug}`}>
                    {t.name}
                  </Link>
                </td>
                <td className="num">{q.issue}</td>
                <td className="muted mono" style={{ fontSize: 12 }}>
                  {q.release}
                </td>
                <td>
                  <span className={`pill ${q.protocol === 'torrent' ? 'accent' : 'info'}`}>{q.client}</span>
                </td>
                <td className="muted">{q.indexer}</td>
                <td className="num muted">{q.size}</td>
                <td>
                  <div className="progress lg">
                    <div className={`fill ${q.state === 'warning' ? 'partial' : 'accent'}`} style={{ width: `${q.progress}%` }} />
                    <div className="label">{q.state === 'queued' ? 'Queued' : `${Math.round(q.progress)}%`}</div>
                  </div>
                </td>
                <td className="num muted">{q.eta}</td>
                <td>
                  <button className="icon-btn" style={{ width: 30, height: 30 }} title="Remove">
                    <XCircle size={16} />
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function HistoryTable() {
  const ev = {
    grabbed: { cls: 'info', label: 'Grabbed' },
    imported: { cls: 'ok', label: 'Imported' },
    failed: { cls: 'danger', label: 'Download Failed' },
    deleted: { cls: '', label: 'File Deleted' },
  } as const;
  return (
    <div className="card table-wrap">
      <table className="table">
        <thead>
          <tr>
            <th>Event</th>
            <th>Title</th>
            <th>Issue</th>
            <th>Source</th>
            <th>Date</th>
          </tr>
        </thead>
        <tbody>
          {history.map((h) => {
            const t = titleById(h.titleId);
            return (
              <tr key={h.id}>
                <td>
                  <span className={`pill ${ev[h.event].cls}`}>{ev[h.event].label}</span>
                </td>
                <td>
                  <Link className="link" to={`/title/${t.slug}`}>
                    {t.name}
                  </Link>
                </td>
                <td className="num">{h.issue}</td>
                <td className="muted">{h.source}</td>
                <td className="num muted">{h.date}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export default function Activity() {
  const { pathname } = useLocation();
  const tab = pathname.endsWith('history') ? 'history' : pathname.endsWith('blocklist') ? 'blocklist' : 'queue';
  return (
    <>
      <Toolbar>
        <Tool icon={RefreshCw} label="Refresh" />
        <Sep />
        <Tool icon={Pause} label="Pause Queue" />
        <Tool icon={Trash2} label="Remove Selected" />
        <Grow />
      </Toolbar>
      <div className="page">
        <h1 className="page-title">
          {tab === 'queue' ? 'Queue' : tab === 'history' ? 'History' : 'Blocklist'}
          {tab === 'queue' && <small>{queue.length} items · 2 downloading</small>}
        </h1>
        {tab === 'queue' && <Queue />}
        {tab === 'history' && <HistoryTable />}
        {tab === 'blocklist' && (
          <div className="card empty">
            <div>
              <Ban size={40} />
              <div style={{ fontWeight: 600, color: 'var(--text)' }}>Blocklist is empty</div>
              <div>Releases that fail to download or import will show up here.</div>
            </div>
          </div>
        )}
      </div>
    </>
  );
}
