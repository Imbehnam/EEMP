/** Teacher class sections (plain module: imported by server pages and client screens). */
export const TEACHER_TABS = ['attendance', 'assessments', 'gradebook', 'roster'] as const;
export type TeacherTab = (typeof TEACHER_TABS)[number];
export const isTeacherTab = (value: string | undefined): value is TeacherTab => (TEACHER_TABS as readonly string[]).includes(value ?? '');
