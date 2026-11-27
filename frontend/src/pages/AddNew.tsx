import { BookOpen, BookText, Check, ExternalLink, Loader2, Newspaper, Plus, Search, UserRound } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Modal } from '../components/Modal';
import { SafeImg } from '../components/SafeImg';
import { useToast } from '../components/Toast';
import { api, isConnected, type BookResult, type LookupResult, type QualityProfile, type RootFolder } from '../data/api';
import { sourceName, type Kind } from '../data/mock';
import { notifyLibrary } from '../data/store';

const frequencies = ['Weekly', 'Fortnightly', 'Monthly', 'Bi-monthly', 'Quarterly', 'Annually'];

interface AddDraft {
  kind: Kind;
  name: string;
  year: number;
  publisher?: string;
  overview?: string;
  coverUrl?: string;
  metadataSource?: string;
  externalId?: string;
  frequency?: string;
  firstIssueDate?: string;
  /** eBooks: add just this book of the author. */
  book?: { externalId: string; title: string };
}

/** Debounced search hook shared by the tabs. */
function useSearch<T>(term: string, enabled: boolean, fn: (t: string) => Promise<T[]>) {
  const [results, setResults] = useState<T[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const t = term.trim();
    if (!enabled || t.length < 2) {
      setResults(null);
      setError(null);
      return;
    }
    if (!isConnected()) {
      setError('Searching needs the Issuarr backend.');
      return;
    }
    setLoading(true);
    const timer = setTimeout(async () => {
      try {
        setResults(await fn(t));
        setError(null);
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Search failed');
        setResults(null);
      } finally {
        setLoading(false);
      }
    }, 500);
    return () => clearTimeout(timer);
    // eslint-disable-next-line
  }, [term, enabled]);
  return { results, loading, error };
}

function SearchState({ loading, error, empty }: { loading: boolean; error: string | null; empty: boolean }) {
  return (
    <>
      {loading && (
        <div className="muted" style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 12 }}>
          <Loader2 size={16} className="spin-icon" /> Searching…
        </div>
      )}
      {error && (
        <div className="card empty">
          <div>
            <div style={{ color: 'var(--danger)', fontWeight: 600 }}>{error}</div>
            {error.includes('Settings') && (
              <Link to="/settings/general" className="btn" style={{ marginTop: 12 }}>
                Open Settings → General
              </Link>
            )}
          </div>
        </div>
      )}
      {!loading && !error && empty && <div className="card empty">Nothing found. Try a shorter search.</div>}
    </>
  );
}

function ResultRow({ cover, title, meta, overview, action }: { cover?: string; title: React.ReactNode; meta: React.ReactNode; overview?: string; action: React.ReactNode }) {
  return (
    <div className="card overview-row" style={{ gridTemplateColumns: '80px 1fr' }}>
      <div className="lookup-cover">
        <SafeImg src={cover} />
      </div>
      <div style={{ minWidth: 0 }}>
        <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start', flexWrap: 'wrap' }}>
          <div style={{ flex: 1, minWidth: 200 }}>
            <h3>{title}</h3>
            <div className="row-meta">{meta}</div>
          </div>
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>{action}</div>
        </div>
        {overview && <p>{overview}</p>}
      </div>
    </div>
  );
}

function SourceLink({ url, source }: { url?: string; source: string }) {
  return url ? (
    <a className="pill" href={url} target="_blank" rel="noreferrer">
      {sourceName[source] ?? source} <ExternalLink size={11} />
    </a>
  ) : null;
}

export default function AddNew() {
  const params = new URLSearchParams(useLocation().search);
  const [tab, setTab] = useState<Kind>((params.get('kind') as Kind) ?? 'comic');
  const [q, setQ] = useState(params.get('term') ?? '');
  const [bookMode, setBookMode] = useState<'books' | 'authors'>('books');
  const [draft, setDraft] = useState<AddDraft | null>(null);
  const [added, setAdded] = useState<Record<string, string>>({});
  const [mag, setMag] = useState<AddDraft>({ kind: 'magazine', name: '', year: new Date().getFullYear(), frequency: 'Monthly', firstIssueDate: new Date().toISOString().slice(0, 8) + '01' });
  const [magQ, setMagQ] = useState('');

  const comics = useSearch<LookupResult>(q, tab === 'comic', (t) => api.lookup(t, undefined, 'comic'));
  const books = useSearch<BookResult>(q, tab === 'book' && bookMode === 'books', (t) => api.lookupBook(t));
  const authors = useSearch<LookupResult>(q, tab === 'book' && bookMode === 'authors', (t) => api.lookup(t, undefined, 'book'));
  const mags = useSearch<LookupResult>(magQ, tab === 'magazine', (t) => api.lookup(t, undefined, 'magazine'));

  return (
    <div className="page">
      <h1 className="page-title">Add New</h1>
      <div className="tabs">
        <button className={tab === 'comic' ? 'on' : ''} onClick={() => setTab('comic')}>
          <BookOpen size={15} style={{ verticalAlign: -2 }} /> Comic series
        </button>
        <button className={tab === 'magazine' ? 'on' : ''} onClick={() => setTab('magazine')}>
          <Newspaper size={15} style={{ verticalAlign: -2 }} /> Magazine
        </button>
        <button className={tab === 'book' ? 'on' : ''} onClick={() => setTab('book')}>
          <BookText size={15} style={{ verticalAlign: -2 }} /> eBook
        </button>
      </div>

      {tab !== 'magazine' && (
        <div style={{ display: 'flex', gap: 10, marginBottom: 24, flexWrap: 'wrap', alignItems: 'center' }}>
          <div className="add-search" style={{ flex: 1, minWidth: 260 }}>
            <Search size={20} />
            <input
              autoFocus
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder={tab === 'comic' ? 'Search for a comic series, e.g. “Saga” or “Batman 2016”' : bookMode === 'books' ? 'Search for a book or author, e.g. “The Way of Kings”' : 'Search for an author, e.g. “Ursula K. Le Guin”'}
            />
          </div>
          {tab === 'book' && (
            <div className="segmented">
              <button className={bookMode === 'books' ? 'on' : ''} onClick={() => setBookMode('books')}>
                Books
              </button>
              <button className={bookMode === 'authors' ? 'on' : ''} onClick={() => setBookMode('authors')}>
                Authors
              </button>
            </div>
          )}
        </div>
      )}

      {tab === 'comic' && (
        <>
          <SearchState loading={comics.loading} error={comics.error} empty={comics.results?.length === 0} />
          <div className="overview-list" style={{ maxWidth: 980 }}>
            {comics.results?.map((r) => {
              const slug = r.existingSlug ?? added[r.source + r.externalId];
              return (
                <ResultRow
                  key={r.externalId}
                  cover={r.coverUrl}
                  title={
                    <>
                      {r.name} {r.year && <span style={{ color: 'var(--text-faint)', fontWeight: 500 }}>({r.year})</span>}
                    </>
                  }
                  meta={
                    <>
                      {r.publisher && <span className="pill">{r.publisher}</span>}
                      <span className="pill">{r.issueCount} issues</span>
                      <SourceLink url={r.siteUrl} source={r.source} />
                    </>
                  }
                  overview={r.overview}
                  action={
                    slug ? (
                      <Link className="btn" to={`/title/${slug}`}>
                        <Check size={16} /> In library
                      </Link>
                    ) : (
                      <button className="btn primary" onClick={() => setDraft({ kind: 'comic', name: r.name, year: r.year ?? new Date().getFullYear(), publisher: r.publisher, overview: r.overview, coverUrl: r.coverUrl, metadataSource: r.source, externalId: r.externalId })}>
                        <Plus size={16} /> Add
                      </button>
                    )
                  }
                />
              );
            })}
          </div>
        </>
      )}

      {tab === 'book' && bookMode === 'books' && (
        <>
          <SearchState loading={books.loading} error={books.error} empty={books.results?.length === 0} />
          <div className="overview-list" style={{ maxWidth: 980 }}>
            {books.results?.map((b) => {
              const authorSlug = b.authorSlug ?? added[b.source + (b.authorId ?? '')];
              const done = b.inLibrary || added[b.source + b.workId];
              const draftFor = (justThisBook: boolean): AddDraft => ({
                kind: 'book',
                name: b.author ?? 'Unknown author',
                year: b.year ?? new Date().getFullYear(),
                metadataSource: b.source,
                externalId: b.authorId,
                coverUrl: undefined,
                book: justThisBook ? { externalId: b.workId, title: b.title } : undefined,
              });
              return (
                <ResultRow
                  key={b.workId}
                  cover={b.coverUrl}
                  title={
                    <>
                      {b.title} {b.year && <span style={{ color: 'var(--text-faint)', fontWeight: 500 }}>({b.year})</span>}
                    </>
                  }
                  meta={
                    <>
                      <span className="pill">
                        <UserRound size={11} /> {b.author}
                      </span>
                      {authorSlug && (
                        <Link className="pill ok" to={`/title/${authorSlug}`}>
                          Author in library
                        </Link>
                      )}
                      <SourceLink url={b.siteUrl} source={b.source} />
                    </>
                  }
                  overview={b.overview}
                  action={
                    done ? (
                      <Link className="btn" to={`/title/${authorSlug ?? ''}`}>
                        <Check size={16} /> In library
                      </Link>
                    ) : (
                      <>
                        <button className="btn primary" onClick={() => setDraft(draftFor(true))}>
                          <Plus size={16} /> Add book
                        </button>
                        {!authorSlug && (
                          <button className="btn" onClick={() => setDraft(draftFor(false))} title={`Follow ${b.author}: all their books`}>
                            <UserRound size={16} /> Add author
                          </button>
                        )}
                      </>
                    )
                  }
                />
              );
            })}
          </div>
        </>
      )}

      {tab === 'book' && bookMode === 'authors' && (
        <>
          <SearchState loading={authors.loading} error={authors.error} empty={authors.results?.length === 0} />
          <div className="overview-list" style={{ maxWidth: 980 }}>
            {authors.results?.map((a) => {
              const slug = a.existingSlug ?? added[a.source + a.externalId];
              return (
                <ResultRow
                  key={a.externalId}
                  cover={a.coverUrl}
                  title={a.name}
                  meta={
                    <>
                      {a.issueCount > 0 && <span className="pill">{a.issueCount} books</span>}
                      <SourceLink url={a.siteUrl} source={a.source} />
                    </>
                  }
                  overview={a.overview}
                  action={
                    slug ? (
                      <Link className="btn" to={`/title/${slug}`}>
                        <Check size={16} /> In library
                      </Link>
                    ) : (
                      <button className="btn primary" onClick={() => setDraft({ kind: 'book', name: a.name, year: new Date().getFullYear(), overview: a.overview, coverUrl: a.coverUrl, metadataSource: a.source, externalId: a.externalId })}>
                        <Plus size={16} /> Add author
                      </button>
                    )
                  }
                />
              );
            })}
          </div>
        </>
      )}

      {tab === 'magazine' && (
        <div style={{ maxWidth: 980 }}>
          <div className="add-search" style={{ marginBottom: 14 }}>
            <Search size={20} />
            <input autoFocus value={magQ} onChange={(e) => setMagQ(e.target.value)} placeholder="Look up a magazine to fill in its details, e.g. “Retro Gamer” (optional)" />
          </div>
          <SearchState loading={mags.loading} error={mags.error} empty={mags.results?.length === 0} />
          {mags.results && mags.results.length > 0 && (
            <div className="overview-list" style={{ marginBottom: 20 }}>
              {mags.results.slice(0, 6).map((r) => {
                const slug = r.existingSlug ?? added[r.source + r.externalId];
                return (
                  <ResultRow
                    key={r.externalId}
                    cover={r.coverUrl}
                    title={
                      <>
                        {r.name} {r.year && <span style={{ color: 'var(--text-faint)', fontWeight: 500 }}>(since {r.year})</span>}
                      </>
                    }
                    meta={
                      <>
                        {r.publisher && <span className="pill">{r.publisher}</span>}
                        {r.frequency && <span className="pill">{r.frequency}</span>}
                        {r.issueCount > 0 && <span className="pill">{r.issueCount} issues</span>}
                        <SourceLink url={r.siteUrl} source={r.source} />
                      </>
                    }
                    overview={r.overview}
                    action={
                      slug ? (
                        <Link className="btn" to={`/title/${slug}`}>
                          <Check size={16} /> In library
                        </Link>
                      ) : (
                        <button
                          className="btn primary"
                          onClick={() => {
                            setMag((m) => ({ ...m, name: r.name, publisher: r.publisher, frequency: r.frequency ?? m.frequency, overview: r.overview, coverUrl: r.coverUrl, metadataSource: r.source, externalId: r.externalId }));
                            setMagQ('');
                          }}
                        >
                          Use this
                        </button>
                      )
                    }
                  />
                );
              })}
            </div>
          )}

          <div className="card card-body" style={{ maxWidth: 760 }}>
            <p className="muted" style={{ marginTop: 0 }}>
              {mag.metadataSource ? (
                <>
                  Details from <b>{sourceName[mag.metadataSource] ?? mag.metadataSource}</b>.{' '}
                  <a style={{ color: 'var(--accent)', cursor: 'pointer' }} onClick={() => setMag((m) => ({ ...m, metadataSource: undefined, externalId: undefined, coverUrl: undefined, overview: undefined }))}>
                    Clear
                  </a>{' '}
                </>
              ) : null}
              Issues are built from how often it comes out: every expected issue since the date you choose, plus the next one, with new ones added as their dates pass.
            </p>
            <div className="form-grid">
              <label>Name *</label>
              <input className="input" value={mag.name} onChange={(e) => setMag({ ...mag, name: e.target.value })} placeholder="e.g. Circuit & Solder" />
              <label>Publisher</label>
              <input className="input" value={mag.publisher ?? ''} onChange={(e) => setMag({ ...mag, publisher: e.target.value })} />
              <label>Comes out</label>
              <select className="input" value={mag.frequency} onChange={(e) => setMag({ ...mag, frequency: e.target.value })}>
                {frequencies.map((f) => (
                  <option key={f}>{f}</option>
                ))}
              </select>
              <label>Track issues from</label>
              <div>
                <input className="input" type="date" value={mag.firstIssueDate} onChange={(e) => setMag({ ...mag, firstIssueDate: e.target.value, year: Number(e.target.value.slice(0, 4)) || mag.year })} />
                <div className="help">Date of the first issue you want. Issues before this are ignored.</div>
              </div>
            </div>
            <button className="btn primary" style={{ marginTop: 18 }} disabled={!mag.name.trim()} onClick={() => setDraft({ ...mag, name: mag.name.trim() })}>
              <Plus size={16} /> Continue
            </button>
          </div>
        </div>
      )}

      {draft && (
        <AddDialog
          draft={draft}
          onClose={() => setDraft(null)}
          onAdded={(slug) => {
            setAdded((a) => ({
              ...a,
              ...(draft.externalId ? { [(draft.metadataSource ?? '') + draft.externalId]: slug } : {}),
              ...(draft.book ? { [(draft.metadataSource ?? '') + draft.book.externalId]: slug } : {}),
            }));
            setDraft(null);
          }}
        />
      )}
    </div>
  );
}

function AddDialog({ draft, onClose, onAdded }: { draft: AddDraft; onClose: () => void; onAdded: (slug: string) => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const [folders, setFolders] = useState<RootFolder[]>([]);
  const [profiles, setProfiles] = useState<QualityProfile[]>([]);
  const [root, setRoot] = useState('');
  const [monitor, setMonitor] = useState(draft.kind === 'book' ? 'future' : 'all');
  const [profile, setProfile] = useState(draft.kind === 'comic' ? 'CBZ / CBR' : draft.kind === 'book' ? 'eBook' : 'PDF Preferred');
  const [search, setSearch] = useState(true);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.rootFolders().then((f) => {
      setFolders(f);
      setRoot((r) => r || f[0]?.path || '');
    });
    api.qualityProfiles().then(setProfiles);
  }, []);

  const what = draft.kind === 'comic' ? 'series' : draft.kind === 'book' ? (draft.book ? 'book' : 'author') : 'magazine';

  const add = async () => {
    setBusy(true);
    try {
      const { title, warning } = await api.addTitle({
        name: draft.name,
        kind: draft.kind,
        year: draft.year,
        publisher: draft.publisher,
        overview: draft.overview,
        coverUrl: draft.coverUrl,
        metadataSource: draft.metadataSource ?? null,
        externalId: draft.externalId ?? null,
        frequency: draft.frequency ?? (draft.kind === 'comic' ? 'Monthly' : undefined),
        firstIssueDate: draft.firstIssueDate,
        bookExternalId: draft.book?.externalId,
        bookTitle: draft.book?.title,
        status: 'continuing',
        qualityProfile: profile,
        rootFolderPath: root,
        monitor,
        searchForMissing: search,
        tags: [],
        palette: [],
      });
      notifyLibrary();
      if (warning) toast(warning, 'error');
      else toast(draft.book ? `${draft.book.title} added to ${title.name}${search ? ' — searching' : ''}` : `${title.name} added${search && monitor !== 'none' ? ' — searching' : ''}`);
      onAdded(title.slug);
      navigate(`/title/${title.slug}`);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not add', 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={draft.book ? `Add “${draft.book.title}” by ${draft.name}` : `Add ${draft.name}`}
      onClose={onClose}
      footer={
        <>
          <span style={{ flex: 1 }} />
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" onClick={add} disabled={busy || !root}>
            {busy ? <Loader2 size={16} className="spin-icon" /> : <Plus size={16} />} Add {what}
          </button>
        </>
      }
    >
      {folders.length === 0 ? (
        <div className="card empty" style={{ padding: 20 }}>
          <div>
            <div style={{ fontWeight: 600, color: 'var(--text)' }}>Add a root folder first</div>
            <Link className="btn" style={{ marginTop: 10 }} to="/settings/media" onClick={onClose}>
              Settings → Media Management
            </Link>
          </div>
        </div>
      ) : (
        <>
          {draft.book && (
            <p className="muted" style={{ marginTop: 0 }}>
              Adds {draft.name} to your library with only this book monitored. Their other books are listed so you can pick more later.
            </p>
          )}
          <div className="form-grid">
            <label>Root folder</label>
            <select className="input" value={root} onChange={(e) => setRoot(e.target.value)}>
              {folders.map((f) => (
                <option key={f.id} value={f.path}>
                  {f.path}
                </option>
              ))}
            </select>
            {!draft.book && (
              <>
                <label>Monitor</label>
                <select className="input" value={monitor} onChange={(e) => setMonitor(e.target.value)}>
                  <option value="all">All {draft.kind === 'book' ? 'books' : 'issues'}</option>
                  <option value="future">Future {draft.kind === 'book' ? 'books' : 'issues'} only</option>
                  <option value="missing">Missing {draft.kind === 'book' ? 'books' : 'issues'}</option>
                  <option value="none">None</option>
                </select>
              </>
            )}
            <label>Quality profile</label>
            <select className="input" value={profile} onChange={(e) => setProfile(e.target.value)}>
              {profiles.map((p) => (
                <option key={p.name}>{p.name}</option>
              ))}
            </select>
            <label>Search now</label>
            <label className="check">
              <input type="checkbox" checked={search} onChange={(e) => setSearch(e.target.checked)} /> {draft.book ? 'Start searching for this book' : `Start searching for missing ${draft.kind === 'book' ? 'books' : 'issues'}`}
            </label>
          </div>
          {draft.kind === 'book' && !draft.book && monitor === 'all' && (
            <div className="help" style={{ marginTop: 10 }}>
              Tip: prolific authors can have hundreds of entries (anthologies, translations, study guides). “Future books only” plus picking older books by hand is usually tidier.
            </div>
          )}
        </>
      )}
    </Modal>
  );
}
