import { useState } from 'react';
import { api } from './api';
import { clearFieldError, handleFormKeyDown, validateForm } from './formUtils';
import { useFeedback } from './Feedback';

export default function PasswordSettings({ token, onChanged }: { token: string; onChanged: () => void }) {
  const { notify } = useFeedback();
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [working, setWorking] = useState(false);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (!validateForm(event.currentTarget) || working) return;
    if (newPassword !== confirmPassword) {
      notify('warning', 'Passwords do not match', 'Enter the same new password twice.');
      return;
    }
    setWorking(true);
    try {
      await api('/api/password/change', token, { method: 'POST',
        body: JSON.stringify({ currentPassword, newPassword }) });
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      onChanged();
      notify('success', 'Password changed', 'Sign in with your new password.');
    } catch (err) {
      notify('error', 'Unable to change password', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">ACCOUNT</span>
      <h1>Change password</h1></div></header>
    <section className="panel">
      <form className="drawer-form" onSubmit={save} onInput={clearFieldError}
        onKeyDown={handleFormKeyDown}>
        <label>Current password<input type="password" required autoComplete="current-password"
          value={currentPassword} onChange={event => setCurrentPassword(event.target.value)} /></label>
        <label>New password<input type="password" required minLength={12} maxLength={128} autoComplete="new-password"
          pattern="(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{12,}"
          title="Use at least 12 characters with uppercase, lowercase, number and symbol."
          value={newPassword} onChange={event => setNewPassword(event.target.value)} /></label>
        <label>Confirm new password<input type="password" required maxLength={128} autoComplete="new-password"
          value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} /></label>
        <p className="field-help">At least 12 characters with uppercase, lowercase, a number and a symbol.</p>
        <button className="primary" disabled={working}>{working ? 'Saving…' : 'Change password'}</button>
      </form>
    </section>
  </section>;
}
