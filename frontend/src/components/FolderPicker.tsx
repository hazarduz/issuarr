import { ArrowUp, Folder, FolderOpen, HardDrive, Loader2, X } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { api, isConnected, type FileSystemListing } from '../data/api';

// Small fake filesystem so the picker works in demo mode too.
const demoTree: Record<string, string[]> = {
  '/': ['config', 'data', 'downloads', 'media'],
  '/data': ['comics', 'magazines', 'torrents', 'usenet'],
  '/data/comics': ['Iron Tide (2022)', 'Neon Alley (2025)'],
  '/data/magazines': [],
  '/data/torrents': ['complete', 'incomplete'],
  '/media': ['books', 'comics', 'magazines', 'movies', 'tv'],
};

function demoListing(path: string): FileSystemListing {
  const p = path || '/';
  const parentPath = p === '/' ? undefined : p.slice(0, p.lastIndexOf('/')) || '/';
  return {
    path: p,
    parent: parentPath,
    directories: (demoTree[p] ?? []).map((name) => ({ name, path: (p === '/' ? '' : p) + '/' + name })),
  };
}

export function FolderPicker({
  initialPath = '',
  onSelect,
  onClose,
}: {
  initialPath?: string;
  onSelect: (path: string) => void;
  onClose: () => void;
}) {
  const [listing, setListing] = useState<FileSystemListing | null>(null);
  const [input, setInput] = useState(initialPath);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const latest = useRef(0);

  const go = async (path: string) => {
    // Only the most recent navigation wins, so a slow response can't overwrite a newer one.
    const ticket = ++latest.current;
    setLoading(true);
    setError(null);
    try {
      const l = isConnected() ? await api.browse(path) : demoListing(path);
      if (ticket !== latest.current) return;
      setListing(l);
      setInput(l.path);
    } catch (e) {
      if (ticket !== latest.current) return;
      setError(e instanceof Error ? e.message : 'Could not open folder');
    } finally {
      if (ticket === latest.current) setLoading(false);
    }
  };

  useEffect(() => {
    go(initialPath);
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const current = listing?.path ?? '';
  const canSelect = !!current && current !== '/' && !error;

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className="modal" role="dialog" aria-label="Choose a folder" onMouseDown={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <FolderOpen size={18} color="var(--accent)" />
          Choose a folder
          <button className="icon-btn" style={{ marginLeft: 'auto', width: 30, height: 30 }} onClick={onClose} aria-label="Close">
            <X size={17} />
          </button>
        </div>

        <div className="modal-body">
          <form
            onSubmit={(e) => {
              e.preventDefault();
              go(input.trim());
            }}
            style={{ display: 'flex', gap: 8, marginBottom: 12 }}
          >
            <input
              ref={inputRef}
              className="input mono"
              style={{ maxWidth: 'none' }}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="/data/comics"
              aria-label="Folder path"
            />
            <button className="btn" type="submit">
              Go
            </button>
          </form>

          {error && <div className="pill danger" style={{ marginBottom: 10, height: 'auto', padding: '4px 10px' }}>{error}</div>}

          <div className="folder-list">
            {loading && (
              <div className="folder-row muted">
                <Loader2 size={16} className="spin-icon" /> Loading…
              </div>
            )}
            {!loading && listing?.parent != null && (
              <button className="folder-row" onClick={() => go(listing.parent!)}>
                <ArrowUp size={16} /> ..
              </button>
            )}
            {!loading &&
              listing?.directories.map((d) => (
                <button className="folder-row" key={d.path} onDoubleClick={() => onSelect(d.path)} onClick={() => go(d.path)}>
                  {listing.path === '' ? <HardDrive size={16} /> : <Folder size={16} />}
                  {d.name}
                </button>
              ))}
            {!loading && listing && listing.directories.length === 0 && (
              <div className="folder-row muted">No sub-folders</div>
            )}
          </div>
          <div className="help" style={{ marginTop: 8 }}>
            Click a folder to open it. In Docker, these are the paths <b>inside the container</b>, such as{' '}
            <span className="mono">/data</span>.
          </div>
        </div>

        <div className="modal-foot">
          <span className="mono muted" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {current || 'Pick a folder'}
          </span>
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" disabled={!canSelect} onClick={() => onSelect(current)}>
            Use this folder
          </button>
        </div>
      </div>
    </div>
  );
}
