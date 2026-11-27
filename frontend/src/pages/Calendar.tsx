import { CalendarDays, ChevronLeft, ChevronRight, Copy, Rss, Search } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Grow, Sep, Tool, Toolbar } from '../components/Toolbar';
import { Modal } from '../components/Modal';
import { useToast } from '../components/Toast';
import { calendarFeedUrl, isConnected } from '../data/api';
import { iso, titles, today, type Issue, type Title } from '../data/mock';
import { runCommand, useLibraryVersion } from '../data/store';

const dows = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

const key = iso;

export default function Calendar() {
  const libVersion = useLibraryVersion();
  const toast = useToast();
  const [feedOpen, setFeedOpen] = useState(false);
  const [searching, setSearching] = useState(false);
  const [month, setMonth] = useState(new Date(today.getFullYear(), today.getMonth(), 1));

  const events = useMemo(() => {
    const m = new Map<string, { title: Title; issue: Issue }[]>();
    titles.forEach((t) =>
      t.issues.forEach((i) => {
        m.set(i.releaseDate, [...(m.get(i.releaseDate) ?? []), { title: t, issue: i }]);
      }),
    );
    return m;
  }, [libVersion]);

  const days = useMemo(() => {
    const first = new Date(month);
    const offset = (first.getDay() + 6) % 7; // Monday first
    const start = new Date(first);
    start.setDate(first.getDate() - offset);
    return Array.from({ length: 42 }, (_, i) => {
      const d = new Date(start);
      d.setDate(start.getDate() + i);
      return d;
    });
  }, [month]);

  const shift = (n: number) => setMonth((m) => new Date(m.getFullYear(), m.getMonth() + n, 1));

  return (
    <>
      <Toolbar>
        <Tool icon={CalendarDays} label="Today" onClick={() => setMonth(new Date(today.getFullYear(), today.getMonth(), 1))} />
        <Tool
          icon={Search}
          label="Search Missing"
          spin={searching}
          onClick={async () => {
            if (!isConnected()) return toast('Not available with sample data.', 'error');
            setSearching(true);
            toast('Searching for missing issues…');
            try {
              toast((await runCommand('MissingSearch')).message ?? 'Done');
            } catch (e) {
              toast(e instanceof Error ? e.message : 'Search failed', 'error');
            } finally {
              setSearching(false);
            }
          }}
        />
        <Sep />
        <Tool icon={Rss} label="iCal Link" onClick={() => (isConnected() ? setFeedOpen(true) : toast('Not available with sample data.', 'error'))} />
        <Grow />
      </Toolbar>
      <div className="page">
        <h1 className="page-title" style={{ alignItems: 'center' }}>
          <button className="icon-btn" onClick={() => shift(-1)} aria-label="Previous month">
            <ChevronLeft size={20} />
          </button>
          {month.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}
          <button className="icon-btn" onClick={() => shift(1)} aria-label="Next month">
            <ChevronRight size={20} />
          </button>
        </h1>
        <div className="cal">
          {dows.map((d) => (
            <div className="cal-dow" key={d}>
              {d}
            </div>
          ))}
          {days.map((d) => {
            const k = key(d);
            const evs = events.get(k) ?? [];
            const dim = d.getMonth() !== month.getMonth();
            return (
              <div key={k} className={`cal-day ${dim ? 'dim' : ''} ${k === key(today) ? 'today' : ''}`}>
                <span className="d">{d.getDate()}</span>
                {evs.slice(0, 3).map(({ title, issue }) => (
                  <Link
                    key={issue.id}
                    to={`/title/${title.slug}`}
                    className={`cal-ev ${issue.status}`}
                    title={`${title.name} ${issue.number}`}
                  >
                    <b>{title.name}</b> {issue.number}
                  </Link>
                ))}
                {evs.length > 3 && <span style={{ fontSize: 11, color: 'var(--text-faint)' }}>+{evs.length - 3} more</span>}
              </div>
            );
          })}
        </div>
        <div className="legend">
          <span>
            <i style={{ background: 'var(--ok)' }} />
            Downloaded
          </span>
          <span>
            <i style={{ background: 'var(--danger)' }} />
            Missing
          </span>
          <span>
            <i style={{ background: 'var(--info)' }} />
            Downloading
          </span>
          <span>
            <i style={{ background: 'var(--accent)' }} />
            Upcoming
          </span>
          <span>
            <i style={{ background: 'var(--idle)' }} />
            Unmonitored
          </span>
        </div>
      </div>
      {feedOpen && (
        <Modal title="Calendar feed" icon={<Rss size={18} color="var(--accent)" />} onClose={() => setFeedOpen(false)}>
          <p style={{ marginTop: 0 }}>Subscribe to this URL in Google Calendar, Apple Calendar or Outlook to see upcoming issues.</p>
          <div style={{ display: 'flex', gap: 8 }}>
            <input className="input mono" style={{ maxWidth: 'none' }} readOnly value={calendarFeedUrl()} onFocus={(e) => e.target.select()} />
            <button className="btn" onClick={() => navigator.clipboard?.writeText(calendarFeedUrl()).then(() => toast('Copied'))}>
              <Copy size={16} />
            </button>
          </div>
          <p className="help">The link contains your API key, so only share it with calendars you trust. Add <span className="mono">&amp;unmonitored=true</span> to include unmonitored titles.</p>
        </Modal>
      )}
    </>
  );
}
