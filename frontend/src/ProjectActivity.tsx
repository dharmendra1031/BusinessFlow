import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import { clearFieldError, handleFormKeyDown, validateForm } from './formUtils';
import type { DevelopmentProject, Employee, Paged, User } from './types';

type Bug = {
  id: number; taskId: number | null; title: string; description: string | null;
  severity: string; status: string; assignedToId: number | null; assignedTo: string | null;
  reporter: string; reportedById: number; createdAtUtc: string;
};
type Worklog = {
  id: number; taskId: number | null; taskTitle: string | null; developer: string;
  userId: number; workDate: string; hours: number; description: string;
};
type BugsResult = Paged<Bug> & { openCount: number };
type WorklogsResult = Paged<Worklog> & { totalHours: number };

const severities = ['Low', 'Medium', 'High', 'Critical'];
const statuses = ['Open', 'InProgress', 'Resolved', 'Closed'];
const label = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');

export default function ProjectActivity({ project, user, token, canManage, permissions, employees }: {
  project: DevelopmentProject; user: User; token: string;
  canManage: boolean; permissions: Record<string, boolean>; employees: Employee[];
}) {
  const { notify } = useFeedback();
  const canViewBugs = permissions['bugs.view'] === true;
  const canCreateBug = permissions['bugs.create'] === true;
  const canEditBug = canManage && permissions['bugs.edit'] === true;
  const canChangeBug = permissions['bugs.status'] === true;
  const canCloseBug = canManage && permissions['bugs.close'] === true;
  const canViewWorklogs = permissions['worklogs.view'] === true;
  const canPostWorklog = permissions['worklogs.post'] === true;
  const base = '/api/projects/' + project.id;
  const [bugs, setBugs] = useState<Bug[]>([]);
  const [bugPage, setBugPage] = useState(1);
  const [bugTotal, setBugTotal] = useState(0);
  const [openCount, setOpenCount] = useState(0);
  const [worklogPage, setWorklogPage] = useState(1);
  const [worklogs, setWorklogs] = useState<WorklogsResult>({ totalHours: 0, total: 0, page: 1, pageSize: 50, items: [] });
  const [loading, setLoading] = useState(true);
  const [working, setWorking] = useState(false);
  const [editingBugId, setEditingBugId] = useState<number | null>(null);
  const [bugTitle, setBugTitle] = useState('');
  const [bugDescription, setBugDescription] = useState('');
  const [severity, setSeverity] = useState('Medium');
  const [bugStatus, setBugStatus] = useState('Open');
  const [bugTask, setBugTask] = useState('');
  const [bugAssignee, setBugAssignee] = useState('');
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [hours, setHours] = useState('1');
  const [notes, setNotes] = useState('');
  const [workTask, setWorkTask] = useState('');
  const [error, setError] = useState('');
  const [updatingBug, setUpdatingBug] = useState<number | null>(null);

  const refresh = useCallback(async () => {
    try {
      const [foundBugs, foundLogs] = await Promise.all([
        canViewBugs ? api<BugsResult>(base + '/bugs?page=' + bugPage, token) : Promise.resolve(null),
        canViewWorklogs ? api<WorklogsResult>(base + '/worklogs?page=' + worklogPage, token) : Promise.resolve(null),
      ]);
      if (foundBugs) {
        setBugs(foundBugs.items);
        setBugTotal(foundBugs.total);
        setOpenCount(foundBugs.openCount);
      }
      if (foundLogs) setWorklogs(foundLogs);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load project activity.');
    } finally {
      setLoading(false);
    }
  }, [base, token, bugPage, worklogPage, canViewBugs, canViewWorklogs]);

  useEffect(() => { void refresh(); }, [refresh]);

  const availableMembers = employees.filter(member => member.isActive &&
    (user.roles.includes('Admin') || member.id === user.id || member.managerId === user.id));
  const tasks = project.tasks || [];
  const myTasks = tasks.filter(task => task.assignedToId === user.id || project.ownerId === user.id);

  function resetBug() {
    setEditingBugId(null); setBugTitle(''); setBugDescription('');
    setSeverity('Medium'); setBugStatus('Open'); setBugTask(''); setBugAssignee('');
  }

  function editBug(bug: Bug) {
    setEditingBugId(bug.id); setBugTitle(bug.title); setBugDescription(bug.description || '');
    setSeverity(bug.severity); setBugStatus(bug.status);
    setBugTask(bug.taskId?.toString() || '');
    setBugAssignee(bug.assignedToId?.toString() || '');
  }

  async function saveBug(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!(editingBugId ? canEditBug : canCreateBug) || !validateForm(event.currentTarget) || working) return;
    setWorking(true);
    try {
      const payload = {
        title: bugTitle.trim(), description: bugDescription.trim() || null, severity,
        taskId: bugTask ? Number(bugTask) : null,
        assignedToId: canManage && bugAssignee ? Number(bugAssignee) : null,
      };
      await api(base + '/bugs' + (editingBugId ? '/' + editingBugId : ''), token, {
        method: editingBugId ? 'PUT' : 'POST',
        body: JSON.stringify(editingBugId ? { ...payload, status: bugStatus } : payload)
      });
      notify('success', editingBugId ? 'Bug updated' : 'Bug reported');
      resetBug();
      await refresh();
    } catch (err) {
      notify('error', 'Unable to save bug', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  async function changeStatus(bug: Bug, status: string) {
    if (!canChangeBug || (status === 'Closed' && !canCloseBug) || bug.status === status || updatingBug) return;
    setUpdatingBug(bug.id);
    try {
      await api(base + '/bugs/' + bug.id + '/status', token, {
        method: 'PATCH', body: JSON.stringify({ status })
      });
      await refresh();
      notify('success', 'Bug status updated');
    } catch (err) {
      notify('error', 'Unable to change bug', err instanceof Error ? err.message : undefined);
    } finally { setUpdatingBug(null); }
  }

  async function addWorklog(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canPostWorklog || !validateForm(event.currentTarget) || working) return;
    setWorking(true);
    try {
      await api(base + '/worklogs', token, {
        method: 'POST',
        body: JSON.stringify({
          workDate: date, hours: Number(hours), description: notes.trim(),
          taskId: workTask ? Number(workTask) : null
        })
      });
      setNotes(''); setHours('1'); setWorkTask('');
      await refresh();
      notify('success', 'Worklog saved');
    } catch (err) {
      notify('error', 'Unable to log work', err instanceof Error ? err.message : undefined);
    } finally { setWorking(false); }
  }

  return <>
    {canViewBugs && <section className="project-section" aria-label="Project bugs">
      <h3>Bug tracking ({openCount} open)</h3>
      {error && <p role="alert">{error} <button onClick={() => void refresh()}>Retry</button></p>}
      {loading ? <p>Loading bugs and worklogs…</p> :
        bugs.length ? <div className="project-tasks">{bugs.map(bug =>
          <article className="project-task" key={bug.id}>
            <div><strong>{bug.title}</strong>
              <p>{bug.description || 'No details'} </p>
              <small>{bug.severity} severity · {bug.assignedTo || 'Unassigned'} · Reported by {bug.reporter}</small>
            </div>
            <div className="project-activity-actions">
              {canChangeBug ? <select aria-label={'Bug status for ' + bug.title} value={bug.status}
                disabled={updatingBug !== null || (!canManage && (bug.assignedToId !== user.id || bug.status === "Closed" || project.status === "Completed"))}
                onChange={event => void changeStatus(bug, event.target.value)}>
                {(canManage ? statuses.filter(status => status !== 'Closed' || canCloseBug) : ['InProgress', 'Resolved'])
                  .filter(status => status !== bug.status || canChangeBug).map(status =>
                  <option key={status} value={status}>{label(status)}</option>)}
                {!((canManage ? statuses.filter(status => status !== 'Closed' || canCloseBug) : ['InProgress', 'Resolved']).includes(bug.status)) &&
                  <option value={bug.status} disabled>{label(bug.status)}</option>}
              </select> : <span className="status-badge">{label(bug.status)}</span>}
              {canEditBug && <button type="button" className="text-action" onClick={() => editBug(bug)}>
                Edit bug</button>}
            </div>
          </article>)}</div> : <p className="field-help">No bugs reported for this project.</p>}
      <div className="pagination"><span>Page {bugPage} · {bugTotal} bugs</span>
        <button type="button" disabled={bugPage <= 1} onClick={() => setBugPage(bugPage - 1)}>Previous</button>
        <button type="button" disabled={bugPage * 50 >= bugTotal} onClick={() => setBugPage(bugPage + 1)}>Next</button>
      </div>
    </section>}

    {(canCreateBug || (canEditBug && editingBugId !== null)) && <details className="project-section" open={editingBugId !== null ? true : undefined}>
      <summary>{editingBugId ? 'Edit reported bug' : 'Report a bug'}</summary>
      <form className="drawer-form" onInput={clearFieldError} onKeyDown={handleFormKeyDown} onSubmit={saveBug}>
        <label>Bug title *<input required minLength={2} maxLength={150}
          value={bugTitle} onChange={event => setBugTitle(event.target.value)} /></label>
        <label>What went wrong?<textarea maxLength={2000} value={bugDescription}
          onChange={event => setBugDescription(event.target.value)} /></label>
        <label>Severity<select value={severity} onChange={event => setSeverity(event.target.value)}>
          {severities.map(value => <option key={value}>{value}</option>)}
        </select></label>
        <label>Related task<select value={bugTask} onChange={event => setBugTask(event.target.value)}>
          <option value="">General project issue</option>
          {tasks.map(task => <option key={task.id} value={task.id}>{task.title}</option>)}
        </select></label>
        {canManage && <label>Assign developer<select value={bugAssignee}
          onChange={event => setBugAssignee(event.target.value)}>
          <option value="">Unassigned</option>
          {availableMembers.map(member =>
            <option key={member.id} value={member.id}>{member.fullName}</option>)}
        </select></label>}
        {editingBugId && canEditBug && <label>Bug status<select value={bugStatus}
          onChange={event => setBugStatus(event.target.value)}>
          {statuses.filter(status => status !== 'Closed' || canCloseBug || status === bugStatus)
            .map(status => <option key={status} value={status}>{label(status)}</option>)}
        </select></label>}
        <div className="drawer-actions">
          {editingBugId && <button type="button" onClick={resetBug}>Cancel edit</button>}
          <button className="primary" disabled={working}>{editingBugId ? 'Save bug' : 'Report bug'}</button>
        </div>
      </form>
    </details>}

    {canViewWorklogs && <section className="project-section" aria-label="Daily developer worklogs">
      <h3>Daily worklogs · {worklogs.totalHours.toFixed(2)} hours shown</h3>
      {worklogs.items.length ? <div className="project-tasks">{worklogs.items.map(log =>
        <article className="project-task" key={log.id}>
          <div><strong>{log.developer} · {log.workDate}</strong>
            <p>{log.description}</p>
            <small>{log.taskTitle || 'General project work'}</small>
          </div>
          <strong>{log.hours.toFixed(2)} h</strong>
        </article>)}</div> : <p className="field-help">No worklogs submitted yet.</p>}
      <div className="pagination"><span>Page {worklogPage} · {worklogs.total} worklogs</span>
        <button type="button" disabled={worklogPage <= 1} onClick={() => setWorklogPage(worklogPage - 1)}>Previous</button>
        <button type="button" disabled={worklogPage * 50 >= worklogs.total} onClick={() => setWorklogPage(worklogPage + 1)}>Next</button>
      </div>
    </section>}
    {canPostWorklog && project.status !== 'Completed' && <details className="project-section">
      <summary>Log today's work</summary>
      <form className="drawer-form" onInput={clearFieldError} onKeyDown={handleFormKeyDown} onSubmit={addWorklog}>
        <label>Work date *<input type="date" required min={new Date(Date.now() - 30 * 86400000).toISOString().slice(0, 10)} max={new Date().toISOString().slice(0, 10)}
          value={date} onChange={event => setDate(event.target.value)} /></label>
        <label>Hours worked *<input type="number" required min="0.25" max="24" step="0.01"
          value={hours} onChange={event => setHours(event.target.value)} /></label>
        <label>Development task<select value={workTask} onChange={event => setWorkTask(event.target.value)}>
          <option value="">General project work</option>
          {myTasks.map(task => <option key={task.id} value={task.id}>{task.title}</option>)}
        </select></label>
        <label>Work summary *<textarea required minLength={2} maxLength={1000}
          value={notes} onChange={event => setNotes(event.target.value)} /></label>
        <button className="primary" disabled={working}>Save worklog</button>
      </form>
    </details>}
  </>;
}
