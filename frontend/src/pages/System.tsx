import { BookOpen, CheckCircle2, CircleAlert, Database, HardDrive, Info, RefreshCw, TriangleAlert } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Grow, Tool, Toolbar } from '../components/Toolbar';
import { api, isConnected, serverInfo, type HealthItem } from '../data/api';
import { stats, titles } from '../data/mock';

const demoHealth: HealthItem[] = [
  {
    source: 'DownloadClientCheck',
    type: 'warning',
    message: 'Transmission is unavailable: unable to connect to 192.168.1.20:9091.',
  },
];

const icon = {
  error: <CircleAlert size={18} color="var(--danger)" />,
  warning: <TriangleAlert size={18} color="var(--warn)" />,
  notice: <Info size={18} color="var(--info)" />,
};

export default function System() {
  const issues = titles.reduce((n, t) => n + stats(t).have, 0);
  const [health, setHealth] = useState<HealthItem[]>(isConnected() ? [] : demoHealth);
  const [status, setStatus] = useState<Record<string, string | boolean> | null>(null);
  const [checking, setChecking] = useState(false);

  const load = async () => {
    if (!isConnected()) return;
    setChecking(true);
    try {
      const [h, s] = await Promise.all([api.health(), api.status()]);
      setHealth(h);
      setStatus(s);
    } finally {
      setChecking(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  return (
    <>
      <Toolbar>
        <Tool icon={RefreshCw} label="Run Health Check" onClick={load} spin={checking} />
        <Grow />
      </Toolbar>
      <div className="page">
        <h1 className="page-title">System</h1>
        <div className="stats">
          <div className="card stat">
            <div className="k">
              <BookOpen size={15} /> Titles
            </div>
            <div className="v">{titles.length}</div>
          </div>
          <div className="card stat">
            <div className="k">
              <Database size={15} /> Issues on disk
            </div>
            <div className="v">{issues}</div>
          </div>
          <div className="card stat">
            <div className="k">
              <HardDrive size={15} /> Mode
            </div>
            <div className="v">{isConnected() ? 'Live' : 'Demo'}</div>
          </div>
          <div className="card stat">
            <div className="k">
              <Info size={15} /> Version
            </div>
            <div className="v">{serverInfo()?.version ?? '0.1.0'}</div>
          </div>
        </div>

        <div className="card" style={{ marginBottom: 22 }}>
          <div className="card-head">Health</div>
          {health.length === 0 ? (
            <div className="health">
              <CheckCircle2 size={18} color="var(--ok)" />
              <div style={{ fontWeight: 600 }}>No issues found</div>
            </div>
          ) : (
            health.map((h) => (
              <div className="health" key={h.source + h.message}>
                {icon[h.type]}
                <div>
                  <div style={{ fontWeight: 600 }}>{h.message}</div>
                  <div style={{ color: 'var(--text-faint)', fontSize: 12 }}>{h.source}</div>
                </div>
              </div>
            ))
          )}
        </div>

        {status && (
          <div className="card">
            <div className="card-head">About</div>
            <div className="card-body">
              {[
                ['Version', status.version],
                ['App data', status.appData],
                ['Database', status.databaseType],
                ['.NET', status.runtimeVersion],
                ['OS', status.osName],
                ['Docker', status.isDocker ? 'Yes' : 'No'],
                ['Started', new Date(String(status.startTime)).toLocaleString()],
              ].map(([k, v]) => (
                <div className="form-row" key={String(k)} style={{ padding: '8px 0' }}>
                  <label style={{ padding: 0 }}>{k}</label>
                  <div className="mono">{String(v)}</div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </>
  );
}
