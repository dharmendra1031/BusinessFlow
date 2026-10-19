import { useState, type FormEvent } from 'react';
import { api } from './api';
import { useFeedback } from './Feedback';
import { clearFieldError, handleFormKeyDown, validateForm } from './formUtils';
import type { DevelopmentProject, ProjectMilestone, ProjectPlan, ProjectSprint } from './types';

const statuses = ['Planned', 'InProgress', 'Completed'];
const label = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');
const percent = (done: number, total: number) => total ? Math.round(done / total * 100) : 0;

export default function ProjectPlanning({ project, token, canManage, planning, onSaved }: {
  project: DevelopmentProject; token: string; canManage: boolean;
  planning: ProjectPlan; onSaved: () => Promise<void>;
}) {
  const { notify } = useFeedback();
  const base = '/api/projects/' + project.id + '/planning';
  const [editingMilestone, setEditingMilestone] = useState<number | null>(null);
  const [editingSprint, setEditingSprint] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [milestoneTitle, setMilestoneTitle] = useState('');
  const [milestoneDescription, setMilestoneDescription] = useState('');
  const [milestoneStatus, setMilestoneStatus] = useState('Planned');
  const [milestoneDate, setMilestoneDate] = useState('');
  const [sprintTitle, setSprintTitle] = useState('');
  const [sprintGoal, setSprintGoal] = useState('');
  const [sprintStatus, setSprintStatus] = useState('Planned');
  const [sprintStart, setSprintStart] = useState('');
  const [sprintDue, setSprintDue] = useState('');

  function editMilestone(item: ProjectMilestone) {
    setEditingMilestone(item.id);
    setMilestoneTitle(item.title);
    setMilestoneDescription(item.description || '');
    setMilestoneStatus(item.status);
    setMilestoneDate(item.dueDate || '');
  }
  function editSprint(item: ProjectSprint) {
    setEditingSprint(item.id);
    setSprintTitle(item.title);
    setSprintGoal(item.goal || '');
    setSprintStatus(item.status);
    setSprintStart(item.startDate);
    setSprintDue(item.dueDate);
  }
  function clearMilestone() {
    setEditingMilestone(null); setMilestoneTitle(''); setMilestoneDescription('');
    setMilestoneStatus('Planned'); setMilestoneDate('');
  }
  function clearSprint() {
    setEditingSprint(null); setSprintTitle(''); setSprintGoal('');
    setSprintStatus('Planned'); setSprintStart(''); setSprintDue('');
  }

  async function saveMilestone(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!validateForm(event.currentTarget) || busy) return;
    setBusy(true);
    try {
      await api(base + '/milestones' + (editingMilestone ? '/' + editingMilestone : ''), token, {
        method: editingMilestone ? 'PUT' : 'POST',
        body: JSON.stringify({
          title: milestoneTitle.trim(), description: milestoneDescription.trim() || null,
          status: milestoneStatus, dueDate: milestoneDate || null,
        }),
      });
      clearMilestone();
      await onSaved();
      notify('success', 'Milestone saved');
    } catch (err) {
      notify('error', 'Unable to save milestone', err instanceof Error ? err.message : undefined);
    } finally { setBusy(false); }
  }

  async function saveSprint(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!validateForm(event.currentTarget) || busy) return;
    if (sprintDue < sprintStart) {
      notify('warning', 'Invalid dates', 'Sprint end must be on or after its start.');
      return;
    }
    setBusy(true);
    try {
      await api(base + '/sprints' + (editingSprint ? '/' + editingSprint : ''), token, {
        method: editingSprint ? 'PUT' : 'POST',
        body: JSON.stringify({
          title: sprintTitle.trim(), goal: sprintGoal.trim() || null,
          status: sprintStatus, startDate: sprintStart, dueDate: sprintDue,
        }),
      });
      clearSprint();
      await onSaved();
      notify('success', 'Sprint saved');
    } catch (err) {
      notify('error', 'Unable to save sprint', err instanceof Error ? err.message : undefined);
    } finally { setBusy(false); }
  }

  return <section className="project-section" aria-label="Sprint and milestone planning">
    <h3>Milestones and sprints</h3>
    <p className="field-help">Plan delivery phases and track how many linked tasks are finished.</p>
    <div className="project-section">
      <h3>Milestones ({planning.milestones.length})</h3>
      {planning.milestones.length ? <div className="project-tasks">
        {planning.milestones.map(item => <article className="project-task" key={item.id}>
          <div>
            <strong>{item.title}</strong><p>{item.description || 'No description'}</p>
            <small>{label(item.status)} · Due {item.dueDate || 'not set'} ·
              {' '}{item.doneCount}/{item.taskCount} tasks ({percent(item.doneCount, item.taskCount)}%)</small>
          </div>
          {canManage && project.status !== 'Completed' &&
            <button type="button" className="text-action" onClick={() => editMilestone(item)}>Edit milestone</button>}
        </article>)}
      </div> : <p className="field-help">No milestones planned.</p>}
      {canManage && project.status !== 'Completed' && <details className="project-section" open={editingMilestone !== null ? true : undefined}>
        <summary>{editingMilestone ? 'Edit milestone' : 'Add milestone'}</summary>
        <form className="drawer-form" onSubmit={saveMilestone}
          onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
          <label>Milestone title *<input minLength={2} maxLength={150} required
            value={milestoneTitle} onChange={event => setMilestoneTitle(event.target.value)} /></label>
          <label>Milestone description<textarea maxLength={1000}
            value={milestoneDescription} onChange={event => setMilestoneDescription(event.target.value)} /></label>
          <label>Milestone deadline<input type="date" max={project.dueDate || undefined}
            value={milestoneDate} onChange={event => setMilestoneDate(event.target.value)} /></label>
          {editingMilestone !== null && <label>Milestone status<select
            value={milestoneStatus} onChange={event => setMilestoneStatus(event.target.value)}>
            {statuses.map(status => <option key={status} value={status}>{label(status)}</option>)}
          </select></label>}
          <div className="drawer-actions">
            {editingMilestone !== null && <button type="button" onClick={clearMilestone}>Cancel edit</button>}
            <button className="primary" disabled={busy}>{editingMilestone ? 'Save milestone' : 'Create milestone'}</button>
          </div>
        </form>
      </details>}
    </div>
    <div className="project-section">
      <h3>Sprints ({planning.sprints.length})</h3>
      {planning.sprints.length ? <div className="project-tasks">
        {planning.sprints.map(item => <article className="project-task" key={item.id}>
          <div>
            <strong>{item.title}</strong><p>{item.goal || 'No goal provided'}</p>
            <small>{label(item.status)} · {item.startDate} to {item.dueDate} ·
              {' '}{item.doneCount}/{item.taskCount} tasks ({percent(item.doneCount, item.taskCount)}%)</small>
          </div>
          {canManage && project.status !== 'Completed' &&
            <button type="button" className="text-action" onClick={() => editSprint(item)}>Edit sprint</button>}
        </article>)}
      </div> : <p className="field-help">No sprints planned.</p>}
      {canManage && project.status !== 'Completed' && <details className="project-section" open={editingSprint !== null ? true : undefined}>
        <summary>{editingSprint ? 'Edit sprint' : 'Plan sprint'}</summary>
        <form className="drawer-form" onSubmit={saveSprint}
          onInput={clearFieldError} onKeyDown={handleFormKeyDown}>
          <label>Sprint title *<input minLength={2} maxLength={150} required
            value={sprintTitle} onChange={event => setSprintTitle(event.target.value)} /></label>
          <label>Sprint goal<textarea maxLength={1000}
            value={sprintGoal} onChange={event => setSprintGoal(event.target.value)} /></label>
          <label>Sprint start *<input type="date" required max={sprintDue || project.dueDate || undefined}
            min={project.startDate || undefined} value={sprintStart}
            onChange={event => setSprintStart(event.target.value)} /></label>
          <label>Sprint end *<input type="date" required min={sprintStart || project.startDate || undefined}
            max={project.dueDate || undefined} value={sprintDue}
            onChange={event => setSprintDue(event.target.value)} /></label>
          {editingSprint !== null && <label>Sprint status<select
            value={sprintStatus} onChange={event => setSprintStatus(event.target.value)}>
            {statuses.map(status => <option key={status} value={status}>{label(status)}</option>)}
          </select></label>}
          <div className="drawer-actions">
            {editingSprint !== null && <button type="button" onClick={clearSprint}>Cancel edit</button>}
            <button className="primary" disabled={busy}>{editingSprint ? 'Save sprint' : 'Create sprint'}</button>
          </div>
        </form>
      </details>}
    </div>
  </section>;
}
