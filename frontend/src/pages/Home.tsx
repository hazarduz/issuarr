import { BookOpenText, Check, ChevronRight, Clock, MoreHorizontal, Plus, Sparkles, X } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Cover } from '../components/Cover';
import { useToast } from '../components/Toast';
import { covers, isConnected, reading, timeAgo, type ShelfItem } from '../data/api';
import { itemWord, titles } from '../data/mock';
import { refreshLibrary, useLibraryVersion } from '../data/store';

/** Issue/book cover: from the file itself, falling back to the title's cover. */
function ItemCover({ item }: { item: ShelfItem }) {
  const [failed, setFailed] = useState(false);
  const title = titles.find((t) => t.id === String(item.titleId));
  const issue = title?.issues.find((i) => i.serverId === item.issueId);
  const src = issue?.coverUrl ?? (['CBZ', 'CBR', 'CB7', 'EPUB'].includes(item.format) ? covers.issueCoverUrl(item.issueId) : undefined);
  if (src && !failed) {
    return (
      <div className="cover has-image">
        <img src={src} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} />
      </div>
    );
  }
  return title ? <Cover title={title} issue={item.number} /> : <div className="cover" />;
}

function ShelfCard({ item, onChanged, showProgress }: { item: ShelfItem; onChanged: () => void; showProgress: boolean }) {
  const navigate = useNavigate();
  const toast = useToast();
  const [menu, setMenu] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const close = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setMenu(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);

  const act = async (fn: () => Promise<unknown>, msg: string) => {
    setMenu(false);
    try {
      await fn();
      toast(msg);
      onChanged();
      refreshLibrary().catch(() => {});
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Failed', 'error');
    }
  };

  const label = item.kind === 'book' ? item.number : `${item.titleName} ${item.number}`;
  return (
    <div className="shelf-card" ref={ref}>
      <button className="shelf-cover" onClick={() => navigate(`/read/${item.issueId}`)} title={`Read ${label}`}>
        <ItemCover item={item} />
        <span className="shelf-play">
          <BookOpenText size={22} />
        </span>
        {showProgress && item.percent != null && item.percent > 0 && (
          <span className="shelf-progress">
            <span style={{ width: `${Math.min(100, item.percent)}%` }} />
          </span>
        )}
      </button>
      <div className="shelf-meta">
        <div style={{ minWidth: 0 }}>
          <Link to={`/title/${item.titleSlug}`} className="shelf-title" title={item.titleName}>
            {item.kind === 'book' ? item.number : item.titleName}
          </Link>
          <div className="shelf-sub">
            {item.kind === 'book' ? item.titleName : item.number}
            {showProgress && item.percent != null && item.percent > 0 ? ` · ${Math.round(item.percent)}%` : ''}
          </div>
        </div>
        <div className="rel-wrap">
          <button className="icon-btn" style={{ width: 28, height: 28 }} onClick={() => setMenu((m) => !m)} aria-label="More">
            <MoreHorizontal size={16} />
          </button>
          {menu && (
            <div className="menu" style={{ minWidth: 190 }}>
              <button onClick={() => act(() => reading.markRead(item.issueId), `Marked ${label} as read`)}>
                <Check size={14} /> Mark as read
              </button>
              {item.percent != null && (
                <button onClick={() => act(() => reading.clear(item.issueId), `Removed ${label} from Continue Reading`)}>
                  <X size={14} /> Mark as unread
                </button>
              )}
              <Link to={`/title/${item.titleSlug}`}>Go to {item.kind === 'book' ? 'author' : 'title'}</Link>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function Shelf({ title, icon, items, empty, onChanged, showProgress = true }: { title: string; icon: React.ReactNode; items: ShelfItem[]; empty?: React.ReactNode; onChanged: () => void; showProgress?: boolean }) {
  if (items.length === 0 && !empty) return null;
  return (
    <section className="shelf">
      <h2>
        {icon} {title}
      </h2>
      {items.length === 0 ? (
        <div className="card empty" style={{ padding: 28 }}>
          {empty}
        </div>
      ) : (
        <div className="shelf-row">
          {items.map((i) => (
            <ShelfCard key={i.issueId} item={i} onChanged={onChanged} showProgress={showProgress} />
          ))}
        </div>
      )}
    </section>
  );
}

export default function Home() {
  useLibraryVersion();
  const [data, setData] = useState<Awaited<ReturnType<typeof reading.home>> | null>(null);
  const load = useCallback(() => {
    if (isConnected()) reading.home().then(setData).catch(() => setData({ continueReading: [], upNext: [], recentlyAdded: [] }));
  }, []);
  useEffect(load, [load]);

  if (!isConnected()) {
    return (
      <div className="page">
        <h1 className="page-title">Home</h1>
        <div className="card empty">Reading shelves need the Issuarr backend. <Link to="/library" style={{ color: 'var(--accent)' }}>Go to the Library</Link></div>
      </div>
    );
  }

  const nothing = data && data.continueReading.length === 0 && data.upNext.length === 0 && data.recentlyAdded.length === 0;
  return (
    <div className="page home">
      <h1 className="page-title">
        Home
        <Link to="/library" className="btn ghost sm" style={{ marginLeft: 'auto' }}>
          Library <ChevronRight size={14} />
        </Link>
      </h1>
      {!data && <div className="card empty">Loading…</div>}
      {nothing && (
        <div className="card empty">
          <div>
            <div style={{ fontWeight: 600, color: 'var(--text)', fontSize: 16 }}>Nothing to read yet</div>
            <p>Add titles or import your collection; anything with a file can be read right here, and your place is remembered.</p>
            <div style={{ display: 'flex', gap: 10, justifyContent: 'center' }}>
              <Link className="btn primary" to="/add">
                <Plus size={16} /> Add New
              </Link>
              <Link className="btn" to="/import">
                Library Import
              </Link>
            </div>
          </div>
        </div>
      )}
      {data && !nothing && (
        <>
          <Shelf
            title="Continue Reading"
            icon={<BookOpenText size={18} color="var(--accent)" />}
            items={data.continueReading}
            onChanged={load}
            empty={<span className="muted">Open any {itemWord('comic')} or book with 📖 and it’ll show up here so you can pick up where you left off.</span>}
          />
          <Shelf title="Up Next" icon={<Sparkles size={18} color="var(--accent)" />} items={data.upNext} onChanged={load} />
          <Shelf title="Recently Added" icon={<Clock size={18} color="var(--accent)" />} items={data.recentlyAdded} onChanged={load} />
          {data.recentlyAdded[0]?.when && <div className="help" style={{ textAlign: 'right' }}>Latest addition {timeAgo(data.recentlyAdded[0].when)}</div>}
        </>
      )}
    </div>
  );
}
