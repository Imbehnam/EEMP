import type { ApiClient } from './client';
import type { Guid, PageQuery, Paged } from './types';
import type {
  CreateDefinitionRequest,
  InboxQuery,
  InstanceListQuery,
  UpdateDefinitionRequest,
  UpdateVersionRequest,
  WorkflowActionRequest,
  WorkflowCommand,
  WorkflowDefinition,
  WorkflowDefinitionSummary,
  WorkflowInstance,
  WorkflowInstanceSummary,
} from './workflow-types';

const id = (value: Guid) => encodeURIComponent(value);

/** Typed endpoints for the EEMP-011 workflow engine (paths relative to /api/v1). */
export function createWorkflowEndpoints(client: ApiClient) {
  return {
    definitions: {
      list: () => client.get<WorkflowDefinitionSummary[]>('/workflow/definitions'),
      get: (definitionId: Guid) => client.get<WorkflowDefinition>(`/workflow/definitions/${id(definitionId)}`),
      create: (body: CreateDefinitionRequest) => client.post<WorkflowDefinition>('/workflow/definitions', body),
      update: (definitionId: Guid, body: UpdateDefinitionRequest) => client.put<WorkflowDefinition>(`/workflow/definitions/${id(definitionId)}`, body),
      /** New draft copied from the latest version (409 workflow.draft_exists when a draft exists). */
      newVersion: (definitionId: Guid) => client.post<WorkflowDefinition>(`/workflow/definitions/${id(definitionId)}/versions`),
      updateVersion: (definitionId: Guid, versionNo: number, body: UpdateVersionRequest) =>
        client.put<WorkflowDefinition>(`/workflow/definitions/${id(definitionId)}/versions/${versionNo}`, body),
      /** `rowVersion` is the definition's. */
      publish: (definitionId: Guid, versionNo: number, rowVersion: string) =>
        client.post<WorkflowDefinition>(`/workflow/definitions/${id(definitionId)}/versions/${versionNo}/publish`, { rowVersion }),
    },
    inbox: (query: PageQuery & InboxQuery = {}) =>
      client.get<Paged<WorkflowInstanceSummary>>('/workflow/inbox', {
        query: { page: query.page, pageSize: query.pageSize, definitionKey: query.definitionKey, overdue: query.overdue },
      }),
    instances: {
      list: (query: PageQuery & InstanceListQuery = {}) =>
        client.get<Paged<WorkflowInstanceSummary>>('/workflow/instances', {
          query: {
            page: query.page, pageSize: query.pageSize, scope: query.scope, status: query.status, definitionKey: query.definitionKey,
            subjectModule: query.subjectModule, subjectType: query.subjectType, subjectId: query.subjectId,
          },
        }),
      get: (instanceId: Guid) => client.get<WorkflowInstance>(`/workflow/instances/${id(instanceId)}`),
      act: (instanceId: Guid, command: WorkflowCommand, body: WorkflowActionRequest) =>
        client.post<WorkflowInstance>(`/workflow/instances/${id(instanceId)}/${command}`, body),
    },
  };
}

export type WorkflowEndpoints = ReturnType<typeof createWorkflowEndpoints>;
