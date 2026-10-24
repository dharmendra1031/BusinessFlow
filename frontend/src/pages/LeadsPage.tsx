import { useEffect, useState } from 'react';
import { api } from '../api';
import { clearFieldError, handleDialogKeyDown, handleFormKeyDown, validateForm } from '../formUtils';
import { Icon } from '../Icon';
import { useFeedback } from '../Feedback';
import LeadStatusRequest from '../LeadStatusRequest';
import LeadDetails from '../LeadDetails';
import EmployeeSearchSelect from '../EmployeeSearchSelect';
import type { Employee, Lead, Lookup, Paged, User } from '../types';
import { EmptyState, ErrorState, useResource } from './Shared';

export function LeadsPage({ token, user, permissions, newLeadRequest, onNewLeadOpened }: {
  token: string; user: User; permissions: Record<string, boolean>;
  newLeadRequest: number; onNewLeadOpened: () => void;
}) {
  const { notify, confirm } = useFeedback();
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [statusId, setStatusId] = useState('');
  const [page, setPage] = useState(1);
  const [showNew, setShowNew] = useState(false);
  const [selectedLead, setSelectedLead] = useState<Lead | null>(null);
  const [statusRequest, setStatusRequest] = useState<{ lead: Lead; statusId: number } | null>(null);
  const [assigningLead, setAssigningLead] = useState<Lead | null>(null);
  const [assignmentId, setAssignmentId] = useState('');
  const [assignmentName, setAssignmentName] = useState('');
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
  useEffect(() => {
    if (sources && !sources.some(source => source.id === sourceId))
      setSourceId(sources[0]?.id ?? 0);
  }, [sources, sourceId]);
  const canCreate = permissions['leads.create'] === true;
  const canEdit = permissions['leads.edit'] === true;
  const canAssign = permissions['leads.assign'] === true;
  const canStatus = permissions['leads.status'] === true;
  const canRequestStatus = permissions['leads.request'] === true;
  const { data: employeeOptions } = useResource<Paged<Employee>>(canAssign ? '/api/employees/lookup?action=leads.assign&pageSize=200' : '', token);
  const employees = employeeOptions?.items;

  async function saveLead(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!validateForm(event.currentTarget) || working) return;
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
    if (nextStatusId === 5 || nextStatusId === 6) {
      if (!canRequestStatus) return;
      setStatusRequest({ lead, statusId: nextStatusId });
      return;
    }
    try {
      await api('/api/leads/' + lead.id + '/status', token, {
        method: 'PATCH', body: JSON.stringify({ statusId: nextStatusId, rowVersion: lead.rowVersion }),
      });
      await refresh();
      notify('success', 'Status updated', lead.name + ' is now ' + status.name + '.');
    } catch (err) {
      notify('error', 'Could not update status', err instanceof Error ? err.message : undefined);
    }
  }

  async function assignLead(lead: Lead, assigneeId: number, selectedName?: string) {
    if (!assigneeId || assigneeId === lead.assignedToId) return;
    const name = selectedName || employees?.find(item => item.id === assigneeId)?.fullName;
    if (!name) return;
    const accepted = await confirm({
      title: 'Assign this lead?',
      message: lead.name + ' will be assigned to ' + name + '. Pending follow-ups will also move to them.',
      confirmText: 'Assign lead',
    });
    if (!accepted) return;
    try {
      await api('/api/leads/' + lead.id + '/assign', token, {
        method: 'POST', body: JSON.stringify({ userId: assigneeId, rowVersion: lead.rowVersion }),
      });
      setAssigningLead(null);
      await refresh();
      notify('success', 'Lead assigned', name + ' is now responsible for this lead.');
    } catch (err) {
      notify('error', 'Assignment failed', err instanceof Error ? err.message : undefined);
    }
  }

  return <section>
    {statusRequest && <LeadStatusRequest lead={statusRequest.lead} statusId={statusRequest.statusId}
      isAdmin={user.roles.includes('Admin')} token={token} onClose={() => setStatusRequest(null)} onSaved={refresh} />}

    <header className="page-header"><div><span className="section-kicker">SALES WORKSPACE</span>
      <h1>Manage leads</h1><p>Track, qualify and follow up with every opportunity.</p></div>
      {canCreate && <button className="primary" onClick={() => setShowNew(true)}><Icon name="plus" size={18} /> Add lead</button>}
    </header>
    <section className="panel leads-panel">
      <div className="table-toolbar">
        <div className="search-field"><Icon name="search" size={18} />
          <input aria-label="Search leads" maxLength={150} placeholder="Search by name, phone or company"
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
              <td>{canStatus || canRequestStatus ? <select className="inline-select" aria-label={'Status for ' + lead.name} value={lead.statusId}
                onChange={event => void changeStatus(lead, Number(event.target.value))}>
                {statuses?.filter(status => status.id === lead.statusId ||
                  (status.id === 5 || status.id === 6 ? canRequestStatus : canStatus))
                  .map(status => <option key={status.id} value={status.id}>{status.name}</option>)}</select>
                : <span>{lead.status}</span>}</td>
              <td><div className="lead-assignment-actions">{canAssign ? <select className="inline-select" aria-label={'Assign ' + lead.name}
                value={lead.assignedToId ?? ''} onChange={event => void assignLead(lead, Number(event.target.value))}>
                <option value="" disabled>Unassigned</option>
                {employees?.filter(employee => employee.isActive).map(employee => <option key={employee.id} value={employee.id}>{employee.fullName}</option>)}
                {!employees?.some(employee => employee.id === lead.assignedToId && employee.isActive) && lead.assignedToId &&
                  <option value={lead.assignedToId}>{lead.assignedTo || 'Current employee'}</option>}
              </select> : (lead.assignedTo || 'Unassigned')}
                {canAssign && <button type="button" className="text-action"
                  aria-label={'Find assignee for ' + lead.name} onClick={() => {
                    setAssigningLead(lead);
                    setAssignmentId(lead.assignedToId?.toString() ?? '');
                    setAssignmentName(lead.assignedTo ?? '');
                  }}>Find employee</button>}</div></td>
              <td><button className="icon-button row-edit" aria-label={(canEdit ? 'Edit ' : 'View ') + lead.name}
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
    {assigningLead && <div className="drawer-backdrop">
      <aside className="detail-drawer" role="dialog" aria-modal="true"
        aria-label={'Assign lead ' + assigningLead.name}
        onKeyDown={event => handleDialogKeyDown(event, () => setAssigningLead(null))}>
        <div className="drawer-header"><h2>Assign {assigningLead.name}</h2>
          <button type="button" className="icon-button" aria-label="Close assignment"
            onClick={() => setAssigningLead(null)}><Icon name="close" /></button></div>
        <form className="drawer-form" onSubmit={event => {
          event.preventDefault();
          void assignLead(assigningLead, Number(assignmentId), assignmentName);
        }}>
          <EmployeeSearchSelect token={token} action="leads.assign" label="Assign to"
            required value={assignmentId} selectedName={assigningLead.assignedTo ?? undefined}
            onChange={(value, employee) => {
              setAssignmentId(value); setAssignmentName(employee?.fullName ?? assigningLead.assignedTo ?? '');
            }} />
          <div className="drawer-actions">
            <button type="button" onClick={() => setAssigningLead(null)}>Cancel</button>
            <button type="submit" className="primary"
              disabled={!assignmentId || Number(assignmentId) === assigningLead.assignedToId}>Assign lead</button>
          </div>
        </form>
      </aside>
    </div>}
    {selectedLead && <LeadDetails key={selectedLead.id} lead={selectedLead} sources={sources ?? []}
      canEdit={canEdit} canManageFollowUps={permissions['followups.manage'] === true}
      canViewFollowUps={permissions['followups.view'] === true} token={token} onClose={() => setSelectedLead(null)}
      onSaved={async () => {
        await refresh();
        const latest = await api<Partial<Lead>>('/api/leads/' + selectedLead.id, token);
        setSelectedLead(current => current?.id === selectedLead.id ? { ...current, ...latest } : current);
      }} />}
    {showNew && <div className="drawer-backdrop" onMouseDown={event => {
      if (event.currentTarget === event.target && !working) setShowNew(false);
    }}>
      <aside className="detail-drawer" role="dialog" aria-modal="true" aria-labelledby="new-lead-title"
        onKeyDown={event => handleDialogKeyDown(event, () => { if (!working) setShowNew(false); })}>
        <div className="drawer-header"><div><span className="section-kicker">NEW OPPORTUNITY</span>
          <h2 id="new-lead-title">Add a lead</h2><p>Capture the key details to get started.</p></div>
          <button className="icon-button" aria-label="Close form" onClick={() => setShowNew(false)}>
            <Icon name="close" /></button></div>
        <form className="drawer-form" onSubmit={saveLead} onInput={clearFieldError}
          onKeyDown={handleFormKeyDown}>
          <label>Full name <span className="required">*</span>
            <input autoFocus required minLength={2} maxLength={150} placeholder="Customer name" value={name}
              onChange={event => setName(event.target.value)} /></label>
          <label>Phone number <span className="required">*</span>
            <input required type="tel" autoComplete="tel" maxLength={30} placeholder="+965 ..." value={phone}
              title="Enter a phone number with 7 to 15 digits."
              onChange={event => setPhone(event.target.value)} /></label>
          <label>Email address<input type="email" maxLength={256} placeholder="customer@example.com"
            value={email} onChange={event => setEmail(event.target.value)} /></label>
          <label>Company name<input maxLength={200} placeholder="Optional"
            value={companyName} onChange={event => setCompanyName(event.target.value)} /></label>
          <label>Address<input maxLength={500} value={address}
            onChange={event => setAddress(event.target.value)} /></label>
          <label>Lead given by <span className="required">*</span><input required maxLength={150} value={leadGivenBy}
            onChange={event => setLeadGivenBy(event.target.value)} /></label>
          <label>Project amount (KWD)<input type="number" min="0" max="999999999999999.999" step="0.001" value={projectAmount}
            onChange={event => setProjectAmount(event.target.value)} /></label>
          <label>Lead status<select value={newStatusId} onChange={event => setNewStatusId(Number(event.target.value))}>
            {statuses?.filter(status => status.id !== 5 && status.id !== 6).map(status => <option key={status.id} value={status.id}>{status.name}</option>)}
          </select></label>
          <label>Lead source<select value={sourceId} onChange={event => setSourceId(Number(event.target.value))}>
            {sources?.map(source => <option key={source.id} value={source.id}>{source.name}</option>)}</select></label>
          <label>Requirement<textarea maxLength={2000} placeholder="What is the customer interested in?"
            value={requirement} onChange={event => setRequirement(event.target.value)} /></label>
          <div className="drawer-actions"><button type="button" disabled={working}
            onClick={() => setShowNew(false)}>Cancel</button>
            <button className="primary" disabled={working || !sources?.length}>{working ? 'Saving…' : 'Create lead'}</button></div>
        </form>
      </aside>
    </div>}
  </section>;
}
