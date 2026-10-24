import ApprovalsPage from './ApprovalsPage';
import { useEffect, useState } from 'react';
import { api, setCsrfToken } from './api';
import { clearFieldError, handleFormKeyDown, validateForm } from './formUtils';
import { FeedbackProvider, useFeedback } from './Feedback';
import { Icon } from './Icon';
import { DashboardPage, EmployeesPage, FollowUpsPage, LeadsPage } from './pages';
import ReportsPage from './ReportsPage';
import AuditPage from './AuditPage';
import PermissionsPage from './PermissionsPage';
import ProjectsPage from './ProjectsPage';
import PasswordSettings from './PasswordSettings';
import OrganizationPage from './OrganizationPage';
import RolesPage from './RolesPage';
import LeadSourcesPage from './LeadSourcesPage';
import LeadDuplicatesPage from './LeadDuplicatesPage';
import LeadRoutingPage from './LeadRoutingPage';
import type { User } from './types';

type Session = { token: string; user: User };
type CurrentSession = User & { csrfToken: string };
type Screen = 'dashboard' | 'leads' | 'projects' | 'followups' | 'employees' | 'reports' | 'audit' | 'permissions' | 'roles' | 'organization' | 'approvals' | 'sources' | 'duplicates' | 'routing' | 'settings';

const navigation = [
  { id: 'dashboard', label: 'Dashboard', icon: 'grid' },
  { id: 'approvals', label: 'Lead approvals', icon: 'clipboard' },
  { id: 'leads', label: 'All leads', icon: 'clipboard' },
  { id: 'projects', label: 'Development projects', icon: 'briefcase' },
  { id: 'followups', label: 'Follow-ups', icon: 'calendar' },
  { id: 'employees', label: 'Team', icon: 'users' },
  { id: 'reports', label: 'Reports', icon: 'clipboard' },
  { id: 'audit', label: 'Audit history', icon: 'clipboard' },
  { id: 'permissions', label: 'Role permissions', icon: 'lock' },
  { id: 'organization', label: 'Organization', icon: 'users' },
  { id: 'roles', label: 'Custom roles', icon: 'lock' },
  { id: 'sources', label: 'Lead sources', icon: 'clipboard' },
  { id: 'duplicates', label: 'Duplicate review', icon: 'clipboard' },
  { id: 'routing', label: 'Lead routing', icon: 'users' },
  { id: 'settings', label: 'Change password', icon: 'lock' },
] as const;

export default function App() {
  return <FeedbackProvider><Workspace /></FeedbackProvider>;
}

function Workspace() {
  const { notify } = useFeedback();
  const [session, setSession] = useState<Session | null>(() => {
    try {
      const saved = sessionStorage.getItem('crmSession');
      return saved && import.meta.env.DEV ? JSON.parse(saved) as Session : null;
    } catch { return null; }
  });
  const [restoring, setRestoring] = useState(true);
  useEffect(() => {
    if (session?.token) {
      setRestoring(false);
      return;
    }
    void api<CurrentSession>('/api/auth/me', '')
      .then(current => {
        setCsrfToken(current.csrfToken);
        setSession({ token: '', user: current });
      })
      .catch(() => {})
      .finally(() => setRestoring(false));
  }, []);
  const [screen, setScreen] = useState<Screen>('dashboard');
  const [menuOpen, setMenuOpen] = useState(false);
  const [newLeadRequest, setNewLeadRequest] = useState(0);
  const [allowed, setAllowed] = useState<Record<string, boolean> | null>(null);
  useEffect(() => {
    if (!session) return;
    let handled = false;
    const onExpired = () => {
      if (handled) return;
      handled = true;
      setCsrfToken('');
      sessionStorage.removeItem('crmSession');
      setSession(null);
      setAllowed(null);
      setMenuOpen(false);
      notify('warning', 'Session expired', 'Please sign in again.');
    };
    window.addEventListener('businessflow:session-expired', onExpired);
    return () => window.removeEventListener('businessflow:session-expired', onExpired);
  }, [session, notify]);
  useEffect(() => {
    if (!session) { setAllowed(null); return; }
    let active = true;
    void api<{ permissions: Record<string, boolean> }>('/api/auth/permissions', session.token)
      .then(result => { if (active) setAllowed(result.permissions); })
      .catch(() => { if (active) setAllowed(null); });
    return () => { active = false; };
  }, [session?.user.id, session?.token]);

  const [reminderCount, setReminderCount] = useState(0);
  useEffect(() => {
    if (!session || allowed?.['followups.view'] !== true) {
      setReminderCount(0);
      return;
    }
    let active = true;
    async function checkReminders() {
      try {
        const data = await api<{ total: number }>('/api/followups/reminders?withinHours=24&pageSize=1', session!.token);
        if (active) setReminderCount(data.total);
      } catch { if (active) setReminderCount(0); }
    }
    void checkReminders();
    const timer = window.setInterval(() => { void checkReminders(); }, 60_000);
    return () => { active = false; window.clearInterval(timer); };
  }, [session?.user.id, session?.token, allowed?.['followups.view']]);

  function navigate(next: Screen) {
    setScreen(next);
    setMenuOpen(false);
  }

  async function logout() {
    try {
      await api('/api/auth/logout', session?.token || '', { method: 'POST' });
      setCsrfToken('');
      sessionStorage.removeItem('crmSession');
      setSession(null);
      setMenuOpen(false);
      notify('info', 'Signed out', 'Your session has ended.');
    } catch (err) {
      notify('error', 'Unable to sign out', err instanceof Error ? err.message : undefined);
    }
  }

  function passwordChanged() {
    sessionStorage.removeItem('crmSession');
    setCsrfToken('');
    setSession(null);
    setMenuOpen(false);
  }

  if (restoring) return <main className="login-layout"><p role="status">Restoring session...</p></main>;
  if (!session) return <Login onLogin={value => {
    if (import.meta.env.DEV && value.token)
      sessionStorage.setItem('crmSession', JSON.stringify(value));
    setSession(value);
    notify('success', 'Welcome back', 'You are signed in.');
  }} />;

  const user = session.user;
  const canSeeTeam = allowed?.['employees.view'] === true;
  const moduleAccess: Partial<Record<Screen, string>> = {
    approvals: 'leads.approve', dashboard: 'dashboard.view', leads: 'leads.view', projects: 'projects.view',
    followups: 'followups.view', employees: 'employees.view', reports: 'reports.view',
  };
  const items = navigation.filter(item =>
    (item.id !== 'approvals' || allowed?.['leads.approve'] === true) &&
    (item.id !== 'employees' || canSeeTeam) &&
    (item.id !== 'audit' && item.id !== 'permissions' && item.id !== 'organization' && item.id !== 'roles' && item.id !== 'sources' && item.id !== 'duplicates' && item.id !== 'routing' || user.roles.includes('Admin')) &&
    (allowed?.[moduleAccess[item.id] || ''] !== false));
  const pageTitle = { dashboard: 'Dashboard', leads: 'All leads', projects: 'Development projects', followups: 'Follow-ups', employees: 'Team', reports: 'Reports', audit: 'Audit history', permissions: 'Role permissions', roles: 'Custom roles', organization: 'Organization', sources: 'Lead sources', duplicates: 'Duplicate review', routing: 'Lead routing', approvals: 'Lead approvals', settings: 'Change password' }[screen];

  return <div className="app-shell">
    <a className="skip-link" href="#main-content">Skip to main content</a>
    {menuOpen && <button className="mobile-backdrop" aria-label="Close navigation"
      onClick={() => setMenuOpen(false)} />}
    <aside className={'sidebar ' + (menuOpen ? 'sidebar-open' : '')}>
      <div className="brand"><span className="brand-mark"><Icon name="target" size={23} /></span>
        <div><strong>BusinessFlow</strong><small>CRM &amp; Projects</small></div>
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
          {reminderCount > 0 && <button type="button" className="secondary-action"
            onClick={() => navigate('followups')} aria-label={'My follow-up reminders: ' + reminderCount}>
            <Icon name="clock" size={17} /> {reminderCount} due
          </button>}
          {allowed?.['leads.create'] === true && <button className="primary topbar-add" onClick={() => {
            navigate('leads');
            setNewLeadRequest(value => value + 1);
          }}><Icon name="plus" size={17} /> New lead</button>}
          <span className="avatar avatar-small" title={user.fullName}>{initials(user.fullName)}</span>
        </div>
      </header>
      <main className="content" id="main-content" tabIndex={-1}>
        {screen === 'dashboard' && <DashboardPage token={session.token} onNavigate={navigate} />}
        {screen === 'leads' && <LeadsPage token={session.token} user={user} permissions={allowed ?? {}}
          newLeadRequest={newLeadRequest} onNewLeadOpened={() => setNewLeadRequest(0)} />}
        {screen === 'projects' && <ProjectsPage token={session.token} user={user} permissions={allowed ?? {}} />}
        {screen === 'followups' && <FollowUpsPage token={session.token} />}
        {screen === 'employees' && canSeeTeam && <EmployeesPage token={session.token} user={user} />}
        {screen === 'reports' && <ReportsPage token={session.token} user={user} />}
        {screen === 'audit' && user.roles.includes('Admin') && <AuditPage token={session.token} />}
        {screen === 'permissions' && user.roles.includes('Admin') && <PermissionsPage token={session.token} />}
        {screen === 'approvals' && allowed?.['leads.approve'] && <ApprovalsPage token={session.token} user={user} />}
        {screen === 'settings' && <PasswordSettings token={session.token} onChanged={passwordChanged} />}
        {screen === 'organization' && user.roles.includes('Admin') && <OrganizationPage token={session.token} />}
        {screen === 'roles' && user.roles.includes('Admin') && <RolesPage token={session.token} />}
        {screen === 'sources' && user.roles.includes('Admin') && <LeadSourcesPage token={session.token} />}
        {screen === 'duplicates' && user.roles.includes('Admin') && <LeadDuplicatesPage token={session.token} />}
        {screen === 'routing' && user.roles.includes('Admin') && <LeadRoutingPage token={session.token} />}
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

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!validateForm(event.currentTarget) || working) return;
    setWorking(true);
    setError('');
    try {
      const result = await api<{ token: string | null; user: User }>('/api/auth/login', '', {
        method: 'POST', body: JSON.stringify({ email: email.trim(), password }),
      });
      const token = result.token || '';
      const current = await api<CurrentSession>('/api/auth/me', token);
      setCsrfToken(current.csrfToken);
      onLogin({ token: import.meta.env.DEV ? token : '', user: current });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Login failed';
      setError(message);
      notify('error', 'Unable to sign in', message);
    } finally { setWorking(false); }
  }

  return <main className="login-layout">
    <section className="login-intro">
      <div className="brand"><span className="brand-mark"><Icon name="target" size={25} /></span>
        <div><strong>BusinessFlow</strong><small>CRM &amp; Projects</small></div></div>
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
      <p className="intro-footer">BusinessFlow · From Lead to Delivery</p>
    </section>
    <section className="login-form-side">
      <form className="login-card" onSubmit={submit} onInput={clearFieldError}
        onKeyDown={handleFormKeyDown}>
        <span className="login-overline">WELCOME BACK</span>
        <h2>Sign in to your account</h2>
        <p>Enter your credentials to access your workspace.</p>
        <label>Email address<input autoFocus type="email" maxLength={256} autoComplete="username" placeholder="you@company.com"
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
