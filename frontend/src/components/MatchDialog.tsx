import { Check, ExternalLink, Link2, Loader2, Search, Unlink } from 'lucide-react';
import { useEffect, useState } from 'react';
import { api, type LookupResult } from '../data/api';
import { itemWord, sourceName, type Kind } from '../data/mock';
import { Modal } from './Modal';
import { SafeImg } from './SafeImg';

/**
 * Search ComicVine / Metron and pick the series a title (or an import folder) really is.
 * Used from the title page and from Library Import.
 */
const sourcesFor: Record<Kind, string[]> = {
  comic: ['comicvine', 'metron'],
  magazine: ['wikidata', 'comicvine'],
  book: ['openlibrary', 'googlebooks'],
};

export function MatchDialog({
  kind = 'comic',
  initialTerm,
  currentId,
  confirmLabel = 'Match',
  showNameOption = true,
  onPick,
  onUnmatch,
  onClose,
}: {
  kind?: Kind;
  initialTerm: string;
  currentId?: string;
  confirmLabel?: string;
  showNameOption?: boolean;
  onPick: (r: LookupResult, useMetadataName: boolean) => Promise<void> | void;
  onUnmatch?: () => void;
  onClose: () => void;
}) {
  const [term, setTerm] = useState(initialTerm);
  const [source, setSource] = useState<string>('');
  const [results, setResults] = useState<LookupResult[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [picked, setPicked] = useState<LookupResult | null>(null);
  const [useName, setUseName] = useState(true);
  const [busy, setBusy] = useState(false);

  const search = async (t = term, s = source) => {
    if (t.trim().length < 2) return;
    setLoading(true);
    setError(null);
    setPicked(null);
    try {
      setResults(await api.lookup(t.trim(), s || undefined, kind));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Search failed');
      setResults(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    // Folder names often carry extras like "(2022)" or "[scans]"; search on the plain name first.
    const cleaned = initialTerm.replace(/\[[^\]]*\]|\(\D[^)]*\)/g, '').replace(/\s{2,}/g, ' ').trim();
    setTerm(cleaned);
    search(cleaned);
    // eslint-disable-next-line
  }, []);

  const confirm = async () => {
    if (!picked) return;
    setBusy(true);
    try {
      await onPick(picked, useName);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={kind === 'book' ? 'Match to an author' : 'Match to a series'}
      icon={<Link2 size={18} color="var(--accent)" />}
      wide
      onClose={onClose}
      footer={
        <>
          {onUnmatch && currentId && (
            <button className="btn ghost" onClick={onUnmatch} title="Stop following the metadata source">
              <Unlink size={16} /> Unmatch
            </button>
          )}
          <span style={{ flex: 1 }} />
          {picked && showNameOption && (
            <label className="check" style={{ marginRight: 8 }}>
              <input type="checkbox" checked={useName} onChange={(e) => setUseName(e.target.checked)} /> Use the name “{picked.name}”
            </label>
          )}
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" disabled={!picked || busy} onClick={confirm}>
            {busy ? <Loader2 size={16} className="spin-icon" /> : <Link2 size={16} />} {confirmLabel}
          </button>
        </>
      }
    >
      <form
        onSubmit={(e) => {
          e.preventDefault();
          search();
        }}
        style={{ display: 'flex', gap: 8, marginBottom: 14 }}
      >
        <div className="add-search" style={{ flex: 1, maxWidth: 'none' }}>
          <Search size={18} />
          <input style={{ height: 42, fontSize: 14 }} value={term} onChange={(e) => setTerm(e.target.value)} placeholder={kind === 'book' ? 'Author name' : 'Series name'} autoFocus />
        </div>
        <select
          className="input"
          style={{ width: 150, height: 42 }}
          value={source}
          onChange={(e) => {
            setSource(e.target.value);
            search(term, e.target.value);
          }}
          title="Metadata source"
        >
          <option value="">Default source</option>
          {sourcesFor[kind].map((s) => (
            <option key={s} value={s}>
              {sourceName[s]}
            </option>
          ))}
        </select>
        <button className="btn" type="submit" style={{ height: 42 }}>
          Search
        </button>
      </form>

      {loading && (
        <div className="muted" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <Loader2 size={16} className="spin-icon" /> Searching…
        </div>
      )}
      {error && <div className="pill danger" style={{ height: 'auto', padding: '8px 12px', whiteSpace: 'normal' }}>{error}</div>}
      {!loading && results?.length === 0 && (
        <div className="empty">{kind === 'magazine' ? 'Nothing found. Try another source, or a shorter name.' : `No ${kind === 'book' ? 'authors' : 'series'} found. Try a shorter name${kind === 'book' ? '' : ', or add the year'}.`}</div>
      )}

      <div style={{ display: 'grid', gap: 8 }}>
        {!loading &&
          results?.map((r) => {
            const isCurrent = currentId === `${r.source}:${r.externalId}`;
            const taken = r.existingTitleId != null && !isCurrent;
            const active = picked?.externalId === r.externalId && picked.source === r.source;
            return (
              <button
                key={r.source + r.externalId}
                type="button"
                className={`match-row ${active ? 'on' : ''}`}
                disabled={taken}
                onClick={() => setPicked(r)}
                title={taken ? 'Already in your library as another title' : undefined}
              >
                <div className="lookup-cover" style={{ width: 54 }}>
                  <SafeImg src={r.coverUrl} />
                </div>
                <div style={{ minWidth: 0, flex: 1, textAlign: 'left' }}>
                  <div style={{ fontWeight: 600 }}>
                    {r.name} {r.year && <span className="muted">({r.year})</span>}
                  </div>
                  <div className="row-meta" style={{ marginTop: 4 }}>
                    {r.publisher && <span className="pill">{r.publisher}</span>}
                    {r.issueCount > 0 && (
                      <span className="pill">
                        {r.issueCount} {itemWord(kind, true)}
                      </span>
                    )}
                    {r.frequency && <span className="pill">{r.frequency}</span>}
                    {isCurrent && <span className="pill ok">Current match</span>}
                    {taken && <span className="pill info">In library</span>}
                    {r.siteUrl && (
                      <a className="pill" href={r.siteUrl} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>
                        {sourceName[r.source] ?? r.source} <ExternalLink size={11} />
                      </a>
                    )}
                  </div>
                  {r.overview && (
                    <div className="muted" style={{ fontSize: 12, marginTop: 6, display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                      {r.overview}
                    </div>
                  )}
                </div>
                {active && <Check size={20} color="var(--accent)" />}
              </button>
            );
          })}
      </div>
    </Modal>
  );
}
