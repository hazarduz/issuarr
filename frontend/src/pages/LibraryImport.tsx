import { CheckCircle2, CircleAlert, FileWarning, FolderInput, FolderOpen, Loader2 } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { FolderPicker } from '../components/FolderPicker';
import { useToast } from '../components/Toast';
import { api, formatBytes, isConnected, type ImportCandidate, type ImportResult } from '../data/api';
import { titles, type Kind } from '../data/mock';
import { notifyLibrary } from '../data/store';

interface Row extends ImportCandidate {
  selected: boolean;
}

// Shown when the UI runs without a backend, so the flow can still be tried.
const demoCandidates: ImportCandidate[] = [
  { path: '/data/comics/Glass Cannon (2024)', folderName: 'Glass Cannon (2024)', name: 'Glass Cannon', year: 2024, kind: 'comic', fileCount: 18, matchedFileCount: 18, size: 1.1 * 1024 ** 3, sampleFiles: ['Glass Cannon 001 (2024).cbz'] },
  { path: '/data/comics/Lighthouse Keepers', folderName: 'Lighthouse Keepers', name: 'Lighthouse Keepers', year: 2025, kind: 'comic', fileCount: 9, matchedFileCount: 8, size: 610 * 1024 ** 2, sampleFiles: ['Lighthouse Keepers #1.cbr'] },
  { path: '/data/comics/Iron Tide (2022)', folderName: 'Iron Tide (2022)', name: 'Iron Tide', year: 2022, kind: 'comic', fileCount: 45, matchedFileCount: 45, size: 3.2 * 1024 ** 3, existingTitleId: 9, existingTitleName: 'Iron Tide', sampleFiles: [] },
];

export default function LibraryImport() {
  const toast = useToast();
  const [picking, setPicking] = useState(false);
  const [folder, setFolder] = useState<string | null>(null);
  const [rows, setRows] = useState<Row[] | null>(null);
  const [scanning, setScanning] = useState(false);
  const [importing, setImporting] = useState(false);
  const [results, setResults] = useState<ImportResult[] | null>(null);

  const scan = async (path: string) => {
    setPicking(false);
    setFolder(path);
    setResults(null);
    setRows(null);
    setScanning(true);
    try {
      const found = isConnected() ? await api.libraryScan(path) : demoCandidates;
      setRows(found.map((c) => ({ ...c, selected: !c.existingTitleId })));
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not scan folder', 'error');
    } finally {
      setScanning(false);
    }
  };

  const update = (path: string, patch: Partial<Row>) =>
    setRows((rs) => rs?.map((r) => (r.path === path ? { ...r, ...patch } : r)) ?? null);

  const selectable = rows?.filter((r) => !r.existingTitleId) ?? [];
  const selected = selectable.filter((r) => r.selected);
  const allSelected = selectable.length > 0 && selected.length === selectable.length;

  const runImport = async () => {
    if (selected.length === 0) return;
    setImporting(true);
    try {
      let res: ImportResult[];
      if (isConnected()) {
        res = await api.libraryImport(selected.map((r) => ({ path: r.path, name: r.name, year: r.year, kind: r.kind })));
        // Pull the new titles into the library so they show up straight away.
        await Promise.all(res.filter((r) => r.titleId).map((r) => api.loadTitle(r.titleId!)));
        notifyLibrary();
      } else {
        res = selected.map((r) => ({ path: r.path, issuesImported: r.matchedFileCount, unmatchedFiles: [] }));
      }
      setResults(res);
      const ok = res.filter((r) => !r.error).length;
      toast(`Imported ${ok} title${ok === 1 ? '' : 's'}`, ok === res.length ? 'success' : 'error');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Import failed', 'error');
    } finally {
      setImporting(false);
    }
  };

  return (
    <div className="page">
      <h1 className="page-title">
        Library Import
        {folder && <small className="mono">{folder}</small>}
      </h1>

      {!folder && (
        <div className="card empty">
          <div>
            <FolderInput size={40} />
            <div style={{ fontWeight: 600, color: 'var(--text)' }}>Import an existing collection</div>
            <p style={{ maxWidth: 460 }}>
              Choose the folder that holds your series, for example <span className="mono">/data/comics</span>. Each
              sub-folder becomes a title, and each CBZ, CBR, PDF or EPUB file becomes an issue.
            </p>
            <button className="btn primary" onClick={() => setPicking(true)}>
              <FolderOpen size={16} /> Choose Folder
            </button>
          </div>
        </div>
      )}

      {scanning && (
        <div className="card empty">
          <div>
            <Loader2 size={32} className="spin-icon" />
            <div>Scanning {folder}…</div>
          </div>
        </div>
      )}

      {rows && !results && (
        <>
          {rows.length === 0 ? (
            <div className="card empty">
              <div>
                <FileWarning size={40} />
                <div style={{ fontWeight: 600, color: 'var(--text)' }}>No comics or magazines found</div>
                <p>There are no CBZ, CBR, CB7, PDF or EPUB files in this folder or its sub-folders.</p>
                <button className="btn" onClick={() => setPicking(true)}>
                  <FolderOpen size={16} /> Choose another folder
                </button>
              </div>
            </div>
          ) : (
            <div className="card table-wrap">
              <table className="table import-table">
                <thead>
                  <tr>
                    <th style={{ width: 40 }}>
                      <input
                        type="checkbox"
                        checked={allSelected}
                        onChange={(e) => setRows((rs) => rs!.map((r) => ({ ...r, selected: !r.existingTitleId && e.target.checked })))}
                        aria-label="Select all"
                      />
                    </th>
                    <th>Folder</th>
                    <th>Title</th>
                    <th>Type</th>
                    <th>Year</th>
                    <th>Issues found</th>
                    <th>Size</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => {
                    const existing = !!r.existingTitleId;
                    const existingSlug = titles.find((t) => t.id === String(r.existingTitleId))?.slug;
                    return (
                      <tr key={r.path} className={existing ? 'dim-row' : ''}>
                        <td>
                          <input
                            type="checkbox"
                            disabled={existing}
                            checked={r.selected}
                            onChange={(e) => update(r.path, { selected: e.target.checked })}
                            aria-label={`Import ${r.folderName}`}
                          />
                        </td>
                        <td className="mono muted" title={r.path}>
                          {r.folderName}
                        </td>
                        <td>
                          {existing ? (
                            <span className="pill info">
                              In library{existingSlug ? ': ' : ''}
                              {existingSlug && <Link to={`/title/${existingSlug}`}>{r.existingTitleName}</Link>}
                            </span>
                          ) : (
                            <input className="input sm" value={r.name} onChange={(e) => update(r.path, { name: e.target.value })} />
                          )}
                        </td>
                        <td>
                          <select
                            className="input sm"
                            disabled={existing}
                            value={r.kind}
                            onChange={(e) => update(r.path, { kind: e.target.value as Kind })}
                          >
                            <option value="comic">Comic</option>
                            <option value="magazine">Magazine</option>
                          </select>
                        </td>
                        <td>
                          <input
                            className="input sm"
                            style={{ width: 80 }}
                            disabled={existing}
                            inputMode="numeric"
                            value={r.year ?? ''}
                            onChange={(e) => update(r.path, { year: e.target.value ? Number(e.target.value.replace(/\D/g, '')) || undefined : undefined })}
                          />
                        </td>
                        <td className="num">
                          {r.matchedFileCount}
                          {r.matchedFileCount < r.fileCount && (
                            <span className="muted" title="Files whose issue number or date couldn't be worked out from the name">
                              {' '}
                              / {r.fileCount} files
                            </span>
                          )}
                        </td>
                        <td className="num muted">{formatBytes(r.size)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}

          {rows.length > 0 && (
            <div className="import-bar">
              <button className="btn ghost" onClick={() => setPicking(true)}>
                <FolderOpen size={16} /> Choose another folder
              </button>
              <span className="muted" style={{ marginLeft: 'auto' }}>
                {selected.length} of {selectable.length} selected
              </span>
              <button className="btn primary" disabled={selected.length === 0 || importing} onClick={runImport}>
                {importing ? <Loader2 size={16} className="spin-icon" /> : <FolderInput size={16} />}
                Import {selected.length} title{selected.length === 1 ? '' : 's'}
              </button>
            </div>
          )}
        </>
      )}

      {results && (
        <>
          <div className="card">
            {results.map((r) => (
              <div className="health" key={r.path}>
                {r.error ? <CircleAlert size={18} color="var(--danger)" /> : <CheckCircle2 size={18} color="var(--ok)" />}
                <div style={{ minWidth: 0 }}>
                  <div style={{ fontWeight: 600 }}>
                    {r.slug ? <Link to={`/title/${r.slug}`}>{rows?.find((x) => x.path === r.path)?.name}</Link> : rows?.find((x) => x.path === r.path)?.name}
                  </div>
                  <div className="muted" style={{ fontSize: 13 }}>
                    {r.error ?? `${r.issuesImported} issue${r.issuesImported === 1 ? '' : 's'} imported`}
                    {r.unmatchedFiles.length > 0 && ` · ${r.unmatchedFiles.length} file${r.unmatchedFiles.length === 1 ? '' : 's'} skipped`}
                  </div>
                  {r.unmatchedFiles.length > 0 && (
                    <details style={{ fontSize: 12, marginTop: 4 }}>
                      <summary className="muted" style={{ cursor: 'pointer' }}>
                        Show skipped files
                      </summary>
                      <ul className="mono muted" style={{ margin: '6px 0 0', paddingLeft: 18 }}>
                        {r.unmatchedFiles.slice(0, 50).map((f) => (
                          <li key={f}>{f}</li>
                        ))}
                      </ul>
                    </details>
                  )}
                </div>
              </div>
            ))}
          </div>
          <div className="import-bar">
            <button className="btn" onClick={() => setPicking(true)}>
              <FolderOpen size={16} /> Import another folder
            </button>
            <Link className="btn primary" to="/" style={{ marginLeft: 'auto' }}>
              Go to Library
            </Link>
          </div>
        </>
      )}

      {picking && <FolderPicker initialPath={folder ?? ''} onSelect={scan} onClose={() => setPicking(false)} />}
    </div>
  );
}
