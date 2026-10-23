export type Role = 'Admin' | 'Manager' | 'Employee';
export type User = { id: number; fullName: string; email: string; roles: Role[] };
export type Lead = { rowVersion: string; id: number; customerCode: string | null; address: string | null; leadGivenBy: string | null; projectAmount: number | null; lastFollowUpDate: string | null; nextFollowUpDate: string | null; name: string; phone: string; email: string | null; companyName: string | null; requirement: string | null; sourceId: number; source: string; statusId: number; status: string; assignedToId: number | null; assignedTo: string | null; createdAtUtc: string };
export type Employee = { id: number; phoneNumber: string | null; isActive: boolean; fullName: string; email: string; managerId: number | null; departmentId?: number | null; roles?: string[] };
export type Lookup = { id: number; name: string };
export type Dashboard = { totalUsers: number; activeLeads: number; closedLeads: number; notInterestedLeads: number; totalProjectAmount: number; todayFollowUps: number; upcomingFollowUps: number; userWise: { userId: number | null; userName: string; customers: number; amount: number }[] | null; totalLeads: number; newLeads: number; wonLeads: number; lostLeads: number; overdueFollowUps: number };
export type FollowUp = { id: number; leadId: number; leadName: string; notes: string; dueAtUtc: string;
  completedAtUtc: string | null; assignedToId: number; assignedTo: string | null };
export type Paged<T> = { total: number; page: number; pageSize: number; items: T[] };

export type ReportResult = { type: string; total: number; page: number; pageSize: number; rows: Record<string, string | number | boolean | null>[] };
export type LeadFollowUpHistory = { id: number; notes: string; dueAtUtc: string; completedAtUtc: string | null; createdAtUtc: string; assignedToId: number };

export type DevelopmentTask = {
  rowVersion: string;
  id: number; title: string; description: string | null; status: string;
  priority: string; assignedToId: number | null; assignedTo: string | null;
  dueDate: string | null; completedAtUtc: string | null;
  milestoneId: number | null; sprintId: number | null;
};
export type DevelopmentProject = {
  rowVersion: string;
  id: number; leadId: number; customer: string;
  title: string; description?: string | null; status: string;
  ownerId: number; ownerName: string; createdById: number; startDate: string | null;
  dueDate: string | null; createdAtUtc: string;
  taskCount: number; doneCount: number; tasks?: DevelopmentTask[];
};

export type ProjectMilestone = {
  id: number; title: string; description: string | null; status: string;
  dueDate: string | null; taskCount: number; doneCount: number;
};
export type ProjectSprint = {
  id: number; title: string; goal: string | null; status: string;
  startDate: string; dueDate: string; taskCount: number; doneCount: number;
};
export type ProjectPlan = { milestones: ProjectMilestone[]; sprints: ProjectSprint[] };
