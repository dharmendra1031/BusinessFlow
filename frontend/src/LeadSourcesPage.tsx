import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';

type LeadSource = { id: number; name: string; isActive: boolean };

export default function LeadSourcesPage({ token }: { token: string }) {
  const { notify, confirm } = useFeedback();
  const [sources, setSources] = useState<LeadSource[]>([]);
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);
  const [editing, setEditing] = useState<number | null>(null);
  const [name, setName] = useState('');

  const load = useCallback(async () => {
    try {
      setSources(await api<LeadSource[]>('/api/admin/lead-sources', token));
    } catch (error) {
      notify('error', 'Unable to load lead sources', error instanceof Error ? error.message : undefined);
    } finally {
      setLoading(false);
    }
  }, [token, notify]);

  useEffect(() => { void load(); }, [load]);

  function reset() {
    setEditing(null);
    setName('');
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (working) return;
    const source = sources.find(item => item.id === editing);
    setWorking(true);
    try {
      await api('/api/admin/lead-sources' + (source ? '/' + source.id : ''), token, {
        method: source ? 'PUT' : 'POST',
        body: JSON.stringify({ name: name.trim(), isActive: source?.isActive ?? true }),
      });
      reset();
      await load();
      notify('success', source ? 'Lead source updated' : 'Lead source added');
    } catch (error) {
      notify('error', 'Unable to save lead source', error instanceof Error ? error.message : undefined);
    } finally {
      setWorking(false);
    }
  }

  async function toggle(source: LeadSource) {
    if (working) return;
    if (source.isActive && !(await confirm({
      title: 'Deactivate lead source?',
      message: source.name + ' will no longer be available for new leads. Existing leads will retain it.',
      confirmText: 'Deactivate',
    }))) return;
    setWorking(true);
    try {
      await api('/api/admin/lead-sources/' + source.id, token, {
        method: 'PUT',
        body: JSON.stringify({ name: source.name, isActive: !source.isActive }),
      });
      await load();
      notify('success', source.isActive ? 'Source deactivated' : 'Source activated');
    } catch (error) {
      notify('error', 'Unable to update lead source', error instanceof Error ? error.message : undefined);
    } finally {
      setWorking(false);
    }
  }

  return <section>
    <header className="page-header"><div>
      <span className="section-kicker">ADMINISTRATION</span>
      <h1>Lead sources</h1>
      <p>Manage where leads come from. Deactivated sources stay on existing leads.</p>
    </div></header>
    <section className="panel" aria-label="Manage lead sources">
      <div className="panel-heading"><h2>{editing ? 'Edit lead source' : 'Add lead source'}</h2></div>
      <form className="form-grid" onSubmit={event => void save(event)}>
        <label>Source name<input required autoFocus minLength={2} maxLength={60}
          value={name} onChange={event => setName(event.target.value)} /></label>
        <div>
          <button className="primary" disabled={working}>{editing ? 'Save source' : 'Add source'}</button>
          {editing !== null && <button type="button" disabled={working} onClick={reset}>Cancel edit</button>}
        </div>
      </form>
    </section>
    <section className="panel" aria-label="Existing lead sources">
      <div className="panel-heading"><h2>Existing lead sources</h2></div>
      {loading ? <p role="status">Loading lead sources…</p> :
        <div className="table-scroll"><table>
          <thead><tr><th>Source</th><th>Status</th><th>Actions</th></tr></thead>
          <tbody>{sources.map(source => <tr key={source.id}>
            <td>{source.name}</td>
            <td>{source.isActive ? 'Active' : 'Inactive'}</td>
            <td>
              <button type="button" disabled={working} onClick={() => {
                setEditing(source.id); setName(source.name);
              }}>Edit</button>
              <button type="button" disabled={working} onClick={() => void toggle(source)}>
                {source.isActive ? 'Deactivate' : 'Activate'}
              </button>
            </td>
          </tr>)}</tbody>
        </table></div>}
    </section>
  </section>;
}
