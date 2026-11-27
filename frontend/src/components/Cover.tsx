import { useState } from 'react';
import type { Title } from '../data/mock';

/**
 * Shows the real cover when the metadata source has one, otherwise a
 * generated, printed-looking placeholder built from the title's palette.
 */
export function Cover({ title, issue, size = 'md' }: { title: Title; issue?: string; size?: 'sm' | 'md' | 'lg' }) {
  const [failed, setFailed] = useState(false);
  const [a, b] = title.palette;

  if (title.coverUrl && !failed) {
    return (
      <div className={`cover has-image ${title.kind}`} style={{ background: `linear-gradient(155deg, ${a} 0%, ${b} 100%)` }}>
        <img src={title.coverUrl} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} />
      </div>
    );
  }

  const fontSize = size === 'lg' ? 30 : size === 'sm' ? 12 : title.name.length > 14 ? 19 : 23;
  return (
    <div className={`cover ${title.kind}`} style={{ background: `linear-gradient(155deg, ${a} 0%, ${b} 100%)` }}>
      <span className="shape" style={{ width: '90%', height: '60%', right: '-35%', bottom: '8%', background: b }} />
      <span className="shape" style={{ width: '55%', height: '40%', left: '-15%', top: '35%', background: a }} />
      <div className="masthead" style={{ fontSize }}>
        {title.name}
      </div>
      <div className="cover-foot">
        <span>{title.publisher}</span>
        <span>{issue ?? (title.kind === 'comic' ? `#${title.issues.length}` : title.frequency)}</span>
      </div>
    </div>
  );
}
