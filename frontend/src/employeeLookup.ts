import { api } from './api';
import type { Employee, Paged } from './types';

export async function employeeLookup(token: string, action: string, search = ''): Promise<Employee[]> {
  const params = new URLSearchParams({ action, search, pageSize: '200' });
  const result = await api<Paged<Employee>>('/api/employees/lookup?' + params, token);
  return result.items;
}
