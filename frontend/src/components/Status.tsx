import { Bookmark, BookmarkCheck } from 'lucide-react';
import type { IssueStatus, Title } from '../data/mock';

const statusMeta: Record<IssueStatus, { label: string; cls: string }> = {
  downloaded: { label: 'Downloaded', cls: 'ok' },
  missing: { label: 'Missing', cls: 'danger' },
  downloading: { label: 'Downloading', cls: 'info' },
  unaired: { label: 'Unreleased', cls: 'accent' },
  unmonitored: { label: 'Unmonitored', cls: '' },
};

export function StatusPill({ status }: { status: IssueStatus }) {
  const m = statusMeta[status];
  return <span className={`pill ${m.cls}`}>{m.label}</span>;
}

export function ProgressBar({
  have,
  total,
  title,
  large,
}: {
  have: number;
  total: number;
  title?: Title;
  large?: boolean;
}) {
  const pct = total ? Math.min(100, (have / total) * 100) : 0;
  let cls = 'partial';
  if (title && !title.monitored) cls = 'unmonitored';
  else if (pct >= 100) cls = title?.status === 'ended' ? 'ended' : '';
  return (
    <div className={`progress ${large ? 'lg' : ''}`} title={`${have} / ${total} issues`}>
      <div className={`fill ${cls}`} style={{ width: `${pct}%` }} />
      {large && (
        <div className="label">
          {have} / {total}
        </div>
      )}
    </div>
  );
}

export function MonitorToggle({ on, onClick }: { on: boolean; onClick?: () => void }) {
  return (
    <button className={`mon-toggle ${on ? 'on' : ''}`} onClick={onClick} title={on ? 'Monitored' : 'Unmonitored'}>
      {on ? <BookmarkCheck size={17} /> : <Bookmark size={17} />}
    </button>
  );
}
