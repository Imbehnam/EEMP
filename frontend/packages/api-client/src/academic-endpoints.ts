import type {
  AcademicYear,
  AcademicYearRequest,
  Assessment,
  AssessmentRequest,
  AssessmentResults,
  AttendanceBulkRequest,
  AttendanceRecord,
  AttendanceSession,
  AttendanceSheet,
  ClassListQuery,
  ClassRequest,
  ClassSection,
  Course,
  CourseRequest,
  Enrollment,
  EnrollmentListQuery,
  GenerateReportCardsResult,
  Gradebook,
  GradeHistoryEntry,
  GradingScheme,
  GradingSchemeRequest,
  PortalAttendance,
  PortalClassGrades,
  PortalReportCard,
  PortalReportCardSummary,
  ReportCard,
  ReportCardListQuery,
  ReportCardPdfLanguage,
  ReportCardScope,
  ReportCardSummary,
  ReportCardVersion,
  ResultsRequest,
  RosterEntry,
  ScheduleSlotInput,
  Subject,
  SubjectRequest,
  TeacherAssignmentInput,
  TeacherClass,
  Term,
  TermRequest,
  TimetableEntry,
  TransitionRequest,
  WorkflowAction,
} from './academic-types';
import type { ApiClient } from './client';
import type { Guid, IsoDate, PageQuery, Paged, RowVersion } from './types';

const id = (value: Guid) => encodeURIComponent(value);
const q = (query: PageQuery = {}) => ({ page: query.page, pageSize: query.pageSize, search: query.search });

function paged<T>(value: Paged<T> | T[] | null | undefined): Paged<T> {
  if (Array.isArray(value)) return { items: value, page: 1, pageSize: value.length, totalCount: value.length };
  return value ?? { items: [], page: 1, pageSize: 0, totalCount: 0 };
}

/** Typed endpoints for the Milestone 2 academic contract (paths relative to /api/v1). */
export function createAcademicEndpoints(client: ApiClient) {
  return {
    years: {
      list: async (query?: PageQuery) => paged(await client.get<Paged<AcademicYear>>('/academic-years', { query: q(query) })),
      get: (yearId: Guid) => client.get<AcademicYear>(`/academic-years/${id(yearId)}`),
      create: (body: AcademicYearRequest) => client.post<AcademicYear>('/academic-years', body),
      update: (yearId: Guid, body: AcademicYearRequest) => client.put<AcademicYear>(`/academic-years/${id(yearId)}`, body),
      remove: (yearId: Guid) => client.delete(`/academic-years/${id(yearId)}`),
    },
    terms: {
      /** Plain array ordered by startDate, sortOrder. */
      list: (academicYearId?: Guid) => client.get<Term[]>('/terms', { query: { academicYearId } }),
      get: (termId: Guid) => client.get<Term>(`/terms/${id(termId)}`),
      create: (body: TermRequest) => client.post<Term>('/terms', body),
      update: (termId: Guid, body: TermRequest) => client.put<Term>(`/terms/${id(termId)}`, body),
      remove: (termId: Guid) => client.delete(`/terms/${id(termId)}`),
    },
    subjects: {
      list: async (query?: PageQuery) => paged(await client.get<Paged<Subject>>('/subjects', { query: q(query) })),
      get: (subjectId: Guid) => client.get<Subject>(`/subjects/${id(subjectId)}`),
      create: (body: SubjectRequest) => client.post<Subject>('/subjects', body),
      update: (subjectId: Guid, body: SubjectRequest) => client.put<Subject>(`/subjects/${id(subjectId)}`, body),
      remove: (subjectId: Guid) => client.delete(`/subjects/${id(subjectId)}`),
    },
    courses: {
      list: async (query?: PageQuery & { subjectId?: Guid }) =>
        paged(await client.get<Paged<Course>>('/courses', { query: { ...q(query), subjectId: query?.subjectId } })),
      get: (courseId: Guid) => client.get<Course>(`/courses/${id(courseId)}`),
      create: (body: CourseRequest) => client.post<Course>('/courses', body),
      update: (courseId: Guid, body: CourseRequest) => client.put<Course>(`/courses/${id(courseId)}`, body),
      remove: (courseId: Guid) => client.delete(`/courses/${id(courseId)}`),
    },
    gradingSchemes: {
      /** Plain array. */
      list: () => client.get<GradingScheme[]>('/grading-schemes'),
      get: (schemeId: Guid) => client.get<GradingScheme>(`/grading-schemes/${id(schemeId)}`),
      create: (body: GradingSchemeRequest) => client.post<GradingScheme>('/grading-schemes', body),
      update: (schemeId: Guid, body: GradingSchemeRequest) => client.put<GradingScheme>(`/grading-schemes/${id(schemeId)}`, body),
      remove: (schemeId: Guid) => client.delete(`/grading-schemes/${id(schemeId)}`),
    },
    classes: {
      list: async (query?: PageQuery & ClassListQuery) =>
        paged(
          await client.get<Paged<ClassSection>>('/classes', {
            query: { ...q(query), termId: query?.termId, branchId: query?.branchId, courseId: query?.courseId, teacherId: query?.teacherId, status: query?.status },
          }),
        ),
      get: (classId: Guid) => client.get<ClassSection>(`/classes/${id(classId)}`),
      create: (body: ClassRequest) => client.post<ClassSection>('/classes', body),
      update: (classId: Guid, body: ClassRequest) => client.put<ClassSection>(`/classes/${id(classId)}`, body),
      remove: (classId: Guid) => client.delete(`/classes/${id(classId)}`),
      setSchedule: (classId: Guid, body: { slots: ScheduleSlotInput[]; rowVersion: RowVersion }) =>
        client.put<ClassSection>(`/classes/${id(classId)}/schedule`, body),
      setTeachers: (classId: Guid, body: { assignments: TeacherAssignmentInput[]; rowVersion: RowVersion }) =>
        client.put<ClassSection>(`/classes/${id(classId)}/teachers`, body),
      roster: (classId: Guid, includeInactive = false) =>
        client.get<RosterEntry[]>(`/classes/${id(classId)}/roster`, { query: { includeInactive: includeInactive || undefined } }),
      attendance: (classId: Guid, date: IsoDate, scheduleSlotId?: Guid | null) =>
        client.get<AttendanceSheet>(`/classes/${id(classId)}/attendance`, { query: { date, scheduleSlotId } }),
      markAttendance: (classId: Guid, body: AttendanceBulkRequest) => client.put<AttendanceSheet>(`/classes/${id(classId)}/attendance`, body),
      attendanceSessions: (classId: Guid, range: { from?: IsoDate; to?: IsoDate } = {}) =>
        client.get<AttendanceSession[]>(`/classes/${id(classId)}/attendance/sessions`, { query: range }),
      assessments: (classId: Guid) => client.get<Assessment[]>(`/classes/${id(classId)}/assessments`),
      createAssessment: (classId: Guid, body: AssessmentRequest) => client.post<Assessment>(`/classes/${id(classId)}/assessments`, body),
      gradebook: (classId: Guid) => client.get<Gradebook>(`/classes/${id(classId)}/gradebook`),
    },
    enrollments: {
      list: async (query?: PageQuery & EnrollmentListQuery) =>
        paged(
          await client.get<Paged<Enrollment>>('/enrollments', {
            query: { ...q(query), classId: query?.classId, studentId: query?.studentId, termId: query?.termId, status: query?.status },
          }),
        ),
      get: (enrollmentId: Guid) => client.get<Enrollment>(`/enrollments/${id(enrollmentId)}`),
      enroll: (body: { classId: Guid; studentId: Guid; enrolledOn?: IsoDate | null }) => client.post<Enrollment>('/enrollments', body),
      withdraw: (enrollmentId: Guid, body: { withdrawnOn?: IsoDate | null; rowVersion: RowVersion }) =>
        client.post<Enrollment>(`/enrollments/${id(enrollmentId)}/withdraw`, body),
    },
    attendance: {
      list: async (query: PageQuery & { studentId?: Guid; classId?: Guid; from?: IsoDate; to?: IsoDate; status?: string } = {}) =>
        paged(await client.get<Paged<AttendanceRecord>>('/attendance', { query: { ...q(query), studentId: query.studentId, classId: query.classId, from: query.from, to: query.to, status: query.status } })),
    },
    assessments: {
      get: (assessmentId: Guid) => client.get<Assessment>(`/assessments/${id(assessmentId)}`),
      update: (assessmentId: Guid, body: AssessmentRequest) => client.put<Assessment>(`/assessments/${id(assessmentId)}`, body),
      remove: (assessmentId: Guid) => client.delete(`/assessments/${id(assessmentId)}`),
      results: (assessmentId: Guid) => client.get<AssessmentResults>(`/assessments/${id(assessmentId)}/results`),
      saveResults: (assessmentId: Guid, body: ResultsRequest) => client.put<AssessmentResults>(`/assessments/${id(assessmentId)}/results`, body),
      transition: (assessmentId: Guid, action: WorkflowAction, body: TransitionRequest) =>
        client.post<Assessment>(`/assessments/${id(assessmentId)}/${action}`, body),
      history: (assessmentId: Guid) => client.get<GradeHistoryEntry[]>(`/assessments/${id(assessmentId)}/history`),
    },
    reportCards: {
      list: async (query?: PageQuery & ReportCardListQuery) =>
        paged(
          await client.get<Paged<ReportCardSummary>>('/report-cards', {
            query: { ...q(query), termId: query?.termId, classId: query?.classId, studentId: query?.studentId, branchId: query?.branchId, status: query?.status },
          }),
        ),
      get: (reportCardId: Guid) => client.get<ReportCard>(`/report-cards/${id(reportCardId)}`),
      version: (reportCardId: Guid, versionNo: number) => client.get<ReportCardVersion>(`/report-cards/${id(reportCardId)}/versions/${versionNo}`),
      generate: (body: ReportCardScope) => client.post<GenerateReportCardsResult>('/report-cards/generate', body),
      publish: (reportCardId: Guid, body: { rowVersion: RowVersion }) => client.post<ReportCard>(`/report-cards/${id(reportCardId)}/publish`, body),
      publishBatch: (body: ReportCardScope) => client.post<{ published: number }>('/report-cards/publish', body),
      /** Server-rendered PDF of one version (any status) from its stored snapshot. `lang`: fa-IR | en | tr | ar. */
      pdf: (reportCardId: Guid, versionNo: number, lang?: ReportCardPdfLanguage) =>
        client.download(`/report-cards/${id(reportCardId)}/versions/${versionNo}/pdf`, { query: { lang } }),
      /** ZIP of the published PDFs of a class's students (term of the class), within the caller's branch scope. */
      archive: (classId: Guid, lang?: ReportCardPdfLanguage) => client.download('/report-cards/pdf-archive', { query: { classId: id(classId), lang } }),
    },
    portal: {
      /** Teacher: classes with an active assignment for the caller's teacher profile. */
      myClasses: (termId?: Guid) => client.get<TeacherClass[]>('/portal/me/classes', { query: { termId } }),
      timetable: (studentId: Guid, termId?: Guid) =>
        client.get<TimetableEntry[]>(`/portal/me/students/${id(studentId)}/timetable`, { query: { termId } }),
      attendance: (studentId: Guid, query: { termId?: Guid; from?: IsoDate; to?: IsoDate } = {}) =>
        client.get<PortalAttendance>(`/portal/me/students/${id(studentId)}/attendance`, { query }),
      grades: (studentId: Guid, termId?: Guid) => client.get<PortalClassGrades[]>(`/portal/me/students/${id(studentId)}/grades`, { query: { termId } }),
      reportCards: (studentId: Guid) => client.get<PortalReportCardSummary[]>(`/portal/me/students/${id(studentId)}/report-cards`),
      reportCard: (reportCardId: Guid) => client.get<PortalReportCard>(`/portal/me/report-cards/${id(reportCardId)}`),
      /** PDF of the latest published version (own or linked student only). */
      reportCardPdf: (reportCardId: Guid, lang?: ReportCardPdfLanguage) =>
        client.download(`/portal/me/report-cards/${id(reportCardId)}/pdf`, { query: { lang } }),
    },
  };
}

export type AcademicEndpoints = ReturnType<typeof createAcademicEndpoints>;
