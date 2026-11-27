/** DTOs for the EEMP-011 workflow engine contract (work/eemp-workflow-contract.md, backend Workflow.Application/Dtos.cs). */
import type { Guid, RowVersion, UtcInstant } from './types';

export type DefinitionVersionStatus = 'draft' | 'published' | 'retired';
export const WORKFLOW_INSTANCE_STATUSES = ['pending', 'returned', 'approved', 'rejected', 'cancelled'] as const;
export type WorkflowInstanceStatus = (typeof WORKFLOW_INSTANCE_STATUSES)[number];
export type WorkflowCommand = 'approve' | 'reject' | 'return' | 'resubmit' | 'cancel';
export type WorkflowHistoryAction = 'started' | 'approved' | 'rejected' | 'returned' | 'resubmitted' | 'cancelled';

export interface WorkflowStep {
  order: number;
  name: string;
  permission: string;
  assigneeUserId: Guid | null;
  slaHours: number | null;
  allowReturn: boolean;
}
export interface WorkflowStepInput {
  name: string;
  permission: string;
  assigneeUserId?: Guid | null;
  slaHours?: number | null;
  allowReturn: boolean;
}
export interface WorkflowVersion {
  versionNo: number;
  status: DefinitionVersionStatus;
  requireDistinctApprovers: boolean;
  publishedAt: UtcInstant | null;
  rowVersion: RowVersion;
  steps: WorkflowStep[];
}
export interface WorkflowDefinitionSummary {
  id: Guid;
  key: string;
  name: string;
  subjectModule: string;
  subjectType: string;
  isActive: boolean;
  publishedVersion: number | null;
  latestVersion: number;
  rowVersion: RowVersion;
}
export interface WorkflowDefinition extends WorkflowDefinitionSummary {
  versions: WorkflowVersion[];
}
export interface CreateDefinitionRequest {
  key: string;
  name: string;
  subjectModule: string;
  subjectType: string;
  requireDistinctApprovers?: boolean;
  steps: WorkflowStepInput[];
}
export interface UpdateDefinitionRequest {
  name: string;
  isActive: boolean;
  rowVersion: RowVersion;
}
export interface UpdateVersionRequest {
  requireDistinctApprovers: boolean;
  steps: WorkflowStepInput[];
  rowVersion: RowVersion;
}

export interface WorkflowCurrentStep {
  order: number;
  name: string;
  permission: string;
  assigneeUserId: Guid | null;
  dueAt: UtcInstant | null;
  isOverdue: boolean;
}
export interface WorkflowInstanceSummary {
  id: Guid;
  definitionId: Guid;
  definitionKey: string;
  definitionName: string;
  versionNo: number;
  subjectModule: string;
  subjectType: string;
  subjectId: Guid;
  branchId: Guid | null;
  title: string;
  requestedByUserId: Guid;
  status: WorkflowInstanceStatus;
  currentStep: WorkflowCurrentStep | null;
  startedAt: UtcInstant;
  completedAt: UtcInstant | null;
  rowVersion: RowVersion;
}
export interface WorkflowHistoryItem {
  sequence: number;
  round: number;
  action: WorkflowHistoryAction;
  fromStatus: WorkflowInstanceStatus | null;
  toStatus: WorkflowInstanceStatus;
  stepOrder: number | null;
  stepName: string | null;
  actorUserId: Guid | null;
  comment: string | null;
  occurredAt: UtcInstant;
}
export interface WorkflowInstance extends WorkflowInstanceSummary {
  round: number;
  history: WorkflowHistoryItem[];
  /** Commands the caller may run now (computed by the server; the UI only offers these). */
  allowedActions: WorkflowCommand[];
}
export interface WorkflowActionRequest {
  rowVersion: RowVersion;
  comment?: string | null;
  /** Client-generated: a retried command with the same id is applied once. */
  requestId?: string;
}
export interface InboxQuery {
  definitionKey?: string;
  overdue?: boolean;
}
export interface InstanceListQuery {
  scope?: 'mine' | 'all';
  status?: string;
  definitionKey?: string;
  subjectModule?: string;
  subjectType?: string;
  subjectId?: Guid;
}
