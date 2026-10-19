import { useEffect, useState } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';

type Permission = { code: string; supported: boolean; enabled: boolean };
type RoleSettings = { role: string; permissions: Permission[] };
type PermissionMatrix = { roles: RoleSettings[] };

export default function PermissionsPage({ token }: { token: string }) {
  const { notify } = useFeedback();
  const [matrix, setMatrix] = useState<PermissionMatrix | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState('');

  useEffect(() => {
    void api<PermissionMatrix>('/api/admin/permissions', token).then(setMatrix)
      .catch(error => notify('error', 'Unable to load permissions',
        error instanceof Error ? error.message : undefined))
      .finally(() => setLoading(false));
  }, [token, notify]);

  async function change(role: string, permission: Permission, enabled: boolean) {
    const key = role + permission.code;
    setSaving(key);
    try {
      await api('/api/admin/permissions/' + role + '/' + permission.code, token, {
        method: 'PUT', body: JSON.stringify({ enabled }),
      });
      setMatrix(previous => previous && ({
        roles: previous.roles.map(item => ({
          ...item, permissions: item.role === role
            ? item.permissions.map(entry => entry.code === permission.code ? { ...entry, enabled } : entry)
            : item.permissions,
        })),
      }));
      notify('success', role + ' permission updated');
    } catch (error) {
      notify('error', 'Permission update failed', error instanceof Error ? error.message : undefined);
    } finally { setSaving(''); }
  }

  return <section>
    <header className="page-header">
      <div><span className="section-kicker">ADMINISTRATION</span>
        <h1>Role permissions</h1>
        <p>Limit access by module and action. Existing role security cannot be expanded here.</p></div>
    </header>
    {loading && <div className="loading-state">Loading role permissions…</div>}
    {matrix?.roles.map(role => <section className="panel" key={role.role}>
      <div className="panel-heading"><h2>{role.role}</h2></div>
      <div className="table-scroll"><table>
        <thead><tr><th>Module / Action</th><th>Access</th></tr></thead>
        <tbody>{role.permissions.map(permission => <tr key={permission.code}>
          <td>{permission.code.split('.').join(' / ')}</td>
          <td><label>
            <input type="checkbox" aria-label={role.role + ' ' + permission.code}
              checked={permission.enabled} disabled={!permission.supported || !!saving}
              onChange={event => void change(role.role, permission, event.target.checked)} />
            {permission.supported ? (permission.enabled ? 'Allowed' : 'Blocked') : 'Not available for role'}
          </label></td>
        </tr>)}</tbody>
      </table></div>
    </section>)}
  </section>;
}
