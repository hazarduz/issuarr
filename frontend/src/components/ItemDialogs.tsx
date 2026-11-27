import { Check, Link2, Loader2, Pencil, Search, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { api, type MetadataCandidate } from '../data/api';
import { itemWord, parseDate, sourceName, type Issue, type Title } from '../data/mock';
import { refreshLibrary } from '../data/store';
import { Modal } from './Modal';
import { SafeImg } from './SafeImg';
import { useToast } from './Toast';

/** Match one issue/book to an entry from the title's metadata, or edit it by hand. */
export function ItemMatchDialog({ title, item, onClose }: { title: Title; item: Issue; onClose: () => void }) {
  const toast = useToast();
  const canMatch = !!title.metadataSource;
  const [tab, setTab] = useState<'match' | 'edit'>(canMatch ? 'match' : 'edit');
  const [candidates, setCandidates] = useState<MetadataCandidate[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState(() => item.number.replace(/^#/, '').replace(/\s+-\s+.*$/, ''));
  const [picked, setPicked] = useState<MetadataCandidate | null>(null);
  const [busy, setBusy] = useState(false);
  const [number, setNumber] = useState(item.number);
  const [issueTitle, setIssueTitle] = useState(item.title ?? '');
  const [date, setDate] = useState(item.releaseDate);

  useEffect(() => {
    if (!canMatch) return;
    api.candidates(title.id).then(setCandidates).catch((e) => setError(e.message));
  }, [title.id, canMatch]);

  const shown = useMemo(() => {
    if (!candidates) return [];
    const f = filter.trim().toLowerCase();
    const list = f ? candidates.filter((c) => `${c.number} ${c.name ?? ''}`.toLowerCase().includes(f)) : candidates;
    return list.slice(0, 200);
  }, [candidates, filter]);

  const confirmMatch = async () => {
    if (!picked) return;
    setBusy(true);
    try {
      await api.matchIssue(item.serverId!, picked.externalId);
      await refreshLibrary();
      const merged = picked.localIssueId != null && picked.localIssueId !== item.serverId;
      toast(merged ? `Merged into “${picked.number}”` : `Matched to “${picked.number}”`);
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Match failed', 'error');
      setBusy(false);
    }
  };

  const saveEdit = async () => {
    setBusy(true);
    try {
      await api.editIssue(item.serverId!, number, issueTitle || undefined, date);
      await refreshLibrary();
      toast('Saved');
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not save', 'error');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`${item.number}`}
      icon={<Link2 size={18} color="var(--accent)" />}
      wide={tab === 'match'}
      onClose={onClose}
      footer={
        <>
          <span style={{ flex: 1 }} className="muted">
            {tab === 'match' && picked?.localIssueId != null && picked.localIssueId !== item.serverId && 'This entry already exists: the two will be merged.'}
          </span>
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          {tab === 'match' ? (
            <button className="btn primary" disabled={!picked || busy} onClick={confirmMatch}>
              {busy ? <Loader2 size={16} className="spin-icon" /> : <Link2 size={16} />} {picked?.localIssueId != null && picked.localIssueId !== item.serverId ? 'Merge' : 'Match'}
            </button>
          ) : (
            <button className="btn primary" disabled={busy} onClick={saveEdit}>
              {busy ? <Loader2 size={16} className="spin-icon" /> : <Check size={16} />} Save
            </button>
          )}
        </>
      }
    >
      <div className="tabs" style={{ marginTop: -6 }}>
        <button className={tab === 'match' ? 'on' : ''} onClick={() => setTab('match')} disabled={!canMatch} title={canMatch ? undefined : 'Match the title to a metadata source first'}>
          <Link2 size={14} style={{ verticalAlign: -2 }} /> Match{title.metadataSource ? ` (${sourceName[title.metadataSource] ?? title.metadataSource})` : ''}
        </button>
        <button className={tab === 'edit' ? 'on' : ''} onClick={() => setTab('edit')}>
          <Pencil size={14} style={{ verticalAlign: -2 }} /> Edit
        </button>
      </div>

      {tab === 'match' && (
        <>
          {item.file && (
            <p className="muted" style={{ marginTop: 0 }}>
              File: <span className="mono">{item.file.name}</span>
            </p>
          )}
          <div className="add-search" style={{ maxWidth: 'none', marginBottom: 12 }}>
            <Search size={18} />
            <input style={{ height: 42, fontSize: 14 }} value={filter} onChange={(e) => setFilter(e.target.value)} placeholder={`Filter ${itemWord(title.kind, true)}`} autoFocus />
          </div>
          {!candidates && !error && (
            <div className="muted" style={{ display: 'flex', gap: 8 }}>
              <Loader2 size={16} className="spin-icon" /> Loading {itemWord(title.kind)} list…
            </div>
          )}
          {error && <div className="pill danger" style={{ height: 'auto', padding: '8px 12px', whiteSpace: 'normal' }}>{error}</div>}
          {candidates && shown.length === 0 && <div className="empty">Nothing matches “{filter}”. Clear the filter to see everything.</div>}
          <div style={{ display: 'grid', gap: 6 }}>
            {shown.map((c) => {
              const isSelf = c.localIssueId === item.serverId;
              const active = picked?.externalId === c.externalId;
              const blocked = c.localHasFile && !isSelf && !!item.file;
              return (
                <button key={c.externalId} type="button" className={`match-row ${active ? 'on' : ''}`} disabled={blocked || isSelf} onClick={() => setPicked(c)} title={blocked ? 'Already has a file' : undefined}>
                  <div className="lookup-cover" style={{ width: 40 }}>
                    <SafeImg src={c.coverUrl} />
                  </div>
                  <div style={{ flex: 1, textAlign: 'left', minWidth: 0 }}>
                    <div style={{ fontWeight: 600 }}>
                      {c.number} {c.name && <span className="muted">— {c.name}</span>}
                    </div>
                    <div className="row-meta" style={{ marginTop: 3 }}>
                      {c.releaseDate && <span className="pill">{parseDate(c.releaseDate).getFullYear()}</span>}
                      {isSelf && <span className="pill ok">Current match</span>}
                      {!isSelf && c.localIssueId != null && <span className="pill info">{c.localHidden ? 'Hidden' : 'In list'}</span>}
                      {!isSelf && c.localHasFile && <span className="pill">Has file</span>}
                    </div>
                  </div>
                  {active && <Check size={20} color="var(--accent)" />}
                </button>
              );
            })}
          </div>
        </>
      )}

      {tab === 'edit' && (
        <div className="form-grid">
          <label>{title.kind === 'book' ? 'Book title' : 'Issue'}</label>
          <input className="input" value={number} onChange={(e) => setNumber(e.target.value)} />
          {title.kind !== 'book' && (
            <>
              <label>Issue title</label>
              <input className="input" value={issueTitle} onChange={(e) => setIssueTitle(e.target.value)} placeholder="Optional" />
            </>
          )}
          <label>{title.kind === 'book' ? 'Published' : 'Release date'}</label>
          <input className="input" type="date" value={date} onChange={(e) => setDate(e.target.value)} />
          <span />
          <div className="help">
            {title.kind === 'comic' ? 'Use a number like 12 or 12.5.' : title.kind === 'book' ? 'Release names are matched against this title.' : ''} Changing it doesn’t rename the file; use Preview Rename for that.
          </div>
        </div>
      )}
    </Modal>
  );
}

export function ItemDeleteDialog({ title, item, onClose }: { title: Title; item: Issue; onClose: () => void }) {
  const toast = useToast();
  const [deleteFile, setDeleteFile] = useState(false);
  const [busy, setBusy] = useState(false);
  const word = itemWord(title.kind);
  const fromMetadata = !!title.metadataSource;

  const remove = async () => {
    setBusy(true);
    try {
      await api.deleteIssue(item.serverId!, deleteFile);
      await refreshLibrary();
      toast(`${item.number} deleted`);
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not delete', 'error');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Delete “${item.number}”?`}
      onClose={onClose}
      footer={
        <>
          <span style={{ flex: 1 }} />
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn danger" onClick={remove} disabled={busy}>
            <Trash2 size={16} /> Delete
          </button>
        </>
      }
    >
      <p style={{ marginTop: 0 }}>
        Removes this {word} from {title.name}.
        {fromMetadata && ` If it came from ${sourceName[title.metadataSource!] ?? 'the metadata source'}, it's hidden so refreshing doesn't bring it back; you can restore hidden ${itemWord(title.kind, true)} from the bottom of the list.`}
      </p>
      {item.file ? (
        <>
          <label className="check">
            <input type="checkbox" checked={deleteFile} onChange={(e) => setDeleteFile(e.target.checked)} /> Also delete the file <span className="mono">{item.file.name}</span>
          </label>
          {!deleteFile && <div className="help">The file stays on disk; Issuarr just stops tracking it.</div>}
          {deleteFile && <div className="pill danger" style={{ marginTop: 10, height: 'auto', padding: '6px 10px' }}>The file is deleted permanently.</div>}
        </>
      ) : null}
    </Modal>
  );
}
