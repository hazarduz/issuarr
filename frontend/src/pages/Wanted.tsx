import { Loader2, Search, SearchCheck, Sparkles } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Cover } from '../components/Cover';
import { useToast } from '../components/Toast';
import { Grow, Tool, Toolbar } from '../components/Toolbar';
import { api, isConnected } from '../data/api';
import { kindLabel, parseDate, titles, type Issue, type Title } from '../data/mock';
import { runCommand, useLibraryVersion } from '../data/store';

interface Row {
  t: Title;
  i: Issue;
}

export default function Wanted() {
  useLibraryVersion();
  const { pathname } = useLocation();
  const cutoff = pathname.endsWith('cutoff');
  const toast = useToast();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState<string | null>(null);
  const [cutoffIds, setCutoffIds] = useState<Set<number> | null>(null);

  useEffect(() => {
    setSelected(new Set());
    if (cutoff && isConnected()) api.cutoffUnmet().then((r) => setCutoffIds(new Set(r.map((x) => x.id))));
  }, [cutoff]);

  const rows: Row[] = useMemo(() => {
    const all = titles.flatMap((t) => t.issues.map((i) => ({ t, i })));
    const list = cutoff
      ? all.filter(({ i }) => i.serverId != null && cutoffIds?.has(i.serverId))
      : all.filter(({ t, i }) => t.monitored && i.status === 'missing');
    return list.sort((a, b) => b.i.releaseDate.localeCompare(a.i.releaseDate));
    // eslint-disable-next-line
  }, [cutoff, cutoffIds, titles.length, titles.map((t) => t.issues.length).join()]);

  const search = async (list: Row[], key: string) => {
    if (!isConnected()) return toast('Not available with sample data.', 'error');
    if (list.length === 0) return;
    setBusy(key);
    toast(`Searching for ${list.length} issue${list.length === 1 ? '' : 's'}…`);
    try {
      // One command per title (searches are per title name).
      const byTitle = new Map<string, Row[]>();
      list.forEach((r) => byTitle.set(r.t.id, [...(byTitle.get(r.t.id) ?? []), r]));
      const results = await Promise.all([...byTitle.entries()].map(([tid, rs]) => runCommand('IssueSearch', tid, rs.map((r) => r.i.serverId!))));
      toast(results.map((r) => r.message).filter((m) => m && !m.startsWith('Nothing')).join(' · ') || 'Nothing new to grab');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Search failed', 'error');
    } finally {
      setBusy(null);
    }
  };

  const searchAll = async () => {
    if (!isConnected()) return toast('Not available with sample data.', 'error');
    if (cutoff) return search(rows, 'all');
    setBusy('all');
    toast('Searching for all missing issues…');
    try {
      const cmd = await runCommand('MissingSearch');
      toast(cmd.message ?? 'Done');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Search failed', 'error');
    } finally {
      setBusy(null);
    }
  };

  const key = (r: Row) => r.i.id;
  const chosen = rows.filter((r) => selected.has(key(r)));

  return (
    <>
      <Toolbar>
        <Tool icon={Search} label={`Search Selected${chosen.length ? ` (${chosen.length})` : ''}`} spin={busy === 'selected'} onClick={() => search(chosen, 'selected')} />
        <Tool icon={SearchCheck} label="Search All" spin={busy === 'all'} onClick={searchAll} />
        <Grow />
      </Toolbar>
      <div className="page">
        <h1 className="page-title">
          {cutoff ? 'Cutoff Unmet' : 'Missing'}
          <small>
            {rows.length} {cutoff ? 'issues could be upgraded' : 'monitored issues'}
          </small>
        </h1>
        {cutoff && cutoffIds === null && isConnected() ? (
          <div className="card empty">
            <Loader2 className="spin-icon" />
          </div>
        ) : rows.length === 0 ? (
          <div className="card empty">
            <div>
              <Sparkles size={40} />
              <div style={{ fontWeight: 600, color: 'var(--text)' }}>{cutoff ? 'Everything meets your quality cutoff' : 'Nothing missing'}</div>
              <div>{cutoff ? 'Issues in a format below their profile’s cutoff (e.g. CBR when you prefer CBZ) appear here.' : 'Every monitored issue that has come out is in your library.'}</div>
            </div>
          </div>
        ) : (
          <div className="card table-wrap">
            <table className="table import-table">
              <thead>
                <tr>
                  <th style={{ width: 40 }}>
                    <input type="checkbox" checked={chosen.length === rows.length} onChange={(e) => setSelected(new Set(e.target.checked ? rows.map(key) : []))} aria-label="Select all" />
                  </th>
                  <th>Title</th>
                  <th>Type</th>
                  <th>Issue</th>
                  <th>Release Date</th>
                  <th>{cutoff ? 'Current' : 'Status'}</th>
                  <th style={{ width: 50 }} />
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={key(r)}>
                    <td>
                      <input
                        type="checkbox"
                        checked={selected.has(key(r))}
                        onChange={(e) =>
                          setSelected((s) => {
                            const n = new Set(s);
                            if (e.target.checked) n.add(key(r));
                            else n.delete(key(r));
                            return n;
                          })
                        }
                      />
                    </td>
                    <td>
                      <Link to={`/title/${r.t.slug}`} className="link" style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                        <div className="thumb">
                          <Cover title={r.t} size="sm" />
                        </div>
                        {r.t.name}
                      </Link>
                    </td>
                    <td className="muted">{kindLabel[r.t.kind]}</td>
                    <td className="num">
                      {r.i.number}
                      {r.i.title && <span className="muted"> — {r.i.title}</span>}
                    </td>
                    <td className="num muted">{parseDate(r.i.releaseDate).toLocaleDateString(undefined, { dateStyle: 'medium' })}</td>
                    <td>{cutoff ? <span className="pill warn">{r.i.file?.format}</span> : <span className="pill danger">Missing</span>}</td>
                    <td>
                      <button className="icon-btn" style={{ width: 30, height: 30 }} onClick={() => search([r], key(r))} title="Search">
                        {busy === key(r) ? <Loader2 size={15} className="spin-icon" /> : <Search size={15} />}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
