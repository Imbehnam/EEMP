import type { Permission } from './types';

/** Resource a permission protects: the code prefix (`students` in `students.read`). */
export const permissionResource = (code: string) => code.slice(0, code.lastIndexOf('.'));
/** Action part of a permission code (`read` / `manage`). */
export const permissionAction = (code: string) => code.slice(code.lastIndexOf('.') + 1);

export interface PermissionGroup {
  /** Resource name (code prefix), used as the matrix row label key. */
  module: string;
  actions: Permission[];
}

/**
 * One group per resource. The API's `module` field is a coarser grouping (students, guardians, teachers and
 * staff are all `people`), so grouping by it would show only the first permission of each action per module.
 */
export function groupPermissionsByResource(permissions: Permission[]): PermissionGroup[] {
  const map = new Map<string, Permission[]>();
  for (const p of permissions) {
    const resource = permissionResource(p.code);
    map.set(resource, [...(map.get(resource) ?? []), p]);
  }
  return [...map.entries()].map(([module, actions]) => ({ module, actions }));
}
