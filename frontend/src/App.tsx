import { useState } from 'react';
import { api } from './api';
import { DashboardPage, EmployeesPage, FollowUpsPage, LeadsPage } from './pages';
import type { User } from './types';

type Session = { token: string; user: User };
type Screen = 'dashboard' | 'leads' | 'followups' | 'employees';

export default function App() {
  const [session, setSession] = useState<Session | null>(() => {
    const saved = sessionStorage.getItem('crmSession');
    try { return saved ? JSON.parse(saved) as Session : null; }
    catch { return null; }
  });
  const [screen, setScreen] = useState<Screen>('dashboard');

  function logout() {
    sessionStorage.removeItem('crmSession');
    setSession(null);
  }

  if (!session) return <Login onLogin={data => {
    sessionStorage.setItem('crmSession', JSON.stringify(data));
    setSession(data);
  }} />;

  const nav: { id: Screen; label: string }[] = [
    { id: 'dashboard', label: 'Dashboard' },
    { id: 'leads', label: 'Leads' },
    { id: 'followups', label: 'Follow-ups' },
    ...(session.user.roles.includes('Admin') || session.user.roles.includes('Manager')
      ? [{ id: 'employees' as Screen, label: 'Employees' }] : []),
  ];

  return (
    <div className="shell">
      <aside className="sidebar">
        <h2>Lead CRM</h2>
        <div className="sidehint">Sales workspace</div>
        <nav>{nav.map(item => <button type="button" key={item.id}
          className={screen === item.id ? 'active' : ''}
          onClick={() => setScreen(item.id)}>{item.label}</button>)}</nav>
        <div className="sidefoot"><strong>{session.user.fullName}</strong>
          <small>{session.user.roles.join(', ')}</small>
          <button type="button" onClick={logout}>Sign out</button></div>
      </aside>
      <main className="content">
        {screen === 'dashboard' && <DashboardPage token={session.token} />}
        {screen === 'leads' && <LeadsPage token={session.token} user={session.user} />}
        {screen === 'followups' && <FollowUpsPage token={session.token} />}
        {screen === 'employees' && <EmployeesPage token={session.token} user={session.user} />}
      </main>
    </div>
  );
}

function Login({ onLogin }: { onLogin: (value: Session) => void }) {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [working, setWorking] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setWorking(true);
    setError('');
    try {
      const result = await api<Session>('/api/auth/login', '', {
        method: 'POST', body: JSON.stringify({ email, password }),
      });
      onLogin(result);
    } catch (err) { setError(err instanceof Error ? err.message : 'Login failed'); }
    finally { setWorking(false); }
  }

  return (
    <main className="login">
      <form className="panel login-card" onSubmit={submit}>
        <h1>Lead Management</h1>
        <p>Sign in to your sales workspace.</p>
        <label>Email<input type="email" value={email} onChange={e => setEmail(e.target.value)} required /></label>
        <label>Password<input type="password" value={password} onChange={e => setPassword(e.target.value)} required /></label>
        {error && <p className="error">{error}</p>}
        <button className="primary" disabled={working}>{working ? 'Signing in...' : 'Sign in'}</button>
      </form>
    </main>
  );
}
