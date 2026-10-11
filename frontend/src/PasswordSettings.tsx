import { useState } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';

export default function PasswordSettings({ token }: { token: string }) {
  const { notify } = useFeedback();
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [working, setWorking] = useState(false);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    setWorking(true);
    try {
      await api('/api/password/change', token, { method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }) });
      setCurrentPassword('');
      setNewPassword('');
      notify('success', 'Password changed', 'Please sign in again when your session expires.');
    } catch (err) {
      notify('error', 'Unable to change password', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">ACCOUNT</span>
      <h1>Change password</h1></div></header>
    <section className="panel">
      <form className="drawer-form" onSubmit={save}>
        <label>Current password<input type="password" required autoComplete="current-password"
          value={currentPassword} onChange={event => setCurrentPassword(event.target.value)} /></label>
        <label>New password<input type="password" required minLength={12} autoComplete="new-password"
          value={newPassword} onChange={event => setNewPassword(event.target.value)} /></label>
        <p className="field-help">At least 12 characters with uppercase, lowercase, a number and a symbol.</p>
        <button className="primary" disabled={working}>{working ? 'Saving…' : 'Change password'}</button>
      </form>
    </section>
  </section>;
}
