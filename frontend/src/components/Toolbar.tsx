import type { LucideIcon } from 'lucide-react';
import type { ReactNode } from 'react';

export function Toolbar({ children }: { children: ReactNode }) {
  return <div className="toolbar">{children}</div>;
}

export function Tool({
  icon: Icon,
  label,
  onClick,
  on,
  spin,
}: {
  icon: LucideIcon;
  label: string;
  onClick?: () => void;
  on?: boolean;
  spin?: boolean;
}) {
  return (
    <button className={`tool ${on ? 'on' : ''} ${spin ? 'spin' : ''}`} onClick={onClick}>
      <Icon size={19} strokeWidth={1.8} />
      {label}
    </button>
  );
}

export const Sep = () => <span className="sep" />;
export const Grow = () => <span className="grow" />;
