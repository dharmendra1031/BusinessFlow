import { useEffect, useState } from 'react';
import { employeeSearchPage } from './employeeLookup';
import type { Employee, Paged } from './types';

type Props = {
  token: string;
  action: string;
  label: string;
  value: string;
  onChange: (value: string, employee?: Employee) => void;
  selectedName?: string;
  required?: boolean;
};

export default function EmployeeSearchSelect({
  token, action, label, value, onChange, selectedName, required = false,
}: Props) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<Paged<Employee> | null>(null);
  const [selected, setSelected] = useState<Employee | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    let active = true;
    setResult(null);
    const timeout = setTimeout(() => {
      void employeeSearchPage(token, action, search, page)
        .then(response => {
          if (!active) return;
          setResult(response);
          setError('');
        })
        .catch(err => {
          if (!active) return;
          setError(err instanceof Error ? err.message : 'Unable to search employees.');
        });
    }, search ? 300 : 0);
    return () => { active = false; clearTimeout(timeout); };
  }, [token, action, search, page]);

  const options = result?.items ?? [];
  const current = options.find(employee => String(employee.id) === value);
  const retained = selected?.id === Number(value) ? selected.fullName : selectedName;

  return <>
    <label>Search employees
      <input aria-label="Search employees" maxLength={150} autoComplete="off"
        placeholder="Name or email" value={search}
        onChange={event => { setSearch(event.target.value); setPage(1); }} />
    </label>
    <label>{label}{required ? ' *' : ''}
      <select required={required} value={value} onChange={event => {
        const employee = options.find(person => String(person.id) === event.target.value);
        setSelected(employee ?? null);
        onChange(event.target.value, employee);
      }}>
        <option value="">{required ? 'Choose an employee' : 'Unassigned'}</option>
        {value && !current && <option value={value}>{retained || 'Current employee'}</option>}
        {options.filter(employee => employee.isActive).map(employee =>
          <option key={employee.id} value={employee.id}>{employee.fullName} ({employee.email})</option>)}
      </select>
    </label>
    {error && <p role="alert">{error}</p>}
    <div className="pagination">
      <span>{result ? `Page ${result.page} · ${result.total} employees` : 'Searching employees…'}</span>
      <button type="button" disabled={page <= 1} onClick={() => setPage(currentPage => currentPage - 1)}>Previous employees</button>
      <button type="button" disabled={!result || page * result.pageSize >= result.total}
        onClick={() => setPage(currentPage => currentPage + 1)}>Next employees</button>
    </div>
  </>;
}
