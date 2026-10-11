import { useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';
import { useFeedback } from './Feedback';
import type { Employee } from './types';

export default function EmployeeManager({ employee, team, token, onClose, onSaved }: {
  employee: Employee;
  team: Employee[];
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const { notify, confirm } = useFeedback();
  const isAdmin = employee.roles?.includes('Admin') ?? false;
  const [fullName, setFullName] = useState(employee.fullName);
  const [phoneNumber, setPhoneNumber] = useState(employee.phoneNumber ?? '');
  const [role, setRole] = useState(employee.roles?.includes('Manager') ? 'Manager' : 'Employee');
  const [managerId, setManagerId] = useState(employee.managerId?.toString() ?? '');
  const [newPassword, setNewPassword] = useState('');
  const [working, setWorking] = useState(false);

  async function save(event: React.FormEvent) {
    event.preventDefault();
    setWorking(true);
    try {
      await api('/api/admin/users/' + employee.id, token, { method: 'PUT',
        body: JSON.stringify({ fullName, phoneNumber: phoneNumber || null,
          role: isAdmin ? null : role, managerId: managerId ? Number(managerId) : null })
      });
      await onSaved();
      notify('success', 'Employee updated');
      onClose();
    } catch (err) {
      notify('error', 'Update failed', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function toggleStatus() {
    const accepted = await confirm({
      title: (employee.isActive ? 'Deactivate' : 'Activate') + ' employee?',
      message: employee.fullName,
      confirmText: employee.isActive ? 'Deactivate' : 'Activate',
      danger: employee.isActive
    });
    if (!accepted) return;
    setWorking(true);
    try {
      await api('/api/admin/users/' + employee.id + '/status', token,
        { method: 'PATCH', body: JSON.stringify(!employee.isActive) });
      await onSaved();
      notify('success', 'Employee status updated');
      onClose();
    } catch (err) {
      notify('error', 'Status update failed', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function resetPassword(event: React.FormEvent) {
    event.preventDefault();
    setWorking(true);
    try {
      await api('/api/password/reset/' + employee.id, token, { method: 'POST',
        body: JSON.stringify({ newPassword }) });
      setNewPassword('');
      notify('success', 'Password reset', 'Ask the employee to sign in again.');
    } catch (err) {
      notify('error', 'Password reset failed', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  return <div className="drawer-backdrop">
    <aside className="detail-drawer" role="dialog" aria-modal="true" aria-labelledby="manage-employee-title">
      <div className="drawer-header"><div><span className="section-kicker">USER MANAGEMENT</span>
        <h2 id="manage-employee-title">{employee.fullName}</h2><p>{employee.email}</p></div>
        <button className="icon-button" onClick={onClose} disabled={working} aria-label="Close"><Icon name="close" /></button>
      </div>
      <form className="drawer-form" onSubmit={save}>
        <label>Full name<input required value={fullName} onChange={event => setFullName(event.target.value)} /></label>
        <label>Mobile<input value={phoneNumber} onChange={event => setPhoneNumber(event.target.value)} /></label>
        {!isAdmin && <label>Role<select value={role} onChange={event => setRole(event.target.value)}>
          <option>Employee</option><option>Manager</option></select></label>}
        <label>Reporting manager<select value={managerId} onChange={event => setManagerId(event.target.value)}>
          <option value="">None</option>{team.filter(member => member.isActive && member.id !== employee.id &&
            member.roles?.includes('Manager')).map(member =>
            <option key={member.id} value={member.id}>{member.fullName}</option>)}
        </select></label>
        <div className="drawer-actions"><button type="button" onClick={() => void toggleStatus()} disabled={working}>
          {employee.isActive ? 'Deactivate' : 'Activate'}</button>
          <button className="primary" disabled={working}>Save changes</button>
        </div>
      </form>
      <form className="drawer-form" onSubmit={resetPassword}>
        <h3>Reset password</h3>
        <label>New password<input type="password" minLength={12} autoComplete="new-password" required
          value={newPassword} onChange={event => setNewPassword(event.target.value)} /></label>
        <p className="field-help">At least 12 characters, uppercase, lowercase, digit and symbol.</p>
        <button className="secondary-action" disabled={working || !newPassword}>Reset password</button>
      </form>
    </aside>
  </div>;
}
