import { BookOpen, Check, ExternalLink, Loader2, Newspaper, Plus, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Modal } from '../components/Modal';
import { useToast } from '../components/Toast';
import { api, isConnected, type LookupResult, type QualityProfile, type RootFolder } from '../data/api';
import { notifyLibrary } from '../data/store';

const frequencies = ['Weekly', 'Fortnightly', 'Monthly', 'Bi-monthly', 'Quarterly', 'Annually'];

interface AddDraft {
  kind: 'comic' | 'magazine';
  name: string;
  year: number;
  publisher?: string;
  overview?: string;
  coverUrl?: string;
  metadataSource?: string;
  externalId?: string;
  frequency?: string;
  firstIssueDate?: string;
}

export default function AddNew() {
  const params = new URLSearchParams(useLocation().search);
  const [tab, setTab] = useState<'comic' | 'magazine'>('comic');
  const [q, setQ] = useState(params.get('term') ?? '');
  const [results, setResults] = useState<LookupResult[] | null>(null);
  const [searching, setSearching] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [draft, setDraft] = useState<AddDraft | null>(null);
  const [added, setAdded] = useState<Record<string, string>>({});

  // Debounced lookup.
  useEffect(() => {
    if (tab !== 'comic') return;
    const term = q.trim();
    if (term.length < 2) {
      setResults(null);
      setError(null);
      return;
    }
    if (!isConnected()) {
      setError('Searching needs the Issuarr backend.');
      return;
    }
    setSearching(true);
    const t = setTimeout(async () => {
      try {
        setResults(await api.lookup(term));
        setError(null);
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Search failed');
        setResults(null);
      } finally {
        setSearching(false);
      }
    }, 500);
    return () => clearTimeout(t);
  }, [q, tab]);

  const [mag, setMag] = useState<AddDraft>({ kind: 'magazine', name: '', year: new Date().getFullYear(), frequency: 'Monthly', firstIssueDate: new Date().toISOString().slice(0, 8) + '01' });

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
      </div>

      {tab === 'comic' && (
        <>
          <div className="add-search" style={{ marginBottom: 24 }}>
            <Search size={20} />
            <input autoFocus value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search for a comic series, e.g. “Saga” or “Batman 2016”" />
          </div>
          {searching && (
            <div className="muted" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
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
          {!searching && results?.length === 0 && <div className="card empty">No series found for “{q}”.</div>}
          <div className="overview-list" style={{ maxWidth: 980 }}>
            {results?.map((r) => {
              const slug = r.existingSlug ?? added[r.externalId];
              return (
                <div key={r.externalId} className="card overview-row" style={{ gridTemplateColumns: '80px 1fr' }}>
                  <div className="lookup-cover">{r.coverUrl ? <img src={r.coverUrl} alt="" loading="lazy" referrerPolicy="no-referrer" /> : <div className="lookup-cover" />}</div>
                  <div style={{ minWidth: 0 }}>
                    <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start', flexWrap: 'wrap' }}>
                      <div style={{ flex: 1, minWidth: 200 }}>
                        <h3>
                          {r.name} {r.year && <span style={{ color: 'var(--text-faint)', fontWeight: 500 }}>({r.year})</span>}
                        </h3>
                        <div className="row-meta">
                          {r.publisher && <span className="pill">{r.publisher}</span>}
                          <span className="pill">{r.issueCount} issues</span>
                          {r.siteUrl && (
                            <a className="pill" href={r.siteUrl} target="_blank" rel="noreferrer">
                              {r.source === 'metron' ? 'Metron' : 'ComicVine'} <ExternalLink size={11} />
                            </a>
                          )}
                        </div>
                      </div>
                      {slug ? (
                        <Link className="btn" to={`/title/${slug}`}>
                          <Check size={16} /> In library
                        </Link>
                      ) : (
                        <button
                          className="btn primary"
                          onClick={() =>
                            setDraft({ kind: 'comic', name: r.name, year: r.year ?? new Date().getFullYear(), publisher: r.publisher, overview: r.overview, coverUrl: r.coverUrl, metadataSource: r.source, externalId: r.externalId })
                          }
                        >
                          <Plus size={16} /> Add
                        </button>
                      )}
                    </div>
                    {r.overview && <p>{r.overview}</p>}
                  </div>
                </div>
              );
            })}
          </div>
        </>
      )}

      {tab === 'magazine' && (
        <div className="card card-body" style={{ maxWidth: 760 }}>
          <p className="muted" style={{ marginTop: 0 }}>
            Magazines don't have a good metadata source, so Issuarr builds the issue list from how often it comes out: every expected issue since the date you choose, plus the next one. New issues appear automatically as their dates pass.
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
      )}

      {draft && (
        <AddDialog
          draft={draft}
          onClose={() => setDraft(null)}
          onAdded={(slug) => {
            if (draft.externalId) setAdded((a) => ({ ...a, [draft.externalId!]: slug }));
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
  const [monitor, setMonitor] = useState(draft.kind === 'magazine' ? 'all' : 'all');
  const [profile, setProfile] = useState(draft.kind === 'comic' ? 'CBZ / CBR' : 'PDF Preferred');
  const [search, setSearch] = useState(true);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.rootFolders().then((f) => {
      setFolders(f);
      setRoot((r) => r || f[0]?.path || '');
    });
    api.qualityProfiles().then(setProfiles);
  }, []);

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
      else toast(`${title.name} added${search && monitor !== 'none' ? ' — searching for issues' : ''}`);
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
      title={`Add ${draft.name}`}
      onClose={onClose}
      footer={
        <>
          <span style={{ flex: 1 }} />
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" onClick={add} disabled={busy || !root}>
            {busy ? <Loader2 size={16} className="spin-icon" /> : <Plus size={16} />} Add {draft.kind === 'comic' ? 'series' : 'magazine'}
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
        <div className="form-grid">
          <label>Root folder</label>
          <select className="input" value={root} onChange={(e) => setRoot(e.target.value)}>
            {folders.map((f) => (
              <option key={f.id} value={f.path}>
                {f.path}
              </option>
            ))}
          </select>
          <label>Monitor</label>
          <select className="input" value={monitor} onChange={(e) => setMonitor(e.target.value)}>
            <option value="all">All issues</option>
            <option value="future">Future issues only</option>
            <option value="missing">Missing issues</option>
            <option value="none">None</option>
          </select>
          <label>Quality profile</label>
          <select className="input" value={profile} onChange={(e) => setProfile(e.target.value)}>
            {profiles.map((p) => (
              <option key={p.name}>{p.name}</option>
            ))}
          </select>
          <label>Search now</label>
          <label className="check">
            <input type="checkbox" checked={search} onChange={(e) => setSearch(e.target.checked)} /> Start searching for missing issues
          </label>
        </div>
      )}
    </Modal>
  );
}
