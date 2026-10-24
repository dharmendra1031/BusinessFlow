import { useEffect, useState } from 'react';
import { api } from '../api';
import { clearFieldError, handleDialogKeyDown, handleFormKeyDown, validateForm } from '../formUtils';
import { Icon } from '../Icon';
import { useFeedback } from '../Feedback';
import EmployeeManager from '../EmployeeManager';
import type { Employee, Paged, User } from '../types';
import { EmptyState, ErrorState, useResource } from './Shared';

export function EmployeesPage({ token, user }: { token: string; user: User }) {
  const { notify } = useFeedback();
  const [directorySearch, setDirectorySearch] = useState('');
  const [directoryRole, setDirectoryRole] = useState('');
  const [activeFilter, setActiveFilter] = useState('');
  const [directoryPage, setDirectoryPage] = useState(1);
  const params = new URLSearchParams({ search: directorySearch, role: directoryRole, page: String(directoryPage), pageSize: '20' });
  if (activeFilter) params.set('isActive', activeFilter);
  const { data: directory, refresh, error } = useResource<Paged<Employee>>('/api/employees?' + params, token);
  const { data: lookup } = useResource<Paged<Employee>>('/api/employees/lookup?action=' +
    (user.roles.includes('Admin') ? 'employees.create' : 'employees.view') + '&pageSize=200', token);
  const data = directory?.items;
  const managers = lookup?.items ?? [];
  const [showForm, setShowForm] = useState(false);
  const [selectedEmployee, setSelectedEmployee] = useState<Employee | null>(null);
  const [working, setWorking] = useState(false);
  const [fullName, setFullName] = useState('');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState('Employee');
  const [managerId, setManagerId] = useState('');

  useEffect(() => {
    if (!showForm) return;
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !working) setShowForm(false);
    }
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [showForm, working]);

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!validateForm(event.currentTarget) || working) return;
    setWorking(true);
    try {
      await api('/api/employees', token, { method: 'POST', body: JSON.stringify({
        fullName, email, password, role, phoneNumber, managerId: managerId ? Number(managerId) : null,
      }) });
      setFullName(''); setPhoneNumber(''); setEmail(''); setPassword(''); setManagerId('');
      setShowForm(false);
      await refresh();
      notify('success', 'Employee created', fullName + ' can now access the CRM.');
    } catch (err) {
      notify('error', 'Could not add employee', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  const canCreate = user.roles.includes('Admin');
  return <section><header className="page-header"><div><span className="section-kicker">YOUR PEOPLE</span>
    <h1>Team members</h1><p>People responsible for keeping leads on track.</p></div>
    {canCreate && <button className="primary" onClick={() => setShowForm(true)}><Icon name="plus" size={18} /> Add employee</button>}
  </header>
    <div className="table-toolbar">
      <label>Search employees<input maxLength={150} value={directorySearch} onChange={event => { setDirectorySearch(event.target.value); setDirectoryPage(1); }} /></label>
      <label>Employee role<input maxLength={80} placeholder="All roles" value={directoryRole} onChange={event => { setDirectoryRole(event.target.value); setDirectoryPage(1); }} /></label>
      <label>Employee status<select value={activeFilter} onChange={event => { setActiveFilter(event.target.value); setDirectoryPage(1); }}>
        <option value="">All statuses</option><option value="true">Active</option><option value="false">Inactive</option></select></label>
    </div>
    {error && <ErrorState message={error} retry={() => void refresh()} />}
    <section className="panel team-panel">
      <div className="panel-heading"><div><h2>Team directory</h2><p>{directory?.total ?? 0} matching members</p></div></div>
      {data?.length ? <div className="table-scroll"><table><thead><tr><th>Employee</th><th>Email</th>
        <th>Reporting manager</th><th>Status</th><th /></tr></thead><tbody>
        {data.map(member => <tr key={member.id}><td><div className="person-cell">
          <span className="avatar avatar-person">{member.fullName.charAt(0).toUpperCase()}</span>
          <strong>{member.fullName}</strong></div></td>
          <td>{member.email}</td><td>{managers.find(person => person.id === member.managerId)?.fullName || '—'}</td>
          <td>{member.isActive ? 'Active' : 'Inactive'}</td>
          <td>{canCreate && <button className="text-action" onClick={() => setSelectedEmployee(member)}>Manage</button>}</td>
        </tr>)}</tbody></table></div> : <EmptyState title="No employees found" detail="Team members will appear here." />}
    </section>
    <div className="table-toolbar"><button disabled={directoryPage === 1} onClick={() => setDirectoryPage(current => current - 1)}>Previous employees</button>
      <span>Page {directoryPage} ? {directory?.total ?? 0} employees</span>
      <button disabled={directoryPage * 20 >= (directory?.total ?? 0)} onClick={() => setDirectoryPage(current => current + 1)}>Next employees</button></div>
    {showForm && <div className="drawer-backdrop" onMouseDown={event => {
      if (event.target === event.currentTarget && !working) setShowForm(false);
    }}><aside className="detail-drawer" role="dialog" aria-modal="true" aria-labelledby="employee-dialog-title"
      onKeyDown={event => handleDialogKeyDown(event, () => { if (!working) setShowForm(false); })}>
      <div className="drawer-header"><div><span className="section-kicker">TEAM MANAGEMENT</span>
        <h2 id="employee-dialog-title">Add employee</h2><p>Set up an account for a team member.</p></div>
        <button className="icon-button" aria-label="Close form" onClick={() => setShowForm(false)}>
          <Icon name="close" /></button></div>
      <form className="drawer-form" onSubmit={save} onInput={clearFieldError}
        onKeyDown={handleFormKeyDown}>
        <label>Full name <span className="required">*</span><input autoFocus required minLength={2} maxLength={150}
          value={fullName} onChange={event => setFullName(event.target.value)} /></label>
        <label>Mobile<input type="tel" autoComplete="tel" maxLength={30}
          title="Enter 7 to 15 digits or leave blank." value={phoneNumber} onChange={event => setPhoneNumber(event.target.value)} /></label>
        <label>Email <span className="required">*</span><input type="email" required maxLength={256} autoComplete="email"
          value={email} onChange={event => setEmail(event.target.value)} /></label>
        <label>Temporary password <span className="required">*</span><input type="password" required minLength={12} maxLength={128}
          pattern="(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{12,}"
          title="Use at least 12 characters with uppercase, lowercase, number and symbol."
          autoComplete="new-password" value={password} onChange={event => setPassword(event.target.value)} /></label>
        <p className="field-help">Use 12+ characters with upper/lowercase letters, numbers and symbols.</p>
        <label>Role<select value={role} onChange={event => setRole(event.target.value)}>
          <option>Employee</option><option>Manager</option></select></label>
        <label>Reporting manager<select value={managerId} onChange={event => setManagerId(event.target.value)}>
          <option value="">None</option>
          {managers.filter(member => member.roles?.includes('Manager')).map(member =>
            <option key={member.id} value={member.id}>{member.fullName}</option>)}
        </select></label>
        <div className="drawer-actions"><button type="button" disabled={working} onClick={() => setShowForm(false)}>Cancel</button>
          <button className="primary" disabled={working}>{working ? 'Creating…' : 'Create employee'}</button></div>
      </form>
    </aside></div>}
    {selectedEmployee && <EmployeeManager employee={selectedEmployee} team={managers}
      token={token} onClose={() => setSelectedEmployee(null)} onSaved={refresh} />}
  </section>;
}
