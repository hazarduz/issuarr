import { CheckCircle2, ChevronDown, Loader2, Plus, Trash2, Zap } from 'lucide-react';
import { useEffect, useState } from 'react';
import { api, isConnected, type Provider, type ProviderKind, type ProviderSchema } from '../data/api';
import { Modal } from './Modal';
import { useToast } from './Toast';

const palette: Record<string, string> = {
  QBittorrent: '#1e66c9', Deluge: '#2b6cb0', Transmission: '#b83232', Sabnzbd: '#d69e2e', NzbGet: '#4d7c0f',
  Newznab: '#2563eb', Torznab: '#2f855a', Prowlarr: '#e66000',
  Komga: '#0f766e', Kavita: '#4c1d95', Discord: '#5865f2', Webhook: '#64748b',
};

const abbr = (name: string) => name.replace(/[^A-Za-z]/g, '').slice(0, 2) || '?';

const kindLabel: Record<ProviderKind, string> = { indexer: 'Indexer', downloadclient: 'Download Client', notification: 'Connection' };

export function ProviderList({ kind }: { kind: ProviderKind }) {
  const toast = useToast();
  const [items, setItems] = useState<Provider[] | null>(null);
  const [schemas, setSchemas] = useState<ProviderSchema[]>([]);
  const [picking, setPicking] = useState(false);
  const [editing, setEditing] = useState<{ provider: Partial<Provider>; schema: ProviderSchema } | null>(null);

  const load = async () => {
    if (!isConnected()) {
      setItems([]);
      return;
    }
    const [list, sch] = await Promise.all([api.providers(kind), api.providerSchema(kind)]);
    setItems(list);
    setSchemas(sch);
  };

  useEffect(() => {
    load().catch((e) => toast(e.message, 'error'));
    // eslint-disable-next-line
  }, [kind]);

  if (!isConnected()) {
    return <div className="card empty">Connect the Issuarr backend to manage {kindLabel[kind].toLowerCase()}s.</div>;
  }

  const startNew = (schema: ProviderSchema) => {
    setPicking(false);
    const settings: Record<string, string> = {};
    schema.fields.forEach((f) => f.default != null && (settings[f.name] = f.default));
    setEditing({ provider: { name: schema.displayName, implementation: schema.implementation, enable: true, priority: 25, settings }, schema });
  };

  return (
    <>
      <div className="settings-grid">
        {items === null && (
          <div className="card provider">
            <Loader2 className="spin-icon" size={18} /> Loading…
          </div>
        )}
        {items?.map((p) => (
          <div
            key={p.id}
            className="card provider"
            onClick={() => {
              const schema = schemas.find((s) => s.implementation === p.implementation);
              if (schema) setEditing({ provider: p, schema });
            }}
            style={{ opacity: p.enable ? 1 : 0.55 }}
          >
            <div className="logo" style={{ background: palette[p.implementation] ?? '#475569' }}>
              {abbr(p.implementationName ?? p.implementation)}
            </div>
            <div style={{ minWidth: 0 }}>
              <div className="name">{p.name}</div>
              <div className="desc">
                {p.implementationName}
                {p.protocol && p.protocol !== 'both' ? ` · ${p.protocol}` : ''}
                {!p.enable && ' · disabled'}
              </div>
            </div>
          </div>
        ))}
        <div className="card provider add" onClick={() => setPicking(true)}>
          <Plus size={18} /> Add {kindLabel[kind]}
        </div>
      </div>

      {picking && (
        <Modal title={`Add ${kindLabel[kind]}`} onClose={() => setPicking(false)}>
          <div className="settings-grid">
            {schemas.map((s) => (
              <div key={s.implementation} className="card provider" onClick={() => startNew(s)}>
                <div className="logo" style={{ background: palette[s.implementation] ?? '#475569' }}>
                  {abbr(s.displayName)}
                </div>
                <div style={{ minWidth: 0 }}>
                  <div className="name">{s.displayName}</div>
                  <div className="desc">{s.description}</div>
                </div>
              </div>
            ))}
          </div>
        </Modal>
      )}

      {editing && (
        <ProviderForm
          kind={kind}
          schema={editing.schema}
          initial={editing.provider}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            load();
          }}
        />
      )}
    </>
  );
}

function ProviderForm({
  kind,
  schema,
  initial,
  onClose,
  onSaved,
}: {
  kind: ProviderKind;
  schema: ProviderSchema;
  initial: Partial<Provider>;
  onClose: () => void;
  onSaved: () => void;
}) {
  const toast = useToast();
  const [p, setP] = useState<Partial<Provider>>(initial);
  const [advanced, setAdvanced] = useState(false);
  const [busy, setBusy] = useState<'test' | 'save' | 'delete' | null>(null);
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const set = (name: string, value: string) => setP((x) => ({ ...x, settings: { ...x.settings, [name]: value } }));
  const fields = schema.fields.filter((f) => advanced || !f.advanced);
  const hasAdvanced = schema.fields.some((f) => f.advanced);

  const test = async () => {
    setBusy('test');
    setResult(null);
    try {
      const r = await api.testProvider(kind, p);
      setResult({ ok: true, message: r.message });
    } catch (e) {
      setResult({ ok: false, message: e instanceof Error ? e.message : 'Test failed' });
    } finally {
      setBusy(null);
    }
  };

  const save = async () => {
    setBusy('save');
    try {
      await api.saveProvider(kind, p);
      toast(`${p.name} saved`);
      onSaved();
    } catch (e) {
      setResult({ ok: false, message: e instanceof Error ? e.message : 'Save failed' });
    } finally {
      setBusy(null);
    }
  };

  const remove = async () => {
    setBusy('delete');
    try {
      await api.deleteProvider(kind, p.id!);
      toast(`${p.name} deleted`);
      onSaved();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Delete failed', 'error');
      setBusy(null);
    }
  };

  return (
    <Modal
      title={`${p.id ? 'Edit' : 'Add'} ${schema.displayName}`}
      onClose={onClose}
      footer={
        <>
          {p.id &&
            (confirmDelete ? (
              <span className="confirm">
                Delete?
                <button className="btn sm danger" onClick={remove} disabled={busy !== null}>
                  Delete
                </button>
                <button className="btn sm ghost" onClick={() => setConfirmDelete(false)}>
                  No
                </button>
              </span>
            ) : (
              <button className="btn ghost" onClick={() => setConfirmDelete(true)} title="Delete">
                <Trash2 size={16} />
              </button>
            ))}
          <span style={{ flex: 1 }} />
          <button className="btn" onClick={test} disabled={busy !== null}>
            {busy === 'test' ? <Loader2 size={16} className="spin-icon" /> : <Zap size={16} />} Test
          </button>
          <button className="btn primary" onClick={save} disabled={busy !== null}>
            {busy === 'save' && <Loader2 size={16} className="spin-icon" />} Save
          </button>
        </>
      }
    >
      <p className="muted" style={{ marginTop: 0 }}>
        {schema.description}
      </p>
      <div className="form-grid">
        <label>Name</label>
        <input className="input" value={p.name ?? ''} onChange={(e) => setP({ ...p, name: e.target.value })} />
        <label>Enable</label>
        <label className="check">
          <input type="checkbox" checked={!!p.enable} onChange={(e) => setP({ ...p, enable: e.target.checked })} />
          {kind === 'indexer' ? 'Use for searches and RSS' : kind === 'downloadclient' ? 'Send downloads here' : 'Send notifications'}
        </label>
        {fields.map((f) => (
          <FieldInput key={f.name} field={f} value={p.settings?.[f.name] ?? ''} onChange={(v) => set(f.name, v)} />
        ))}
        {(kind !== 'notification' || advanced) && advanced && (
          <>
            <label>Priority</label>
            <div>
              <input className="input" type="number" min={1} max={50} value={p.priority ?? 25} onChange={(e) => setP({ ...p, priority: Number(e.target.value) })} />
              <div className="help">1 = highest. Used to choose between {kind === 'indexer' ? 'equal releases' : 'clients of the same type'}.</div>
            </div>
          </>
        )}
      </div>
      {(hasAdvanced || kind !== 'notification') && (
        <button className="btn ghost sm" style={{ marginTop: 12 }} onClick={() => setAdvanced((a) => !a)}>
          <ChevronDown size={14} style={{ transform: advanced ? 'rotate(180deg)' : undefined }} /> {advanced ? 'Hide' : 'Show'} advanced settings
        </button>
      )}
      {result && (
        <div className={`pill ${result.ok ? 'ok' : 'danger'}`} style={{ marginTop: 14, height: 'auto', padding: '8px 12px', whiteSpace: 'normal', display: 'flex' }}>
          {result.ok && <CheckCircle2 size={14} />} {result.message}
        </div>
      )}
    </Modal>
  );
}

function FieldInput({ field, value, onChange }: { field: { name: string; label: string; type: string; helpText?: string; options?: string[]; required: boolean }; value: string; onChange: (v: string) => void }) {
  const label = (
    <label>
      {field.label}
      {field.required && <span style={{ color: 'var(--accent)' }}> *</span>}
    </label>
  );
  const help = field.helpText && <div className="help">{field.helpText}</div>;

  if (field.type === 'checkbox') {
    return (
      <>
        {label}
        <div>
          <label className="check">
            <input type="checkbox" checked={value === 'true'} onChange={(e) => onChange(String(e.target.checked))} />
            {field.helpText ?? 'Enabled'}
          </label>
        </div>
      </>
    );
  }

  return (
    <>
      {label}
      <div>
        {field.type === 'select' ? (
          <select className="input" value={value} onChange={(e) => onChange(e.target.value)}>
            {field.options?.map((o) => (
              <option key={o}>{o}</option>
            ))}
          </select>
        ) : (
          <input
            className={`input ${field.type === 'password' ? 'mono' : ''}`}
            type={field.type === 'password' ? 'password' : field.type === 'number' ? 'number' : 'text'}
            value={value}
            autoComplete="off"
            onFocus={(e) => field.type === 'password' && value === '********' && e.target.select()}
            onChange={(e) => onChange(e.target.value)}
          />
        )}
        {help}
      </div>
    </>
  );
}
