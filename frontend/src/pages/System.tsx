import { BookOpen, CheckCircle2, Database, HardDrive, Info, RefreshCw, TriangleAlert } from 'lucide-react';
import { Grow, Tool, Toolbar } from '../components/Toolbar';
import { stats, titles } from '../data/mock';

export default function System() {
  const issues = titles.reduce((n, t) => n + stats(t).have, 0);
  return (
    <>
      <Toolbar>
        <Tool icon={RefreshCw} label="Run Health Check" />
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
              <HardDrive size={15} /> Free space
            </div>
            <div className="v">1.2 TB</div>
          </div>
          <div className="card stat">
            <div className="k">
              <Info size={15} /> Version
            </div>
            <div className="v">0.1.0</div>
          </div>
        </div>

        <div className="card">
          <div className="card-head">Health</div>
          <div className="health">
            <TriangleAlert size={18} color="var(--warn)" />
            <div>
              <div style={{ fontWeight: 600 }}>Transmission is unavailable</div>
              <div style={{ color: 'var(--text-muted)', fontSize: 13 }}>
                Unable to connect to 192.168.1.20:9091. Check the host and credentials in Download Clients.
              </div>
            </div>
          </div>
          <div className="health">
            <CheckCircle2 size={18} color="var(--ok)" />
            <div>
              <div style={{ fontWeight: 600 }}>Prowlarr sync healthy</div>
              <div style={{ color: 'var(--text-muted)', fontSize: 13 }}>6 indexers synced 4 minutes ago.</div>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
