import type { Title } from '../data/mock';

/**
 * Generated placeholder cover. Real covers will come from metadata
 * providers; until then every title gets a distinctive, printed-looking
 * card built from its palette.
 */
export function Cover({ title, issue, size = 'md' }: { title: Title; issue?: string; size?: 'sm' | 'md' | 'lg' }) {
  const [a, b] = title.palette;
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
