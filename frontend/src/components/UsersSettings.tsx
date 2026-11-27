import { Check, Copy, KeyRound, Link2, Shield, Trash2, TriangleAlert, UserPlus } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { currentUser, serverInfo, timeAgo, users, type InviteInfo, type UserInfo } from '../data/api';
import { Modal } from './Modal';
import { useToast } from './Toast';

const roleHelp = {
  reader: 'Can browse the library, read in the browser, download, and use OPDS and KOReader sync. Has their own Continue Reading. Can’t change anything.',
  admin: 'Full control, like you: settings, adding and deleting titles, searching, and managing users.',
};

function InviteDialog({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const toast = useToast();
  const [role, setRole] = useState<'reader' | 'admin'>('reader');
  const [note, setNote] = useState('');
  const [days, setDays] = useState(7);
  const [invite, setInvite] = useState<InviteInfo | null>(null);
  const [busy, setBusy] = useState(false);

  const create = async () => {
    setBusy(true);
    try {
      setInvite(await users.invite(role, note, days));
      onCreated();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not create the invite', 'error');
    } finally {
      setBusy(false);
    }
  };

  const url = invite ? users.inviteUrl(invite.token) : '';
  return (
    <Modal
      title={invite ? 'Invite link ready' : 'Invite someone'}
      icon={<UserPlus size={18} />}
      onClose={onClose}
      footer={
        invite ? (
          <button className="btn primary" onClick={onClose}>
            Done
          </button>
        ) : (
          <>
            <button className="btn" onClick={onClose}>
              Cancel
            </button>
            <button className="btn primary" onClick={create} disabled={busy}>
              <Link2 size={16} /> Create link
            </button>
          </>
        )
      }
    >
      {invite ? (
        <>
          <p style={{ marginTop: 0 }}>Send this link{invite.note ? ` to ${invite.note}` : ''}. They choose their own username and password. It works once and expires {new Date(invite.expires).toLocaleDateString(undefined, { dateStyle: 'medium' })}.</p>
          <div style={{ display: 'flex', gap: 8 }}>
            <input className="input mono" readOnly value={url} onFocus={(e) => e.target.select()} autoFocus />
            <button className="btn" onClick={() => navigator.clipboard?.writeText(url).then(() => toast('Invite link copied'))}>
              <Copy size={16} /> Copy
            </button>
          </div>
          <p className="help" style={{ marginBottom: 0 }}>
            The link uses the address you’re viewing Issuarr on now. If your friend is outside your home network, Issuarr needs to be reachable from the internet (for example through a reverse proxy), and you should send them that address instead of a local one.
          </p>
        </>
      ) : (
        <>
          <div className="form-row">
            <label>Who’s it for?</label>
            <div>
              <input className="input" placeholder="e.g. Sam (just a note for you)" value={note} onChange={(e) => setNote(e.target.value)} autoFocus />
            </div>
          </div>
          <div className="form-row">
            <label>Access</label>
            <div>
              <div className="segmented">
                <button className={role === 'reader' ? 'on' : ''} onClick={() => setRole('reader')}>
                  Reader
                </button>
                <button className={role === 'admin' ? 'on' : ''} onClick={() => setRole('admin')}>
                  Admin
                </button>
              </div>
              <div className="help">{roleHelp[role]}</div>
            </div>
          </div>
          <div className="form-row">
            <label>Link expires after</label>
            <div>
              <select className="input" style={{ maxWidth: 180 }} value={days} onChange={(e) => setDays(Number(e.target.value))}>
                {[1, 3, 7, 14, 30].map((d) => (
                  <option key={d} value={d}>
                    {d} {d === 1 ? 'day' : 'days'}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </>
      )}
    </Modal>
  );
}

function PasswordDialog({ user, onClose }: { user: UserInfo; onClose: () => void }) {
  const toast = useToast();
  const [password, setPassword] = useState('');
  const save = async () => {
    try {
      await users.update(user.id, { password });
      toast(`New password set for ${user.username}`);
      onClose();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not set the password', 'error');
    }
  };
  return (
    <Modal
      title={`Reset password · ${user.username}`}
      icon={<KeyRound size={18} />}
      onClose={onClose}
      footer={
        <>
          <button className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" onClick={save} disabled={password.length < 6}>
            Set password
          </button>
        </>
      }
    >
      <div className="form-row">
        <label>New password</label>
        <div>
          <input className="input" type="text" autoComplete="off" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus />
          <div className="help">At least 6 characters. Let {user.username} know it; they can change it in My Account.</div>
        </div>
      </div>
    </Modal>
  );
}

/** Settings → Users: accounts, roles and invite links. */
export function UsersSettings() {
  const toast = useToast();
  const me = currentUser();
  const [list, setList] = useState<UserInfo[] | null>(null);
  const [invites, setInvites] = useState<InviteInfo[]>([]);
  const [inviting, setInviting] = useState(false);
  const [resetting, setResetting] = useState<UserInfo | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<number | null>(null);

  const load = useCallback(() => {
    users.list().then(setList).catch((e) => toast(e.message, 'error'));
    users.invites().then(setInvites).catch(() => {});
  }, [toast]);
  useEffect(load, [load]);

  const auth = serverInfo()?.authentication ?? 'none';
  const setRole = async (u: UserInfo, role: 'admin' | 'reader') => {
    try {
      await users.update(u.id, { role });
      toast(`${u.username} is now ${role === 'admin' ? 'an admin' : 'a reader'}`);
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not change the role', 'error');
    }
  };
  const remove = async (u: UserInfo) => {
    try {
      await users.remove(u.id);
      toast(`Removed ${u.username}`);
      setConfirmDelete(null);
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Could not remove the user', 'error');
    }
  };

  return (
    <>
      {(auth === 'none' || auth === 'external') && (
        <div className="callout warn">
          <TriangleAlert size={18} />
          <div>
            <b>Login is turned off</b>, so everyone who opens Issuarr is treated as you, the owner. Turn on <b>Forms</b> login in Settings → General → Security before inviting anyone.
          </div>
        </div>
      )}

      <div style={{ display: 'flex', alignItems: 'center', gap: 12, margin: '4px 0 14px' }}>
        <p className="muted" style={{ margin: 0, flex: 1 }}>
          Invite friends and family to read your library. Each person gets their own login, Continue Reading, read badges, OPDS shelves and KOReader sync.
        </p>
        <button className="btn primary" onClick={() => setInviting(true)}>
          <UserPlus size={16} /> Invite
        </button>
      </div>

      <div className="card">
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>User</th>
                <th>Access</th>
                <th>Last seen</th>
                <th>KOReader sync</th>
                <th style={{ width: 90 }} />
              </tr>
            </thead>
            <tbody>
              {list?.map((u) => (
                <tr key={u.id}>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                      <span className="avatar-btn static">{u.username.slice(0, 1).toUpperCase()}</span>
                      <div>
                        <div style={{ fontWeight: 600 }}>
                          {u.username} {u.id === me?.id && <span className="muted">(you)</span>}
                        </div>
                        <div className="muted" style={{ fontSize: 12 }}>
                          {u.isOwner ? 'Owner · login set in General' : `Joined ${new Date(u.created).toLocaleDateString(undefined, { dateStyle: 'medium' })}`}
                        </div>
                      </div>
                    </div>
                  </td>
                  <td>
                    {u.isOwner ? (
                      <span className="pill accent">
                        <Shield size={11} /> Owner
                      </span>
                    ) : (
                      <select className="input sm" style={{ width: 120 }} value={u.role} disabled={u.id === me?.id} onChange={(e) => setRole(u, e.target.value as 'admin' | 'reader')} title={roleHelp[u.role]}>
                        <option value="reader">Reader</option>
                        <option value="admin">Admin</option>
                      </select>
                    )}
                  </td>
                  <td className="muted">{u.lastSeen ? timeAgo(u.lastSeen) : 'Never'}</td>
                  <td className="muted">{u.isOwner ? '—' : u.koSync ? <Check size={16} color="var(--ok)" /> : 'Not set up'}</td>
                  <td>
                    {!u.isOwner && u.id !== me?.id && (
                      <div style={{ display: 'flex', gap: 2, justifyContent: 'flex-end' }}>
                        {confirmDelete === u.id ? (
                          <>
                            <button className="btn sm danger" onClick={() => remove(u)}>
                              Remove
                            </button>
                            <button className="btn sm" onClick={() => setConfirmDelete(null)}>
                              Keep
                            </button>
                          </>
                        ) : (
                          <>
                            <button className="icon-btn" style={{ width: 30, height: 30 }} title="Reset password" onClick={() => setResetting(u)}>
                              <KeyRound size={15} />
                            </button>
                            <button className="icon-btn danger-hover" style={{ width: 30, height: 30 }} title={`Remove ${u.username}`} onClick={() => setConfirmDelete(u.id)}>
                              <Trash2 size={15} />
                            </button>
                          </>
                        )}
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {invites.length > 0 && (
        <>
          <h3 style={{ margin: '28px 0 12px' }}>Pending invites</h3>
          <div className="card">
            <div className="table-wrap">
              <table className="table">
                <tbody>
                  {invites.map((i) => (
                    <tr key={i.token}>
                      <td style={{ fontWeight: 600 }}>{i.note || 'Unnamed invite'}</td>
                      <td>
                        <span className="pill">{i.role === 'admin' ? 'Admin' : 'Reader'}</span>
                      </td>
                      <td className="muted">Expires {new Date(i.expires).toLocaleDateString(undefined, { dateStyle: 'medium' })}</td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                          <button className="btn sm" onClick={() => navigator.clipboard?.writeText(users.inviteUrl(i.token)).then(() => toast('Invite link copied'))}>
                            <Copy size={14} /> Copy link
                          </button>
                          <button
                            className="btn sm"
                            onClick={async () => {
                              await users.revoke(i.token);
                              toast('Invite cancelled');
                              load();
                            }}
                          >
                            Cancel
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}

      {inviting && <InviteDialog onClose={() => setInviting(false)} onCreated={load} />}
      {resetting && <PasswordDialog user={resetting} onClose={() => setResetting(null)} />}
    </>
  );
}
