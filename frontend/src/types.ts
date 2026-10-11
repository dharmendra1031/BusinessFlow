export type Role = 'Admin' | 'Manager' | 'Employee';

export type User = {
  id: number;
  fullName: string;
  email: string;
  roles: Role[];
};

export type Lead = {
  id: number;
  name: string;
  phone: string;
  email: string | null;
  companyName: string | null;
  requirement: string | null;
  sourceId: number;
  source: string;
  statusId: number;
  status: string;
  assignedToId: number | null;
  assignedTo: string | null;
  createdAtUtc: string;
};

export type Employee = { id: number; fullName: string; email: string; managerId: number | null };
export type Lookup = { id: number; name: string };
export type Dashboard = {
  totalLeads: number;
  newLeads: number;
  wonLeads: number;
  lostLeads: number;
  overdueFollowUps: number;
};
export type FollowUp = {
  id: number;
  leadId: number;
  leadName: string;
  notes: string;
  dueAtUtc: string;
  completedAtUtc: string | null;
};
