import { useState } from 'react';

/** A cover image that quietly falls back to a blank placeholder if it can't load. */
export function SafeImg({ src }: { src?: string }) {
  const [failed, setFailed] = useState(false);
  if (!src || failed) return <div style={{ aspectRatio: '2/3', background: 'var(--surface-2)', borderRadius: 6 }} />;
  return <img src={src} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} />;
}
