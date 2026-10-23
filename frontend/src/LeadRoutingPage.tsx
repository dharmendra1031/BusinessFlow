import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';

type Source = { id: number; name: string; isActive: boolean };
type Department = Source;
type Team = Source & { departmentId: number };
type Organization = { departments: Department[]; teams: Team[] };
type Rule = { id: number; sourceId: number; teamId: number; maxOpenLeads: number; isActive: boolean };

export default function LeadRoutingPage({ token }: { token: string }) {
  const { notify, confirm } = useFeedback();
  const [rules, setRules] = useState<Rule[]>([]);
  const [sources, setSources] = useState<Source[]>([]);
  const [organization, setOrganization] = useState<Organization>({ departments: [], teams: [] });
  const [editing, setEditing] = useState<number | null>(null);
  const [sourceId, setSourceId] = useState('');
  const [teamId, setTeamId] = useState('');
  const [maxOpenLeads, setMaxOpenLeads] = useState(30);
  const [isActive, setIsActive] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    const [policies, origins, org] = await Promise.all([
      api<Rule[]>('/api/admin/lead-routing', token),
      api<Source[]>('/api/admin/lead-sources', token),
      api<Organization>('/api/admin/organization', token),
    ]);
    setRules(policies);
    setSources(origins);
    setOrganization(org);
    setError('');
  }, [token]);
  useEffect(() => {
    void load().catch(err => setError(err instanceof Error ? err.message : 'Unable to load routing rules.'));
  }, [load]);

  function reset() {
    setEditing(null);
    setSourceId('');
    setTeamId('');
    setMaxOpenLeads(30);
    setIsActive(false);
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (working || !sourceId || !teamId || maxOpenLeads < 1 || maxOpenLeads > 500) return;
    setWorking(true);
    try {
      await api('/api/admin/lead-routing' + (editing !== null ? '/' + editing : ''), token, {
        method: editing !== null ? 'PUT' : 'POST',
        body: JSON.stringify({
          sourceId: Number(sourceId), teamId: Number(teamId), maxOpenLeads, isActive,
        }),
      });
      await load();
      reset();
      notify('success', 'Routing rule saved');
    } catch (err) {
      notify('error', 'Unable to save routing rule', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function toggle(rule: Rule) {
    const title = rule.isActive ? 'Disable routing?' : 'Enable routing?';
    if (!await confirm({
      title, confirmText: rule.isActive ? 'Disable' : 'Enable',
      message: rule.isActive
        ? 'New website leads will revert to the configured intake owner.'
        : 'New leads for this source will be routed to eligible members of the selected team.',
    })) return;
    setWorking(true);
    try {
      await api('/api/admin/lead-routing/' + rule.id, token, {
        method: 'PUT', body: JSON.stringify({ ...rule, isActive: !rule.isActive }),
      });
      await load();
      if (editing === rule.id) reset();
      notify('success', 'Routing rule updated');
    } catch (err) {
      notify('error', 'Unable to change routing rule', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  const availableSources = sources.filter(s => s.isActive || Number(sourceId) === s.id);
  const availableTeams = organization.teams.filter(t =>
    (t.isActive && organization.departments.some(d => d.id === t.departmentId && d.isActive)) ||
    Number(teamId) === t.id);
  const sourceName = (id: number) => sources.find(s => s.id === id)?.name ?? '#' + id;
  const teamName = (id: number) => organization.teams.find(t => t.id === id)?.name ?? '#' + id;

  return <section>
    <header className="page-header"><div><span className="section-kicker">ADMINISTRATION</span>
      <h1>Lead routing</h1><p>Assign incoming website leads by source, active team and open workload.</p>
    </div></header>
    <section className="panel" aria-label="Routing rule editor">
      <div className="panel-heading"><h2>{editing !== null ? 'Edit routing rule' : 'New routing rule'}</h2></div>
      {error && <p role="alert">{error}</p>}
      <form className="form-grid" onSubmit={event => void save(event)}>
        <label>Lead source<select required value={sourceId} onChange={event => setSourceId(event.target.value)}>
          <option value="">Select source</option>{availableSources
            .filter(x => editing !== null || !rules.some(r => r.sourceId === x.id))
            .map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <label>Target team<select required value={teamId} onChange={event => setTeamId(event.target.value)}>
          <option value="">Select team</option>{availableTeams.map(t =>
            <option key={t.id} value={t.id}>{t.name} · {organization.departments.find(d => d.id === t.departmentId)?.name}</option>)}
        </select></label>
        <label>Maximum open leads per employee
          <input type="number" required min={1} max={500} value={maxOpenLeads}
            onChange={event => setMaxOpenLeads(Number(event.target.value))} /></label>
        <label><input type="checkbox" checked={isActive}
          onChange={event => setIsActive(event.target.checked)} /> Enable automatic routing</label>
        <div><button className="primary" disabled={working || !sourceId || !teamId}>Save routing rule</button>
          {editing !== null && <button type="button" disabled={working} onClick={reset}>Cancel edit</button>}</div>
      </form>
      <p>Routing applies to new website submissions only. Existing Leads and manual assignments are unaffected.</p>
    </section>
    <section className="panel" aria-label="Routing rules">
      <div className="panel-heading"><h2>Existing routing rules</h2></div>
      <div className="table-scroll"><table><thead><tr>
        <th>Source</th><th>Team</th><th>Max open</th><th>Status</th><th>Actions</th>
      </tr></thead><tbody>{rules.map(rule => <tr key={rule.id}>
        <td>{sourceName(rule.sourceId)}</td><td>{teamName(rule.teamId)}</td>
        <td>{rule.maxOpenLeads}</td><td>{rule.isActive ? 'Enabled' : 'Disabled'}</td>
        <td><button type="button" disabled={working} onClick={() => {
          setEditing(rule.id); setSourceId(String(rule.sourceId));
          setTeamId(String(rule.teamId)); setMaxOpenLeads(rule.maxOpenLeads);
          setIsActive(rule.isActive);
        }}>Edit</button>
        <button type="button" disabled={working} onClick={() => void toggle(rule)}>
          {rule.isActive ? 'Disable' : 'Enable'}</button></td>
      </tr>)}</tbody></table></div>
      {rules.length === 0 && <p>No routing rules configured. Website leads use the existing intake owner.</p>}
    </section>
  </section>;
}
