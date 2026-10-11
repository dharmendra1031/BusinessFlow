import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
import { Icon } from './Icon';
import { useFeedback } from './Feedback';
import LeadDetails from './LeadDetails';
import EmployeeManager from './EmployeeManager';
import type { Dashboard, Employee, FollowUp, Lead, Lookup, User } from './types';

function useResource<T>(path: string, token: string) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const refresh = useCallback(async () => {
    if (!path) { setLoading(false); return; }
    setLoading(true);
    try {
      setData(await api<T>(path, token));
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load data');
    } finally { setLoading(false); }
  }, [path, token]);
  useEffect(() => { void refresh(); }, [refresh]);
  return { data, error, loading, refresh };
}

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return <div className="empty-state">
    <span className="empty-icon"><Icon name="clipboard" size={27} /></span>
    <h3>{title}</h3><p>{detail}</p>
  </div>;
}

function ErrorState({ message, retry }: { message: string; retry: () => void }) {
  return <div className="error-state" role="alert">
    <Icon name="warning" size={19} /><span>{message}</span>
    <button onClick={retry}>Retry</button>
  </div>;
}

function StatusBadge({ status }: { status: string }) {
  const level = status.toLowerCase().replace(/[^a-z]/g, '');
  return <span className={'status-badge status-' + level}><span className="status-dot" />{status}</span>;
}

function dateLabel(date: string) {
  return new Date(date).toLocaleString(undefined, {
    day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit'
  });
}

export function DashboardPage({ token, onNavigate }: {
  token: string;
  onNavigate: (screen: 'leads' | 'followups') => void;
}) {
  const { data, error, loading, refresh } = useResource<Dashboard>('/api/dashboard', token);
  const { data: recent } = useResource<{ items: Lead[] }>('/api/leads?page=1&pageSize=5', token);
  const { data: followups } = useResource<FollowUp[]>('/api/followups', token);
  const due = (followups ?? []).filter(item => !item.completedAtUtc)
    .slice(0, 4);

  const metrics = [
    { label: 'Total leads', value: data?.totalLeads, icon: 'clipboard', tone: 'blue', helper: 'Across your workspace' },
    { label: 'New leads', value: data?.newLeads, icon: 'plus', tone: 'violet', helper: 'Awaiting first contact' },
    { label: 'Won leads', value: data?.wonLeads, icon: 'check', tone: 'green', helper: 'Successfully converted' },
    { label: 'Overdue follow-ups', value: data?.overdueFollowUps, icon: 'clock', tone: 'orange', helper: 'Need your attention' },
    { label: 'Project amount (KWD)', value: data?.totalProjectAmount, icon: 'clipboard', tone: 'green', helper: 'Expected project value' },
    { label: "Today's follow-ups", value: data?.todayFollowUps, icon: 'calendar', tone: 'blue', helper: 'Due today' },
  ] as const;
  const total = data?.totalLeads ?? 0;
  const won = data?.wonLeads ?? 0;
  const lost = data?.lostLeads ?? 0;
  const active = Math.max(0, total - won - lost);
  const stages = [
    { label: 'Active', value: active, tone: 'violet' },
    { label: 'Won', value: won, tone: 'green' },
    { label: 'Lost', value: lost, tone: 'orange' },
  ];

  return <section>
    <header className="page-header">
      <div><span className="section-kicker">OVERVIEW</span><h1>Good to see you!</h1>
        <p>Here's what is happening with your leads.</p></div>
      <button className="secondary-action" onClick={() => void refresh()} disabled={loading}>
        <Icon name="refresh" size={17} /> Refresh
      </button>
    </header>
    {error && <ErrorState message={error} retry={() => void refresh()} />}
    <div className="metrics">
      {metrics.map(metric => <article className="metric" key={metric.label}>
        <div className="metric-top"><span>{metric.label}</span>
          <span className={'metric-icon tone-' + metric.tone}><Icon name={metric.icon} size={20} /></span>
        </div>
        <strong>{loading && !data ? '—' : (metric.value ?? 0).toLocaleString()}</strong>
        <small>{metric.helper}</small>
      </article>)}
    </div>
    <div className="dashboard-grid">
      <section className="panel pipeline-card">
        <div className="panel-heading"><div><h2>Lead pipeline</h2><p>How your opportunities are progressing</p></div>
          <button className="text-action" onClick={() => onNavigate('leads')}>View leads <Icon name="arrow" size={16} /></button>
        </div>
        {total === 0 ? <EmptyState title="Your pipeline starts here"
          detail="Add your first lead to start tracking progress." /> : <>
          <div className="pipeline-bar" aria-label="Lead pipeline breakdown">
            {stages.filter(stage => stage.value > 0).map(stage =>
              <div key={stage.label} className={'pipeline-segment bar-' + stage.tone}
                style={{ width: (stage.value / total * 100) + '%' }} title={stage.label + ': ' + stage.value} />)}
          </div>
          <div className="pipeline-stats">{stages.map(stage => <div key={stage.label}>
            <span className={'legend-dot bar-' + stage.tone} /><span>{stage.label}</span>
            <strong>{stage.value.toLocaleString()}</strong>
          </div>)}</div>
        </>}
      </section>
      <section className="panel followup-card">
        <div className="panel-heading"><div><h2>Next follow-ups</h2><p>Stay on top of every conversation</p></div>
          <button className="text-action" onClick={() => onNavigate('followups')}>View all <Icon name="arrow" size={16} /></button></div>
        {due.length === 0 ? <EmptyState title="Nothing pending" detail="New follow-ups will appear here." /> :
          <div className="task-list">{due.map(item => <div className="task" key={item.id}>
            <span className="task-avatar">{item.leadName.slice(0, 1).toUpperCase()}</span>
            <div><strong>{item.leadName}</strong><small>{item.notes}</small></div>
            <span className={'task-date ' + (new Date(item.dueAtUtc) < new Date() ? 'overdue-text' : '')}>
              {dateLabel(item.dueAtUtc)}</span></div>)}</div>}
      </section>
    </div>
    <section className="panel recent-panel">
      <div className="panel-heading"><div><h2>Recent leads</h2><p>Latest opportunities in your workspace</p></div>
        <button className="text-action" onClick={() => onNavigate('leads')}>View all <Icon name="arrow" size={16} /></button>
      </div>
      {recent?.items.length ? <div className="table-scroll"><table><thead><tr><th>Customer</th>
        <th>Phone</th><th>Source</th><th>Status</th><th>Owner</th></tr></thead><tbody>
        {recent.items.map(lead => <tr key={lead.id}><td><strong className="cell-name">{lead.name}</strong>
          <small>{lead.companyName || 'Individual'}</small></td><td>{lead.phone}</td><td>{lead.source}</td>
          <td><StatusBadge status={lead.status} /></td><td>{lead.assignedTo || 'Unassigned'}</td></tr>)}
      </tbody></table></div> : <EmptyState title="No leads yet" detail="Create a lead to see it here." />}
    </section>
  </section>;
}

export function LeadsPage({ token, user, newLeadRequest, onNewLeadOpened }: {
  token: string; user: User; newLeadRequest: number; onNewLeadOpened: () => void;
}) {
  const { notify, confirm } = useFeedback();
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [statusId, setStatusId] = useState('');
  const [page, setPage] = useState(1);
  const [showNew, setShowNew] = useState(false);
  const [selectedLead, setSelectedLead] = useState<Lead | null>(null);
  const [working, setWorking] = useState(false);
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [companyName, setCompanyName] = useState('');
  const [email, setEmail] = useState('');
  const [requirement, setRequirement] = useState('');
  const [sourceId, setSourceId] = useState(1);
  const [address, setAddress] = useState('');
  const [leadGivenBy, setLeadGivenBy] = useState(user.fullName);
  const [projectAmount, setProjectAmount] = useState('');
  const [newStatusId, setNewStatusId] = useState(1);

  useEffect(() => {
    if (!showNew) return;
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !working) setShowNew(false);
    }
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [showNew, working]);

  useEffect(() => {
    const timer = setTimeout(() => { setQuery(search); setPage(1); }, 350);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    if (newLeadRequest) {
      setShowNew(true);
      onNewLeadOpened();
    }
  }, [newLeadRequest, onNewLeadOpened]);

  const params = new URLSearchParams({ page: String(page), pageSize: '20', search: query });
  if (statusId) params.set('statusId', statusId);
  const { data, refresh, loading, error } = useResource<{ items: Lead[]; total: number }>(
    '/api/leads?' + params, token);
  const { data: statuses } = useResource<Lookup[]>('/api/masters/lead-statuses', token);
  const { data: sources } = useResource<Lookup[]>('/api/masters/lead-sources', token);
  const canAssign = user.roles.includes('Admin') || user.roles.includes('Manager');
  const { data: employees } = useResource<Employee[]>(canAssign ? '/api/employees' : '', token);

  async function saveLead(event: React.FormEvent) {
    event.preventDefault();
    if (working) return;
    setWorking(true);
    try {
      await api('/api/leads', token, { method: 'POST',
        body: JSON.stringify({ name, phone, email: email || null, companyName, requirement, sourceId,
          address, leadGivenBy, projectAmount: projectAmount ? Number(projectAmount) : null, statusId: newStatusId }) });
      setName(''); setPhone(''); setEmail(''); setCompanyName(''); setRequirement('');
      setAddress(''); setLeadGivenBy(user.fullName); setProjectAmount(''); setNewStatusId(1);
      setShowNew(false);
      setPage(1);
      await refresh();
      notify('success', 'Lead created', name + ' was added successfully.');
    } catch (err) {
      notify('error', 'Could not create lead', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function changeStatus(lead: Lead, nextStatusId: number) {
    const status = statuses?.find(item => item.id === nextStatusId);
    if (!status || nextStatusId === lead.statusId) return;
    if (status.name === 'Won' || status.name === 'Lost') {
      const accepted = await confirm({
        title: 'Mark lead as ' + status.name.toLowerCase() + '?',
        message: 'This will move ' + lead.name + ' to ' + status.name + ' in your pipeline.',
        confirmText: 'Mark ' + status.name,
        danger: status.name === 'Lost',
      });
      if (!accepted) return;
    }
    try {
      await api('/api/leads/' + lead.id + '/status', token, {
        method: 'PATCH', body: JSON.stringify({ statusId: nextStatusId }),
      });
      await refresh();
      notify('success', 'Status updated', lead.name + ' is now ' + status.name + '.');
    } catch (err) {
      notify('error', 'Could not update status', err instanceof Error ? err.message : undefined);
    }
  }

  async function assignLead(lead: Lead, assigneeId: number) {
    if (!assigneeId || assigneeId === lead.assignedToId) return;
    const employee = employees?.find(item => item.id === assigneeId);
    if (!employee) return;
    const accepted = await confirm({
      title: 'Assign this lead?',
      message: lead.name + ' will be assigned to ' + employee.fullName + '. Pending follow-ups will also move to them.',
      confirmText: 'Assign lead',
    });
    if (!accepted) return;
    try {
      await api('/api/leads/' + lead.id + '/assign', token, {
        method: 'POST', body: JSON.stringify({ userId: assigneeId }),
      });
      await refresh();
      notify('success', 'Lead assigned', employee.fullName + ' is now responsible for this lead.');
    } catch (err) {
      notify('error', 'Assignment failed', err instanceof Error ? err.message : undefined);
    }
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">SALES WORKSPACE</span>
      <h1>Manage leads</h1><p>Track, qualify and follow up with every opportunity.</p></div>
      <button className="primary" onClick={() => setShowNew(true)}><Icon name="plus" size={18} /> Add lead</button>
    </header>
    <section className="panel leads-panel">
      <div className="table-toolbar">
        <div className="search-field"><Icon name="search" size={18} />
          <input aria-label="Search leads" placeholder="Search by name, phone or company"
            value={search} onChange={event => setSearch(event.target.value)} /></div>
        <div className="filter-field"><Icon name="filter" size={17} />
          <select aria-label="Filter by status" value={statusId}
            onChange={event => { setStatusId(event.target.value); setPage(1); }}>
            <option value="">All statuses</option>
            {statuses?.map(status => <option key={status.id} value={status.id}>{status.name}</option>)}
          </select></div>
        <span className="result-count">{data?.total ?? 0} leads</span>
      </div>
      {error && <ErrorState message={error} retry={() => void refresh()} />}
      {loading && !data ? <div className="loading-state">Loading leads…</div> :
        data?.items.length ? <div className="table-scroll">
          <table><thead><tr><th>Customer</th><th>Contact</th><th>Amount (KWD)</th><th>Source</th>
            <th>Status</th><th>Assigned to</th><th /></tr></thead><tbody>
            {data.items.map(lead => <tr key={lead.id}>
              <td><button type="button" className="lead-link" onClick={() => setSelectedLead(lead)}>{lead.name}</button>
                <small>{lead.customerCode || ('Lead #' + lead.id)} · {lead.companyName || lead.requirement || 'Individual'}</small></td>
              <td><a className="table-phone" href={'tel:' + lead.phone}>{lead.phone}</a></td>
              <td>{lead.projectAmount?.toLocaleString('en-KW') ?? '—'}</td>
              <td><span className="source-tag">{lead.source}</span></td>
              <td><select className="inline-select" aria-label={'Status for ' + lead.name} value={lead.statusId}
                onChange={event => void changeStatus(lead, Number(event.target.value))}>
                {statuses?.map(status => <option key={status.id} value={status.id}>{status.name}</option>)}</select></td>
              <td>{canAssign ? <select className="inline-select" aria-label={'Assign ' + lead.name}
                value={lead.assignedToId ?? ''} onChange={event => void assignLead(lead, Number(event.target.value))}>
                <option value="" disabled>Unassigned</option>
                {employees?.map(employee => <option key={employee.id} value={employee.id}>{employee.fullName}</option>)}
                {!employees?.some(employee => employee.id === lead.assignedToId) && lead.assignedToId &&
                  <option value={lead.assignedToId}>{lead.assignedTo || 'Current employee'}</option>}
              </select> : (lead.assignedTo || 'Unassigned')}</td>
              <td><button className="icon-button row-edit" aria-label={'Edit ' + lead.name}
                onClick={() => setSelectedLead(lead)}><Icon name="chevron" size={19} /></button></td>
            </tr>)}</tbody></table>
        </div> : !error && <EmptyState title="No leads found"
          detail={query || statusId ? 'Try clearing your search or filters.' : 'Add your first lead to get started.'} />}
      <div className="pagination"><span>Page {page} · {data?.total ?? 0} results</span>
        <div><button disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
          <button disabled={page * 20 >= (data?.total ?? 0)}
            onClick={() => setPage(value => value + 1)}>Next <Icon name="chevron" size={15} /></button></div>
      </div>
    </section>
    {selectedLead && <LeadDetails key={selectedLead.id} lead={selectedLead} sources={sources ?? []}
      token={token} onClose={() => setSelectedLead(null)}
      onSaved={async () => { await refresh(); }} />}
    {showNew && <div className="drawer-backdrop" onMouseDown={event => {
      if (event.currentTarget === event.target && !working) setShowNew(false);
    }}>
      <aside className="detail-drawer" role="dialog" aria-modal="true" aria-labelledby="new-lead-title">
        <div className="drawer-header"><div><span className="section-kicker">NEW OPPORTUNITY</span>
          <h2 id="new-lead-title">Add a lead</h2><p>Capture the key details to get started.</p></div>
          <button className="icon-button" aria-label="Close form" onClick={() => setShowNew(false)}>
            <Icon name="close" /></button></div>
        <form className="drawer-form" onSubmit={saveLead}>
          <label>Full name <span className="required">*</span>
            <input autoFocus required maxLength={150} placeholder="Customer name" value={name}
              onChange={event => setName(event.target.value)} /></label>
          <label>Phone number <span className="required">*</span>
            <input required maxLength={30} placeholder="+965 ..." value={phone}
              onChange={event => setPhone(event.target.value)} /></label>
          <label>Email address<input type="email" maxLength={256} placeholder="customer@example.com"
            value={email} onChange={event => setEmail(event.target.value)} /></label>
          <label>Company name<input maxLength={200} placeholder="Optional"
            value={companyName} onChange={event => setCompanyName(event.target.value)} /></label>
          <label>Address<input maxLength={500} value={address}
            onChange={event => setAddress(event.target.value)} /></label>
          <label>Lead given by <span className="required">*</span><input required maxLength={150} value={leadGivenBy}
            onChange={event => setLeadGivenBy(event.target.value)} /></label>
          <label>Project amount (KWD)<input type="number" min="0" step="0.001" value={projectAmount}
            onChange={event => setProjectAmount(event.target.value)} /></label>
          <label>Lead status<select value={newStatusId} onChange={event => setNewStatusId(Number(event.target.value))}>
            {statuses?.map(status => <option key={status.id} value={status.id}>{status.name}</option>)}
          </select></label>
          <label>Lead source<select value={sourceId} onChange={event => setSourceId(Number(event.target.value))}>
            {sources?.map(source => <option key={source.id} value={source.id}>{source.name}</option>)}</select></label>
          <label>Requirement<textarea maxLength={2000} placeholder="What is the customer interested in?"
            value={requirement} onChange={event => setRequirement(event.target.value)} /></label>
          <div className="drawer-actions"><button type="button" disabled={working}
            onClick={() => setShowNew(false)}>Cancel</button>
            <button className="primary" disabled={working}>{working ? 'Saving…' : 'Create lead'}</button></div>
        </form>
      </aside>
    </div>}
  </section>;
}

export function FollowUpsPage({ token }: { token: string }) {
  const { notify, confirm } = useFeedback();
  const { data, refresh, error, loading } = useResource<FollowUp[]>('/api/followups', token);
  const [tab, setTab] = useState<'pending' | 'overdue' | 'completed' | 'all'>('pending');
  const [working, setWorking] = useState<number | null>(null);
  const now = Date.now();
  const tasks = (data ?? []).filter(item => {
    if (tab === 'completed') return !!item.completedAtUtc;
    if (tab === 'pending') return !item.completedAtUtc;
    if (tab === 'overdue') return !item.completedAtUtc && new Date(item.dueAtUtc).getTime() < now;
    return true;
  });

  async function complete(item: FollowUp) {
    const accepted = await confirm({
      title: 'Complete this follow-up?',
      message: 'Mark the task for ' + item.leadName + ' as completed?',
      confirmText: 'Mark complete',
    });
    if (!accepted) return;
    setWorking(item.id);
    try {
      await api('/api/followups/' + item.id + '/complete', token, { method: 'PATCH' });
      await refresh();
      notify('success', 'Follow-up completed', item.leadName + ' was updated.');
    } catch (err) {
      notify('error', 'Could not complete follow-up', err instanceof Error ? err.message : undefined);
    } finally { setWorking(null); }
  }

  return <section><header className="page-header"><div><span className="section-kicker">YOUR TASKS</span>
    <h1>Follow-ups</h1><p>Keep every customer conversation moving forward.</p></div>
    <button className="secondary-action" onClick={() => void refresh()}><Icon name="refresh" size={17} /> Refresh</button>
  </header>
    <section className="panel followup-panel">
      <div className="tab-list" role="tablist" aria-label="Filter follow-ups">
        {(['pending', 'overdue', 'completed', 'all'] as const).map(item =>
          <button key={item} role="tab" aria-selected={tab === item} className={tab === item ? 'selected' : ''}
            onClick={() => setTab(item)}>{item[0].toUpperCase() + item.slice(1)}</button>)}
      </div>
      {error && <ErrorState message={error} retry={() => void refresh()} />}
      {loading && !data ? <div className="loading-state">Loading follow-ups…</div> :
        tasks.length ? <div className="task-table">{tasks.map(item => <article className="followup-row" key={item.id}>
          <span className="task-avatar">{item.leadName[0]?.toUpperCase() || 'L'}</span>
          <div className="followup-info"><strong>{item.leadName}</strong><p>{item.notes}</p></div>
          <div className="followup-due"><Icon name="calendar" size={16} />
            <span className={!item.completedAtUtc && new Date(item.dueAtUtc).getTime() < now ? 'overdue-text' : ''}>
              {dateLabel(item.dueAtUtc)}</span></div>
          <div className="followup-action">{item.completedAtUtc ? <StatusBadge status="Completed" /> :
            <button disabled={working === item.id} onClick={() => void complete(item)}>
              <Icon name="check" size={16} /> {working === item.id ? 'Saving…' : 'Complete'}</button>}</div>
        </article>)}</div> : !error && <EmptyState title="All caught up"
          detail={tab === 'overdue' ? 'No overdue follow-ups in this list.' : 'No follow-ups in this view.'} />}
    </section>
  </section>;
}

export function EmployeesPage({ token, user }: { token: string; user: User }) {
  const { notify } = useFeedback();
  const { data, refresh, error } = useResource<Employee[]>('/api/employees', token);
  const [showForm, setShowForm] = useState(false);
  const [selectedEmployee, setSelectedEmployee] = useState<Employee | null>(null);
  const [working, setWorking] = useState(false);
  const [fullName, setFullName] = useState('');
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

  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (working) return;
    setWorking(true);
    try {
      await api('/api/employees', token, { method: 'POST', body: JSON.stringify({
        fullName, email, password, role, managerId: managerId ? Number(managerId) : null,
      }) });
      setFullName(''); setEmail(''); setPassword(''); setManagerId('');
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
    {error && <ErrorState message={error} retry={() => void refresh()} />}
    <section className="panel team-panel">
      <div className="panel-heading"><div><h2>Team directory</h2><p>{data?.filter(member => member.isActive).length ?? 0} active members</p></div></div>
      {data?.length ? <div className="table-scroll"><table><thead><tr><th>Employee</th><th>Email</th>
        <th>Reporting manager</th><th>Status</th><th /></tr></thead><tbody>
        {data.map(member => <tr key={member.id}><td><div className="person-cell">
          <span className="avatar avatar-person">{member.fullName.charAt(0).toUpperCase()}</span>
          <strong>{member.fullName}</strong></div></td>
          <td>{member.email}</td><td>{data.find(person => person.id === member.managerId)?.fullName || '—'}</td>
          <td>{member.isActive ? 'Active' : 'Inactive'}</td>
          <td>{canCreate && <button className="text-action" onClick={() => setSelectedEmployee(member)}>Manage</button>}</td>
        </tr>)}</tbody></table></div> : <EmptyState title="No employees found" detail="Team members will appear here." />}
    </section>
    {showForm && <div className="drawer-backdrop" onMouseDown={event => {
      if (event.target === event.currentTarget && !working) setShowForm(false);
    }}><aside className="detail-drawer" role="dialog" aria-modal="true" aria-labelledby="employee-dialog-title">
      <div className="drawer-header"><div><span className="section-kicker">TEAM MANAGEMENT</span>
        <h2 id="employee-dialog-title">Add employee</h2><p>Set up an account for a team member.</p></div>
        <button className="icon-button" aria-label="Close form" onClick={() => setShowForm(false)}>
          <Icon name="close" /></button></div>
      <form className="drawer-form" onSubmit={save}>
        <label>Full name <span className="required">*</span><input autoFocus required maxLength={150}
          value={fullName} onChange={event => setFullName(event.target.value)} /></label>
        <label>Email <span className="required">*</span><input type="email" required
          value={email} onChange={event => setEmail(event.target.value)} /></label>
        <label>Temporary password <span className="required">*</span><input type="password" required minLength={12}
          autoComplete="new-password" value={password} onChange={event => setPassword(event.target.value)} /></label>
        <p className="field-help">Use 12+ characters with upper/lowercase letters, numbers and symbols.</p>
        <label>Role<select value={role} onChange={event => setRole(event.target.value)}>
          <option>Employee</option><option>Manager</option></select></label>
        <label>Reporting manager<select value={managerId} onChange={event => setManagerId(event.target.value)}>
          <option value="">None</option>
          {data?.filter(member => member.roles?.includes('Manager')).map(member =>
            <option key={member.id} value={member.id}>{member.fullName}</option>)}
        </select></label>
        <div className="drawer-actions"><button type="button" disabled={working} onClick={() => setShowForm(false)}>Cancel</button>
          <button className="primary" disabled={working}>{working ? 'Creating…' : 'Create employee'}</button></div>
      </form>
    </aside></div>}
    {selectedEmployee && <EmployeeManager employee={selectedEmployee} team={data ?? []}
      token={token} onClose={() => setSelectedEmployee(null)} onSaved={refresh} />}
  </section>;
}
