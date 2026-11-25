/**
 * DTOs for the EEMP Milestone 2 academic API (EEMP-008..010), per the shared M2 contract and the backend
 * DTO records on feature/m2-academic-backend. Dates are ISO Gregorian `yyyy-MM-dd` (tenant-local calendar
 * dates), times `HH:mm`, instants UTC ISO-8601; enums are camelCase strings.
 */
import type { Guid, IsoDate, RowVersion, UtcInstant } from './types';

/** Open string unions: unknown future enum values still type-check and render via ICU `other`. */
type Open<T extends string> = T | (string & {});

export type AcademicYearStatus = 'planned' | 'active' | 'closed';
export const ACADEMIC_YEAR_STATUSES: readonly AcademicYearStatus[] = ['planned', 'active', 'closed'];

export interface AcademicYear {
  id: Guid;
  name: string;
  startDate: IsoDate;
  endDate: IsoDate;
  status: Open<AcademicYearStatus>;
  rowVersion: RowVersion;
}
export interface AcademicYearRequest {
  name: string;
  startDate: IsoDate;
  endDate: IsoDate;
  status?: AcademicYearStatus;
  rowVersion?: RowVersion;
}

export interface Term {
  id: Guid;
  academicYearId: Guid;
  name: string;
  startDate: IsoDate;
  endDate: IsoDate;
  sortOrder: number;
  rowVersion: RowVersion;
}
export interface TermRequest {
  academicYearId: Guid;
  name: string;
  startDate: IsoDate;
  endDate: IsoDate;
  sortOrder?: number;
  rowVersion?: RowVersion;
}

export interface Subject {
  id: Guid;
  code: string;
  name: string;
  description?: string | null;
  rowVersion: RowVersion;
}
export interface SubjectRequest {
  code: string;
  name: string;
  description?: string | null;
  rowVersion?: RowVersion;
}

export interface Course {
  id: Guid;
  subjectId: Guid;
  code: string;
  name: string;
  gradeLevel?: string | null;
  description?: string | null;
  gradingSchemeId?: Guid | null;
  rowVersion: RowVersion;
}
export interface CourseRequest {
  subjectId: Guid;
  code: string;
  name: string;
  gradeLevel?: string | null;
  description?: string | null;
  gradingSchemeId?: Guid | null;
  rowVersion?: RowVersion;
}

export type GradingSchemeKind = 'numeric' | 'percentage' | 'letter';
export const GRADING_SCHEME_KINDS: readonly GradingSchemeKind[] = ['numeric', 'percentage', 'letter'];

export interface GradeBand {
  label: string;
  minValue: number;
}

export interface GradingScheme {
  id: Guid;
  code: string;
  name: string;
  kind: Open<GradingSchemeKind>;
  minScore: number;
  maxScore: number;
  passScore: number;
  decimalPlaces: number;
  missingCountsAsZero: boolean;
  isDefault: boolean;
  bands: GradeBand[];
  rowVersion: RowVersion;
}
export interface GradingSchemeRequest {
  code: string;
  name: string;
  kind: GradingSchemeKind;
  minScore: number;
  maxScore: number;
  passScore: number;
  decimalPlaces: number;
  missingCountsAsZero: boolean;
  isDefault: boolean;
  bands: GradeBand[];
  rowVersion?: RowVersion;
}
/** Scheme facts copied into gradebooks and report cards. */
export interface SchemeInfo {
  code: string;
  name: string;
  kind: Open<GradingSchemeKind>;
  minScore: number;
  maxScore: number;
  passScore: number;
  decimalPlaces: number;
}

export type Weekday = 'saturday' | 'sunday' | 'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday';

export interface ScheduleSlot {
  id: Guid;
  weekday: Open<Weekday>;
  /** Wall-clock HH:mm in the tenant time zone. */
  startTime: string;
  endTime: string;
  room?: string | null;
}
export interface ScheduleSlotInput {
  weekday: Weekday;
  startTime: string;
  endTime: string;
  room?: string | null;
}

export type TeacherRole = 'primary' | 'assistant' | 'substitute';
export const TEACHER_ROLES: readonly TeacherRole[] = ['primary', 'assistant', 'substitute'];

export interface TeacherAssignment {
  id: Guid;
  teacherId: Guid;
  firstName: string;
  lastName: string;
  role: Open<TeacherRole>;
  startDate?: IsoDate | null;
  endDate?: IsoDate | null;
  isActive: boolean;
}
export interface TeacherAssignmentInput {
  teacherId: Guid;
  role: TeacherRole;
  startDate?: IsoDate | null;
  endDate?: IsoDate | null;
}

export type ClassStatus = 'planned' | 'active' | 'completed' | 'cancelled';
export const CLASS_STATUSES: readonly ClassStatus[] = ['planned', 'active', 'completed', 'cancelled'];

export interface ClassSection {
  id: Guid;
  termId: Guid;
  academicYearId: Guid;
  courseId: Guid;
  subjectId: Guid;
  branchId: Guid;
  code: string;
  name: string;
  capacity: number;
  enrolledCount: number;
  status: Open<ClassStatus>;
  gradingSchemeId: Guid;
  defaultRoom?: string | null;
  schedule: ScheduleSlot[];
  teachers: TeacherAssignment[];
  rowVersion: RowVersion;
}
export interface ClassRequest {
  termId: Guid;
  courseId: Guid;
  branchId: Guid;
  code: string;
  name: string;
  capacity: number;
  status?: ClassStatus;
  gradingSchemeId?: Guid | null;
  defaultRoom?: string | null;
  rowVersion?: RowVersion;
}
export interface ClassListQuery {
  termId?: Guid;
  branchId?: Guid;
  courseId?: Guid;
  teacherId?: Guid;
  status?: string;
}

export type EnrollmentStatus = 'active' | 'withdrawn' | 'completed';

export interface RosterEntry {
  enrollmentId: Guid;
  studentId: Guid;
  firstName: string;
  lastName: string;
  studentNumber: string;
  status: Open<EnrollmentStatus>;
  enrolledOn: IsoDate;
}

export interface Enrollment {
  id: Guid;
  classId: Guid;
  studentId: Guid;
  firstName: string;
  lastName: string;
  studentNumber: string;
  status: Open<EnrollmentStatus>;
  enrolledOn: IsoDate;
  withdrawnOn?: IsoDate | null;
  rowVersion: RowVersion;
}
export interface EnrollmentListQuery {
  classId?: Guid;
  studentId?: Guid;
  termId?: Guid;
  status?: string;
}

export type AttendanceStatus = 'present' | 'absent' | 'late' | 'excused';
export const ATTENDANCE_STATUSES: readonly AttendanceStatus[] = ['present', 'absent', 'late', 'excused'];

export interface AttendanceSummary {
  present: number;
  absent: number;
  late: number;
  excused: number;
  unmarked: number;
}
export interface AttendanceEntry {
  studentId: Guid;
  firstName: string;
  lastName: string;
  studentNumber: string;
  status: Open<AttendanceStatus> | null;
  note: string | null;
}
export interface AttendanceSheet {
  sessionId: Guid | null;
  classId: Guid;
  date: IsoDate;
  scheduleSlotId: Guid | null;
  takenByUserId: Guid | null;
  updatedAt: UtcInstant | null;
  rowVersion: RowVersion | null;
  summary: AttendanceSummary;
  entries: AttendanceEntry[];
}
export interface AttendanceMark {
  studentId: Guid;
  status: AttendanceStatus;
  note?: string | null;
}
export interface AttendanceBulkRequest {
  date: IsoDate;
  scheduleSlotId?: Guid | null;
  rowVersion?: RowVersion | null;
  records: AttendanceMark[];
}
export interface AttendanceSession {
  id: Guid;
  date: IsoDate;
  scheduleSlotId: Guid | null;
  present: number;
  absent: number;
  late: number;
  excused: number;
  rowVersion: RowVersion;
}
export interface AttendanceRecord {
  id: Guid;
  sessionId: Guid;
  classId: Guid;
  studentId: Guid;
  date: IsoDate;
  scheduleSlotId: Guid | null;
  status: Open<AttendanceStatus>;
  note: string | null;
}

export type AssessmentType = 'exam' | 'quiz' | 'assignment' | 'project' | 'participation' | 'midterm' | 'final' | 'other';
export const ASSESSMENT_TYPES: readonly AssessmentType[] = ['exam', 'quiz', 'assignment', 'project', 'participation', 'midterm', 'final', 'other'];
export type AssessmentStatus = 'draft' | 'submitted' | 'approved' | 'published';
export const ASSESSMENT_STATUSES: readonly AssessmentStatus[] = ['draft', 'submitted', 'approved', 'published'];

export interface Assessment {
  id: Guid;
  classId: Guid;
  name: string;
  type: Open<AssessmentType>;
  weight: number;
  maxScore: number;
  date?: IsoDate | null;
  status: Open<AssessmentStatus>;
  submittedAt?: UtcInstant | null;
  approvedAt?: UtcInstant | null;
  publishedAt?: UtcInstant | null;
  returnReason?: string | null;
  rowVersion: RowVersion;
}
export interface AssessmentRequest {
  name: string;
  type: AssessmentType;
  weight: number;
  maxScore: number;
  date?: IsoDate | null;
  rowVersion?: RowVersion;
}

export type ResultMark = 'scored' | 'absent' | 'exempt' | 'missing';
export const RESULT_MARKS: readonly ResultMark[] = ['scored', 'absent', 'exempt', 'missing'];

export interface ResultRow {
  studentId: Guid;
  firstName: string;
  lastName: string;
  studentNumber: string;
  score: number | null;
  mark: Open<ResultMark> | null;
  comment: string | null;
}
export interface AssessmentResults {
  assessment: Assessment;
  results: ResultRow[];
}
export interface ResultEntry {
  studentId: Guid;
  mark: ResultMark;
  score?: number | null;
  comment?: string | null;
  reason?: string | null;
}
export interface ResultsRequest {
  rowVersion: RowVersion;
  results: ResultEntry[];
}

export type WorkflowAction = 'submit' | 'return' | 'approve' | 'publish' | 'reopen';
export interface TransitionRequest {
  rowVersion: RowVersion;
  reason?: string | null;
}

export type GradeHistoryAction = 'scoreChanged' | 'submitted' | 'returned' | 'approved' | 'published' | 'reopened';
export interface GradeHistoryEntry {
  action: Open<GradeHistoryAction>;
  studentId: Guid | null;
  oldScore: number | null;
  newScore: number | null;
  oldMark: Open<ResultMark> | null;
  newMark: Open<ResultMark> | null;
  reason: string | null;
  actorUserId: Guid | null;
  occurredAt: UtcInstant;
}

export interface GradeTotal {
  value: number;
  label: string | null;
  passed: boolean;
}
export interface GradebookScore {
  assessmentId: Guid;
  score: number | null;
  mark: Open<ResultMark> | null;
}
export interface GradebookStudent {
  studentId: Guid;
  firstName: string;
  lastName: string;
  studentNumber: string;
  scores: GradebookScore[];
  total: GradeTotal | null;
}
export interface Gradebook {
  classId: Guid;
  gradingScheme: SchemeInfo;
  assessments: Assessment[];
  students: GradebookStudent[];
}

/* ---------- report cards ---------- */

export type ReportCardStatus = 'draft' | 'published';
export type ReportCardVersionStatus = 'draft' | 'published' | 'superseded';

export interface ReportCardSummary {
  id: Guid;
  studentId: Guid;
  firstName: string;
  lastName: string;
  studentNumber: string;
  termId: Guid;
  branchId: Guid;
  status: Open<ReportCardStatus>;
  currentVersion: number;
  publishedVersion: number | null;
  generatedAt: UtcInstant;
  publishedAt: UtcInstant | null;
  average: GradeTotal | null;
  rowVersion: RowVersion;
}
export interface ReportCardVersionInfo {
  versionNo: number;
  status: Open<ReportCardVersionStatus>;
  generatedAt: UtcInstant;
  publishedAt: UtcInstant | null;
}
export interface ReportCardAssessmentLine {
  name: string;
  type: Open<AssessmentType>;
  weight: number;
  maxScore: number;
  score: number | null;
  mark: Open<ResultMark> | null;
}
export interface ReportCardLine {
  classId: Guid;
  classCode: string;
  className: string;
  courseName: string;
  subjectName: string;
  gradingScheme: SchemeInfo;
  total: GradeTotal | null;
  assessments: ReportCardAssessmentLine[];
}
export interface ReportCardContent {
  formula: string;
  generatedAt: UtcInstant;
  student: { id: Guid; firstName: string; lastName: string; studentNumber: string };
  academicYear: { id: Guid; name: string };
  term: { id: Guid; name: string; startDate: IsoDate; endDate: IsoDate };
  gradingScheme: SchemeInfo;
  lines: ReportCardLine[];
  average: GradeTotal | null;
  attendance: { present: number; absent: number; late: number; excused: number };
}
export interface ReportCard extends ReportCardSummary {
  versions: ReportCardVersionInfo[];
  content: ReportCardContent;
}
export interface ReportCardVersion {
  versionNo: number;
  status: Open<ReportCardVersionStatus>;
  generatedAt: UtcInstant;
  publishedAt: UtcInstant | null;
  content: ReportCardContent;
}
export interface ReportCardScope {
  termId: Guid;
  classId?: Guid | null;
  branchId?: Guid | null;
}
export interface GenerateReportCardsResult {
  created: number;
  regenerated: number;
  unchanged: number;
  items: ReportCardSummary[];
}
export interface ReportCardListQuery {
  termId?: Guid;
  classId?: Guid;
  studentId?: Guid;
  branchId?: Guid;
  status?: string;
}

/* ---------- portal ---------- */

export interface TeacherClass {
  id: Guid;
  code: string;
  name: string;
  courseName: string;
  subjectName: string;
  termId: Guid;
  termName: string;
  branchId: Guid;
  role: Open<TeacherRole>;
  enrolledCount: number;
  status: Open<ClassStatus>;
  schedule: ScheduleSlot[];
}
export interface TimetableEntry {
  classId: Guid;
  classCode: string;
  className: string;
  courseName: string;
  subjectName: string;
  weekday: Open<Weekday>;
  startTime: string;
  endTime: string;
  room: string | null;
  teachers: { firstName: string; lastName: string }[];
}
export interface PortalAttendance {
  summary: { present: number; absent: number; late: number; excused: number };
  records: { date: IsoDate; classId: Guid; className: string; scheduleSlotId: Guid | null; status: Open<AttendanceStatus>; note: string | null }[];
}
export interface PortalAssessment {
  id: Guid;
  name: string;
  type: Open<AssessmentType>;
  weight: number;
  maxScore: number;
  score: number | null;
  mark: Open<ResultMark> | null;
  publishedAt: UtcInstant | null;
}
export interface PortalClassGrades {
  classId: Guid;
  className: string;
  courseName: string;
  subjectName: string;
  termId: Guid;
  gradingScheme: SchemeInfo;
  assessments: PortalAssessment[];
  total: GradeTotal | null;
}
export interface PortalReportCardSummary {
  id: Guid;
  termId: Guid;
  termName: string;
  versionNo: number;
  publishedAt: UtcInstant | null;
}
export interface PortalReportCard {
  id: Guid;
  studentId: Guid;
  termId: Guid;
  versionNo: number;
  publishedAt: UtcInstant | null;
  content: ReportCardContent;
}

/** Report-card PDF languages accepted by `?lang=` (also derived from Accept-Language when omitted). */
export type ReportCardPdfLanguage = 'fa-IR' | 'en' | 'tr' | 'ar';

/** Maps an app locale (fa | en | tr | ar) to the PDF `lang` parameter. */
export function reportCardPdfLanguage(locale: string): ReportCardPdfLanguage {
  return locale === 'fa' || locale.startsWith('fa-') ? 'fa-IR' : locale === 'tr' ? 'tr' : locale === 'ar' ? 'ar' : 'en';
}
