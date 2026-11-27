import { Loader2, Pin } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { api, canManage, isConnected } from '../data/api';
import { parseDate, sourceName, type Issue, type Title } from '../data/mock';
import { notifyLibrary } from '../data/store';
import { useToast } from './Toast';

/**
 * The release date in an issue/book row. Click it to type a date or pick one from the
 * calendar; Enter or clicking away saves, Esc cancels. Dates set by hand are pinned so
 * metadata refreshes don't overwrite them; click the pin to unpin.
 */
export function ReleaseDateCell({ title, issue, onChanged }: { title: Title; issue: Issue; onChanged?: (date: string) => void }) {
  const toast = useToast();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(issue.releaseDate);
  const [saving, setSaving] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const cancelled = useRef(false);

  useEffect(() => setValue(issue.releaseDate), [issue.releaseDate]);
  useEffect(() => {
    if (editing) {
      input.current?.focus();
      cancelled.current = false;
    }
  }, [editing]);

  const reload = async () => {
    await api.loadTitle(title.id);
    notifyLibrary();
  };

  const save = async () => {
    setEditing(false);
    if (cancelled.current || !value || value === issue.releaseDate) {
      setValue(issue.releaseDate);
      return;
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      toast('That isn’t a complete date', 'error');
      setValue(issue.releaseDate);
      return;
    }
    setSaving(true);
    try {
      await api.setReleaseDate(issue.serverId!, value);
      onChanged?.(value);
      await reload();
      toast(`${issue.number}: release date set to ${parseDate(value).toLocaleDateString(undefined, { dateStyle: 'medium' })}`);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not save the date', 'error');
      setValue(issue.releaseDate);
    } finally {
      setSaving(false);
    }
  };

  const unpin = async () => {
    setSaving(true);
    try {
      await api.setReleaseDate(issue.serverId!, null);
      await reload();
      toast(title.metadataSource ? `Unpinned. The next refresh uses ${sourceName[title.metadataSource] ?? 'the metadata'}'s date.` : 'Unpinned');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Failed', 'error');
    } finally {
      setSaving(false);
    }
  };

  if (editing) {
    return (
      <input
        ref={input}
        className="input sm date-edit"
        type="date"
        value={value}
        max="2100-12-31"
        onChange={(e) => setValue(e.target.value)}
        onBlur={save}
        onKeyDown={(e) => {
          if (e.key === 'Enter') input.current?.blur();
          if (e.key === 'Escape') {
            cancelled.current = true;
            input.current?.blur();
          }
        }}
        aria-label="Release date"
      />
    );
  }

  const label = parseDate(issue.releaseDate).toLocaleDateString(undefined, { dateStyle: 'medium' });
  if (!isConnected() || issue.serverId == null) return <span>{label}</span>;

  if (!canManage()) return <span className="date-cell">{label}</span>;

  return (
    <span className="date-cell">
      <button className="date-button" onClick={() => setEditing(true)} title="Click to change the release date">
        {label}
      </button>
      {saving && <Loader2 size={13} className="spin-icon" />}
      {!saving && issue.releaseDateLocked && (
        <button className="date-pin" onClick={unpin} title="Set by you, so refreshes won’t change it. Click to unpin.">
          <Pin size={12} />
        </button>
      )}
    </span>
  );
}
