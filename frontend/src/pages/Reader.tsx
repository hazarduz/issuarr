import ePub, { type Book, type Rendition } from 'epubjs';
import { ArrowLeft, BookOpen, ChevronLeft, ChevronRight, Columns2, Download, List, Loader2, Maximize, Minus, Moon, Plus, RectangleVertical, Settings2, SkipBack, SkipForward, Sun } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { formatBytes, reader, type ReadInfo } from '../data/api';
import { refreshLibrary } from '../data/store';

type Fit = 'width' | 'height' | 'original';

function usePref<T extends string>(key: string, initial: T): [T, (v: T) => void] {
  const [v, setV] = useState<T>(() => {
    try {
      return (localStorage.getItem('issuarr-reader-' + key) as T) || initial;
    } catch {
      return initial;
    }
  });
  const set = (x: T) => {
    setV(x);
    try {
      localStorage.setItem('issuarr-reader-' + key, x);
    } catch {
      /* ignore */
    }
  };
  return [v, set];
}

/** Saves progress at most once a second, and on leaving the page. */
function useProgressSaver(issueId: string) {
  const pending = useRef<{ position: string; percent: number } | null>(null);
  const flush = useCallback(() => {
    const p = pending.current;
    if (!p) return;
    pending.current = null;
    reader.saveProgress(issueId, p.position, p.percent).catch(() => {});
  }, [issueId]);
  useEffect(() => {
    const t = setInterval(flush, 1000);
    window.addEventListener('pagehide', flush);
    return () => {
      clearInterval(t);
      window.removeEventListener('pagehide', flush);
      flush();
      refreshLibrary().catch(() => {});
    };
  }, [flush]);
  return (position: string, percent: number) => (pending.current = { position, percent });
}

export default function Reader() {
  const { issueId = '' } = useParams();
  const [info, setInfo] = useState<ReadInfo | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setInfo(null);
    setError(null);
    reader.info(issueId).then(setInfo).catch((e) => setError(e.message));
  }, [issueId]);

  if (error || info?.error) {
    return (
      <div className="reader">
        <div className="reader-message">
          <div>{error ?? info?.error}</div>
          <Link className="btn" to={info ? `/title/${info.titleSlug}` : '/'}>
            <ArrowLeft size={16} /> Back
          </Link>
          {info && (
            <a className="btn" href={reader.fileUrl(issueId, true)}>
              <Download size={16} /> Download
            </a>
          )}
        </div>
      </div>
    );
  }

  // While switching issues the previous issue's info is still here for one render; don't
  // mount a reader for it (it would save its starting page over the real progress).
  if (!info || String(info.issueId) !== issueId) {
    return (
      <div className="reader">
        <div className="reader-message">
          <Loader2 className="spin-icon" />
        </div>
      </div>
    );
  }

  if (info.mode === 'epub') return <EpubReader key={info.issueId} info={info} />;
  if (info.mode === 'comic' || info.mode === 'pdf') return <PagedReader key={info.issueId} info={info} />;

  return (
    <div className="reader">
      <div className="reader-message">
        <BookOpen size={40} />
        <div style={{ fontWeight: 600 }}>{info.format} files can't be read in the browser</div>
        <div className="muted">Download it for your e-reader or an app like Calibre or KOReader.</div>
        <div style={{ display: 'flex', gap: 10 }}>
          <Link className="btn" to={`/title/${info.titleSlug}`}>
            <ArrowLeft size={16} /> Back
          </Link>
          <a className="btn primary" href={reader.fileUrl(issueId, true)}>
            <Download size={16} /> Download {info.fileName} ({formatBytes(info.size)})
          </a>
        </div>
      </div>
    </div>
  );
}

/** Top/bottom bars that hide while reading and come back on tap or mouse move. */
function Chrome({ info, visible, center, right, bottom }: { info: ReadInfo; visible: boolean; center?: ReactNode; right?: ReactNode; bottom?: ReactNode }) {
  const navigate = useNavigate();
  return (
    <>
      <div className={`reader-bar top ${visible ? '' : 'hidden'}`} onClick={(e) => e.stopPropagation()}>
        <button className="icon-btn" title="Back" onClick={() => navigate(`/title/${info.titleSlug}`)}>
          <ArrowLeft size={20} />
        </button>
        <div className="reader-title">
          <div>{info.titleName}</div>
          <small>
            {info.number}
            {info.issueTitle ? ` — ${info.issueTitle}` : ''}
          </small>
        </div>
        <span style={{ flex: 1 }}>{center}</span>
        {right}
        {info.previousId && (
          <button className="icon-btn" title="Previous" onClick={() => navigate(`/read/${info.previousId}`)}>
            <SkipBack size={18} />
          </button>
        )}
        {info.nextId && (
          <button className="icon-btn" title="Next" onClick={() => navigate(`/read/${info.nextId}`)}>
            <SkipForward size={18} />
          </button>
        )}
        <a className="icon-btn" title="Download" href={reader.fileUrl(info.issueId, true)}>
          <Download size={18} />
        </a>
        <button className="icon-btn" title="Full screen" onClick={() => (document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen()).catch(() => {})}>
          <Maximize size={18} />
        </button>
      </div>
      {bottom && (
        <div className={`reader-bar bottom ${visible ? '' : 'hidden'}`} onClick={(e) => e.stopPropagation()}>
          {bottom}
        </div>
      )}
    </>
  );
}

function useAutoHide() {
  const [visible, setVisible] = useState(true);
  const timer = useRef<number | undefined>(undefined);
  const poke = useCallback(() => {
    setVisible(true);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setVisible(false), 3000);
  }, []);
  useEffect(() => {
    poke();
    return () => window.clearTimeout(timer.current);
  }, [poke]);
  return { visible, poke, toggle: () => (visible ? setVisible(false) : poke()) };
}

/* ------------------------------------------------------------------ */
/* Comics (CBZ/CBR/CB7) and PDFs: one page (or a two-page spread) at a time. */

type PdfDoc = { numPages: number; getPage: (n: number) => Promise<PdfPage> };
type PdfPage = { getViewport: (o: { scale: number }) => { width: number; height: number }; render: (o: { canvasContext: CanvasRenderingContext2D; viewport: unknown; canvas: HTMLCanvasElement }) => { promise: Promise<void>; cancel: () => void } };

function PagedReader({ info }: { info: ReadInfo }) {
  const navigate = useNavigate();
  const [pdf, setPdf] = useState<PdfDoc | null>(null);
  const [pageCount, setPageCount] = useState(info.pages ?? 0);
  const saved = Number(info.progress?.position);
  const [page, setPage] = useState(Number.isFinite(saved) && saved > 0 && saved < (info.pages ?? Infinity) ? saved : 0);
  const [fit, setFit] = usePref<Fit>('fit', 'height');
  const [spread, setSpread] = usePref<'single' | 'double'>('spread', 'single');
  const [direction, setDirection] = usePref<'ltr' | 'rtl'>('dir', 'ltr');
  const [settingsOpen, setSettingsOpen] = useState(false);
  const { visible, poke, toggle } = useAutoHide();
  const save = useProgressSaver(String(info.issueId));
  const touchX = useRef<number | null>(null);

  // PDFs: load with pdf.js (only when needed, it's large).
  useEffect(() => {
    if (info.mode !== 'pdf') return;
    let cancelled = false;
    (async () => {
      const pdfjs = await import('pdfjs-dist');
      const worker = await import('pdfjs-dist/build/pdf.worker.min.mjs?url');
      pdfjs.GlobalWorkerOptions.workerSrc = worker.default;
      const doc = (await pdfjs.getDocument({ url: reader.fileUrl(info.issueId), rangeChunkSize: 1 << 20 }).promise) as unknown as PdfDoc;
      if (cancelled) return;
      setPdf(doc);
      setPageCount(doc.numPages);
      setPage((p) => Math.min(p, doc.numPages - 1));
    })();
    return () => {
      cancelled = true;
    };
  }, [info.issueId, info.mode]);

  const double = spread === 'double' && fit !== 'width';
  const step = double ? 2 : 1;
  const last = Math.max(0, pageCount - 1);

  const go = useCallback(
    (to: number) => {
      const clamped = Math.max(0, Math.min(last, to));
      setPage(clamped);
      if (pageCount > 0) save(String(clamped), ((clamped + (double ? 2 : 1)) / pageCount) * 100);
    },
    [last, pageCount, save, double],
  );
  const next = useCallback(() => (page >= last - (double ? 1 : 0) ? info.nextId && navigate(`/read/${info.nextId}`) : go(page + step)), [page, last, double, step, go, info.nextId, navigate]);
  const prev = useCallback(() => go(page - step), [page, step, go]);
  const forward = direction === 'rtl' ? prev : next;
  const backward = direction === 'rtl' ? next : prev;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight' || e.key === ' ' || e.key === 'PageDown') (e.key === 'ArrowRight' ? forward : next)();
      else if (e.key === 'ArrowLeft' || e.key === 'PageUp') (e.key === 'ArrowLeft' ? backward : prev)();
      else if (e.key === 'Home') go(0);
      else if (e.key === 'End') go(last);
      else return;
      e.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [forward, backward, next, prev, go, last]);

  useEffect(() => {
    if (pageCount > 0) save(String(page), ((page + 1) / pageCount) * 100);
    // eslint-disable-next-line
  }, [pageCount]);

  const pages = double && page + 1 <= last ? (direction === 'rtl' ? [page + 1, page] : [page, page + 1]) : [page];
  const ready = info.mode === 'comic' || pdf;

  return (
    <div
      className={`reader paged fit-${fit} ${double ? 'double' : ''}`}
      onMouseMove={poke}
      onTouchStart={(e) => (touchX.current = e.touches[0].clientX)}
      onTouchEnd={(e) => {
        if (touchX.current == null) return;
        const dx = e.changedTouches[0].clientX - touchX.current;
        touchX.current = null;
        if (Math.abs(dx) > 50) (dx < 0 ? forward : backward)();
      }}
    >
      <div
        className="reader-stage"
        onClick={(e) => {
          const x = e.clientX / window.innerWidth;
          if (x < 0.3) backward();
          else if (x > 0.7) forward();
          else toggle();
        }}
      >
        {!ready && <Loader2 className="spin-icon" color="#fff" />}
        {ready &&
          pages.map((n) =>
            info.mode === 'comic' ? <img key={n} className="reader-page" src={reader.pageUrl(info.issueId, n)} alt={`Page ${n + 1}`} draggable={false} /> : <PdfCanvas key={n} pdf={pdf!} page={n} fit={fit} double={double} />,
          )}
        {/* Preload the next pages so turning is instant. */}
        {info.mode === 'comic' && [1, 2, 3].map((d) => page + step - 1 + d <= last && <link key={d} rel="prefetch" href={reader.pageUrl(info.issueId, page + step - 1 + d)} />)}
      </div>

      <Chrome
        info={info}
        visible={visible || settingsOpen}
        center={
          pageCount > 0 && (
            <span className="muted" style={{ fontSize: 13 }}>
              {pages.map((n) => n + 1).join('–')} / {pageCount}
            </span>
          )
        }
        right={
          <div className="rel-wrap">
            <button className="icon-btn" title="Reading options" onClick={() => setSettingsOpen((o) => !o)}>
              <Settings2 size={18} />
            </button>
            {settingsOpen && (
              <div className="menu reader-menu">
                <div className="menu-label">Fit</div>
                {(['height', 'width', 'original'] as Fit[]).map((f) => (
                  <button key={f} className={fit === f ? 'on' : ''} onClick={() => setFit(f)}>
                    {f === 'height' ? 'Fit to screen' : f === 'width' ? 'Fit width (scroll)' : 'Original size'}
                  </button>
                ))}
                <div className="menu-label">Layout</div>
                <button className={spread === 'single' ? 'on' : ''} onClick={() => setSpread('single')}>
                  <RectangleVertical size={14} /> Single page
                </button>
                <button className={spread === 'double' ? 'on' : ''} onClick={() => setSpread('double')}>
                  <Columns2 size={14} /> Two-page spread
                </button>
                <div className="menu-label">Direction</div>
                <button className={direction === 'ltr' ? 'on' : ''} onClick={() => setDirection('ltr')}>
                  Left to right
                </button>
                <button className={direction === 'rtl' ? 'on' : ''} onClick={() => setDirection('rtl')}>
                  Right to left (manga)
                </button>
              </div>
            )}
          </div>
        }
        bottom={
          pageCount > 1 && (
            <>
              <button className="icon-btn" onClick={backward} title="Previous page">
                <ChevronLeft size={20} />
              </button>
              <input
                type="range"
                min={0}
                max={last}
                value={page}
                onChange={(e) => go(Number(e.target.value))}
                style={{ flex: 1, accentColor: 'var(--accent)', direction: direction === 'rtl' ? 'rtl' : 'ltr' }}
                aria-label="Page"
              />
              <button className="icon-btn" onClick={forward} title="Next page">
                <ChevronRight size={20} />
              </button>
            </>
          )
        }
      />
      {page >= last - (double ? 1 : 0) && pageCount > 0 && visible && (
        <div className="reader-end" onClick={(e) => e.stopPropagation()}>
          {info.nextId ? (
            <button className="btn primary" onClick={() => navigate(`/read/${info.nextId}`)}>
              Next {info.kind === 'book' ? 'book' : 'issue'} <SkipForward size={16} />
            </button>
          ) : (
            <Link className="btn" to={`/title/${info.titleSlug}`}>
              Finished — back to {info.titleName}
            </Link>
          )}
        </div>
      )}
    </div>
  );
}

/** Renders one PDF page to a canvas sized for the screen (sharp on high-DPI displays). */
function PdfCanvas({ pdf, page, fit, double }: { pdf: PdfDoc; page: number; fit: Fit; double: boolean }) {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    let task: { promise: Promise<void>; cancel: () => void } | null = null;
    let cancelled = false;
    pdf.getPage(page + 1).then((p) => {
      if (cancelled || !ref.current) return;
      const base = p.getViewport({ scale: 1 });
      const availW = (window.innerWidth - 16) / (double ? 2 : 1);
      const availH = window.innerHeight - 8;
      const scale = fit === 'width' ? availW / base.width : fit === 'height' ? Math.min(availW / base.width, availH / base.height) : 1.5;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const vp = p.getViewport({ scale: scale * dpr });
      const canvas = ref.current;
      canvas.width = vp.width;
      canvas.height = vp.height;
      canvas.style.width = `${vp.width / dpr}px`;
      canvas.style.height = `${vp.height / dpr}px`;
      task = p.render({ canvasContext: canvas.getContext('2d')!, viewport: vp, canvas });
      task.promise.catch(() => {});
    });
    return () => {
      cancelled = true;
      task?.cancel();
    };
  }, [pdf, page, fit, double]);
  return <canvas ref={ref} className="reader-page" />;
}

/* ------------------------------------------------------------------ */
/* EPUB: flowing text with themes, font size and a table of contents. */

const themes = {
  dark: { body: { background: '#14161c', color: '#dfe2e8' }, a: { color: '#ff9a62' } },
  light: { body: { background: '#fbfaf7', color: '#1d1d1f' }, a: { color: '#c2410c' } },
  sepia: { body: { background: '#f4ecd8', color: '#5b4636' }, a: { color: '#9a3412' } },
};

/**
 * Cover and full-page illustration pages are usually one small image at its original size.
 * Scale those up to fill the page, centred across both pages of a spread.
 */
function fitImagePages(contents: { document: Document }) {
  const doc = contents.document;
  const body = doc.body;
  if (!body) return;
  const images = body.querySelectorAll('img, svg, image');
  const text = (body.textContent ?? '').replace(/\s+/g, '');
  if (images.length === 0 || images.length > 2 || text.length > 40) return;
  const style = doc.createElement('style');
  style.textContent = `
    body > * { column-span: all; }
    body { text-align: center; }
    img, svg { display: block; margin: 0 auto !important; height: calc(100vh - 8px) !important; width: auto !important; max-width: 100% !important; object-fit: contain; }
    div, p, section, figure { margin: 0 !important; padding: 0 !important; text-indent: 0 !important; }
  `;
  doc.head.appendChild(style);
}

function EpubReader({ info }: { info: ReadInfo }) {
  const host = useRef<HTMLDivElement>(null);
  const rendition = useRef<Rendition | null>(null);
  const book = useRef<Book | null>(null);
  const [theme, setTheme] = usePref<keyof typeof themes>('theme', 'dark');
  const [fontSize, setFontSize] = usePref<string>('font', '110');
  const [toc, setToc] = useState<{ label: string; href: string }[]>([]);
  const [tocOpen, setTocOpen] = useState(false);
  const [percent, setPercent] = useState<number | null>(info.progress ? Math.round(info.progress.percent) : null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState<string | null>(null);
  const { visible, poke, toggle } = useAutoHide();
  const save = useProgressSaver(String(info.issueId));

  useEffect(() => {
    const b = ePub(reader.fileUrl(info.issueId), { openAs: 'epub' });
    book.current = b;
    const r = b.renderTo(host.current!, { width: '100%', height: '100%', flow: 'paginated', spread: 'auto', allowScriptedContent: false });
    rendition.current = r;
    r.hooks.content.register(fitImagePages);
    Object.entries(themes).forEach(([name, styles]) => r.themes.register(name, styles));
    r.themes.select(theme);
    r.themes.fontSize(`${fontSize}%`);

    const start = info.progress?.position && info.progress.position.startsWith('epubcfi') ? info.progress.position : undefined;
    r.display(start).then(() => setLoading(false)).catch((e: Error) => setFailed(`Couldn't open this EPUB: ${e.message}`));
    b.loaded.navigation.then((nav) => setToc(nav.toc.map((t) => ({ label: t.label.trim(), href: t.href }))));

    // Locations give an accurate percentage; generating them takes a moment on big books.
    b.ready.then(() => b.locations.generate(1200)).then(() => {
      // Progress synced from KOReader arrives as a percentage ("pct:0.42"): jump there.
      const pos = info.progress?.position;
      if (pos?.startsWith('pct:')) {
        const cfi = b.locations.cfiFromPercentage(Number(pos.slice(4)));
        if (cfi) r.display(cfi);
      }
      const loc = r.currentLocation() as unknown as { start?: { cfi: string } };
      if (loc?.start) setPercent(Math.round(b.locations.percentageFromCfi(loc.start.cfi) * 100));
    });

    r.on('relocated', (loc: { start: { cfi: string; percentage: number } }) => {
      const pct = b.locations.length() > 0 ? b.locations.percentageFromCfi(loc.start.cfi) * 100 : loc.start.percentage * 100;
      setPercent(Math.round(pct));
      save(loc.start.cfi, pct);
    });
    r.on('keyup', (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight') r.next();
      if (e.key === 'ArrowLeft') r.prev();
    });
    r.on('click', () => toggle());

    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight' || e.key === 'PageDown') r.next();
      if (e.key === 'ArrowLeft' || e.key === 'PageUp') r.prev();
    };
    window.addEventListener('keydown', onKey);
    const onResize = () => r.resize(host.current?.clientWidth ?? window.innerWidth, host.current?.clientHeight ?? window.innerHeight);
    window.addEventListener('resize', onResize);
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('resize', onResize);
      b.destroy();
    };
    // eslint-disable-next-line
  }, [info.issueId]);

  useEffect(() => {
    rendition.current?.themes.select(theme);
  }, [theme]);
  useEffect(() => {
    rendition.current?.themes.fontSize(`${fontSize}%`);
  }, [fontSize]);

  const bg = themes[theme].body.background;

  return (
    <div className={`reader epub theme-${theme}`} style={{ background: bg }} onMouseMove={poke}>
      <div className="epub-host" ref={host} />
      <button className="epub-zone left" aria-label="Previous page" onClick={() => rendition.current?.prev()} />
      <button className="epub-zone right" aria-label="Next page" onClick={() => rendition.current?.next()} />
      {(loading || failed) && (
        <div className="reader-message" style={{ position: 'absolute', inset: 0 }}>
          {failed ?? <Loader2 className="spin-icon" />}
        </div>
      )}
      <Chrome
        info={info}
        visible={visible || tocOpen}
        center={percent != null && <span className="muted" style={{ fontSize: 13 }}>{percent}%</span>}
        right={
          <>
            <button className="icon-btn" title="Contents" onClick={() => setTocOpen((o) => !o)}>
              <List size={18} />
            </button>
            <button className="icon-btn" title="Smaller text" onClick={() => setFontSize(String(Math.max(70, Number(fontSize) - 10)))}>
              <Minus size={16} />
            </button>
            <button className="icon-btn" title="Larger text" onClick={() => setFontSize(String(Math.min(200, Number(fontSize) + 10)))}>
              <Plus size={16} />
            </button>
            <button className="icon-btn" title="Theme" onClick={() => setTheme(theme === 'dark' ? 'light' : theme === 'light' ? 'sepia' : 'dark')}>
              {theme === 'dark' ? <Moon size={18} /> : <Sun size={18} />}
            </button>
          </>
        }
      />
      {tocOpen && (
        <div className="epub-toc" onClick={(e) => e.stopPropagation()}>
          <div style={{ fontWeight: 600, padding: '8px 12px' }}>Contents</div>
          {toc.length === 0 && <div className="muted" style={{ padding: 12 }}>No table of contents</div>}
          {toc.map((t) => (
            <button
              key={t.href}
              onClick={() => {
                rendition.current?.display(t.href);
                setTocOpen(false);
              }}
            >
              {t.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
