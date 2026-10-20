import { employeeLookup } from './employeeLookup';
import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api, ApiError } from './api';
import { Icon } from './Icon';
import { useFeedback } from './Feedback';
import ProjectActivity from './ProjectActivity';
import ProjectPlanning from './ProjectPlanning';
import { clearFieldError, handleDialogKeyDown, handleFormKeyDown, validateForm } from './formUtils';
import type { DevelopmentProject, DevelopmentTask, Employee, Paged, ProjectPlan, User } from './types';

type WonLead = { id: number; name: string; customerCode: string | null };
const projectStatuses = ['Planning', 'InProgress', 'OnHold', 'Completed'];
const taskStatuses = ['Todo', 'InProgress', 'Blocked', 'Done'];
const label = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');

export default function ProjectsPage({ token, user }: { token: string; user: User }) {
  const { notify } = useFeedback();
  const canManage = user.roles.includes('Admin') || user.roles.includes('Manager');
  const [projects, setProjects] = useState<DevelopmentProject[]>([]);
  const [projectPage, setProjectPage] = useState(1);
  const [projectTotal, setProjectTotal] = useState(0);
  const [leads, setLeads] = useState<WonLead[]>([]);
  const [leadPage, setLeadPage] = useState(1);
  const [leadTotal, setLeadTotal] = useState(0);
  const [leadSearch, setLeadSearch] = useState('');
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [selected, setSelected] = useState<DevelopmentProject | null>(null);
  const [conflict, setConflict] = useState(false);
  const [projectVersion, setProjectVersion] = useState('');
  const [taskVersion, setTaskVersion] = useState('');
  const [planning, setPlanning] = useState<ProjectPlan>({ milestones: [], sprints: [] });
  const [showCreate, setShowCreate] = useState(false);
  const [working, setWorking] = useState(false);
  const [taskSaving, setTaskSaving] = useState(false);
  const [error, setError] = useState('');
  const [leadId, setLeadId] = useState('');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [ownerId, setOwnerId] = useState(String(user.id));
  const [startDate, setStartDate] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [projectStatus, setProjectStatus] = useState('Planning');
  const [taskTitle, setTaskTitle] = useState('');
  const [taskPriority, setTaskPriority] = useState('Medium');
  const [taskDescription, setTaskDescription] = useState('');
  const [assigneeId, setAssigneeId] = useState('');
  const [taskDueDate, setTaskDueDate] = useState('');
  const [editingTask, setEditingTask] = useState<number | null>(null);
  const [taskMilestone, setTaskMilestone] = useState('');
  const [taskSprint, setTaskSprint] = useState('');

  const load = useCallback(async () => {
    try {
      const result = await api<Paged<DevelopmentProject>>('/api/projects?page=' + projectPage, token);
      setProjects(result.items);
      setProjectTotal(result.total);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load projects.');
    }
  }, [token, projectPage]);

  const loadEligible = useCallback(async () => {
    const params = new URLSearchParams({ page: String(leadPage), search: leadSearch });
    const result = await api<Paged<WonLead>>('/api/projects/eligible-leads?' + params, token);
    setLeads(result.items);
    setLeadTotal(result.total);
  }, [token, leadPage, leadSearch]);

  useEffect(() => {
    void load();
    if (!canManage) return;
    void loadEligible()
      .catch(() => notify('error', 'Unable to load won leads'));
    void employeeLookup(token, 'projects.create').then(setEmployees)
      .catch(() => notify('error', 'Unable to load team'));
  }, [load, loadEligible, canManage, token, notify]);

  const availableOwners = employees.filter(member => member.isActive &&
    (user.roles.includes('Admin') || member.id === user.id || member.managerId === user.id));
  const mayManage = (project: DevelopmentProject) => canManage && (
    user.roles.includes('Admin') || project.ownerId === user.id || project.createdById === user.id ||
    employees.some(member => member.id === project.ownerId && member.managerId === user.id));
  const progress = (project: DevelopmentProject) => project.taskCount
    ? Math.round(project.doneCount / project.taskCount * 100) : 0;

  async function openProject(id: number) {
    if (selected?.id !== id) setEditingTask(null);
    try {
      const [project, plan] = await Promise.all([
        api<DevelopmentProject>('/api/projects/' + id, token),
        api<ProjectPlan>('/api/projects/' + id + '/planning', token),
      ]);
      setSelected(project);
      setPlanning(plan);
    } catch (err) {
      notify('error', 'Unable to open project', err instanceof Error ? err.message : undefined);
    }
  }

  function startEdit(project: DevelopmentProject) {
    setProjectVersion(project.rowVersion);
    setName(project.title);
    setDescription(project.description || '');
    setOwnerId(String(project.ownerId));
    setProjectStatus(project.status);
    setStartDate(project.startDate?.slice(0, 10) || '');
    setDueDate(project.dueDate?.slice(0, 10) || '');
  }

  function createNew() {
    setLeadId('');
    setName('');
    setDescription('');
    setOwnerId(String(user.id));
    setProjectStatus('Planning');
    setStartDate('');
    setDueDate('');
    setShowCreate(true);
  }

  async function saveProject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!validateForm(event.currentTarget) || working) return;
    if (startDate && dueDate && dueDate < startDate) {
      notify('warning', 'Invalid date range', 'Due date cannot be before the start date.');
      return;
    }
    setWorking(true);
    try {
      const payload = {
        title: name.trim(), description: description.trim() || null,
        ownerId: Number(ownerId), startDate: startDate || null, dueDate: dueDate || null,
      };
      if (showCreate) {
        const result = await api<{ id: number }>('/api/projects', token, {
          method: 'POST', body: JSON.stringify({ ...payload, leadId: Number(leadId) }),
        });
        setShowCreate(false);
        await load();
        await openProject(result.id);
        notify('success', 'Project created', 'You can now add development tasks.');
      } else if (selected) {
        await api('/api/projects/' + selected.id, token, {
          method: 'PUT', body: JSON.stringify({ ...payload, status: projectStatus, rowVersion: projectVersion }),
        });
        await load();
        await openProject(selected.id);
        notify('success', 'Project updated');
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) setConflict(true);
      notify('error', 'Unable to save project', err instanceof Error ? err.message : undefined);
    } finally {
      setWorking(false);
    }
  }

  function startEditTask(task: DevelopmentTask) {
    setTaskVersion(task.rowVersion);
    setEditingTask(task.id);
    setTaskTitle(task.title);
    setTaskPriority(task.priority);
    setTaskDescription(task.description || '');
    setAssigneeId(task.assignedToId?.toString() || '');
    setTaskDueDate(task.dueDate || '');
    setTaskMilestone(task.milestoneId?.toString() || '');
    setTaskSprint(task.sprintId?.toString() || '');
  }

  async function saveTask(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected || !validateForm(event.currentTarget) || working) return;
    setWorking(true);
    try {
      await api('/api/projects/' + selected.id + '/tasks' +
        (editingTask ? '/' + editingTask : ''), token, {
        method: editingTask ? 'PUT' : 'POST',
        body: JSON.stringify({
          rowVersion: editingTask ? taskVersion : undefined,
          title: taskTitle.trim(), priority: taskPriority, description: taskDescription.trim() || null,
          assignedToId: assigneeId ? Number(assigneeId) : null,
          dueDate: taskDueDate || null,
          milestoneId: taskMilestone ? Number(taskMilestone) : null,
          sprintId: taskSprint ? Number(taskSprint) : null,
        }),
      });
      setTaskTitle('');
      setTaskPriority('Medium');
      setTaskDescription('');
      setAssigneeId('');
      setTaskDueDate('');
      setTaskMilestone(''); setTaskSprint('');
      setEditingTask(null);
      await openProject(selected.id);
      await load();
      notify('success', editingTask ? 'Task updated' : 'Task created');
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) setConflict(true);
      notify('error', 'Unable to create task', err instanceof Error ? err.message : undefined);
    } finally {
      setWorking(false);
    }
  }

  async function changeTask(task: DevelopmentTask, status: string) {
    if (!selected || task.status === status || taskSaving) return;
    setTaskSaving(true);
    try {
      await api('/api/projects/' + selected.id + '/tasks/' + task.id + '/status', token, {
        method: 'PATCH', body: JSON.stringify({ status, rowVersion: task.rowVersion }),
      });
      await openProject(selected.id);
      await load();
      notify('success', 'Task updated');
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) setConflict(true);
      notify('error', 'Unable to update task', err instanceof Error ? err.message : undefined);
    } finally { setTaskSaving(false); }
  }

  return <section>
    <header className="page-header">
      <div><span className="section-kicker">DELIVERY WORKSPACE</span>
        <h1>Development projects</h1>
        <p>Take won leads into delivery, assign work and track progress.</p></div>
      {canManage && <button className="primary" onClick={createNew}>
        <Icon name="plus" size={18} /> New project</button>}
    </header>
    {error && <div className="error-state" role="alert">{error}
      <button onClick={() => void load()}>Retry</button></div>}
    <section className="panel">
      <div className="panel-heading project-heading">
        <div><h2>Project overview</h2><p>{projectTotal} projects visible to your account</p></div>
      </div>
      {projects.length ? <div className="table-scroll"><table>
        <thead><tr><th>Project</th><th>Client</th><th>Owner</th><th>Status</th>
          <th>Progress</th><th>Due date</th><th>Action</th></tr></thead>
        <tbody>{projects.map(project => <tr key={project.id}>
          <td><strong>{project.title}</strong></td><td>{project.customer}</td>
          <td>{project.ownerName}</td>
          <td><span className="status-badge">{label(project.status)}</span></td>
          <td>{project.doneCount}/{project.taskCount} tasks · {progress(project)}%</td>
          <td>{project.dueDate?.slice(0, 10) || '—'}</td>
          <td><button className="secondary-action" onClick={() => void openProject(project.id)}>
            View project</button></td>
        </tr>)}</tbody></table></div> :
        <div className="empty-state"><h3>No development projects yet</h3>
          <p>{canManage ? 'Mark a lead as Won, then create your first project.' :
            'Projects assigned to you will appear here.'}</p></div>}
    </section>

    <div className="pagination"><span>Page {projectPage} · {projectTotal} projects</span>
      <button disabled={projectPage <= 1} onClick={() => setProjectPage(projectPage - 1)}>Previous</button>
      <button disabled={projectPage * 50 >= projectTotal} onClick={() => setProjectPage(projectPage + 1)}>Next</button>
    </div>

    {showCreate && <div className="drawer-backdrop">
      <aside className="detail-drawer" role="dialog" aria-modal="true" aria-label="New development project"
        onKeyDown={event => handleDialogKeyDown(event, () => { if (!working) setShowCreate(false); })}>
        <div className="drawer-header"><div><span className="section-kicker">NEW PROJECT</span>
          <h2>Create development project</h2></div>
          <button className="icon-button" aria-label="Close" onClick={() => setShowCreate(false)}>
            <Icon name="close" /></button></div>
        <form className="drawer-form" onSubmit={saveProject} onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
          <label>Find won lead<input value={leadSearch} onChange={event => { setLeadSearch(event.target.value); setLeadPage(1); }} /></label>
          <label>Won lead *<select required autoFocus value={leadId} onChange={event => {
            const value = event.target.value;
            setLeadId(value);
            if (!name) setName(leads.find(lead => String(lead.id) === value)?.name || '');
          }}><option value="">Choose a won lead</option>
            {leads.map(lead => <option key={lead.id} value={lead.id}>{lead.name}</option>)}
          </select></label>
          <div className="pagination"><span>Page {leadPage} · {leadTotal} eligible leads</span>
            <button type="button" disabled={leadPage <= 1} onClick={() => setLeadPage(leadPage - 1)}>Previous</button>
            <button type="button" disabled={leadPage * 50 >= leadTotal} onClick={() => setLeadPage(leadPage + 1)}>Next</button>
          </div>
          <ProjectFields {...{ name, setName, description, setDescription, ownerId, setOwnerId,
            startDate, setStartDate, dueDate, setDueDate, availableOwners }} />
          <div className="drawer-actions"><button type="button" onClick={() => setShowCreate(false)}>Cancel</button>
            <button className="primary" disabled={working}>Create project</button></div>
        </form>
      </aside>
    </div>}

    {selected && <div className="drawer-backdrop">
      <aside className="detail-drawer" role="dialog" aria-modal="true" aria-label={'Project ' + selected.title}
        onKeyDown={event => handleDialogKeyDown(event, () => { if (!working) setSelected(null); })}>
        <div className="drawer-header"><div><span className="section-kicker">DEVELOPMENT PROJECT</span>
          <h2>{selected.title}</h2><p>{selected.customer} · {label(selected.status)}</p></div>
          <button className="icon-button" aria-label="Close" onClick={() => setSelected(null)}>
            <Icon name="close" /></button></div>
    {conflict && selected && <div className="conflict-notice" role="alert"><strong>This project changed.</strong>
      <p>Your input is preserved. Reload replaces the editor with the latest saved details.</p>
      <button disabled={working || taskSaving} onClick={() => void (async () => {
        try {
          const current = await api<DevelopmentProject>('/api/projects/' + selected.id, token);
          setSelected(current); startEdit(current);
          if (editingTask) {
            const task = current.tasks?.find(item => item.id === editingTask);
            if (task) startEditTask(task); else setEditingTask(null);
          }
          setConflict(false);
        } catch (error) { notify('error', 'Unable to reload project', error instanceof Error ? error.message : undefined); }
      })()}>Reload latest project</button></div>}
        <p className="field-help">Owner: {selected.ownerName} ·
          {selected.tasks?.filter(task => task.status === 'Done').length || 0}/{selected.tasks?.length || 0} tasks done</p>
        {mayManage(selected) && <details className="project-section">
          <summary onClick={() => startEdit(selected)}>Edit project details</summary>
          <form className="drawer-form" onSubmit={saveProject} onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
            <ProjectFields {...{ name, setName, description, setDescription, ownerId, setOwnerId,
              startDate, setStartDate, dueDate, setDueDate, availableOwners }} />
            <label>Status<select aria-label="Project status" value={projectStatus} onChange={event => setProjectStatus(event.target.value)}>
              {projectStatuses.map(status => <option key={status} value={status}>{label(status)}</option>)}
            </select></label>
            <button className="primary" disabled={working}>Save project</button>
          </form>
        </details>}
        <ProjectPlanning project={selected} token={token} planning={planning}
          canManage={mayManage(selected)} onSaved={() => openProject(selected.id)} />
        <div className="project-section"><h3>Development tasks</h3>
          {selected.tasks?.length ? <div className="project-tasks">{selected.tasks.map(task =>
            <div className="project-task" key={task.id}>
              <div><strong>{task.title}</strong><p>{task.description || 'No description'}</p>
                <small>{task.assignedTo || 'Unassigned'} · {task.priority} · {task.dueDate?.slice(0, 10) || 'No deadline'}</small>
              </div>
              <select aria-label={'Status for ' + task.title} value={task.status}
                disabled={taskSaving || selected.status === 'Completed' || (!mayManage(selected) && task.assignedToId !== user.id)}
                onChange={event => void changeTask(task, event.target.value)}>
                {taskStatuses.map(status => <option key={status} value={status}>{label(status)}</option>)}
              </select>
              {mayManage(selected) && selected.status !== 'Completed' &&
                <button type="button" className="text-action" onClick={() => startEditTask(task)}>Edit task</button>}
            </div>)}</div> : <p className="field-help">No tasks yet.</p>}
        </div>
        <ProjectActivity project={selected} token={token} user={user}
          canManage={mayManage(selected)} employees={employees} />
        {mayManage(selected) && selected.status !== 'Completed' && <details className="project-section" open={editingTask !== null ? true : undefined}>
          <summary>{editingTask ? 'Edit development task' : 'Add development task'}</summary>
          <form className="drawer-form" onSubmit={saveTask} onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
            <label>Task title *<input required minLength={2} maxLength={150} value={taskTitle}
              onChange={event => setTaskTitle(event.target.value)} /></label>
            <label>Details<textarea maxLength={2000} value={taskDescription}
              onChange={event => setTaskDescription(event.target.value)} /></label>
            <label>Priority<select value={taskPriority} onChange={event => setTaskPriority(event.target.value)}>
              {["Low", "Medium", "High", "Urgent"].map(value => <option key={value}>{value}</option>)}
            </select></label>
            <label>Assign to<select value={assigneeId} onChange={event => setAssigneeId(event.target.value)}>
              <option value="">Unassigned</option>
              {availableOwners.map(member => <option key={member.id} value={member.id}>{member.fullName}</option>)}
            </select></label>
            <label>Milestone<select aria-label="Milestone" value={taskMilestone} onChange={event => setTaskMilestone(event.target.value)}>
              <option value="">No milestone</option>
              {planning.milestones.filter(item => item.status !== 'Completed' ||
                String(item.id) === taskMilestone).map(item =>
                <option key={item.id} value={item.id}>{item.title}</option>)}
            </select></label>
            <label>Sprint<select aria-label="Sprint" value={taskSprint} onChange={event => setTaskSprint(event.target.value)}>
              <option value="">No sprint</option>
              {planning.sprints.filter(item => item.status !== 'Completed' ||
                String(item.id) === taskSprint).map(item =>
                <option key={item.id} value={item.id}>{item.title}</option>)}
            </select></label>
            <label>Due date<input type="date" max={selected.dueDate || undefined} value={taskDueDate}
              onChange={event => setTaskDueDate(event.target.value)} /></label>
            <div className="drawer-actions">
              {editingTask !== null && <button type="button" onClick={() => {
                setEditingTask(null); setTaskTitle(''); setTaskDescription('');
                setTaskPriority('Medium'); setAssigneeId(''); setTaskDueDate('');
                setTaskMilestone(''); setTaskSprint('');
              }}>Cancel edit</button>}
              <button className="primary" disabled={working}>{editingTask ? 'Save task' : 'Add task'}</button>
            </div>
          </form>
        </details>}
      </aside>
    </div>}
  </section>;
}

type ProjectFieldsProps = {
  name: string; setName: (value: string) => void;
  description: string; setDescription: (value: string) => void;
  ownerId: string; setOwnerId: (value: string) => void;
  startDate: string; setStartDate: (value: string) => void;
  dueDate: string; setDueDate: (value: string) => void;
  availableOwners: Employee[];
};

function ProjectFields(props: ProjectFieldsProps) {
  const { name, setName, description, setDescription, ownerId, setOwnerId,
    startDate, setStartDate, dueDate, setDueDate, availableOwners } = props;
  return <>
    <label>Project name *<input required minLength={2} maxLength={150} value={name}
      onChange={event => setName(event.target.value)} /></label>
    <label>Description<textarea maxLength={2000} value={description}
      onChange={event => setDescription(event.target.value)} /></label>
    <label>Project owner *<select required value={ownerId} onChange={event => setOwnerId(event.target.value)}>
      <option value="">Choose owner</option>
      {availableOwners.map(member => <option key={member.id} value={member.id}>{member.fullName}</option>)}
    </select></label>
    <label>Start date<input type="date" value={startDate} max={dueDate || undefined}
      onChange={event => setStartDate(event.target.value)} /></label>
    <label>Due date<input type="date" value={dueDate} min={startDate || undefined}
      onChange={event => setDueDate(event.target.value)} /></label>
  </>;
}
