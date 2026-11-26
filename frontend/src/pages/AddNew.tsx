import { Check, FolderInput, Plus, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { Cover } from '../components/Cover';
import { useToast } from '../components/Toast';
import { api, isConnected } from '../data/api';
import type { Title } from '../data/mock';

// A handful of fake metadata lookups so the add flow can be demoed.
const catalog: Title[] = [
  ['Copperhead Lane', 'comic', 'Northlight Comics', 2026, ['#0b486b', '#f56217']],
  ['Copper & Clay', 'magazine', 'Hearth Publishing', 2014, ['#b79891', '#94716b']],
  ['Lighthouse Keepers', 'comic', 'Small Hours', 2025, ['#2b5876', '#4e4376']],
  ['The Analog Gardener', 'magazine', 'Open Country Press', 2011, ['#3ca55c', '#b5ac49']],
  ['Glass Cannon', 'comic', 'Gravel & Ink', 2024, ['#ee0979', '#ff6a00']],
  ['Retro Byte', 'magazine', 'Respawn Media', 2020, ['#00c6ff', '#0072ff']],
].map(([name, kind, publisher, year, palette], i) => ({
  id: `c${i}`,
  slug: '',
  name: name as string,
  kind: kind as Title['kind'],
  publisher: publisher as string,
  year: year as number,
  frequency: kind === 'comic' ? 'Monthly' : 'Monthly',
  status: 'continuing',
  monitored: true,
  qualityProfile: 'Any',
  path: '',
  overview: '',
  tags: [],
  palette: palette as [string, string],
  issues: [],
}));

export default function AddNew() {
  const params = new URLSearchParams(useLocation().search);
  const importMode = useLocation().pathname === '/import';
  const [q, setQ] = useState(params.get('term') ?? '');
  const [added, setAdded] = useState<Set<string>>(new Set());
  const toast = useToast();

  const results = useMemo(() => {
    const s = q.trim().toLowerCase();
    return s ? catalog.filter((c) => c.name.toLowerCase().includes(s) || c.publisher.toLowerCase().includes(s)) : catalog;
  }, [q]);

  if (importMode) {
    return (
      <div className="page">
        <h1 className="page-title">Library Import</h1>
        <div className="card empty">
          <div>
            <FolderInput size={40} />
            <div style={{ fontWeight: 600, color: 'var(--text)' }}>Import an existing collection</div>
            <p style={{ maxWidth: 460 }}>
              Point Issuarr at a folder of CBZ, CBR or PDF files and it will match each folder to a title and each file to an
              issue.
            </p>
            <button className="btn primary">
              <FolderInput size={16} /> Choose Folder
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <h1 className="page-title">Add New</h1>
      <div className="add-search" style={{ marginBottom: 24 }}>
        <Search size={20} />
        <input
          autoFocus
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder="Search for a comic series or magazine, e.g. “Copper”"
        />
      </div>
      <div className="overview-list" style={{ maxWidth: 900 }}>
        {results.map((t) => {
          const isAdded = added.has(t.id);
          return (
            <div key={t.id} className="card overview-row" style={{ alignItems: 'center' }}>
              <div style={{ borderRadius: 6, overflow: 'hidden' }}>
                <Cover title={t} size="sm" issue={String(t.year)} />
              </div>
              <div style={{ display: 'flex', gap: 16, alignItems: 'center', flexWrap: 'wrap' }}>
                <div style={{ flex: 1, minWidth: 200 }}>
                  <h3>
                    {t.name} <span style={{ color: 'var(--text-faint)', fontWeight: 500 }}>({t.year})</span>
                  </h3>
                  <div className="row-meta">
                    <span className="pill">{t.kind === 'comic' ? 'Comic' : 'Magazine'}</span>
                    <span className="pill">{t.publisher}</span>
                  </div>
                </div>
                <button
                  className={`btn ${isAdded ? '' : 'primary'}`}
                  disabled={isAdded}
                  onClick={async () => {
                    if (isConnected()) {
                      try {
                        const folders = await api.rootFolders();
                        if (folders.length === 0) {
                          toast('Add a root folder in Settings → Media Management first', 'error');
                          return;
                        }
                        await api.addTitle(t, folders[0].path);
                      } catch {
                        toast(`Could not add ${t.name}`, 'error');
                        return;
                      }
                    }
                    setAdded((s) => new Set(s).add(t.id));
                    toast(`${t.name} added to your library`);
                  }}
                >
                  {isAdded ? <Check size={16} /> : <Plus size={16} />}
                  {isAdded ? 'Added' : 'Add'}
                </button>
              </div>
            </div>
          );
        })}
        {results.length === 0 && <div className="card empty">No results for “{q}”.</div>}
      </div>
    </div>
  );
}
