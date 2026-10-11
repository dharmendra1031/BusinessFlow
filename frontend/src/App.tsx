import { useState } from 'react';
import { api } from './api';
import { FeedbackProvider, useFeedback } from './Feedback';
import { Icon } from './Icon';
import { DashboardPage, EmployeesPage, FollowUpsPage, LeadsPage } from './pages';
import type { User } from './types';

type Session = { token: string; user: User };
type Screen = 'dashboard' | 'leads' | 'followups' | 'employees';

const navigation = [
  { id: 'dashboard', label: 'Dashboard', icon: 'grid' },
  { id: 'leads', label: 'All leads', icon: 'clipboard' },
  { id: 'followups', label: 'Follow-ups', icon: 'calendar' },
  { id: 'employees', label: 'Team', icon: 'users' },
] as const;

export default function App() {
  return <FeedbackProvider><Workspace /></FeedbackProvider>;
}

function Workspace() {
  const { notify } = useFeedback();
  const [session, setSession] = useState<Session | null>(() => {
    try {
      const saved = sessionStorage.getItem('crmSession');
      return saved ? JSON.parse(saved) as Session : null;
    } catch { return null; }
  });
  const [screen, setScreen] = useState<Screen>('dashboard');
  const [menuOpen, setMenuOpen] = useState(false);
  const [newLeadRequest, setNewLeadRequest] = useState(0);

  function navigate(next: Screen) {
    setScreen(next);
    setMenuOpen(false);
  }

  function logout() {
    sessionStorage.removeItem('crmSession');
    setSession(null);
    setMenuOpen(false);
    notify('info', 'Signed out', 'Your session has ended.');
  }

  if (!session) return <Login onLogin={value => {
    sessionStorage.setItem('crmSession', JSON.stringify(value));
    setSession(value);
    notify('success', 'Welcome back', 'You are signed in.');
  }} />;

  const user = session.user;
  const canSeeTeam = user.roles.includes('Admin') || user.roles.includes('Manager');
  const items = navigation.filter(item => item.id !== 'employees' || canSeeTeam);
  const pageTitle = { dashboard: 'Dashboard', leads: 'All leads', followups: 'Follow-ups', employees: 'Team' }[screen];

  return <div className="app-shell">
    {menuOpen && <button className="mobile-backdrop" aria-label="Close navigation"
      onClick={() => setMenuOpen(false)} />}
    <aside className={'sidebar ' + (menuOpen ? 'sidebar-open' : '')}>
      <div className="brand"><span className="brand-mark"><Icon name="target" size={23} /></span>
        <div><strong>Lead CRM</strong><small>Sales CRM</small></div>
        <button className="icon-button mobile-close" aria-label="Close menu"
          onClick={() => setMenuOpen(false)}><Icon name="close" /></button>
      </div>
      <p className="nav-label">WORKSPACE</p>
      <nav aria-label="Main navigation">{items.map(item =>
        <button key={item.id} className={'nav-link ' + (screen === item.id ? 'active' : '')}
          aria-current={screen === item.id ? 'page' : undefined} onClick={() => navigate(item.id)}>
          <Icon name={item.icon} size={19} /><span>{item.label}</span>
          {screen === item.id && <Icon name="chevron" size={16} className="nav-chevron" />}
        </button>)}</nav>
      <div className="sidebar-bottom">
        <div className="sidebar-user"><span className="avatar">{initials(user.fullName)}</span>
          <div className="user-meta"><strong>{user.fullName}</strong><small>{user.roles.join(', ')}</small></div></div>
        <button className="signout" onClick={logout}><Icon name="logout" size={18} /> Sign out</button>
      </div>
    </aside>
    <div className="app-main">
      <header className="topbar">
        <div className="topbar-left">
          <button className="icon-button menu-button" onClick={() => setMenuOpen(true)}
            aria-label="Open navigation"><Icon name="menu" /></button>
          <div><span className="eyebrow">WORKSPACE / {pageTitle.toUpperCase()}</span>
            <strong>{pageTitle}</strong></div>
        </div>
        <div className="topbar-actions">
          <span className="today">{new Date().toLocaleDateString(undefined, {
            day: 'numeric', month: 'short', year: 'numeric'
          })}</span>
          <button className="primary topbar-add" onClick={() => {
            navigate('leads');
            setNewLeadRequest(value => value + 1);
          }}><Icon name="plus" size={17} /> New lead</button>
          <span className="avatar avatar-small" title={user.fullName}>{initials(user.fullName)}</span>
        </div>
      </header>
      <main className="content" id="main-content">
        {screen === 'dashboard' && <DashboardPage token={session.token} onNavigate={navigate} />}
        {screen === 'leads' && <LeadsPage token={session.token} user={user}
          newLeadRequest={newLeadRequest} onNewLeadOpened={() => setNewLeadRequest(0)} />}
        {screen === 'followups' && <FollowUpsPage token={session.token} />}
        {screen === 'employees' && canSeeTeam && <EmployeesPage token={session.token} user={user} />}
      </main>
    </div>
  </div>;
}

function initials(name: string) {
  return name.trim().split(/\s+/).slice(0, 2).map(word => word[0]?.toUpperCase() || '').join('') || 'U';
}

function Login({ onLogin }: { onLogin: (value: Session) => void }) {
  const { notify } = useFeedback();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState('');
  const [working, setWorking] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (working) return;
    setWorking(true);
    setError('');
    try {
      const result = await api<Session>('/api/auth/login', '', {
        method: 'POST', body: JSON.stringify({ email, password }),
      });
      onLogin(result);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Login failed';
      setError(message);
      notify('error', 'Unable to sign in', message);
    } finally { setWorking(false); }
  }

  return <main className="login-layout">
    <section className="login-intro">
      <div className="brand"><span className="brand-mark"><Icon name="target" size={25} /></span>
        <div><strong>Lead CRM</strong><small>Sales CRM</small></div></div>
      <div className="login-intro-content">
        <span className="intro-label">YOUR SALES WORKSPACE</span>
        <h1>Every lead matters.<br /><em>Every follow-up counts.</em></h1>
        <p>Keep your team aligned, conversations organized, and opportunities moving forward.</p>
        <div className="intro-steps">
          <span><Icon name="check" size={17} /> Manage leads in one place</span>
          <span><Icon name="check" size={17} /> Never miss a follow-up</span>
          <span><Icon name="check" size={17} /> See your team's progress</span>
        </div>
      </div>
      <p className="intro-footer">Lead CRM · Built for focused sales teams</p>
    </section>
    <section className="login-form-side">
      <form className="login-card" onSubmit={submit}>
        <span className="login-overline">WELCOME BACK</span>
        <h2>Sign in to your account</h2>
        <p>Enter your credentials to access your workspace.</p>
        <label>Email address<input type="email" autoComplete="username" placeholder="you@company.com"
          value={email} onChange={event => setEmail(event.target.value)} required /></label>
        <label>Password<div className="password-field"><input type={showPassword ? 'text' : 'password'}
          autoComplete="current-password" placeholder="Enter password"
          value={password} onChange={event => setPassword(event.target.value)} required />
          <button type="button" onClick={() => setShowPassword(value => !value)}
            aria-label={showPassword ? 'Hide password' : 'Show password'}>{showPassword ? 'Hide' : 'Show'}</button>
        </div></label>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary login-submit" disabled={working}>
          {working ? 'Signing in…' : 'Sign in'} <Icon name="arrow" size={18} /></button>
        <small className="login-footnote">Contact your CRM administrator if you need access.</small>
      </form>
    </section>
  </main>;
}
