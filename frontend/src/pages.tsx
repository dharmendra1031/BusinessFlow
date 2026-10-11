import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
import LeadDetails from './LeadDetails';
import type { Dashboard, Employee, FollowUp, Lead, Lookup, User } from './types';

function Message({ error }: { error: string }) {
  return error ? <p className="error" role="alert">{error}</p> : null;
}

function useResource<T>(path: string, token: string) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState('');
  const refresh = useCallback(async () => {
    if (!path) return;
    try { setData(await api<T>(path, token)); setError(''); }
    catch (err) { setError(err instanceof Error ? err.message : 'Could not load data'); }
  }, [path, token]);
  useEffect(() => { void refresh(); }, [refresh]);
  return { data, error, refresh };
}

export function DashboardPage({ token }: { token: string }) {
  const { data, error, refresh } = useResource<Dashboard>('/api/dashboard', token);
  const metrics = [
    ['Total Leads', data?.totalLeads], ['New Leads', data?.newLeads],
    ['Won Leads', data?.wonLeads], ['Lost Leads', data?.lostLeads],
    ['Overdue Follow-ups', data?.overdueFollowUps],
  ];
  return <section><header className="page-header"><div><h1>Dashboard</h1>
    <p>Your team's lead overview</p></div><button onClick={() => void refresh()}>Refresh</button></header>
    <Message error={error} /><div className="metrics">{metrics.map(([name, value]) =>
      <div className="metric" key={name}><span>{name}</span><strong>{value ?? '—'}</strong></div>)}</div></section>;
}

export function LeadsPage({ token, user }: { token: string; user: User }) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState(false);
  const [selectedLead, setSelectedLead] = useState<Lead | null>(null);
  const [error, setError] = useState('');
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [companyName, setCompanyName] = useState('');
  const [requirement, setRequirement] = useState('');
  const [sourceId, setSourceId] = useState(1);
  const { data, refresh, error: loadError } = useResource<{ items: Lead[]; total: number }>(
    `/api/leads?page=${page}&pageSize=20&search=${encodeURIComponent(search)}`, token);
  const { data: statuses } = useResource<Lookup[]>('/api/masters/lead-statuses', token);
  const { data: sources } = useResource<Lookup[]>('/api/masters/lead-sources', token);
  const canAssign = user.roles.includes('Admin') || user.roles.includes('Manager');
  const { data: employees } = useResource<Employee[]>(canAssign ? '/api/employees' : '', token);

  async function saveLead(event: React.FormEvent) {
    event.preventDefault();
    try {
      await api('/api/leads', token, { method: 'POST', body: JSON.stringify({
        name, phone, companyName, requirement, sourceId,
      }) });
      setName(''); setPhone(''); setCompanyName(''); setRequirement('');
      setEditing(false); setError(''); await refresh();
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not add lead'); }
  }

  async function update(id: number, path: string, payload: object) {
    try {
      await api(`/api/leads/${id}/${path}`, token, {
        method: path === 'status' ? 'PATCH' : 'POST', body: JSON.stringify(payload),
      });
      setError(''); await refresh();
    } catch (err) { setError(err instanceof Error ? err.message : 'Update failed'); }
  }

  return <section>
    <header className="page-header"><div><h1>Leads</h1><p>Manage sales enquiries and assignments</p></div>
      <button className="primary" onClick={() => setEditing(value => !value)}>
        {editing ? 'Close form' : '+ New lead'}</button></header>
    <Message error={error || loadError} />
    {selectedLead && <LeadDetails key={selectedLead.id} lead={selectedLead} sources={sources ?? []}
      token={token} onClose={() => setSelectedLead(null)} onSaved={refresh} />}
    {editing && <form className="panel form-grid" onSubmit={saveLead}>
      <label>Name<input required value={name} maxLength={150} onChange={e => setName(e.target.value)} /></label>
      <label>Phone<input required value={phone} maxLength={30} onChange={e => setPhone(e.target.value)} /></label>
      <label>Company<input value={companyName} onChange={e => setCompanyName(e.target.value)} /></label>
      <label>Source<select value={sourceId} onChange={e => setSourceId(Number(e.target.value))}>
        {sources?.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
      <label className="wide">Requirement<textarea value={requirement} onChange={e => setRequirement(e.target.value)} /></label>
      <button className="primary">Save lead</button>
    </form>}
    <div className="panel"><div className="table-top"><input placeholder="Search name, phone or company"
      value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} />
      <span>{data?.total ?? 0} leads</span></div>
      <div className="table-scroll"><table><thead><tr><th>Lead</th><th>Phone</th><th>Source</th>
        <th>Status</th><th>Assigned to</th></tr></thead><tbody>
        {data?.items.map(lead => <tr key={lead.id}>
          <td><button type="button" className="lead-link" onClick={() => setSelectedLead(lead)}>{lead.name}</button>
            <small>{lead.companyName || lead.requirement || '—'}</small></td>
          <td>{lead.phone}</td><td>{lead.source}</td><td><select aria-label={`Status for ${lead.name}`}
            value={lead.statusId} onChange={e => void update(lead.id, 'status', { statusId: Number(e.target.value) })}>
            {statuses?.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></td>
          <td>{canAssign ? <select aria-label={`Assign ${lead.name}`} value={lead.assignedToId ?? ''}
            onChange={e => void update(lead.id, 'assign', { userId: Number(e.target.value) })}>
            <option value="" disabled>Unassigned</option>
            {employees?.map(u => <option key={u.id} value={u.id}>{u.fullName}</option>)}
            {!employees?.some(u => u.id === lead.assignedToId) && lead.assignedToId &&
              <option value={lead.assignedToId}>{lead.assignedTo ?? 'Current employee'}</option>}
          </select> : (lead.assignedTo || 'Unassigned')}</td>
        </tr>)}</tbody></table>
      </div><div className="pagination"><button disabled={page <= 1} onClick={() => setPage(n => n - 1)}>Previous</button>
      <span>Page {page}</span><button disabled={page * 20 >= (data?.total ?? 0)} onClick={() => setPage(n => n + 1)}>Next</button></div>
    </div>
  </section>;
}

export function FollowUpsPage({ token }: { token: string }) {
  const { data, refresh, error } = useResource<FollowUp[]>('/api/followups', token);
  const [actionError, setActionError] = useState('');
  async function complete(id: number) {
    try { await api(`/api/followups/${id}/complete`, token, { method: 'PATCH' }); setActionError(''); await refresh(); }
    catch (err) { setActionError(err instanceof Error ? err.message : 'Update failed'); }
  }
  return <section><header className="page-header"><div><h1>Follow-ups</h1><p>Upcoming and overdue tasks</p></div>
    <button onClick={() => void refresh()}>Refresh</button></header>
    <Message error={error || actionError} /><div className="panel table-scroll"><table><thead><tr>
    <th>Lead</th><th>Notes</th><th>Due</th><th>Action</th></tr></thead><tbody>
    {data?.map(item => <tr key={item.id}><td>{item.leadName}</td><td>{item.notes}</td>
      <td>{new Date(item.dueAtUtc).toLocaleString()}</td><td>{item.completedAtUtc ? 'Completed' :
        <button onClick={() => void complete(item.id)}>Mark complete</button>}</td></tr>)}</tbody></table></div>
  </section>;
}

export function EmployeesPage({ token, user }: { token: string; user: User }) {
  const { data, refresh, error } = useResource<Employee[]>('/api/employees', token);
  const [message, setMessage] = useState('');
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState('Employee');
  const [managerId, setManagerId] = useState('');
  async function save(event: React.FormEvent) {
    event.preventDefault();
    try {
      await api('/api/employees', token, { method: 'POST', body: JSON.stringify({
        fullName, email, password, role, managerId: managerId ? Number(managerId) : null,
      }) });
      setFullName(''); setEmail(''); setPassword(''); setMessage('Employee created');
      await refresh();
    } catch (err) { setMessage(err instanceof Error ? err.message : 'Could not create employee'); }
  }
  return <section><header className="page-header"><div><h1>Employees</h1><p>Sales team members</p></div></header>
    <Message error={error} />
    {user.roles.includes('Admin') && <form className="panel form-grid" onSubmit={save}>
      <h3 className="wide">Add employee</h3>
      <label>Full name<input required value={fullName} onChange={e => setFullName(e.target.value)} /></label>
      <label>Email<input type="email" required value={email} onChange={e => setEmail(e.target.value)} /></label>
      <label>Password<input type="password" minLength={12} required value={password} onChange={e => setPassword(e.target.value)} /></label>
      <label>Role<select value={role} onChange={e => setRole(e.target.value)}><option>Employee</option><option>Manager</option></select></label>
      <label>Reporting manager<select value={managerId} onChange={e => setManagerId(e.target.value)}>
        <option value="">None</option>{data?.map(item => <option key={item.id} value={item.id}>{item.fullName}</option>)}</select></label>
      <div className="wide"><button className="primary">Create employee</button></div>
      {message && <p className="wide">{message}</p>}
    </form>}
    <div className="panel table-scroll"><table><thead><tr><th>Name</th><th>Email</th><th>Manager ID</th></tr></thead>
      <tbody>{data?.map(item => <tr key={item.id}><td>{item.fullName}</td><td>{item.email}</td>
        <td>{item.managerId ?? '—'}</td></tr>)}</tbody></table></div>
  </section>;
}
