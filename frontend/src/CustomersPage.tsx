import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import CustomerBillingFields, { emptyCustomerBilling, type CustomerBilling } from './CustomerBillingFields';
import { Icon } from './Icon';
import { useFeedback } from './Feedback';
import { clearFieldError, handleDialogKeyDown, handleFormKeyDown, validateForm } from './formUtils';
import type { Paged } from './types';

type Customer = {
  id: number; name: string; email: string | null; phone: string | null;
  address?: string | null; isActive: boolean; rowVersion: string;
  legalName?: string | null; gstin?: string | null; billingAddress?: string | null;
  billingCity?: string | null; billingState?: string | null;
  billingPostalCode?: string | null; billingCountry?: string | null;
  leadCount?: number; projectCount?: number;
  mergedIntoCustomerId?: number | null; mergedAtUtc?: string | null;
};
type Contact = {
  id: number; name: string; designation: string | null; email: string | null;
  phone: string | null; isPrimary: boolean; isActive: boolean;
};
type LinkedLead = { id: number; name: string; status: string; rowVersion: string };
type LeadSearch = LinkedLead & { phone: string };
type LinkedProject = { id: number; title: string; status: string; leadId: number };
type Detail = {
  customer: Customer; contacts: Contact[];
  leads: LinkedLead[]; leadCount: number;
  projects: LinkedProject[]; projectCount: number;
};
type CustomerHistory = {
  id: number; action: string; tableName: string; recordId: number;
  userId: number; createdAtUtc: string;
};

function currentBilling(customer: Customer): CustomerBilling {
  return {
    legalName: customer.legalName ?? '',
    gstin: customer.gstin ?? '',
    billingAddress: customer.billingAddress ?? '',
    billingCity: customer.billingCity ?? '',
    billingState: customer.billingState ?? '',
    billingPostalCode: customer.billingPostalCode ?? '',
    billingCountry: customer.billingCountry ?? '',
  };
}

type ContactForm = {
  name: string; designation: string; email: string; phone: string;
  isPrimary: boolean; isActive: boolean;
};

const blankContact = (): ContactForm =>
  ({ name: '', designation: '', email: '', phone: '', isPrimary: false, isActive: true });

export default function CustomersPage({ token }: { token: string }) {
  const { notify, confirm } = useFeedback();
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [detail, setDetail] = useState<Detail | null>(null);
  const [detailTab, setDetailTab] = useState<'overview' | 'contacts' | 'relationships' | 'history' | 'merge'>('overview');
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [error, setError] = useState('');
  const [newName, setNewName] = useState('');
  const [newEmail, setNewEmail] = useState('');
  const [newPhone, setNewPhone] = useState('');
  const [newBilling, setNewBilling] = useState<CustomerBilling>(emptyCustomerBilling);
  const [editBilling, setEditBilling] = useState<CustomerBilling>(emptyCustomerBilling);
  const [history, setHistory] = useState<Paged<CustomerHistory> | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [leadPage, setLeadPage] = useState(1);
  const [projectPage, setProjectPage] = useState(1);
  const [loadingLinked, setLoadingLinked] = useState(false);
  const [editName, setEditName] = useState('');
  const [editEmail, setEditEmail] = useState('');
  const [editPhone, setEditPhone] = useState('');
  const [editAddress, setEditAddress] = useState('');
  const [editActive, setEditActive] = useState(true);
  const [contact, setContact] = useState<ContactForm>(blankContact);
  const [editingContact, setEditingContact] = useState<number | null>(null);
  const [leadSearch, setLeadSearch] = useState('');
  const [foundLeads, setFoundLeads] = useState<LeadSearch[]>([]);
  const [mergeSourceId, setMergeSourceId] = useState('');
  const [mergeSource, setMergeSource] = useState<Detail | null>(null);
  const [mergeReason, setMergeReason] = useState('');

  const load = useCallback(async () => {
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '30', search: query });
      const result = await api<Paged<Customer>>('/api/admin/customers?' + params, token);
      setCustomers(result.items);
      setTotal(result.total);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load customers.');
    } finally {
      setLoading(false);
    }
  }, [token, page, query]);

  useEffect(() => { void load(); }, [load]);

  async function openCustomer(id: number) {
    try {
      if (detail?.customer.id !== id) setDetailTab('overview');
      const [current, recentHistory] = await Promise.all([
        api<Detail>('/api/admin/customers/' + id, token),
        api<Paged<CustomerHistory>>('/api/admin/customers/' + id + '/history', token),
      ]);
      setDetail(current);
      setMergeSource(null);
      setMergeSourceId('');
      setMergeReason('');
      setHistory(recentHistory);
      setHistoryPage(1);
      setLeadPage(1);
      setProjectPage(1);
      setEditBilling(currentBilling(current.customer));
      setEditName(current.customer.name);
      setEditEmail(current.customer.email ?? '');
      setEditPhone(current.customer.phone ?? '');
      setEditAddress(current.customer.address ?? '');
      setEditActive(current.customer.isActive);
    } catch (err) {
      notify('error', 'Unable to open customer', err instanceof Error ? err.message : undefined);
    }
  }

  function close() {
    setDetail(null);
    setDetailTab('overview');
    setHistory(null);
    setHistoryPage(1);
    setLeadPage(1);
    setProjectPage(1);
    setEditingContact(null);
    setContact(blankContact());
    setLeadSearch('');
    setFoundLeads([]);
    setMergeSource(null);
    setMergeSourceId('');
    setMergeReason('');
  }

  async function createCustomer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (working || !validateForm(event.currentTarget)) return;
    setWorking(true);
    try {
      const created = await api<{ id: number }>('/api/admin/customers', token, {
        method: 'POST', body: JSON.stringify({
          name: newName.trim(), email: newEmail.trim() || null, phone: newPhone.trim() || null,
          ...newBilling, gstin: newBilling.gstin.trim() || null,
        }),
      });
      setNewName(''); setNewEmail(''); setNewPhone(''); setNewBilling(emptyCustomerBilling());
      setShowCreate(false);
      await load();
      await openCustomer(created.id);
      notify('success', 'Customer created');
    } catch (err) {
      notify('error', 'Unable to create customer', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function saveCustomer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (working || !detail || !validateForm(event.currentTarget)) return;
    setWorking(true);
    try {
      await api('/api/admin/customers/' + detail.customer.id, token, {
        method: 'PUT', body: JSON.stringify({
          name: editName.trim(), email: editEmail.trim() || null,
          phone: editPhone.trim() || null, address: editAddress.trim() || null,
          ...editBilling, gstin: editBilling.gstin.trim() || null,
          isActive: editActive, rowVersion: detail.customer.rowVersion,
        }),
      });
      await Promise.all([load(), openCustomer(detail.customer.id)]);
      notify('success', 'Customer updated');
    } catch (err) {
      notify('error', 'Unable to update customer', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  function editContact(item: Contact) {
    setEditingContact(item.id);
    setContact({
      name: item.name, designation: item.designation ?? '',
      email: item.email ?? '', phone: item.phone ?? '',
      isPrimary: item.isPrimary, isActive: item.isActive,
    });
  }

  async function saveContact(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (working || !detail || !validateForm(event.currentTarget)) return;
    setWorking(true);
    try {
      const id = detail.customer.id;
      await api('/api/admin/customers/' + id + '/contacts' +
        (editingContact === null ? '' : '/' + editingContact), token, {
        method: editingContact === null ? 'POST' : 'PUT',
        body: JSON.stringify({
          ...contact, name: contact.name.trim(),
          designation: contact.designation.trim() || null,
          email: contact.email.trim() || null,
          phone: contact.phone.trim() || null,
        }),
      });
      setEditingContact(null);
      setContact(blankContact());
      await openCustomer(id);
      notify('success', 'Contact saved');
    } catch (err) {
      notify('error', 'Unable to save contact', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function deactivateContact(item: Contact) {
    if (working || !detail) return;
    const proceed = await confirm({
      title: 'Deactivate contact?',
      message: item.name + ' will stay in history but be marked inactive.',
      confirmText: 'Deactivate',
    });
    if (!proceed) return;
    const id = detail.customer.id;
    setWorking(true);
    try {
      await api('/api/admin/customers/' + id + '/contacts/' + item.id, token, {
        method: 'PUT', body: JSON.stringify({
          name: item.name, designation: item.designation, email: item.email,
          phone: item.phone, isActive: false, isPrimary: false,
        }),
      });
      await openCustomer(id);
      notify('success', 'Contact deactivated');
    } catch (err) {
      notify('error', 'Unable to deactivate contact', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function findLeads(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!leadSearch.trim()) return;
    try {
      const results = await api<LeadSearch[]>('/api/admin/customers/unlinked-leads?search=' +
        encodeURIComponent(leadSearch.trim()), token);
      setFoundLeads(results);
    } catch (err) {
      notify('error', 'Unable to search leads', err instanceof Error ? err.message : undefined);
    }
  }

  async function linkLead(item: LinkedLead) {
    if (working || !detail) return;
    const id = detail.customer.id;
    setWorking(true);
    try {
      await api('/api/admin/customers/' + id + '/leads/' + item.id, token, {
        method: 'PUT', body: JSON.stringify({ rowVersion: item.rowVersion }),
      });
      setFoundLeads(items => items.filter(x => x.id !== item.id));
      await Promise.all([load(), openCustomer(id)]);
      notify('success', 'Lead linked to customer');
    } catch (err) {
      notify('error', 'Unable to link lead', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function unlinkLead(item: LinkedLead) {
    if (working || !detail) return;
    if (!(await confirm({
      title: 'Unlink this lead?',
      message: 'The lead and its projects will remain unchanged. Only their customer grouping will change.',
      confirmText: 'Unlink',
    }))) return;
    const id = detail.customer.id;
    setWorking(true);
    try {
      await api('/api/admin/customers/' + id + '/leads/' + item.id, token, {
        method: 'DELETE', body: JSON.stringify({ rowVersion: item.rowVersion }),
      });
      await Promise.all([load(), openCustomer(id)]);
      notify('success', 'Lead unlinked');
    } catch (err) {
      notify('error', 'Unable to unlink lead', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function reviewMergeSource(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (working || !detail || !validateForm(event.currentTarget)) return;
    const sourceId = Number(mergeSourceId);
    if (!Number.isSafeInteger(sourceId) || sourceId <= 0 || sourceId === detail.customer.id) {
      notify('warning', 'Choose a different source customer ID.');
      return;
    }
    setWorking(true);
    setMergeSource(null);
    setMergeReason('');
    try {
      const source = await api<Detail>('/api/admin/customers/' + sourceId, token);
      if (!source.customer.isActive || source.customer.mergedIntoCustomerId != null) {
        notify('warning', 'The source customer must be active and not previously merged.');
        return;
      }
      setMergeSource(source);
    } catch (err) {
      notify('error', 'Unable to review source customer', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function mergeCustomers(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (working || !detail || !mergeSource || !detail.customer.isActive ||
        detail.customer.mergedIntoCustomerId != null || !validateForm(event.currentTarget)) return;
    const target = detail.customer;
    const source = mergeSource.customer;
    const reason = mergeReason.trim();
    if (source.id === target.id || reason.length < 10 || reason.length > 500) return;
    if (!(await confirm({
      title: 'Merge customer records?',
      message: source.name + ' (#' + source.id + ') will be merged into ' +
        target.name + ' (#' + target.id + '). ' + mergeSource.leadCount +
        ' leads and ' + mergeSource.contacts.length +
        ' contacts will move. Source will become read-only. This cannot be undone.',
      confirmText: 'Merge customers',
      danger: true,
    }))) return;

    setWorking(true);
    try {
      await api('/api/admin/customers/' + target.id + '/merge', token, {
        method: 'POST',
        body: JSON.stringify({
          sourceId: source.id,
          targetRowVersion: target.rowVersion,
          sourceRowVersion: source.rowVersion,
          reason,
        }),
      });
      await Promise.all([load(), openCustomer(target.id)]);
      notify('success', 'Customers merged');
    } catch (err) {
      notify('error', 'Unable to merge customers', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function loadMoreHistory() {
    if (!detail || !history || history.items.length >= history.total) return;
    const next = historyPage + 1;
    try {
      const extra = await api<Paged<CustomerHistory>>(
        '/api/admin/customers/' + detail.customer.id + '/history?page=' + next, token);
      setHistory(previous => previous
        ? { ...extra, items: [...previous.items, ...extra.items] } : extra);
      setHistoryPage(next);
    } catch (err) {
      notify('error', 'Unable to load customer history', err instanceof Error ? err.message : undefined);
    }
  }

  async function loadMoreLinked(kind: 'leads' | 'projects') {
    if (!detail || loadingLinked) return;
    const id = detail.customer.id;
    const next = (kind === 'leads' ? leadPage : projectPage) + 1;
    setLoadingLinked(true);
    try {
      if (kind === 'leads') {
        const result = await api<Paged<LinkedLead>>(
          `/api/admin/customers/${id}/leads?page=${next}&pageSize=100`, token);
        setDetail(current => current && current.customer.id === id
          ? { ...current, leads: [...current.leads, ...result.items], leadCount: result.total } : current);
        setLeadPage(next);
      } else {
        const result = await api<Paged<LinkedProject>>(
          `/api/admin/customers/${id}/projects?page=${next}&pageSize=100`, token);
        setDetail(current => current && current.customer.id === id
          ? { ...current, projects: [...current.projects, ...result.items], projectCount: result.total } : current);
        setProjectPage(next);
      }
    } catch (err) {
      notify('error', 'Unable to load linked ' + kind, err instanceof Error ? err.message : undefined);
    } finally { setLoadingLinked(false); }
  }

  return <section>
    <header className="page-header"><div>
      <span className="section-kicker">CLIENT RELATIONSHIPS</span>
      <h1>Customer master</h1>
      <p>Find a customer first. Open a record to manage contacts, leads and billing information.</p>
    </div>
    <button type="button" className="primary" disabled={working}
      aria-expanded={showCreate} aria-controls="customer-create"
      onClick={() => setShowCreate(value => !value)}>
      {showCreate ? 'Close form' : 'Add customer'}
    </button></header>

    {showCreate && <section id="customer-create" className="panel" aria-label="Add customer">
      <div className="panel-heading"><h2>Add customer</h2><p>Duplicate phone, email or GSTIN requires review. Leads are never linked automatically.</p></div>
      <form className="form-grid" onSubmit={createCustomer} onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
        <label>Customer / company name *<input autoFocus required minLength={2} maxLength={200}
          value={newName} onChange={event => setNewName(event.target.value)} /></label>
        <label>Email<input type="email" maxLength={256}
          value={newEmail} onChange={event => setNewEmail(event.target.value)} /></label>
        <label>Phone<input type="tel" maxLength={30}
          value={newPhone} onChange={event => setNewPhone(event.target.value)} /></label>
        <CustomerBillingFields value={newBilling} onChange={setNewBilling} />
        <div><button className="primary" disabled={working}>Add customer</button></div>
      </form>
    </section>}

    <section className="panel" aria-label="Customer directory">
      <div className="panel-heading"><h2>Customer directory</h2><p>{total} customer records</p></div>
      <form className="form-grid" onSubmit={event => {
        event.preventDefault(); setPage(1); setQuery(search.trim());
      }}>
        <label>Search customers<input value={search} maxLength={200}
          onChange={event => setSearch(event.target.value)} placeholder="Name, legal name, email, phone or GSTIN" /></label>
        <div><button type="submit">Search</button></div>
      </form>
      {error && <p className="error" role="alert">{error}
        <button type="button" onClick={() => void load()}>Retry</button></p>}
      {loading ? <p role="status">Loading customers…</p> :
        customers.length ? <div className="table-scroll"><table>
          <thead><tr><th>Customer</th><th>Contact</th><th>Leads</th><th>Projects</th><th>Status</th><th>Action</th></tr></thead>
          <tbody>{customers.map(customer => <tr key={customer.id}>
            <td><strong>{customer.name}</strong></td>
            <td>{customer.email || customer.phone || '—'}</td>
            <td>{customer.leadCount}</td><td>{customer.projectCount}</td>
            <td>{customer.mergedIntoCustomerId != null ? 'Merged' : customer.isActive ? 'Active' : 'Inactive'}</td>
            <td><button type="button" onClick={() => void openCustomer(customer.id)}>View customer</button></td>
          </tr>)}</tbody>
        </table></div> : <p className="empty-state">No customers found.</p>}
      <div className="pagination"><span>Page {page} · {total} customers</span>
        <button disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
        <button disabled={page * 30 >= total} onClick={() => setPage(value => value + 1)}>Next</button>
      </div>
    </section>

    {detail && <div className="drawer-backdrop">
      <aside className="detail-drawer wide-drawer" role="dialog" aria-modal="true" aria-label={'Customer ' + detail.customer.name}
        onKeyDown={event => handleDialogKeyDown(event, () => { if (!working) close(); })}>
        <div className="drawer-header"><div><span className="section-kicker">CUSTOMER DETAILS</span>
          <h2>{detail.customer.name}</h2>
          <p>{detail.customer.mergedIntoCustomerId != null ? 'Merged' : detail.customer.isActive ? 'Active' : 'Inactive'} · Customer #{detail.customer.id}</p></div>
          <button className="icon-button" aria-label="Close customer" disabled={working}
            onClick={close}><Icon name="close" /></button>
        </div>

        {detail.customer.mergedIntoCustomerId != null && <section className="project-section">
          <h3>Merged customer</h3>
          <p className="field-help">This record is read-only. Leads and contacts moved to customer
            #{detail.customer.mergedIntoCustomerId}.</p>
          <button type="button" disabled={working}
            onClick={() => void openCustomer(detail.customer.mergedIntoCustomerId!)}>
            Open current customer</button>
        </section>}
        {detail.customer.mergedIntoCustomerId == null && <div className="drawer-tabs" role="tablist" aria-label="Customer sections">
          {([
            ['overview', 'Overview'], ['contacts', 'Contacts'],
            ['relationships', 'Leads & projects'], ['history', 'Activity'],
            ...(detail.customer.isActive ? [['merge', 'Merge']] : []),
          ] as const).map(([id, label]) =>
            <button type="button" key={id} role="tab" aria-selected={detailTab === id}
              className={detailTab === id ? 'selected' : ''}
              onClick={() => setDetailTab(id)}>{label}</button>)}
        </div>}
        {detail.customer.mergedIntoCustomerId == null && <>
        {detailTab === 'overview' && <>
          <section className="project-section" aria-label="Customer overview">
            <h3>Customer overview</h3>
            <p>{detail.customer.email || 'No email'} · {detail.customer.phone || 'No phone'}</p>
            <p>{detail.contacts.length} contacts · {detail.leadCount} linked leads · {detail.projectCount} projects</p>
          </section>
        <details className="project-section">
          <summary>Edit customer</summary>
          <form className="drawer-form" onSubmit={saveCustomer} onInput={clearFieldError}
            onKeyDown={handleFormKeyDown}>
            <label>Name *<input required minLength={2} maxLength={200}
              value={editName} onChange={event => setEditName(event.target.value)} /></label>
            <label>Email<input type="email" maxLength={256}
              value={editEmail} onChange={event => setEditEmail(event.target.value)} /></label>
            <label>Phone<input type="tel" maxLength={30}
              value={editPhone} onChange={event => setEditPhone(event.target.value)} /></label>
            <label>Address<textarea maxLength={500}
              value={editAddress} onChange={event => setEditAddress(event.target.value)} /></label>
            <CustomerBillingFields value={editBilling} onChange={setEditBilling} />
            <label><input type="checkbox" checked={editActive}
              onChange={event => setEditActive(event.target.checked)} /> Active customer</label>
            <button className="primary" disabled={working}>Save customer</button>
          </form>
        </details>
        </>}

        {detailTab === 'contacts' && <section className="project-section" aria-label="Customer contacts">
          <h3>Contacts ({detail.contacts.length})</h3>
          {detail.contacts.length ? <div className="project-tasks">{detail.contacts.map(item =>
            <div className="project-task" key={item.id}>
              <div><strong>{item.name} {item.isPrimary ? '· Primary' : ''}</strong>
                <p>{item.designation || 'Contact'} · {item.email || item.phone || 'No contact details'}</p>
                <small>{item.isActive ? 'Active' : 'Inactive'}</small></div>
              <div><button type="button" disabled={working} onClick={() => editContact(item)}>Edit</button>
                {item.isActive && <button type="button" disabled={working}
                  onClick={() => void deactivateContact(item)}>Deactivate</button>}</div>
            </div>)}</div> : <p className="field-help">No contact persons yet.</p>}
          <form className="drawer-form" onSubmit={saveContact} onInput={clearFieldError}
            onKeyDown={handleFormKeyDown}>
            <h3>{editingContact === null ? 'Add contact person' : 'Edit contact person'}</h3>
            <label>Contact name *<input required minLength={2} maxLength={150} value={contact.name}
              onChange={event => setContact(c => ({ ...c, name: event.target.value }))} /></label>
            <label>Designation<input maxLength={100} value={contact.designation}
              onChange={event => setContact(c => ({ ...c, designation: event.target.value }))} /></label>
            <label>Email<input type="email" maxLength={256} value={contact.email}
              onChange={event => setContact(c => ({ ...c, email: event.target.value }))} /></label>
            <label>Phone<input type="tel" maxLength={30} value={contact.phone}
              onChange={event => setContact(c => ({ ...c, phone: event.target.value }))} /></label>
            <label><input type="checkbox" checked={contact.isPrimary}
              onChange={event => setContact(c => ({ ...c, isPrimary: event.target.checked, isActive: true }))} />
              Primary contact</label>
            {editingContact !== null && <label><input type="checkbox" checked={contact.isActive}
              onChange={event => setContact(c => ({
                ...c, isActive: event.target.checked, isPrimary: event.target.checked ? c.isPrimary : false,
              }))} /> Active contact</label>}
            <div className="drawer-actions">
              {editingContact !== null && <button type="button" onClick={() => {
                setEditingContact(null); setContact(blankContact());
              }}>Cancel edit</button>}
              <button className="primary" disabled={working}>Save contact</button>
            </div>
          </form>
        </section>

        {detailTab === 'relationships' && <>
        <section className="project-section" aria-label="Linked leads">
          <h3>Linked leads ({detail.leadCount})</h3>
          <p className="field-help">Linking does not change the lead status or owner.</p>
          {detail.leads.map(item => <div className="project-task" key={item.id}>
            <div><strong>{item.name}</strong><p>{item.status} · Lead #{item.id}</p></div>
            <button type="button" disabled={working} onClick={() => void unlinkLead(item)}>Unlink</button>
          </div>)}
          {detail.leads.length < detail.leadCount && <button type="button" disabled={loadingLinked}
            onClick={() => void loadMoreLinked('leads')}>Load more leads</button>}
          <form className="drawer-form" onSubmit={findLeads}>
            <label>Find existing unlinked lead<input maxLength={150} value={leadSearch}
              placeholder="Name, phone or lead code" onChange={event => setLeadSearch(event.target.value)} /></label>
            <button type="submit" disabled={!leadSearch.trim()}>Find leads</button>
          </form>
          {foundLeads.map(item => <div className="project-task" key={item.id}>
            <div><strong>{item.name}</strong><p>{item.phone} · {item.status}</p></div>
            <button disabled={working} onClick={() => void linkLead(item)}>Link to customer</button>
          </div>)}
        </section>

        <section className="project-section" aria-label="Customer projects">
          <h3>Projects ({detail.projectCount})</h3>
          <p className="field-help">Projects are shown through linked Won leads.</p>
          {detail.projects.map(project => <div className="project-task" key={project.id}>
            <div><strong>{project.title}</strong><p>{project.status} · Project #{project.id}</p></div>
          </div>)}
          {detail.projects.length < detail.projectCount && <button type="button" disabled={loadingLinked}
            onClick={() => void loadMoreLinked('projects')}>Load more projects</button>}
          {!detail.projects.length && <p className="field-help">No linked development projects.</p>}
        </section>
        </>}

        {detailTab === 'merge' && detail.customer.isActive && <section className="project-section" aria-label="Merge customers">
          <h3>Merge another customer</h3>
          <p className="field-help">Keep this customer's details. Move the other customer's
            contacts and linked leads here; their projects stay linked to the same leads.
            The source record becomes read-only. This cannot be undone.</p>
          <form className="drawer-form" onSubmit={reviewMergeSource}
            onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
            <label>Source customer ID *<input type="number" min="1" step="1" required
              value={mergeSourceId} disabled={working} onChange={event => {
                setMergeSourceId(event.target.value);
                setMergeSource(null);
                setMergeReason('');
              }} /></label>
            <button type="submit" disabled={working || !mergeSourceId.trim()}>Review source</button>
          </form>
          {mergeSource && <div className="project-task">
            <div><strong>{mergeSource.customer.name} · #{mergeSource.customer.id}</strong>
              <p>{mergeSource.leadCount} leads · {mergeSource.contacts.length} contacts ·
                {mergeSource.projectCount} projects</p></div>
          </div>}
          {mergeSource && <form className="drawer-form" onSubmit={mergeCustomers}
            onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
            <label>Reason for merge *<textarea required minLength={10} maxLength={500}
              value={mergeReason} onChange={event => setMergeReason(event.target.value)} /></label>
            <button type="submit" className="primary" disabled={working || mergeReason.trim().length < 10}>
              Merge into this customer</button>
          </form>}
        </section>}
        </>}

        {(detail.customer.mergedIntoCustomerId != null || detailTab === 'history') &&
          <section className="project-section" aria-label="Customer activity history">
          <h3>Activity history ({history?.total ?? 0})</h3>
          {history?.items.map(entry => <div key={entry.id} className="project-task">
            <div><strong>{entry.action} · {entry.tableName}</strong>
              <p>{new Date(entry.createdAtUtc).toLocaleString()} · User #{entry.userId}</p></div>
          </div>)}
          {history && history.items.length < history.total &&
            <button type="button" onClick={() => void loadMoreHistory()}>Load more history</button>}
          {history?.items.length === 0 && <p className="field-help">No activity recorded yet.</p>}
        </section>}
      </aside>
    </div>}
  </section>;
}
