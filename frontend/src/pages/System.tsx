import { BookOpen, CheckCircle2, CircleAlert, Database, HardDrive, Info, Loader2, Play, RefreshCw, TriangleAlert } from 'lucide-react';
import { Link } from 'react-router-dom';
import { useToast } from '../components/Toast';
import { runCommand } from '../data/store';
import { useEffect, useState } from 'react';
import { Grow, Tool, Toolbar } from '../components/Toolbar';
import { api, isConnected, serverInfo, timeAgo, type Command, type HealthItem, type ScheduledTask } from '../data/api';
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
  const [tasks, setTasks] = useState<ScheduledTask[]>([]);
  const [commands, setCommands] = useState<Command[]>([]);
  const [running, setRunning] = useState<string | null>(null);
  const toast = useToast();

  useEffect(() => {
    if (!isConnected()) return;
    const poll = () => {
      api.tasks().then(setTasks).catch(() => {});
      api.commands().then(setCommands).catch(() => {});
    };
    poll();
    const t = setInterval(poll, 5000);
    return () => clearInterval(t);
  }, []);

  const runTask = async (name: string) => {
    setRunning(name);
    try {
      const cmd = await runCommand(name);
      toast(cmd.message ?? 'Done');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Failed', 'error');
    } finally {
      setRunning(null);
      api.tasks().then(setTasks);
    }
  };

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
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600 }}>{h.message}</div>
                  <div style={{ color: 'var(--text-faint)', fontSize: 12 }}>{h.source}</div>
                </div>
                {h.link && (
                  <Link className="btn sm" to={h.link}>
                    Fix
                  </Link>
                )}
              </div>
            ))
          )}
        </div>

        {isConnected() && (
          <div className="card table-wrap" style={{ marginBottom: 22 }}>
            <div className="card-head">Scheduled Tasks</div>
            <table className="table">
              <thead>
                <tr>
                  <th>Task</th>
                  <th>Every</th>
                  <th>Last run</th>
                  <th>Next run</th>
                  <th style={{ width: 60 }} />
                </tr>
              </thead>
              <tbody>
                {tasks.map((t) => (
                  <tr key={t.name}>
                    <td style={{ fontWeight: 600 }}>{t.label}</td>
                    <td className="muted">{t.interval >= 60 ? `${t.interval / 60} hours` : `${t.interval} min`}</td>
                    <td className="muted">{timeAgo(t.lastRun)}</td>
                    <td className="muted">{timeAgo(t.nextRun)}</td>
                    <td>
                      <button className="icon-btn" style={{ width: 30, height: 30 }} title="Run now" onClick={() => runTask(t.name)} disabled={running !== null}>
                        {running === t.name ? <Loader2 size={15} className="spin-icon" /> : <Play size={15} />}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {commands.length > 0 && (
          <div className="card table-wrap" style={{ marginBottom: 22 }}>
            <div className="card-head">Recent Jobs</div>
            <table className="table">
              <thead>
                <tr>
                  <th>Job</th>
                  <th>Status</th>
                  <th>Result</th>
                  <th>Started</th>
                  <th>Took</th>
                </tr>
              </thead>
              <tbody>
                {commands.slice(0, 15).map((c) => (
                  <tr key={c.id}>
                    <td>
                      {c.name.replace(/([a-z])([A-Z])/g, '$1 $2')} {!c.manual && <span className="pill">scheduled</span>}
                    </td>
                    <td>
                      <span className={`pill ${c.status === 'completed' ? 'ok' : c.status === 'failed' ? 'danger' : 'info'}`}>{c.status}</span>
                    </td>
                    <td className="muted" style={{ whiteSpace: 'normal' }}>
                      {c.message ?? ''}
                    </td>
                    <td className="muted">{timeAgo(c.started ?? c.queued)}</td>
                    <td className="muted num">{c.started && c.ended ? `${((new Date(c.ended).getTime() - new Date(c.started).getTime()) / 1000).toFixed(1)}s` : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

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
