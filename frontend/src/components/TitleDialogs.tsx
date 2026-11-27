import { Download, ExternalLink, FileText, Loader2, Search, Trash2, Wand2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, formatBytes, timeAgo, type HistoryRecord, type IssueFileInfo, type QualityProfile, type Release, type RenamePreview } from '../data/api';
import type { Title } from '../data/mock';
import { notifyLibrary, refreshLibrary, runCommand } from '../data/store';
import { Modal } from './Modal';
import { useToast } from './Toast';

const frequencies = ['Weekly', 'Fortnightly', 'Monthly', 'Bi-monthly', 'Quarterly', 'Annually'];

export function EditTitleDialog({ title, onClose }: { title: Title; onClose: () => void }) {
  const toast = useToast();
  const [profiles, setProfiles] = useState<QualityProfile[]>([]);
  const [t, setT] = useState({ ...title, tagsText: title.tags.join(', ') });
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    api.qualityProfiles().then(setProfiles);
  }, []);

  const save = async () => {
    setBusy(true);
    try {
      await api.updateTitle(title, {
        monitored: t.monitored,
        qualityProfile: t.qualityProfile,
        path: t.path,
        frequency: t.frequency,
        status: t.status,
        tags: t.tagsText.split(',').map((x) => x.trim()).filter(Boolean),
      });
      await api.loadTitle(title.id);
      notifyLibrary();
      toast('Saved');
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not save', 'error');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Edit ${title.name}`}
      onClose={onClose}
      footer={
        <>
          <span style={{ flex: 1 }} />
          <button className="btn ghost" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" onClick={save} disabled={busy}>
            Save
          </button>
        </>
      }
    >
      <div className="form-grid">
        <label>Monitored</label>
        <label className="check">
          <input type="checkbox" checked={t.monitored} onChange={(e) => setT({ ...t, monitored: e.target.checked })} /> Download new and missing issues
        </label>
        <label>Quality profile</label>
        <select className="input" value={t.qualityProfile} onChange={(e) => setT({ ...t, qualityProfile: e.target.value })}>
          {profiles.map((p) => (
            <option key={p.name}>{p.name}</option>
          ))}
        </select>
        {title.kind === 'magazine' && (!title.metadataSource || title.metadataSource === 'wikidata') && (
          <>
            <label>Comes out</label>
            <select className="input" value={t.frequency} onChange={(e) => setT({ ...t, frequency: e.target.value })}>
              {frequencies.map((f) => (
                <option key={f}>{f}</option>
              ))}
            </select>
          </>
        )}
        <label>Status</label>
        <select className="input" value={t.status} onChange={(e) => setT({ ...t, status: e.target.value as Title['status'] })}>
          <option value="continuing">Continuing</option>
          <option value="ended">Ended</option>
        </select>
        <label>Path</label>
        <div>
          <input className="input mono" value={t.path} onChange={(e) => setT({ ...t, path: e.target.value })} />
          <div className="help">Changing this doesn't move files.</div>
        </div>
        <label>Tags</label>
        <input className="input" value={t.tagsText} onChange={(e) => setT({ ...t, tagsText: e.target.value })} placeholder="comma separated" />
      </div>
    </Modal>
  );
}

export function DeleteTitleDialog({ title, onClose }: { title: Title; onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const [deleteFiles, setDeleteFiles] = useState(false);
  const [busy, setBusy] = useState(false);
  const fileCount = title.issues.filter((i) => i.file).length;

  const remove = async () => {
    setBusy(true);
    try {
      await api.deleteTitle(title.id, deleteFiles);
      await refreshLibrary();
      toast(`${title.name} deleted`);
      navigate('/');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not delete', 'error');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Delete ${title.name}?`}
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
      <p style={{ marginTop: 0 }}>This removes the title and its issue list from Issuarr.</p>
      <label className="check">
        <input type="checkbox" checked={deleteFiles} onChange={(e) => setDeleteFiles(e.target.checked)} />
        Also delete {fileCount} file{fileCount === 1 ? '' : 's'} from <span className="mono">{title.path}</span>
      </label>
      {deleteFiles && <div className="pill danger" style={{ marginTop: 12, height: 'auto', padding: '6px 10px' }}>Files are deleted permanently.</div>}
    </Modal>
  );
}

export function RenameDialog({ title, onClose }: { title: Title; onClose: () => void }) {
  const toast = useToast();
  const [items, setItems] = useState<RenamePreview[] | null>(null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.renamePreview(title.id).then((p) => {
      setItems(p);
      setSelected(new Set(p.map((x) => x.issueFileId)));
    }).catch((e) => toast(e.message, 'error'));
    // eslint-disable-next-line
  }, [title.id]);

  const rename = async () => {
    setBusy(true);
    try {
      const cmd = await runCommand('RenameTitle', title.id, [...selected]);
      toast(cmd.message ?? 'Renamed');
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Rename failed', 'error');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={`Rename files · ${title.name}`}
      icon={<Wand2 size={18} color="var(--accent)" />}
      wide
      onClose={onClose}
      footer={
        <>
          <span className="muted" style={{ flex: 1 }}>
            Formats are set in Settings → Media Management
          </span>
          <button className="btn ghost" onClick={onClose}>
            Close
          </button>
          <button className="btn primary" disabled={busy || selected.size === 0} onClick={rename}>
            {busy && <Loader2 size={16} className="spin-icon" />} Rename {selected.size} file{selected.size === 1 ? '' : 's'}
          </button>
        </>
      }
    >
      {items === null && <Loader2 className="spin-icon" />}
      {items?.length === 0 && <div className="empty">All files already match your naming format.</div>}
      {items && items.length > 0 && (
        <div className="table-wrap">
          <table className="table import-table">
            <thead>
              <tr>
                <th style={{ width: 36 }}>
                  <input type="checkbox" checked={selected.size === items.length} onChange={(e) => setSelected(new Set(e.target.checked ? items.map((x) => x.issueFileId) : []))} />
                </th>
                <th>Issue</th>
                <th>Current</th>
                <th>New</th>
              </tr>
            </thead>
            <tbody>
              {items.map((r) => (
                <tr key={r.issueFileId}>
                  <td>
                    <input
                      type="checkbox"
                      checked={selected.has(r.issueFileId)}
                      onChange={(e) =>
                        setSelected((s) => {
                          const n = new Set(s);
                          if (e.target.checked) n.add(r.issueFileId);
                          else n.delete(r.issueFileId);
                          return n;
                        })
                      }
                    />
                  </td>
                  <td>{r.issue}</td>
                  <td className="mono muted" style={{ fontSize: 12 }}>
                    {r.existingPath}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {r.newPath}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Modal>
  );
}

export function FilesDialog({ title, onClose }: { title: Title; onClose: () => void }) {
  const toast = useToast();
  const [files, setFiles] = useState<IssueFileInfo[] | null>(null);
  const [confirm, setConfirm] = useState<number | null>(null);
  const load = () => api.titleFiles(title.id).then(setFiles);
  useEffect(() => {
    load();
    // eslint-disable-next-line
  }, [title.id]);

  const remove = async (f: IssueFileInfo) => {
    setConfirm(null);
    try {
      await api.deleteIssueFile(f.id);
      await load();
      await api.loadTitle(title.id);
      notifyLibrary();
      toast('File deleted');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not delete', 'error');
    }
  };

  return (
    <Modal title={`Files · ${title.name}`} icon={<FileText size={18} color="var(--accent)" />} wide onClose={onClose}>
      <div className="muted mono" style={{ fontSize: 12, marginBottom: 10 }}>
        {title.path}
      </div>
      {files === null && <Loader2 className="spin-icon" />}
      {files?.length === 0 && <div className="empty">No files yet.</div>}
      {files && files.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>File</th>
                <th>Format</th>
                <th>Size</th>
                <th>Added</th>
                <th style={{ width: 150 }} />
              </tr>
            </thead>
            <tbody>
              {files.map((f) => (
                <tr key={f.id}>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {f.relativePath} {!f.exists && <span className="pill danger">missing on disk</span>}
                  </td>
                  <td>
                    <span className="pill">{f.format}</span>
                  </td>
                  <td className="num muted">{formatBytes(f.size)}</td>
                  <td className="muted">{timeAgo(f.dateAdded)}</td>
                  <td style={{ textAlign: 'right' }}>
                    {confirm === f.id ? (
                      <span className="confirm" style={{ justifyContent: 'flex-end' }}>
                        <button className="btn sm danger" onClick={() => remove(f)}>
                          Delete file
                        </button>
                        <button className="btn sm ghost" onClick={() => setConfirm(null)}>
                          No
                        </button>
                      </span>
                    ) : (
                      <button className="icon-btn" style={{ width: 30, height: 30 }} title="Delete file" onClick={() => setConfirm(f.id)}>
                        <Trash2 size={15} />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Modal>
  );
}

export const eventMeta: Record<HistoryRecord['eventType'], { cls: string; label: string }> = {
  Grabbed: { cls: 'info', label: 'Grabbed' },
  Imported: { cls: 'ok', label: 'Imported' },
  DownloadFailed: { cls: 'danger', label: 'Download failed' },
  ImportFailed: { cls: 'warn', label: 'Import failed' },
  FileDeleted: { cls: '', label: 'File deleted' },
  FileRenamed: { cls: '', label: 'Renamed' },
};

export function HistoryDialog({ title, onClose }: { title: Title; onClose: () => void }) {
  const [items, setItems] = useState<HistoryRecord[] | null>(null);
  useEffect(() => {
    api.history(1, 200, title.id).then((r) => setItems(r.records));
  }, [title.id]);
  return (
    <Modal title={`History · ${title.name}`} wide onClose={onClose}>
      {items === null && <Loader2 className="spin-icon" />}
      {items?.length === 0 && <div className="empty">Nothing has happened for this title yet.</div>}
      {items && items.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Event</th>
                <th>Issue</th>
                <th>Source</th>
                <th>Details</th>
                <th>When</th>
              </tr>
            </thead>
            <tbody>
              {items.map((h) => (
                <tr key={h.id}>
                  <td>
                    <span className={`pill ${eventMeta[h.eventType].cls}`}>{eventMeta[h.eventType].label}</span>
                  </td>
                  <td>{h.issue ?? '—'}</td>
                  <td className="mono muted" style={{ fontSize: 12, whiteSpace: 'normal' }}>
                    {h.sourceTitle}
                  </td>
                  <td className="muted" style={{ fontSize: 12, whiteSpace: 'normal' }}>
                    {h.data.indexer ?? h.data.message ?? h.data.path ?? ''}
                  </td>
                  <td className="muted" title={new Date(h.date).toLocaleString()}>
                    {timeAgo(h.date)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Modal>
  );
}

export function InteractiveSearchDialog({ title, issueId, issueLabel, onClose }: { title: Title; issueId?: number; issueLabel?: string; onClose: () => void }) {
  const toast = useToast();
  const [releases, setReleases] = useState<Release[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [onlyIssue, setOnlyIssue] = useState(issueId != null);
  const [grabbing, setGrabbing] = useState<string | null>(null);
  const [grabbed, setGrabbed] = useState<Set<string>>(new Set());

  useEffect(() => {
    api.releases(title.id, issueId).then(setReleases).catch((e) => setError(e.message));
  }, [title.id, issueId]);

  const grab = async (r: Release) => {
    setGrabbing(r.guid);
    try {
      await api.grab(r.guid, title.id, r.issueId ?? issueId);
      setGrabbed((g) => new Set(g).add(r.guid));
      toast(`Sent to download client: ${r.title}`);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Grab failed', 'error');
    } finally {
      setGrabbing(null);
    }
  };

  const shown = releases?.filter((r) => !onlyIssue || r.issueId === issueId) ?? [];

  return (
    <Modal title={`Interactive search · ${title.name}${issueLabel ? ` ${issueLabel}` : ''}`} icon={<Search size={18} color="var(--accent)" />} wide onClose={onClose}>
      {releases === null && !error && (
        <div className="muted" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <Loader2 size={16} className="spin-icon" /> Searching your indexers…
        </div>
      )}
      {error && <div className="pill danger" style={{ height: 'auto', padding: '8px 12px' }}>{error}</div>}
      {releases && (
        <>
          <div style={{ display: 'flex', gap: 16, alignItems: 'center', marginBottom: 10, flexWrap: 'wrap' }}>
            <span className="muted">
              {shown.length} of {releases.length} releases
            </span>
            {issueId != null && (
              <label className="check">
                <input type="checkbox" checked={onlyIssue} onChange={(e) => setOnlyIssue(e.target.checked)} /> Only {issueLabel}
              </label>
            )}
          </div>
          {shown.length === 0 ? (
            <div className="empty">No releases found{onlyIssue ? ` for ${issueLabel}` : ''}.</div>
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Release</th>
                    <th>Issue</th>
                    <th>Format</th>
                    <th>Indexer</th>
                    <th>Size</th>
                    <th>Peers</th>
                    <th>Age</th>
                    <th style={{ width: 60 }} />
                  </tr>
                </thead>
                <tbody>
                  {shown.map((r) => (
                    <tr key={r.guid}>
                      <td style={{ whiteSpace: 'normal', minWidth: 280 }}>
                        <div className="mono" style={{ fontSize: 12 }}>
                          {r.title}{' '}
                          {r.infoUrl && (
                            <a href={r.infoUrl} target="_blank" rel="noreferrer" title="Open on indexer">
                              <ExternalLink size={11} />
                            </a>
                          )}
                        </div>
                        {r.rejections.length > 0 && (
                          <ul className="reject-list">
                            {r.rejections.map((x) => (
                              <li key={x}>{x}</li>
                            ))}
                          </ul>
                        )}
                      </td>
                      <td>{r.issueNumber ?? '—'}</td>
                      <td>{r.format ? <span className="pill">{r.format}</span> : <span className="muted">?</span>}</td>
                      <td className="muted">
                        {r.indexer} <span className={`pill ${r.protocol === 'torrent' ? 'accent' : 'info'}`}>{r.protocol}</span>
                      </td>
                      <td className="num muted">{formatBytes(r.size)}</td>
                      <td className="num muted">{r.protocol === 'torrent' ? `${r.seeders ?? '?'} / ${r.peers ?? '?'}` : '—'}</td>
                      <td className="muted">{r.publishDate ? timeAgo(r.publishDate) : '—'}</td>
                      <td>
                        <button
                          className={`icon-btn ${r.approved ? '' : 'muted'}`}
                          style={{ width: 32, height: 32, color: grabbed.has(r.guid) ? 'var(--ok)' : r.approved ? 'var(--accent)' : undefined }}
                          title={r.approved ? 'Download' : `Download anyway (${r.rejections.join('; ')})`}
                          disabled={grabbing !== null || grabbed.has(r.guid) || !r.titleId}
                          onClick={() => grab(r)}
                        >
                          {grabbing === r.guid ? <Loader2 size={16} className="spin-icon" /> : <Download size={16} />}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </Modal>
  );
}
