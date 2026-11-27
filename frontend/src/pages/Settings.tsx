import { Copy, Folder, LogOut, Plus, RefreshCw, Save, Trash2 } from 'lucide-react';
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { NavLink, useParams } from 'react-router-dom';
import { FolderPicker } from '../components/FolderPicker';
import { ProviderList } from '../components/Providers';
import { useToast } from '../components/Toast';
import { Grow, Tool, Toolbar } from '../components/Toolbar';
import { api, formatBytes, isConnected, serverInfo, type HostConfig, type QualityProfile, type RootFolder } from '../data/api';

const sections = [
  { id: 'media', label: 'Media Management' },
  { id: 'profiles', label: 'Profiles' },
  { id: 'indexers', label: 'Indexers' },
  { id: 'downloadclients', label: 'Download Clients' },
  { id: 'connect', label: 'Connect' },
  { id: 'general', label: 'General' },
];

/** Settings stored in the database, shared by several sections. */
function useConfig() {
  const toast = useToast();
  const [saved, setSaved] = useState<Record<string, string> | null>(null);
  const [values, setValues] = useState<Record<string, string>>({});

  useEffect(() => {
    if (!isConnected()) return;
    api.config().then((c) => {
      setSaved(c);
      setValues(c);
    }).catch((e) => toast(e.message, 'error'));
    // eslint-disable-next-line
  }, []);

  const dirty = saved != null && Object.keys(values).some((k) => values[k] !== saved[k]);
  const set = (k: string, v: string) => setValues((x) => ({ ...x, [k]: v }));
  const save = useCallback(async () => {
    if (!saved) return;
    const changed = Object.fromEntries(Object.entries(values).filter(([k, v]) => v !== saved[k]));
    const next = await api.saveConfig(changed);
    setSaved(next);
    setValues(next);
  }, [values, saved]);

  return { values, set, dirty, save, loaded: saved != null };
}

type Config = ReturnType<typeof useConfig>;

function Row({ label, help, children }: { label: string; help?: ReactNode; children: ReactNode }) {
  return (
    <div className="form-row">
      <label>{label}</label>
      <div>
        {children}
        {help && <div className="help">{help}</div>}
      </div>
    </div>
  );
}

function Switch({ on, onChange }: { on: boolean; onChange: (v: boolean) => void }) {
  return <button type="button" className={`switch ${on ? 'on' : ''}`} onClick={() => onChange(!on)} aria-pressed={on} />;
}

function RootFolders() {
  const toast = useToast();
  const [folders, setFolders] = useState<RootFolder[]>([]);
  const [picking, setPicking] = useState(false);
  const [confirming, setConfirming] = useState<number | null>(null);

  const load = () => isConnected() && api.rootFolders().then(setFolders);
  useEffect(() => {
    load();
  }, []);

  const add = async (path: string) => {
    setPicking(false);
    try {
      await api.addRootFolder(path);
      await load();
      toast('Root folder added');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not add root folder', 'error');
    }
  };

  const remove = async (f: RootFolder) => {
    setConfirming(null);
    try {
      await api.deleteRootFolder(f.id);
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
      <button className="btn" style={{ marginTop: 8 }} onClick={() => setPicking(true)} disabled={!isConnected()}>
        <Plus size={16} /> Add Root Folder
      </button>
      <div className="help">Removing a root folder doesn’t delete any files or titles.</div>
      {picking && <FolderPicker initialPath="" onSelect={add} onClose={() => setPicking(false)} />}
    </div>
  );
}

const tokens = ['{Title}', '{Title Year}', '{Publisher}', '{Issue}', '{Issue:000}', '{Issue Title}', '{Release Date}', '{Release Date:yyyy-MM}', '{Release Year}', '{Format}'];

/** Client-side preview of a naming format, using a sample issue. */
function preview(format: string, kind: 'comic' | 'magazine') {
  const sample =
    kind === 'comic'
      ? { title: 'Starfall Rangers', year: '2023', publisher: 'Northlight Comics', issue: '37', padded: '037', issueTitle: 'Signal Lost', date: '2026-11-07', ym: '2026-11', ry: '2026', fmt: 'CBZ', ext: 'cbz' }
      : { title: 'Circuit & Solder', year: '2015', publisher: 'Workbench Media', issue: 'Nov 2026', padded: 'Nov 2026', issueTitle: '', date: '2026-11-01', ym: '2026-11', ry: '2026', fmt: 'PDF', ext: 'pdf' };
  const name = format.replace(/\{([^}:]+)(?::([^}]+))?\}/g, (m, token: string, spec?: string) => {
    switch (token.trim().toLowerCase()) {
      case 'title':
        return sample.title;
      case 'title year':
        return sample.year;
      case 'publisher':
        return sample.publisher;
      case 'issue':
        return spec && /^0+$/.test(spec) ? (kind === 'comic' ? sample.issue.padStart(spec.length, '0') : sample.padded) : sample.issue;
      case 'issue title':
        return sample.issueTitle;
      case 'release date':
        return spec === 'yyyy-MM' ? sample.ym : sample.date;
      case 'release year':
        return sample.ry;
      case 'format':
        return sample.fmt;
      default:
        return m;
    }
  });
  return name.replace(/\(\s*\)|\[\s*\]/g, '').replace(/\s{2,}/g, ' ').replace(/:/g, ' -').trim() + '.' + sample.ext;
}

function NamingInput({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const ref = useRef<HTMLInputElement>(null);
  const insert = (t: string) => {
    const el = ref.current;
    if (!el) return onChange(value + t);
    const start = el.selectionStart ?? value.length;
    const end = el.selectionEnd ?? value.length;
    onChange(value.slice(0, start) + t + value.slice(end));
    requestAnimationFrame(() => {
      el.focus();
      el.setSelectionRange(start + t.length, start + t.length);
    });
  };
  return (
    <>
      <input ref={ref} className="input mono" value={value} onChange={(e) => onChange(e.target.value)} />
      <div style={{ marginTop: 6 }}>
        {tokens.map((t) => (
          <span className="token mono" key={t} onClick={() => insert(t)} title="Insert">
            {t}
          </span>
        ))}
      </div>
    </>
  );
}

function Media({ cfg }: { cfg: Config }) {
  const v = cfg.values;
  const bool = (k: string) => v[k] === 'true';
  return (
    <div className="card card-body">
      <Row label="Root Folders" help="Where your comics and magazines live. New titles get a folder inside one of these.">
        <RootFolders />
      </Row>
      <Row label="Rename Issues" help="Rename imported files using the formats below. Off keeps the original file name.">
        <Switch on={bool('renameIssues')} onChange={(x) => cfg.set('renameIssues', String(x))} />
      </Row>
      <Row label="Comic Issue Format" help={<>Example: <span className="mono">{preview(v.comicIssueFormat ?? '', 'comic')}</span></>}>
        <NamingInput value={v.comicIssueFormat ?? ''} onChange={(x) => cfg.set('comicIssueFormat', x)} />
      </Row>
      <Row label="Magazine Issue Format" help={<>Example: <span className="mono">{preview(v.magazineIssueFormat ?? '', 'magazine')}</span></>}>
        <NamingInput value={v.magazineIssueFormat ?? ''} onChange={(x) => cfg.set('magazineIssueFormat', x)} />
      </Row>
      <Row label="Title Folder Format" help={<>Example: <span className="mono">{preview(v.titleFolderFormat ?? '', 'comic').replace(/\.cbz$/, '')}</span></>}>
        <NamingInput value={v.titleFolderFormat ?? ''} onChange={(x) => cfg.set('titleFolderFormat', x)} />
      </Row>
      <Row label="Use Hardlinks" help="Hardlink torrent downloads instead of copying, so they keep seeding without using extra space. Needs the downloads and library on the same drive.">
        <Switch on={bool('useHardlinks')} onChange={(x) => cfg.set('useHardlinks', String(x))} />
      </Row>
      <Row label="Write ComicInfo.xml" help="Add series, number, date and publisher to CBZ files for Komga and Kavita. Skipped for hardlinked files so seeding torrents aren't changed.">
        <Switch on={bool('writeComicInfo')} onChange={(x) => cfg.set('writeComicInfo', String(x))} />
      </Row>
    </div>
  );
}

function Profiles() {
  const [profiles, setProfiles] = useState<QualityProfile[]>([]);
  useEffect(() => {
    if (isConnected()) api.qualityProfiles().then(setProfiles);
  }, []);
  return (
    <>
      <p className="muted" style={{ marginTop: 0 }}>
        A title's profile decides which file formats Issuarr accepts and which it upgrades. Pick one per title from its Edit dialog.
      </p>
      <div className="settings-grid">
        {profiles.map((p) => (
          <div key={p.name} className="card" style={{ padding: 16 }}>
            <div style={{ fontWeight: 600, marginBottom: 4 }}>{p.name}</div>
            <div className="muted" style={{ fontSize: 13, marginBottom: 10 }}>
              {p.description}
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              {p.formats.map((f, i) => (
                <span key={f} className={`pill ${i === 0 && p.upgradesAllowed ? 'accent' : ''}`}>
                  {i + 1}. {f}
                </span>
              ))}
            </div>
            <div className="help">{p.upgradesAllowed ? `Upgrades until ${p.cutoff}` : 'No upgrades'}</div>
          </div>
        ))}
      </div>
    </>
  );
}

function Indexers({ cfg }: { cfg: Config }) {
  return (
    <>
      <ProviderList kind="indexer" />
      <h3 style={{ margin: '28px 0 12px' }}>Options</h3>
      <div className="card card-body">
        <Row label="RSS Sync Interval" help="Minutes between checks of your indexers' newest releases (minimum 10).">
          <input className="input" type="number" min={10} style={{ maxWidth: 140 }} value={cfg.values.rssSyncInterval ?? ''} onChange={(e) => cfg.set('rssSyncInterval', e.target.value)} />
        </Row>
        <Row label="Minimum Seeders" help="Skip torrents with fewer seeders than this.">
          <input className="input" type="number" min={0} style={{ maxWidth: 140 }} value={cfg.values.minimumSeeders ?? ''} onChange={(e) => cfg.set('minimumSeeders', e.target.value)} />
        </Row>
      </div>
      <p className="help" style={{ marginTop: 14 }}>
        Using Prowlarr? Add it once with the <b>Prowlarr</b> type to search all its indexers, or add individual indexers with their Torznab/Newznab feed URLs.
      </p>
    </>
  );
}

function DownloadClients({ cfg }: { cfg: Config }) {
  const bool = (k: string) => cfg.values[k] === 'true';
  return (
    <>
      <ProviderList kind="downloadclient" />
      <h3 style={{ margin: '28px 0 12px' }}>Completed &amp; Failed Download Handling</h3>
      <div className="card card-body">
        <Row label="Remove Completed Torrents" help="Remove torrents from the client after importing. Off keeps them seeding (usenet downloads are always cleaned up).">
          <Switch on={bool('removeCompletedDownloads')} onChange={(x) => cfg.set('removeCompletedDownloads', String(x))} />
        </Row>
        <Row label="Redownload Failed" help="When a download fails, blocklist it and search for another release automatically.">
          <Switch on={bool('autoRedownloadFailed')} onChange={(x) => cfg.set('autoRedownloadFailed', String(x))} />
        </Row>
      </div>
      <p className="help" style={{ marginTop: 14 }}>
        Running the download client in another container? Set <b>Remote Path</b> and <b>Local Path</b> under the client's advanced settings so Issuarr can find finished downloads.
      </p>
    </>
  );
}

function General({ cfg, host, setHost }: { cfg: Config; host: HostConfig | null; setHost: (h: HostConfig) => void }) {
  const toast = useToast();
  const v = cfg.values;
  if (!host) return <div className="card empty">Loading…</div>;
  const set = <K extends keyof HostConfig>(k: K, val: HostConfig[K]) => setHost({ ...host, [k]: val });
  const loginOn = host.authenticationMethod === 'forms' || host.authenticationMethod === 'basic';

  return (
    <>
      <h3 style={{ margin: '0 0 12px' }}>Metadata</h3>
      <div className="card card-body">
        <Row label="Comic Metadata Source" help="Where Add New finds comic series and their issue lists.">
          <select className="input" value={v.metadataSource ?? 'comicvine'} onChange={(e) => cfg.set('metadataSource', e.target.value)}>
            <option value="comicvine">ComicVine</option>
            <option value="metron">Metron</option>
          </select>
        </Row>
        {(v.metadataSource ?? 'comicvine') === 'comicvine' ? (
          <Row label="ComicVine API Key" help={<>Free: sign up at <a href="https://comicvine.gamespot.com/api/" target="_blank" rel="noreferrer" style={{ color: 'var(--accent)' }}>comicvine.gamespot.com/api</a>.</>}>
            <input className="input mono" type="password" autoComplete="off" value={v.comicVineApiKey ?? ''} onChange={(e) => cfg.set('comicVineApiKey', e.target.value)} />
          </Row>
        ) : (
          <>
            <Row label="Metron Username" help={<>Free account at <a href="https://metron.cloud" target="_blank" rel="noreferrer" style={{ color: 'var(--accent)' }}>metron.cloud</a>.</>}>
              <input className="input" autoComplete="off" value={v.metronUsername ?? ''} onChange={(e) => cfg.set('metronUsername', e.target.value)} />
            </Row>
            <Row label="Metron Password">
              <input className="input" type="password" autoComplete="new-password" value={v.metronPassword ?? ''} onChange={(e) => cfg.set('metronPassword', e.target.value)} />
            </Row>
          </>
        )}
      </div>

      <h3 style={{ margin: '28px 0 12px' }}>Security</h3>
      <div className="card card-body">
        <Row label="Authentication" help="Forms shows a login page. Basic uses the browser's login prompt.">
          <select className="input" value={host.authenticationMethod} onChange={(e) => set('authenticationMethod', e.target.value as HostConfig['authenticationMethod'])}>
            <option value="none">None</option>
            <option value="forms">Forms (login page)</option>
            <option value="basic">Basic (browser popup)</option>
            <option value="external">External (reverse proxy handles it)</option>
          </select>
        </Row>
        {loginOn && (
          <>
            <Row label="Authentication Required" help="Skipping login on your home network is convenient; Issuarr still asks when you connect from outside.">
              <select className="input" value={host.authenticationRequired} onChange={(e) => set('authenticationRequired', e.target.value as HostConfig['authenticationRequired'])}>
                <option value="enabled">Enabled</option>
                <option value="disabledForLocalAddresses">Disabled for local addresses</option>
              </select>
            </Row>
            <Row label="Username">
              <input className="input" autoComplete="username" value={host.username} onChange={(e) => set('username', e.target.value)} />
            </Row>
            <Row label="Password" help={host.hasPassword ? 'Leave empty to keep the current password.' : 'At least 6 characters.'}>
              <input className="input" type="password" autoComplete="new-password" value={host.password ?? ''} onChange={(e) => set('password', e.target.value)} />
            </Row>
          </>
        )}
        <Row label="API Key" help="Other apps use this to talk to Issuarr.">
          <div style={{ display: 'flex', gap: 8, maxWidth: 520 }}>
            <input className="input mono" readOnly value={host.apiKey} onFocus={(e) => e.target.select()} />
            <button className="btn" title="Copy" onClick={() => navigator.clipboard?.writeText(host.apiKey).then(() => toast('API key copied'))}>
              <Copy size={16} />
            </button>
            <button
              className="btn"
              title="Regenerate"
              onClick={async () => {
                if (!window.confirm('Regenerate the API key? Apps using the old key will stop working.')) return;
                const { apiKey } = await api.regenerateApiKey();
                setHost({ ...host, apiKey });
                toast('New API key generated. Reloading…');
                setTimeout(() => window.location.reload(), 1200);
              }}
            >
              <RefreshCw size={16} />
            </button>
          </div>
        </Row>
      </div>

      <h3 style={{ margin: '28px 0 12px' }}>Host</h3>
      <div className="card card-body">
        <Row label="Instance Name">
          <input className="input" value={host.instanceName} onChange={(e) => set('instanceName', e.target.value)} />
        </Row>
        <Row label="Bind Address" help="* listens on every network interface. Needs a restart.">
          <input className="input" value={host.bindAddress} onChange={(e) => set('bindAddress', e.target.value)} />
        </Row>
        <Row label="Port" help="Needs a restart.">
          <input className="input" type="number" style={{ maxWidth: 140 }} value={host.port} onChange={(e) => set('port', Number(e.target.value))} />
        </Row>
        <Row label="URL Base" help="For reverse proxies, e.g. /issuarr. Needs a restart.">
          <input className="input" placeholder="/issuarr" value={host.urlBase} onChange={(e) => set('urlBase', e.target.value)} />
        </Row>
      </div>
      {serverInfo()?.authentication !== 'none' && (
        <a className="btn" style={{ marginTop: 16 }} href={`${serverInfo()?.urlBase ?? ''}/logout`}>
          <LogOut size={16} /> Log out
        </a>
      )}
    </>
  );
}

export default function Settings() {
  const { section = 'media' } = useParams();
  const toast = useToast();
  const current = sections.find((s) => s.id === section) ?? sections[0];
  const cfg = useConfig();
  const [host, setHostState] = useState<HostConfig | null>(null);
  const [savedHost, setSavedHost] = useState<HostConfig | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!isConnected()) return;
    api.hostConfig().then((h) => {
      setHostState(h);
      setSavedHost(h);
    });
  }, []);

  const hostDirty = host != null && savedHost != null && JSON.stringify(host) !== JSON.stringify(savedHost);
  const dirty = cfg.dirty || hostDirty;

  const save = async () => {
    setSaving(true);
    try {
      if (cfg.dirty) await cfg.save();
      if (hostDirty && host) {
        const r = await api.saveHostConfig(host);
        const next = { ...r.config, password: '' };
        setHostState(next);
        setSavedHost(next);
        if (r.restartNeeded) toast('Saved. Restart Issuarr for the port / address / URL base change to take effect.');
        if (host.authenticationMethod !== savedHost?.authenticationMethod) {
          toast('Saved. Login settings are active now.');
        }
      }
      toast('Settings saved');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not save', 'error');
    } finally {
      setSaving(false);
    }
  };

  const showsSave = ['media', 'indexers', 'downloadclients', 'general'].includes(current.id);

  return (
    <>
      <Toolbar>
        {showsSave && <Tool icon={Save} label={dirty ? 'Save Changes •' : 'Save Changes'} on={dirty} spin={false} onClick={save} />}
        {saving && <span className="muted">Saving…</span>}
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
        {!isConnected() && current.id !== 'profiles' && (
          <div className="card empty" style={{ marginBottom: 16 }}>
            You're looking at sample data. Run the Issuarr backend to change settings.
          </div>
        )}
        {isConnected() && current.id === 'media' && <Media cfg={cfg} />}
        {current.id === 'profiles' && <Profiles />}
        {isConnected() && current.id === 'indexers' && <Indexers cfg={cfg} />}
        {isConnected() && current.id === 'downloadclients' && <DownloadClients cfg={cfg} />}
        {isConnected() && current.id === 'connect' && <ProviderList kind="notification" />}
        {isConnected() && current.id === 'general' && <General cfg={cfg} host={host} setHost={setHostState} />}
        {showsSave && dirty && (
          <div className="import-bar">
            <span className="muted" style={{ marginLeft: 'auto' }}>
              You have unsaved changes
            </span>
            <button className="btn primary" onClick={save} disabled={saving}>
              <Save size={16} /> Save Changes
            </button>
          </div>
        )}
      </div>
    </>
  );
}
