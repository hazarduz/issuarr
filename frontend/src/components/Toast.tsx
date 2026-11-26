import { CheckCircle2, CircleAlert } from 'lucide-react';
import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';

type Kind = 'success' | 'error';
const ToastCtx = createContext<(msg: string, kind?: Kind) => void>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [msg, setMsg] = useState<string | null>(null);
  const [kind, setKind] = useState<Kind>('success');
  const timer = useRef<number | undefined>(undefined);
  const show = useCallback((m: string, k: Kind = 'success') => {
    setMsg(m);
    setKind(k);
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setMsg(null), k === 'error' ? 5000 : 2600);
  }, []);
  return (
    <ToastCtx.Provider value={show}>
      {children}
      {msg && (
        <div className={`toast ${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
          {kind === 'error' ? <CircleAlert size={18} color="var(--danger)" /> : <CheckCircle2 size={18} color="var(--ok)" />}
          {msg}
        </div>
      )}
    </ToastCtx.Provider>
  );
}

export const useToast = () => useContext(ToastCtx);
