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
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');
  const [newName, setNewName] = useState('');
  const [newEmail, setNewEmail] = useState('');
  const [newPhone, setNewPhone] = useState('');
  const [newBilling, setNewBilling] = useState<CustomerBilling>(emptyCustomerBilling);
  const [editBilling, setEditBilling] = useState<CustomerBilling>(emptyCustomerBilling);
  const [history, setHistory] = useState<Paged<CustomerHistory> | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [editName, setEditName] = useState('');
  const [editEmail, setEditEmail] = useState('');
  const [editPhone, setEditPhone] = useState('');
  const [editAddress, setEditAddress] = useState('');
  const [editActive, setEditActive] = useState(true);
  const [contact, setContact] = useState<ContactForm>(blankContact);
  const [editingContact, setEditingContact] = useState<number | null>(null);
  const [leadSearch, setLeadSearch] = useState('');
  const [foundLeads, setFoundLeads] = useState<LeadSearch[]>([]);

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
      const [current, recentHistory] = await Promise.all([
        api<Detail>('/api/admin/customers/' + id, token),
        api<Paged<CustomerHistory>>('/api/admin/customers/' + id + '/history', token),
      ]);
      setDetail(current);
      setHistory(recentHistory);
      setHistoryPage(1);
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
    setHistory(null);
    setHistoryPage(1);
    setEditingContact(null);
    setContact(blankContact());
    setLeadSearch('');
    setFoundLeads([]);
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

  return <section>
    <header className="page-header"><div>
      <span className="section-kicker">CLIENT RELATIONSHIPS</span>
      <h1>Customer master</h1>
      <p>One customer record for contacts, leads and multiple development projects.</p>
    </div></header>

    <section className="panel" aria-label="Add customer">
      <div className="panel-heading"><h2>Add customer</h2><p>Duplicate phone, email or GSTIN requires review. Leads are never linked automatically.</p></div>
      <form className="form-grid" onSubmit={createCustomer} onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
        <label>Customer / company name *<input required minLength={2} maxLength={200}
          value={newName} onChange={event => setNewName(event.target.value)} /></label>
        <label>Email<input type="email" maxLength={256}
          value={newEmail} onChange={event => setNewEmail(event.target.value)} /></label>
        <label>Phone<input type="tel" maxLength={30}
          value={newPhone} onChange={event => setNewPhone(event.target.value)} /></label>
        <CustomerBillingFields value={newBilling} onChange={setNewBilling} />
        <div><button className="primary" disabled={working}>Add customer</button></div>
      </form>
    </section>

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
            <td>{customer.isActive ? 'Active' : 'Inactive'}</td>
            <td><button type="button" onClick={() => void openCustomer(customer.id)}>View customer</button></td>
          </tr>)}</tbody>
        </table></div> : <p className="empty-state">No customers found.</p>}
      <div className="pagination"><span>Page {page} · {total} customers</span>
        <button disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
        <button disabled={page * 30 >= total} onClick={() => setPage(value => value + 1)}>Next</button>
      </div>
    </section>

    {detail && <div className="drawer-backdrop">
      <aside className="detail-drawer" role="dialog" aria-modal="true" aria-label={'Customer ' + detail.customer.name}
        onKeyDown={event => handleDialogKeyDown(event, () => { if (!working) close(); })}>
        <div className="drawer-header"><div><span className="section-kicker">CUSTOMER DETAILS</span>
          <h2>{detail.customer.name}</h2>
          <p>{detail.customer.isActive ? 'Active' : 'Inactive'} · Customer #{detail.customer.id}</p></div>
          <button className="icon-button" aria-label="Close customer" disabled={working}
            onClick={close}><Icon name="close" /></button>
        </div>

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

        <section className="project-section" aria-label="Customer contacts">
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

        <section className="project-section" aria-label="Linked leads">
          <h3>Linked leads ({detail.leadCount})</h3>
          <p className="field-help">Showing latest 100 leads. Linking does not change the lead status or owner.</p>
          {detail.leads.map(item => <div className="project-task" key={item.id}>
            <div><strong>{item.name}</strong><p>{item.status} · Lead #{item.id}</p></div>
            <button type="button" disabled={working} onClick={() => void unlinkLead(item)}>Unlink</button>
          </div>)}
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
          <p className="field-help">Showing latest 100 projects through linked Won leads.</p>
          {detail.projects.map(project => <div className="project-task" key={project.id}>
            <div><strong>{project.title}</strong><p>{project.status} · Project #{project.id}</p></div>
          </div>)}
          {!detail.projects.length && <p className="field-help">No linked development projects.</p>}
        </section>

        <section className="project-section" aria-label="Customer activity history">
          <h3>Activity history ({history?.total ?? 0})</h3>
          {history?.items.map(entry => <div key={entry.id} className="project-task">
            <div><strong>{entry.action} · {entry.tableName}</strong>
              <p>{new Date(entry.createdAtUtc).toLocaleString()} · User #{entry.userId}</p></div>
          </div>)}
          {history && history.items.length < history.total &&
            <button type="button" onClick={() => void loadMoreHistory()}>Load more history</button>}
          {history?.items.length === 0 && <p className="field-help">No activity recorded yet.</p>}
        </section>
      </aside>
    </div>}
  </section>;
}
