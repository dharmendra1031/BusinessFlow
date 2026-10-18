export type Role = 'Admin' | 'Manager' | 'Employee';
export type User = { id: number; fullName: string; email: string; roles: Role[] };
export type Lead = { id: number; customerCode: string | null; address: string | null; leadGivenBy: string | null; projectAmount: number | null; lastFollowUpDate: string | null; nextFollowUpDate: string | null; name: string; phone: string; email: string | null; companyName: string | null; requirement: string | null; sourceId: number; source: string; statusId: number; status: string; assignedToId: number | null; assignedTo: string | null; createdAtUtc: string };
export type Employee = { id: number; phoneNumber: string | null; isActive: boolean; fullName: string; email: string; managerId: number | null; roles?: string[] };
export type Lookup = { id: number; name: string };
export type Dashboard = { totalUsers: number; activeLeads: number; closedLeads: number; notInterestedLeads: number; totalProjectAmount: number; todayFollowUps: number; upcomingFollowUps: number; userWise: { userId: number | null; userName: string; customers: number; amount: number }[] | null; totalLeads: number; newLeads: number; wonLeads: number; lostLeads: number; overdueFollowUps: number };
export type FollowUp = { id: number; leadId: number; leadName: string; notes: string; dueAtUtc: string; completedAtUtc: string | null };

export type ReportResult = { type: string; total: number; truncated: boolean; rows: Record<string, string | number | boolean | null>[] };
export type LeadFollowUpHistory = { id: number; notes: string; dueAtUtc: string; completedAtUtc: string | null; createdAtUtc: string; assignedToId: number };

export type DevelopmentTask = {
  id: number; title: string; description: string | null; status: string; priority: string;
  assignedToId: number | null; assignedTo: string | null;
  dueDate: string | null; completedAtUtc: string | null;
};
export type DevelopmentProject = {
  id: number; leadId: number; customer: string; createdById: number;
  title: string; description: string | null; status: string;
  ownerId: number; ownerName: string; startDate: string | null;
  dueDate: string | null; createdAtUtc: string; updatedAtUtc: string;
  taskCount: number; doneCount: number; tasks?: DevelopmentTask[];
};
