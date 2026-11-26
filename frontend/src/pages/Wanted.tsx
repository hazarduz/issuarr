import { Search, SearchCheck, Sparkles } from 'lucide-react';
import { Link, useLocation } from 'react-router-dom';
import { Cover } from '../components/Cover';
import { Grow, Sep, Tool, Toolbar } from '../components/Toolbar';
import { useToast } from '../components/Toast';
import { kindLabel, parseDate, titles } from '../data/mock';

export default function Wanted() {
  const { pathname } = useLocation();
  const cutoff = pathname.endsWith('cutoff');
  const toast = useToast();

  const missing = titles
    .filter((t) => t.monitored)
    .flatMap((t) => t.issues.filter((i) => i.status === 'missing').map((i) => ({ t, i })))
    .sort((a, b) => b.i.releaseDate.localeCompare(a.i.releaseDate));

  return (
    <>
      <Toolbar>
        <Tool icon={Search} label="Search Selected" />
        <Tool icon={SearchCheck} label="Search All" onClick={() => toast(`Searching for ${missing.length} issues`)} />
        <Sep />
        <Grow />
      </Toolbar>
      <div className="page">
        <h1 className="page-title">
          {cutoff ? 'Cutoff Unmet' : 'Missing'}
          {!cutoff && <small>{missing.length} monitored issues</small>}
        </h1>
        {cutoff ? (
          <div className="card empty">
            <div>
              <Sparkles size={40} />
              <div style={{ fontWeight: 600, color: 'var(--text)' }}>Everything meets your quality cutoff</div>
              <div>Issues that could be upgraded (e.g. CBR → CBZ, scan → digital) appear here.</div>
            </div>
          </div>
        ) : (
          <div className="card table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Type</th>
                  <th>Issue</th>
                  <th>Release Date</th>
                  <th>Status</th>
                  <th style={{ width: 50 }} />
                </tr>
              </thead>
              <tbody>
                {missing.map(({ t, i }) => (
                  <tr key={i.id}>
                    <td>
                      <Link to={`/title/${t.slug}`} className="link" style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                        <div className="thumb">
                          <Cover title={t} size="sm" />
                        </div>
                        {t.name}
                      </Link>
                    </td>
                    <td className="muted">{kindLabel[t.kind]}</td>
                    <td className="num">
                      {i.number}
                      {i.title && <span className="muted"> — {i.title}</span>}
                    </td>
                    <td className="num muted">{parseDate(i.releaseDate).toLocaleDateString(undefined, { dateStyle: 'medium' })}</td>
                    <td>
                      <span className="pill danger">Missing</span>
                    </td>
                    <td>
                      <button
                        className="icon-btn"
                        style={{ width: 30, height: 30 }}
                        onClick={() => toast(`Searching for ${t.name} ${i.number}`)}
                        title="Search"
                      >
                        <Search size={15} />
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
