import { useEffect, useState } from 'react';
import { api, isConnected, reloadTitles, type Command } from './api';

/**
 * Tiny change-notification hub: anything that changes the library calls
 * notifyLibrary(), and components using useLibraryVersion() re-render.
 */
const listeners = new Set<() => void>();
let version = 0;

export function notifyLibrary() {
  version++;
  listeners.forEach((l) => l());
}

export function useLibraryVersion() {
  const [, setV] = useState(version);
  useEffect(() => {
    const l = () => setV(version);
    listeners.add(l);
    return () => {
      listeners.delete(l);
    };
  }, []);
  return version;
}

export async function refreshLibrary() {
  if (!isConnected()) return;
  await reloadTitles();
  notifyLibrary();
}

/**
 * Starts a background command and resolves when it finishes, then reloads the
 * library so the result (new issues, grabbed downloads, renamed files) shows up.
 */
export async function runCommand(name: string, titleId?: number | string, issueIds?: number[]): Promise<Command> {
  let cmd = await api.command(name, titleId, issueIds);
  const started = Date.now();
  while (cmd.status === 'queued' || cmd.status === 'started') {
    await new Promise((r) => setTimeout(r, 1000));
    cmd = await api.getCommand(cmd.id);
    if (Date.now() - started > 10 * 60 * 1000) break;
  }
  await refreshLibrary().catch(() => {});
  if (cmd.status === 'failed') throw new Error(cmd.message ?? `${name} failed`);
  return cmd;
}
