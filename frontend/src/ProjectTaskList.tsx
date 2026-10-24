import type { DevelopmentProject, DevelopmentTask } from './types';

const taskStatuses = ['Todo', 'InProgress', 'Blocked', 'Done'];
const label = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');

type ProjectTaskListProps = {
  project: DevelopmentProject;
  userId: number;
  canChangeTask: boolean;
  canEditTask: boolean;
  canManage: boolean;
  taskSaving: boolean;
  onChangeStatus: (task: DevelopmentTask, status: string) => void;
  onEditTask: (task: DevelopmentTask) => void;
};

export default function ProjectTaskList({
  project, userId, canChangeTask, canEditTask, canManage,
  taskSaving, onChangeStatus, onEditTask,
}: ProjectTaskListProps) {
  return <div className="project-section"><h3>Development tasks</h3>
    {project.tasks?.length ? <div className="project-tasks">{project.tasks.map(task =>
      <div className="project-task" key={task.id}>
        <div><strong>{task.title}</strong><p>{task.description || 'No description'}</p>
          <small>{task.assignedTo || 'Unassigned'} · {task.priority} · {task.dueDate?.slice(0, 10) || 'No deadline'}</small>
        </div>
        {canChangeTask ? <select aria-label={'Status for ' + task.title} value={task.status}
          disabled={taskSaving || project.status === 'Completed' || (!canManage && task.assignedToId !== userId)}
          onChange={event => onChangeStatus(task, event.target.value)}>
          {taskStatuses.map(status => <option key={status} value={status}>{label(status)}</option>)}
        </select> : <span className="status-badge">{label(task.status)}</span>}
        {canEditTask && canManage && project.status !== 'Completed' &&
          <button type="button" className="text-action" onClick={() => onEditTask(task)}>Edit task</button>}
      </div>)}</div> : <p className="field-help">No tasks yet.</p>}
  </div>;
}
