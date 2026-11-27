import { Copy, KeyRound, LogOut, Tablet } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useToast } from '../components/Toast';
import { currentUser, kosyncUrl, opdsUrl, serverInfo, users, type UserInfo } from '../data/api';

function CopyField({ value, what }: { value: string; what: string }) {
  const toast = useToast();
  return (
    <div style={{ display: 'flex', gap: 8, maxWidth: 560 }}>
      <input className="input mono" readOnly value={value} onFocus={(e) => e.target.select()} />
      <button className="btn" title="Copy" onClick={() => navigator.clipboard?.writeText(value).then(() => toast(`${what} copied`))}>
        <Copy size={16} />
      </button>
    </div>
  );
}

/** My Account: your password, and how to connect your e-reader. */
export default function Account() {
  const toast = useToast();
  const me = currentUser();
  const [info, setInfo] = useState<{ user: UserInfo; koSyncEnabled: boolean } | null>(null);
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [sync, setSync] = useState('');

  const load = () => users.me().then(setInfo).catch(() => {});
  useEffect(() => {
    load();
  }, []);

  if (!me) return null;
  const changePassword = async () => {
    if (next !== confirm) return toast('The new passwords don’t match', 'error');
    try {
      await users.changePassword(current, next);
      setCurrent('');
      setNext('');
      setConfirm('');
      toast('Password changed');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not change the password', 'error');
    }
  };
  const saveSync = async (value: string | null) => {
    try {
      await users.setKoSyncPassword(value);
      setSync('');
      await load();
      toast(value ? 'KOReader sync password set' : 'KOReader sync turned off for your account');
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not save', 'error');
    }
  };

  const base = serverInfo()?.urlBase ?? '';
  return (
    <div className="page" style={{ maxWidth: 900 }}>
      <h1 className="page-title">My Account</h1>

      <div className="card card-body account-card">
        <span className="avatar-btn static large">{me.username.slice(0, 1).toUpperCase()}</span>
        <div style={{ flex: 1 }}>
          <div style={{ fontWeight: 700, fontSize: 18 }}>{me.username}</div>
          <div className="muted">{me.isOwner ? 'Owner' : me.role === 'admin' ? 'Admin' : 'Reader: browse and read the library'}</div>
        </div>
        {me.loggedIn && (
          <a className="btn" href={`${base}/logout`}>
            <LogOut size={16} /> Log out
          </a>
        )}
      </div>

      {me.loggedIn && (
        <>
          <h3 style={{ margin: '28px 0 12px' }}>Password</h3>
          <div className="card card-body">
            <div className="form-row">
              <label>Current password</label>
              <div>
                <input className="input" type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} style={{ maxWidth: 320 }} />
              </div>
            </div>
            <div className="form-row">
              <label>New password</label>
              <div>
                <input className="input" type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} style={{ maxWidth: 320 }} />
                <div className="help">At least 6 characters.</div>
              </div>
            </div>
            <div className="form-row">
              <label>Confirm</label>
              <div>
                <input className="input" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} style={{ maxWidth: 320 }} />
              </div>
            </div>
            <div className="form-row">
              <label />
              <div>
                <button className="btn primary" disabled={!current || next.length < 6} onClick={changePassword}>
                  <KeyRound size={16} /> Change password
                </button>
              </div>
            </div>
          </div>
        </>
      )}

      <h3 style={{ margin: '28px 0 12px' }}>
        <Tablet size={17} style={{ verticalAlign: -3 }} /> E-readers (Kobo, KOReader…)
      </h3>
      <div className="card card-body">
        <div className="form-row">
          <label>OPDS catalog</label>
          <div>
            <CopyField value={opdsUrl(false)} what="OPDS address" />
            <div className="help">
              In KOReader: <b>Search → OPDS catalog → +</b>, add this address with your Issuarr username and password. Your Continue Reading shelf is in there too.
            </div>
          </div>
        </div>
        {info?.koSyncEnabled ? (
          <>
            <div className="form-row">
              <label>KOReader sync</label>
              <div>
                <CopyField value={kosyncUrl()} what="Sync address" />
                <div className="help">
                  With a book open in KOReader: <b>Tools → Progress sync → Custom sync server</b>, enter this address, then <b>Login</b> with your Issuarr username (<b>{me.username}</b>) and the sync password below. Turn on <b>Auto sync</b>.
                </div>
              </div>
            </div>
            <div className="form-row">
              <label>Sync password</label>
              <div>
                <div style={{ display: 'flex', gap: 8, maxWidth: 420 }}>
                  <input className="input" type="password" autoComplete="new-password" placeholder={info.user.koSync ? '•••••••• (set)' : 'Choose one'} value={sync} onChange={(e) => setSync(e.target.value)} />
                  <button className="btn primary" disabled={sync.length < 4} onClick={() => saveSync(sync)}>
                    Save
                  </button>
                  {info.user.koSync && (
                    <button className="btn" onClick={() => saveSync(null)}>
                      Turn off
                    </button>
                  )}
                </div>
                <div className="help">
                  A separate password just for KOReader, because KOReader sends it in a weakly protected form. Don’t reuse your login password.
                  {me.isOwner && ' (Your original sync login in Settings → General keeps working too.)'}
                </div>
              </div>
            </div>
          </>
        ) : (
          <div className="form-row">
            <label>KOReader sync</label>
            <div className="help" style={{ marginTop: 8 }}>
              {me.role === 'admin' ? 'Turn on KOReader Sync in Settings → General to sync reading positions with KOReader.' : 'Not turned on for this Issuarr. Ask the owner if you’d like your Kobo to sync your place.'}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
