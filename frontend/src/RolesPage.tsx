import { employeeLookup } from './employeeLookup';
import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import type { Employee } from './types';

type Role = {
  id: number; name: string; isBuiltIn: boolean;
  grants: { permission: string; scope: string }[]; memberIds: number[];
};
type RoleMatrix = { roles: Role[]; permissions: string[]; scopes: string[] };

export default function RolesPage({ token }: { token: string }) {
  const { notify, confirm } = useFeedback();
  const [matrix, setMatrix] = useState<RoleMatrix | null>(null);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [name, setName] = useState('');
  const [rename, setRename] = useState('');
  const [roleId, setRoleId] = useState('');
  const [action, setAction] = useState('leads.view');
  const [scope, setScope] = useState('Self');
  const [memberId, setMemberId] = useState('');
  const [restrictedId, setRestrictedId] = useState('');
  const [denied, setDenied] = useState<string[]>([]);
  const [working, setWorking] = useState(false);
  const role = matrix?.roles.find(x => x.id === Number(roleId));

  const load = useCallback(async () => {
    const [settings, people] = await Promise.all([
      api<RoleMatrix>('/api/admin/roles', token), employeeLookup(token, 'roles.manage'),
    ]);
    setMatrix(settings); setEmployees(people);
  }, [token]);
  useEffect(() => { void load().catch(error => notify('error', 'Unable to load roles', String(error))); }, [load, notify]);

  async function mutate(path: string, method: string, body?: unknown) {
    if (working) return null;
    setWorking(true);
    try {
      const result = await api<{ id: number }>('/api/admin/roles' + path, token, {
        method, ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      });
      await load();
      notify('success', 'Access settings updated');
      return result;
    } catch (error) {
      notify('error', 'Unable to update access', error instanceof Error ? error.message : undefined);
      return null;
    } finally { setWorking(false); }
  }

  async function create(event: FormEvent) {
    event.preventDefault();
    const created = await mutate('', 'POST', { name });
    if (created) { setRoleId(created.id.toString()); setRename(name.trim()); setName(''); }
  }

  useEffect(() => {
    setDenied([]);
    if (!restrictedId) return;
    let current = true;
    void api<{ denied: string[] }>('/api/admin/roles/employees/' + restrictedId + '/restrictions', token)
      .then(result => { if (current) setDenied(result.denied); })
      .catch(error => { if (current) notify('error', 'Unable to load employee restrictions', String(error)); });
    return () => { current = false; };
  }, [restrictedId, token, notify]);

  async function restrict(permission: string, blocked: boolean) {
    const id = restrictedId;
    const result = await mutate('/employees/' + id + '/restrictions/' + permission, 'PUT', { denied: blocked });
    if (result) setDenied(current => blocked ? [...current, permission] : current.filter(x => x !== permission));
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">ADMINISTRATION</span>
      <h1>Custom roles</h1><p>Grant actions with a data scope and restrict individual employee access.</p></div></header>
    {!matrix && <p role="status">Loading roles…</p>}
    {matrix && <>
      <section className="panel" aria-label="Custom role settings">
        <div className="panel-heading"><h2>Roles</h2></div>
        <p>New roles have no access. Built-in Admin, Manager and Employee roles are protected.</p>
        <form className="form-grid" onSubmit={event => void create(event)}>
          <label>Role name<input required minLength={2} maxLength={80} value={name} onChange={event => setName(event.target.value)} /></label>
          <button className="primary" disabled={working}>Create role</button>
        </form>
        <label>Custom role<select aria-label="Custom role" value={roleId} disabled={working} onChange={event => {
          setRoleId(event.target.value); setRename(matrix.roles.find(x => x.id === Number(event.target.value))?.name ?? '');
        }}><option value="">Select role</option>{matrix.roles.filter(x => !x.isBuiltIn).map(item =>
          <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        {role && <>
          <form className="form-grid" onSubmit={event => { event.preventDefault(); void mutate('/' + role.id, 'PUT', { name: rename }); }}>
            <label>Rename role<input required minLength={2} maxLength={80} value={rename} onChange={event => setRename(event.target.value)} /></label>
            <button disabled={working}>Rename role</button>
            <button type="button" disabled={working} onClick={() => void (async () => {
              if (await confirm({ title: 'Delete role?', message: 'Remove all memberships before deleting ' + role.name + '.', confirmText: 'Delete role', danger: true })) {
                if (await mutate('/' + role.id, 'DELETE')) setRoleId('');
              }
            })()}>Delete role</button>
          </form>
          <form className="form-grid" onSubmit={event => { event.preventDefault(); void mutate('/' + role.id + '/grants/' + action, 'PUT', { scope }); }}>
            <label>Action<select aria-label="Action" value={action} onChange={event => setAction(event.target.value)}>
              {matrix.permissions.map(code => <option key={code} value={code}>{code}</option>)}</select></label>
            <label>Data scope<select aria-label="Data scope" value={scope} onChange={event => setScope(event.target.value)}>
              {matrix.scopes.map(value => <option key={value} value={value}>{value}</option>)}</select></label>
            <button className="primary" disabled={working}>Save grant</button>
          </form>
          <div className="table-scroll"><table><thead><tr><th>Action</th><th>Scope</th><th>Access</th></tr></thead>
            <tbody>{role.grants.map(grant => <tr key={grant.permission}><td>{grant.permission}</td><td>{grant.scope}</td>
              <td><button aria-label={'Revoke ' + grant.permission} disabled={working} onClick={() =>
                void mutate('/' + role.id + '/grants/' + grant.permission, 'PUT', { scope: null })}>Revoke</button></td></tr>)}</tbody></table></div>
          <h3>Role membership</h3><p>Changing role membership signs the employee out.</p>
          <form className="form-grid" onSubmit={event => { event.preventDefault(); void mutate('/' + role.id + '/members/' + memberId, 'PUT', { assigned: true }); }}>
            <label>Role employee<select aria-label="Role employee" required value={memberId} onChange={event => setMemberId(event.target.value)}>
              <option value="">Select employee</option>{employees.filter(x => x.isActive && !role.memberIds.includes(x.id)).map(item =>
                <option key={item.id} value={item.id}>{item.fullName} ({item.email})</option>)}</select></label>
            <button disabled={working}>Assign role</button>
          </form>
          <ul>{role.memberIds.map(id => <li key={id}>{employees.find(x => x.id === id)?.fullName ?? 'Employee ' + id}{' '}
            <button disabled={working} aria-label={'Remove role from employee ' + id} onClick={() =>
              void mutate('/' + role.id + '/members/' + id, 'PUT', { assigned: false })}>Remove</button></li>)}</ul>
        </>}
      </section>
      <section className="panel" aria-label="Employee restrictions">
        <div className="panel-heading"><h2>Employee restrictions</h2></div>
        <p>Restrictions only remove access. Unchecking a restriction restores the employee’s role permissions.</p>
        <label>Restricted employee<select aria-label="Restricted employee" value={restrictedId} disabled={working} onChange={event => setRestrictedId(event.target.value)}>
          <option value="">Select employee</option>{employees.filter(x => !x.roles?.includes('Admin')).map(item =>
            <option key={item.id} value={item.id}>{item.fullName} ({item.email})</option>)}</select></label>
        {restrictedId && <div className="table-scroll"><table><thead><tr><th>Action</th><th>Restriction</th></tr></thead>
          <tbody>{matrix.permissions.map(code => <tr key={code}><td>{code}</td><td><label>
            <input type="checkbox" aria-label={'Block ' + code} disabled={working} checked={denied.includes(code)}
              onChange={event => void restrict(code, event.target.checked)} />Blocked</label></td></tr>)}</tbody></table></div>}
      </section>
    </>}
  </section>;
}
