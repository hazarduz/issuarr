import { useEffect, useState } from 'react';
import { covers, isConnected, mediaUrl } from '../data/api';
import type { Issue, Title } from '../data/mock';

export const fileCoverFormats = ['CBZ', 'CBR', 'CB7', 'EPUB', 'PDF'];

/** Image sources to try, best first: chosen/metadata cover, then the first file's own cover. */
function sources(title: Title): string[] {
  const list: string[] = [];
  if (title.coverUrl) list.push(mediaUrl(title.coverUrl)!);
  if (isConnected()) {
    const first = [...title.issues]
      .filter((i) => i.file && i.serverId != null && fileCoverFormats.includes(i.file.format))
      .sort((a, b) => a.releaseDate.localeCompare(b.releaseDate))[0];
    if (first) list.push(covers.issueCoverUrl(first.serverId!));
  }
  return list;
}

/**
 * Shows the real cover when there is one (custom, metadata, or the first file's own cover),
 * otherwise a generated, printed-looking placeholder built from the title's palette.
 */
export function Cover({ title, issue, size = 'md' }: { title: Title; issue?: string; size?: 'sm' | 'md' | 'lg' }) {
  const list = sources(title);
  const key = list.join('|');
  const [index, setIndex] = useState(0);
  useEffect(() => setIndex(0), [key]);
  const [a, b] = title.palette;

  if (index < list.length) {
    return (
      <div className={`cover has-image ${title.kind}`} style={{ background: `linear-gradient(155deg, ${a} 0%, ${b} 100%)` }}>
        <img src={list[index]} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setIndex((i) => i + 1)} />
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
        <span>{issue ?? (title.kind === 'comic' ? `#${title.issues.length}` : title.kind === 'book' ? `${title.issues.length} books` : title.frequency)}</span>
      </div>
    </div>
  );
}

/**
 * One issue's/book's own cover: the metadata cover, else the file's cover (first page, embedded
 * EPUB cover, or PDF page 1, rendered and cached by the server), else a generated placeholder.
 */
export function IssueCover({ title, issue, size = 'md' }: { title: Title; issue: Issue; size?: 'sm' | 'md' }) {
  const list: string[] = [];
  if (issue.coverUrl) list.push(mediaUrl(issue.coverUrl)!);
  if (isConnected() && issue.file && issue.serverId != null && fileCoverFormats.includes(issue.file.format)) list.push(covers.issueCoverUrl(issue.serverId));
  const key = list.join('|');
  const [index, setIndex] = useState(0);
  useEffect(() => setIndex(0), [key]);
  const [a, b] = title.palette;

  if (index < list.length) {
    return (
      <div className={`cover has-image ${title.kind}`} style={{ background: `linear-gradient(155deg, ${a} 0%, ${b} 100%)` }}>
        <img src={list[index]} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setIndex((i) => i + 1)} />
      </div>
    );
  }
  return <Cover title={{ ...title, coverUrl: undefined, issues: [] }} issue={issue.number} size={size} />;
}
