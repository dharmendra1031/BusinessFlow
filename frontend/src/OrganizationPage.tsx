import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import type { Employee } from './types';

type Department = { id: number; name: string; isActive: boolean };
type Team = Department & { departmentId: number };
type Organization = {
  departments: Department[]; teams: Team[];
  memberships: { id: number; teamId: number; userId: number }[];
};

export default function OrganizationPage({ token }: { token: string }) {
  const { notify } = useFeedback();
  const [data, setData] = useState<Organization | null>(null);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [working, setWorking] = useState(false);
  const [departmentId, setDepartmentId] = useState<number | null>(null);
  const [departmentName, setDepartmentName] = useState('');
  const [teamId, setTeamId] = useState<number | null>(null);
  const [teamName, setTeamName] = useState('');
  const [teamDepartment, setTeamDepartment] = useState('');
  const [employeeId, setEmployeeId] = useState('');
  const [employeeDepartment, setEmployeeDepartment] = useState('');
  const [memberships, setMemberships] = useState<number[]>([]);
  const [managerId, setManagerId] = useState('');

  const load = useCallback(async () => {
    const [organization, people] = await Promise.all([
      api<Organization>('/api/admin/organization', token), api<Employee[]>('/api/employees', token),
    ]);
    setData(organization);
    setEmployees(people);
  }, [token]);
  useEffect(() => { void load().catch(error => notify('error', 'Unable to load organization', String(error))); }, [load, notify]);

  async function save(path: string, body: unknown, method = 'PUT') {
    if (working) return false;
    setWorking(true);
    try {
      await api('/api/admin/organization/' + path, token, { method, body: JSON.stringify(body) });
      await load();
      notify('success', 'Organization updated');
      return true;
    } catch (error) {
      notify('error', 'Unable to update organization', error instanceof Error ? error.message : undefined);
      return false;
    } finally { setWorking(false); }
  }

  async function saveDepartment(event: FormEvent) {
    event.preventDefault();
    const current = data?.departments.find(x => x.id === departmentId);
    if (await save('departments' + (departmentId ? '/' + departmentId : ''),
      { name: departmentName, isActive: current?.isActive ?? true }, departmentId ? 'PUT' : 'POST')) {
      setDepartmentId(null); setDepartmentName('');
    }
  }

  async function saveTeam(event: FormEvent) {
    event.preventDefault();
    const current = data?.teams.find(x => x.id === teamId);
    if (await save('teams' + (teamId ? '/' + teamId : ''),
      { name: teamName, departmentId: Number(teamDepartment), isActive: current?.isActive ?? true }, teamId ? 'PUT' : 'POST')) {
      setTeamId(null); setTeamName('');
    }
  }

  function selectEmployee(value: string) {
    const employee = employees.find(x => x.id === Number(value));
    setEmployeeId(value);
    setEmployeeDepartment(employee?.departmentId?.toString() ?? '');
    setMemberships(data?.memberships.filter(x => x.userId === Number(value)).map(x => x.teamId) ?? []);
    setManagerId(employee?.managerId?.toString() ?? '');
  }

  return <section>
    <header className="page-header"><div><span className="section-kicker">ADMINISTRATION</span>
      <h1>Organization</h1><p>Manage departments, teams, memberships and reporting relationships.</p></div></header>
    {!data && <p role="status">Loading organization…</p>}
    {data && <>
      <section className="panel" aria-label="Departments">
        <div className="panel-heading"><h2>Departments</h2></div>
        <form onSubmit={event => void saveDepartment(event)} className="form-grid">
          <label>Department name<input value={departmentName} required minLength={2} maxLength={150}
            onChange={event => setDepartmentName(event.target.value)} /></label>
          <div><button className="primary" disabled={working}>Save department</button>
            {departmentId && <button type="button" onClick={() => { setDepartmentId(null); setDepartmentName(''); }}>Cancel edit</button>}</div>
        </form>
        <div className="table-scroll"><table><thead><tr><th>Name</th><th>Status</th><th>Actions</th></tr></thead>
          <tbody>{data.departments.map(item => <tr key={item.id}><td>{item.name}</td><td>{item.isActive ? 'Active' : 'Inactive'}</td>
            <td><button disabled={working} onClick={() => { setDepartmentId(item.id); setDepartmentName(item.name); }}>Edit</button>
              <button disabled={working} onClick={() => void save('departments/' + item.id, { name: item.name, isActive: !item.isActive })}>
                {item.isActive ? 'Deactivate' : 'Activate'}</button></td></tr>)}</tbody></table></div>
      </section>
      <section className="panel" aria-label="Teams">
        <div className="panel-heading"><h2>Teams</h2></div>
        <form onSubmit={event => void saveTeam(event)} className="form-grid">
          <label>Team name<input required minLength={2} maxLength={150} value={teamName} onChange={event => setTeamName(event.target.value)} /></label>
          <label>Team department<select required value={teamDepartment} onChange={event => setTeamDepartment(event.target.value)}>
            <option value="">Select department</option>{data.departments.filter(x => x.isActive || x.id === Number(teamDepartment))
              .map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
          <div><button className="primary" disabled={working}>Save team</button>
            {teamId && <button type="button" onClick={() => { setTeamId(null); setTeamName(''); }}>Cancel edit</button>}</div>
        </form>
        <div className="table-scroll"><table><thead><tr><th>Name</th><th>Department</th><th>Status</th><th>Actions</th></tr></thead>
          <tbody>{data.teams.map(item => <tr key={item.id}><td>{item.name}</td><td>{data.departments.find(x => x.id === item.departmentId)?.name}</td>
            <td>{item.isActive ? 'Active' : 'Inactive'}</td><td><button disabled={working} onClick={() => {
              setTeamId(item.id); setTeamName(item.name); setTeamDepartment(item.departmentId.toString());
            }}>Edit</button><button disabled={working} onClick={() => void save('teams/' + item.id, {
              name: item.name, departmentId: item.departmentId, isActive: !item.isActive,
            })}>{item.isActive ? 'Deactivate' : 'Activate'}</button></td></tr>)}</tbody></table></div>
      </section>
      <section className="panel" aria-label="Membership and reporting">
        <div className="panel-heading"><h2>Membership and reporting</h2></div>
        <label>Employee<select value={employeeId} onChange={event => selectEmployee(event.target.value)}>
          <option value="">Select employee</option>{employees.filter(x => x.isActive).map(item =>
            <option key={item.id} value={item.id}>{item.fullName} ({item.email})</option>)}</select></label>
        {employeeId && <>
          <form className="form-grid" onSubmit={event => { event.preventDefault(); void save('employees/' + employeeId + '/membership', {
            departmentId: employeeDepartment ? Number(employeeDepartment) : null, teamIds: memberships,
          }); }}>
            <label>Employee department<select value={employeeDepartment} onChange={event => {
              setEmployeeDepartment(event.target.value); setMemberships([]);
            }}><option value="">No department</option>{data.departments.filter(x => x.isActive).map(item =>
              <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
            <fieldset><legend>Team memberships</legend>{data.teams.filter(x => x.isActive && x.departmentId === Number(employeeDepartment)).map(item =>
              <label key={item.id}><input type="checkbox" checked={memberships.includes(item.id)} onChange={event =>
                setMemberships(current => event.target.checked ? [...current, item.id] : current.filter(id => id !== item.id))} />{item.name}</label>)}</fieldset>
            <p>Saving replaces the employee’s department and team memberships together.</p>
            <button className="primary" disabled={working}>Save membership</button>
          </form>
          <form className="form-grid" onSubmit={event => { event.preventDefault(); void save('employees/' + employeeId + '/manager', {
            managerId: managerId ? Number(managerId) : null,
          }); }}><label>Reporting manager<select value={managerId} onChange={event => setManagerId(event.target.value)}>
            <option value="">No reporting manager</option>{employees.filter(x => x.isActive && x.id !== Number(employeeId)).map(item =>
              <option key={item.id} value={item.id}>{item.fullName} ({item.email})</option>)}</select></label>
            <button className="primary" disabled={working}>Save reporting manager</button></form>
        </>}
      </section>
    </>}
  </section>;
}
