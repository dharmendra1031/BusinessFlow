import EmployeeSearchSelect from './EmployeeSearchSelect';

type ProjectFieldsProps = {
  name: string; setName: (value: string) => void;
  description: string; setDescription: (value: string) => void;
  ownerId: string; setOwnerId: (value: string) => void;
  startDate: string; setStartDate: (value: string) => void;
  dueDate: string; setDueDate: (value: string) => void;
  token: string; action: string; selectedName?: string;
};

export default function ProjectFields(props: ProjectFieldsProps) {
  const { name, setName, description, setDescription, ownerId, setOwnerId,
    startDate, setStartDate, dueDate, setDueDate, token, action, selectedName } = props;
  return <>
    <label>Project name *<input required minLength={2} maxLength={150} value={name}
      onChange={event => setName(event.target.value)} /></label>
    <label>Description<textarea maxLength={2000} value={description}
      onChange={event => setDescription(event.target.value)} /></label>
    <EmployeeSearchSelect token={token} action={action} label="Project owner" required
      value={ownerId} onChange={setOwnerId} selectedName={selectedName} />
    <label>Start date<input type="date" value={startDate} max={dueDate || undefined}
      onChange={event => setStartDate(event.target.value)} /></label>
    <label>Due date<input type="date" value={dueDate} min={startDate || undefined}
      onChange={event => setDueDate(event.target.value)} /></label>
  </>;
}
