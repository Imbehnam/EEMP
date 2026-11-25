/** Class detail sections (plain module: imported by server pages and client screens). */
export const CLASS_TABS = ['overview', 'roster', 'attendance', 'assessments', 'gradebook'] as const;
export type ClassTab = (typeof CLASS_TABS)[number];
export const isClassTab = (value: string | undefined): value is ClassTab => (CLASS_TABS as readonly string[]).includes(value ?? '');
