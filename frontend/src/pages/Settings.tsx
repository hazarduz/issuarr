import { Folder, Plus, Save, Trash2, Zap } from 'lucide-react';
import { useEffect, useState } from 'react';
import { FolderPicker } from '../components/FolderPicker';
import { api, formatBytes, isConnected } from '../data/api';
import { NavLink, useParams } from 'react-router-dom';
import { Grow, Tool, Toolbar } from '../components/Toolbar';
import { useToast } from '../components/Toast';

const sections = [
  { id: 'media', label: 'Media Management' },
  { id: 'profiles', label: 'Profiles' },
  { id: 'indexers', label: 'Indexers' },
  { id: 'downloadclients', label: 'Download Clients' },
  { id: 'connect', label: 'Connect' },
  { id: 'general', label: 'General' },
];

function Switch({ initial = false }: { initial?: boolean }) {
  const [on, setOn] = useState(initial);
  return <button className={`switch ${on ? 'on' : ''}`} onClick={() => setOn(!on)} aria-pressed={on} />;
}

function Provider({ name, desc, color, abbr }: { name: string; desc: string; color: string; abbr: string }) {
  return (
    <div className="card provider">
      <div className="logo" style={{ background: color }}>
        {abbr}
      </div>
      <div>
        <div className="name">{name}</div>
        <div className="desc">{desc}</div>
      </div>
    </div>
  );
}

function AddCard({ label }: { label: string }) {
  return (
    <div className="card provider add">
      <Plus size={18} /> {label}
    </div>
  );
}

type RootFolderRow = { id: number; path: string; freeSpace?: number; accessible: boolean };

function RootFolders() {
  const toast = useToast();
  const [folders, setFolders] = useState<RootFolderRow[]>(
    isConnected()
      ? []
      : [
          { id: 1, path: '/data/comics', freeSpace: 612 * 1024 ** 3, accessible: true },
          { id: 2, path: '/data/magazines', freeSpace: 612 * 1024 ** 3, accessible: true },
        ],
  );
  const [picking, setPicking] = useState(false);
  const [confirming, setConfirming] = useState<number | null>(null);

  const load = () => isConnected() && api.rootFolders().then(setFolders);
  useEffect(() => {
    load();
  }, []);

  const add = async (path: string) => {
    setPicking(false);
    if (!isConnected()) {
      if (folders.some((f) => f.path === path)) {
        toast(`${path} is already a root folder`, 'error');
        return;
      }
      setFolders((f) => [...f, { id: Date.now(), path, accessible: true }]);
      toast('Root folder added');
      return;
    }
    try {
      await api.addRootFolder(path);
      await load();
      toast('Root folder added');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not add root folder', 'error');
    }
  };

  const remove = async (f: RootFolderRow) => {
    setConfirming(null);
    try {
      if (isConnected()) await api.deleteRootFolder(f.id);
      setFolders((list) => list.filter((x) => x.id !== f.id));
      toast(`Removed ${f.path}`);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not remove root folder', 'error');
    }
  };

  return (
    <div>
      {folders.length === 0 && <div style={{ padding: '8px 0', color: 'var(--text-faint)' }}>No root folders yet.</div>}
      {folders.map((f) => (
        <div className="root-folder" key={f.id}>
          <Folder size={16} color="var(--accent)" />
          <span className="path mono" title={f.path}>
            {f.path}
          </span>
          {f.accessible ? (
            <span className="pill ok">{f.freeSpace != null ? `${formatBytes(f.freeSpace)} free` : 'OK'}</span>
          ) : (
            <span className="pill danger">Not found</span>
          )}
          {confirming === f.id ? (
            <span className="confirm">
              Remove?
              <button className="btn sm danger" onClick={() => remove(f)}>
                Remove
              </button>
              <button className="btn sm ghost" onClick={() => setConfirming(null)}>
                Cancel
              </button>
            </span>
          ) : (
            <button className="icon-btn" title="Remove root folder" aria-label={`Remove ${f.path}`} onClick={() => setConfirming(f.id)}>
              <Trash2 size={16} />
            </button>
          )}
        </div>
      ))}
      <button className="btn" style={{ marginTop: 8 }} onClick={() => setPicking(true)}>
        <Plus size={16} /> Add Root Folder
      </button>
      <div className="help">Removing a root folder doesn’t delete any files or titles.</div>
      {picking && <FolderPicker initialPath="" onSelect={add} onClose={() => setPicking(false)} />}
    </div>
  );
}

function Media() {
  const tokens = ['{Title}', '{Title Year}', '{Issue}', '{Issue:000}', '{Issue Title}', '{Release Date}', '{Publisher}', '{Format}'];
  return (
    <div className="card card-body">
      <div className="form-row">
        <label>Rename Issues</label>
        <div>
          <Switch initial />
          <div className="help">Rename downloaded files using the formats below. Off keeps the release name.</div>
        </div>
      </div>
      <div className="form-row">
        <label>Comic Issue Format</label>
        <div>
          <input className="input mono" defaultValue="{Title} {Issue:000} ({Release Date:yyyy})" />
          <div className="help">
            Example: <span className="mono">Starfall Rangers 037 (2026).cbz</span>
          </div>
          <div style={{ marginTop: 6 }}>
            {tokens.map((t) => (
              <span className="token mono" key={t}>
                {t}
              </span>
            ))}
          </div>
        </div>
      </div>
      <div className="form-row">
        <label>Magazine Issue Format</label>
        <div>
          <input className="input mono" defaultValue="{Title} - {Release Date:yyyy-MM}" />
          <div className="help">
            Example: <span className="mono">Circuit &amp; Solder - 2026-11.pdf</span>
          </div>
        </div>
      </div>
      <div className="form-row">
        <label>Title Folder Format</label>
        <div>
          <input className="input mono" defaultValue="{Title} ({Title Year})" />
        </div>
      </div>
      <div className="form-row">
        <label>Root Folders</label>
        <RootFolders />
      </div>
      <div className="form-row">
        <label>Embed ComicInfo.xml</label>
        <div>
          <Switch initial />
          <div className="help">Write series, issue and publisher metadata into CBZ files for Komga and Kavita.</div>
        </div>
      </div>
      <div className="form-row">
        <label>Use Hardlinks</label>
        <div>
          <Switch initial />
          <div className="help">Hardlink instead of copying so torrents can keep seeding without using extra space.</div>
        </div>
      </div>
    </div>
  );
}

function Profiles() {
  return (
    <div className="settings-grid">
      <Provider name="CBZ / CBR" desc="CBZ > CBR > PDF · cutoff CBZ" color="#7b5cff" abbr="CB" />
      <Provider name="PDF Preferred" desc="True PDF > Scan PDF > EPUB" color="#e0483e" abbr="PDF" />
      <Provider name="Any" desc="Any format, no upgrades" color="#4b5563" abbr="∗" />
      <AddCard label="Add Profile" />
    </div>
  );
}

function Indexers() {
  return (
    <div className="settings-grid">
      <Provider name="Prowlarr" desc="Synced · 6 indexers" color="#e66000" abbr="P" />
      <Provider name="Torznab Generic" desc="Torrent · RSS, search" color="#2f855a" abbr="TZ" />
      <Provider name="NZBgeek" desc="Usenet · via Prowlarr" color="#2563eb" abbr="NZ" />
      <AddCard label="Add Indexer" />
    </div>
  );
}

function Clients() {
  return (
    <div className="settings-grid">
      <Provider name="qBittorrent" desc="Torrent · category: issuarr" color="#1e66c9" abbr="qB" />
      <Provider name="Deluge" desc="Torrent · label: issuarr" color="#2b6cb0" abbr="De" />
      <Provider name="Transmission" desc="Torrent · disabled" color="#9b2c2c" abbr="Tr" />
      <Provider name="SABnzbd" desc="Usenet · category: issuarr" color="#d69e2e" abbr="SAB" />
      <AddCard label="Add Download Client" />
    </div>
  );
}

function Connect() {
  return (
    <div className="settings-grid">
      <Provider name="Komga" desc="Library scan on import" color="#0f766e" abbr="Ko" />
      <Provider name="Kavita" desc="Library scan on import" color="#4c1d95" abbr="Ka" />
      <Provider name="Discord" desc="On grab, import, health" color="#5865f2" abbr="Di" />
      <AddCard label="Add Connection" />
    </div>
  );
}

function General() {
  return (
    <div className="card card-body">
      <div className="form-row">
        <label>Port</label>
        <input className="input" defaultValue="8788" />
      </div>
      <div className="form-row">
        <label>URL Base</label>
        <input className="input" placeholder="/issuarr" />
      </div>
      <div className="form-row">
        <label>API Key</label>
        <input className="input mono" defaultValue="9c1f0a7d2e4b48b6a1d3f5e7c9b0a2d4" readOnly />
      </div>
      <div className="form-row">
        <label>Authentication</label>
        <select className="input" defaultValue="forms">
          <option value="forms">Forms (login page)</option>
          <option value="basic">Basic</option>
          <option value="external">External</option>
        </select>
      </div>
      <div className="form-row">
        <label>Metadata Source</label>
        <select className="input" defaultValue="comicvine">
          <option value="comicvine">ComicVine</option>
          <option value="metron">Metron</option>
          <option value="manual">Manual only</option>
        </select>
      </div>
    </div>
  );
}

export default function Settings() {
  const { section = 'media' } = useParams();
  const toast = useToast();
  const current = sections.find((s) => s.id === section) ?? sections[0];
  return (
    <>
      <Toolbar>
        <Tool icon={Save} label="Save Changes" onClick={() => toast('Settings saved')} />
        <Tool icon={Zap} label="Test All" onClick={() => toast('All connections OK')} />
        <Grow />
        <div className="segmented">
          {sections.map((s) => (
            <NavLink key={s.id} to={`/settings/${s.id}`} className={s.id === current.id ? 'on' : ''}>
              {s.label}
            </NavLink>
          ))}
        </div>
      </Toolbar>
      <div className="page">
        <h1 className="page-title">{current.label}</h1>
        {current.id === 'media' && <Media />}
        {current.id === 'profiles' && <Profiles />}
        {current.id === 'indexers' && <Indexers />}
        {current.id === 'downloadclients' && <Clients />}
        {current.id === 'connect' && <Connect />}
        {current.id === 'general' && <General />}
      </div>
    </>
  );
}
