import { BookOpen, BookOpenText, Check, ChevronLeft, ChevronRight, Library, MoreHorizontal, Newspaper, Plus, Sparkles, X } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Cover } from '../components/Cover';
import { useToast } from '../components/Toast';
import { covers, isConnected, reading, type ShelfItem } from '../data/api';
import { itemWord, titles } from '../data/mock';
import { refreshLibrary, useLibraryVersion } from '../data/store';

/** Issue/book cover: from the file itself, falling back to the title's cover. */
function ItemCover({ item }: { item: ShelfItem }) {
  const [failed, setFailed] = useState(false);
  const title = titles.find((t) => t.id === String(item.titleId));
  const issue = title?.issues.find((i) => i.serverId === item.issueId);
  const src = issue?.coverUrl ?? (['CBZ', 'CBR', 'CB7', 'EPUB', 'PDF'].includes(item.format) ? covers.issueCoverUrl(item.issueId) : undefined);
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
  // The menu floats above the page (position: fixed) so the scrolling shelf doesn't clip it.
  const [menu, setMenu] = useState<{ top: number; left: number } | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!menu) return;
    const close = (e: Event) => (e.type === 'scroll' || (ref.current && !ref.current.contains(e.target as Node))) && setMenu(null);
    document.addEventListener('mousedown', close);
    window.addEventListener('scroll', close, true);
    window.addEventListener('resize', close);
    return () => {
      document.removeEventListener('mousedown', close);
      window.removeEventListener('scroll', close, true);
      window.removeEventListener('resize', close);
    };
  }, [menu]);
  const openMenu = (e: React.MouseEvent<HTMLButtonElement>) => {
    if (menu) return setMenu(null);
    const r = e.currentTarget.getBoundingClientRect();
    const height = 120;
    setMenu({
      left: Math.max(8, Math.min(r.right - 200, window.innerWidth - 208)),
      top: r.bottom + height + 8 > window.innerHeight ? r.top - height - 4 : r.bottom + 4,
    });
  };

  const act = async (fn: () => Promise<unknown>, msg: string) => {
    setMenu(null);
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
      <div
        className="shelf-cover"
        role="button"
        tabIndex={0}
        onClick={() => navigate(`/read/${item.issueId}`)}
        onKeyDown={(e) => e.key === 'Enter' && navigate(`/read/${item.issueId}`)}
        title={`Read ${label}`}
      >
        <ItemCover item={item} />
        <span className="shelf-play">
          <BookOpenText size={22} />
        </span>
        {showProgress && item.percent != null && item.percent > 0 && (
          <span className="shelf-progress">
            <span style={{ width: `${Math.min(100, item.percent)}%` }} />
          </span>
        )}
      </div>
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
        <div>
          <button className="icon-btn" style={{ width: 28, height: 28 }} onClick={openMenu} aria-label="More">
            <MoreHorizontal size={16} />
          </button>
          {menu && (
            <div className="menu shelf-menu" style={{ top: menu.top, left: menu.left }}>
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

function Shelf({
  title,
  icon,
  items,
  empty,
  onChanged,
  showProgress = true,
  more,
}: {
  title: string;
  icon: React.ReactNode;
  items: ShelfItem[];
  empty?: React.ReactNode;
  onChanged: () => void;
  showProgress?: boolean;
  more?: string;
}) {
  const track = useRef<HTMLDivElement>(null);
  const [edges, setEdges] = useState({ start: true, end: true });
  const measure = useCallback(() => {
    const t = track.current;
    if (t) setEdges({ start: t.scrollLeft <= 2, end: t.scrollLeft + t.clientWidth >= t.scrollWidth - 2 });
  }, []);
  useEffect(() => {
    measure();
    window.addEventListener('resize', measure);
    return () => window.removeEventListener('resize', measure);
  }, [measure, items.length]);
  const page = (dir: 1 | -1) => track.current?.scrollBy({ left: dir * track.current.clientWidth * 0.85, behavior: 'smooth' });

  if (items.length === 0 && !empty) return null;
  return (
    <section className="shelf">
      <div className="shelf-head">
        <h2>
          {icon} {title}
        </h2>
        {more && (
          <Link to={more} className="shelf-more">
            See all <ChevronRight size={14} />
          </Link>
        )}
        {items.length > 0 && !(edges.start && edges.end) && (
          <span className="shelf-arrows">
            <button className="icon-btn" aria-label="Scroll left" disabled={edges.start} onClick={() => page(-1)}>
              <ChevronLeft size={18} />
            </button>
            <button className="icon-btn" aria-label="Scroll right" disabled={edges.end} onClick={() => page(1)}>
              <ChevronRight size={18} />
            </button>
          </span>
        )}
      </div>
      {items.length === 0 ? (
        <div className="card empty" style={{ padding: 28 }}>
          {empty}
        </div>
      ) : (
        <div className="shelf-row" ref={track} onScroll={measure}>
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
    if (isConnected()) reading.home().then(setData).catch(() => setData({ continueReading: [], upNext: [], recentlyAdded: [], latestComics: [], latestMagazines: [], latestBooks: [] }));
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
          <Shelf title="Latest Comics" icon={<BookOpen size={18} color="var(--accent)" />} items={data.latestComics} onChanged={load} more="/library?kind=comic" />
          <Shelf title="Latest Magazines" icon={<Newspaper size={18} color="var(--accent)" />} items={data.latestMagazines} onChanged={load} more="/library?kind=magazine" />
          <Shelf title="Latest Books" icon={<Library size={18} color="var(--accent)" />} items={data.latestBooks} onChanged={load} more="/library?kind=book" />
        </>
      )}
    </div>
  );
}
