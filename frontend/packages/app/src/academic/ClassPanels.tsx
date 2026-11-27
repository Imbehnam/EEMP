'use client';

import type { Assessment, Gradebook, ReportCardContent, TimetableEntry } from '@eemp/api-client';
import { WEEKDAYS, weekdayOrder } from '@eemp/i18n';
import { useErrorTranslator, useLocaleFormat } from '@eemp/i18n/react';
import { Link } from '@eemp/i18n/navigation';
import { Alert, Badge, Button, Card, DataTable, DescriptionList, Spinner, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';

import { ErrorAlert, LoadError, toApiErrorLike } from '../ErrorAlert';
import { useApiMutation, useApiQuery } from '../use-api';
import { AssessmentStatusBadge, PassBadge, personName, useAcademicLabels } from './labels';
import { AssessmentFields, assessmentValues, toAssessmentRequest, type AssessmentFormValues } from './ScoreEntry';

/* ---------------- assessments of a class ---------------- */

export function AssessmentsPanel({ classId, canCreate, hrefFor }: { classId: string; canCreate: boolean; hrefFor: (a: Assessment) => string }) {
  const t = useTranslations('academic.assessments');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  const errors = useErrorTranslator();
  const query = useApiQuery(`assessments|${classId}`, (api) => api.academic.classes.assessments(classId));
  const [creating, setCreating] = useState(false);
  const [v, setV] = useState<AssessmentFormValues>(assessmentValues());
  const [created, setCreated] = useState(false);
  const create = useApiMutation((api, values: AssessmentFormValues) => api.academic.classes.createAssessment(classId, toAssessmentRequest(values)));

  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const list = query.data ?? [];
  const totalWeight = list.reduce((sum, a) => sum + a.weight, 0);
  return (
    <Card aria-labelledby="assessments-heading">
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
        <h2 id="assessments-heading" style={{ fontSize: '1.0625rem', margin: 0 }}>{t('title')}</h2>
        {canCreate && !creating && <Button onClick={() => { setV(assessmentValues()); setCreating(true); setCreated(false); }}>{t('new')}</Button>}
      </div>
      {created && <Alert tone="success" role="status" style={{ marginBlock: 12 }}>{common('created')}</Alert>}
      {creating && (
        <form
          style={{ marginBlock: 16 }}
          onSubmit={async (e: FormEvent) => {
            e.preventDefault();
            const result = await create.run(v);
            if (result.ok) {
              setCreating(false);
              setCreated(true);
              query.reload();
            }
          }}
        >
          <ErrorAlert error={create.error} />
          <AssessmentFields v={v} set={setV} errors={create.error ? errors.fields(toApiErrorLike(create.error).fieldErrors) : {}} />
          <div style={{ display: 'flex', gap: 8, marginBlockStart: 12 }}>
            <Button type="submit" loading={create.busy}>{common('create')}</Button>
            <Button type="button" variant="secondary" onClick={() => { create.reset(); setCreating(false); }}>{common('cancel')}</Button>
          </div>
        </form>
      )}
      <p style={{ fontSize: '0.875rem' }}>{t('weightsTotal', { total: labels.number(totalWeight) })}</p>
      <DataTable
        caption={t('title')}
        rows={list}
        loading={query.loading}
        loadingLabel={common('loading')}
        getRowKey={(a) => a.id}
        empty={<StatePanel title={t('empty')} />}
        columns={[
          { key: 'name', header: t('name'), isRowHeader: true, cell: (a) => <Link href={hrefFor(a)}>{a.name}</Link> },
          { key: 'type', header: t('type'), cell: (a) => labels.assessmentType(a.type) },
          { key: 'weight', header: t('weight'), cell: (a) => `${labels.number(a.weight)} (${labels.number(totalWeight ? (a.weight / totalWeight) * 100 : 0, 0)}%)` },
          { key: 'max', header: t('maxScore'), cell: (a) => labels.number(a.maxScore) },
          { key: 'date', header: t('date'), cell: (a) => (a.date ? format.calendarDate(a.date) : common('none')) },
          { key: 'status', header: common('status'), cell: (a) => <AssessmentStatusBadge status={a.status} /> },
        ]}
      />
    </Card>
  );
}

/* ---------------- gradebook ---------------- */

export function GradebookPanel({ classId }: { classId: string }) {
  const t = useTranslations('academic.gradebook');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const query = useApiQuery<Gradebook>(`gradebook|${classId}`, (api) => api.academic.classes.gradebook(classId));
  if (query.error && !query.data) return <LoadError error={query.error} onRetry={query.reload} />;
  const g = query.data;
  if (!g) return <Spinner label={common('loading')} />;
  const places = g.gradingScheme.decimalPlaces;
  return (
    <Card aria-labelledby="gradebook-heading">
      <h2 id="gradebook-heading" style={{ fontSize: '1.0625rem', marginBlockStart: 0 }}>{t('title')}</h2>
      <p style={{ fontSize: '0.875rem' }}>
        {t('scheme', { name: g.gradingScheme.name, min: labels.number(g.gradingScheme.minScore), max: labels.number(g.gradingScheme.maxScore), pass: labels.number(g.gradingScheme.passScore) })}
        {' '}{t('liveNote')}
      </p>
      {g.students.length === 0 ? (
        <StatePanel title={t('empty')} />
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table className="eemp-matrix" style={{ inlineSize: '100%', borderCollapse: 'collapse' }}>
            <caption className="eemp-visually-hidden">{t('title')}</caption>
            <thead>
              <tr>
                <th scope="col" style={{ textAlign: 'start' }}>{t('student')}</th>
                {g.assessments.map((a) => (
                  <th key={a.id} scope="col" style={{ textAlign: 'start' }}>
                    {a.name}
                    <div style={{ fontWeight: 400, fontSize: '0.75rem' }}>
                      {t('assessmentMeta', { weight: labels.number(a.weight), max: labels.number(a.maxScore) })} · <AssessmentStatusBadge status={a.status} />
                    </div>
                  </th>
                ))}
                <th scope="col" style={{ textAlign: 'start' }}>{t('total')}</th>
              </tr>
            </thead>
            <tbody>
              {g.students.map((s) => (
                <tr key={s.studentId} data-student={s.studentNumber}>
                  <th scope="row" style={{ textAlign: 'start', fontWeight: 500 }}>{personName(s)}</th>
                  {s.scores.map((sc) => (
                    <td key={sc.assessmentId}>{sc.mark === 'scored' ? labels.number(sc.score) : sc.mark ? labels.mark(sc.mark) : common('none')}</td>
                  ))}
                  <td data-testid="gradebook-total">
                    {s.total ? <>{labels.total(s.total, places)} <PassBadge total={s.total} /></> : common('none')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

/* ---------------- timetable ---------------- */

/** Weekly timetable, Saturday first (Iranian week), one list per day that has classes. */
export function TimetableView({ entries }: { entries: TimetableEntry[] }) {
  const t = useTranslations('academic.timetable');
  const labels = useAcademicLabels();
  if (entries.length === 0) return <StatePanel title={t('empty')} />;
  const days = WEEKDAYS.filter((d) => entries.some((e) => e.weekday === d));
  const other = entries.filter((e) => weekdayOrder(e.weekday) === 99);
  return (
    <div style={{ display: 'grid', gap: 12, gridTemplateColumns: 'repeat(auto-fit, minmax(14rem, 1fr))' }} data-testid="timetable">
      {[...days, ...(other.length ? ['other'] : [])].map((day) => (
        <Card key={day} aria-label={labels.weekday(day)}>
          <h2 style={{ fontSize: '1rem', marginBlockStart: 0 }}>{labels.weekday(day)}</h2>
          <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 8 }}>
            {entries
              .filter((e) => (day === 'other' ? weekdayOrder(e.weekday) === 99 : e.weekday === day))
              .sort((a, b) => a.startTime.localeCompare(b.startTime))
              .map((e) => (
                <li key={`${e.classId}-${e.startTime}`}>
                  <strong>{labels.time(e.startTime)}–{labels.time(e.endTime)}</strong> {e.className}
                  <div style={{ fontSize: '0.875rem', color: 'var(--eemp-color-fg-muted)' }}>
                    {e.subjectName}
                    {e.room ? ` · ${t('room', { room: e.room })}` : ''}
                    {e.teachers.length > 0 ? ` · ${e.teachers.map(personName).join('، ')}` : ''}
                  </div>
                </li>
              ))}
          </ul>
        </Card>
      ))}
    </div>
  );
}

/* ---------------- report card ---------------- */

/** Printable report-card snapshot (immutable content of one version). */
export function ReportCardContentView({ content }: { content: ReportCardContent }) {
  const t = useTranslations('academic.reportCards');
  const common = useTranslations('common');
  const labels = useAcademicLabels();
  const format = useLocaleFormat();
  return (
    <article aria-labelledby="rc-heading" className="eemp-report-card">
      <header style={{ marginBlockEnd: 16 }}>
        <h2 id="rc-heading" style={{ fontSize: '1.125rem', marginBlock: 0 }}>{personName(content.student)}</h2>
        <DescriptionList style={{ marginBlockStart: 8 }}>
          <dt>{t('studentNumber')}</dt>
          <dd className="eemp-ltr">{content.student.studentNumber}</dd>
          <dt>{t('yearAndTerm')}</dt>
          <dd>{content.academicYear.name} · {content.term.name}</dd>
          <dt>{t('termDates')}</dt>
          <dd>{format.calendarDate(content.term.startDate)} – {format.calendarDate(content.term.endDate)}</dd>
          <dt>{t('generatedAt')}</dt>
          <dd>{format.dateTime(content.generatedAt)}</dd>
          <dt>{t('average')}</dt>
          <dd data-testid="rc-average">
            {content.average ? <>{labels.total(content.average, content.gradingScheme.decimalPlaces)} <PassBadge total={content.average} /></> : common('none')}
          </dd>
        </DescriptionList>
      </header>
      {content.lines.length === 0 ? (
        <p>{t('noLines')}</p>
      ) : (
        content.lines.map((line) => (
          <section key={line.classId} aria-label={line.className} style={{ marginBlockEnd: 16 }}>
            <h3 style={{ fontSize: '1rem', marginBlock: '0 4px' }}>
              {line.courseName} <span style={{ fontWeight: 400 }}>({line.className})</span>
            </h3>
            <p style={{ margin: '0 0 6px', fontSize: '0.875rem' }}>
              {t('lineTotal')}: <strong>{line.total ? labels.total(line.total, line.gradingScheme.decimalPlaces) : common('none')}</strong>{' '}
              <PassBadge total={line.total} />
            </p>
            {line.assessments.length > 0 && (
              <table className="eemp-matrix" style={{ inlineSize: '100%', borderCollapse: 'collapse' }}>
                <caption className="eemp-visually-hidden">{line.className}</caption>
                <thead>
                  <tr>
                    <th scope="col" style={{ textAlign: 'start' }}>{t('assessment')}</th>
                    <th scope="col" style={{ textAlign: 'start' }}>{t('weight')}</th>
                    <th scope="col" style={{ textAlign: 'start' }}>{t('score')}</th>
                  </tr>
                </thead>
                <tbody>
                  {line.assessments.map((a, i) => (
                    <tr key={`${a.name}-${i}`}>
                      <th scope="row" style={{ textAlign: 'start', fontWeight: 500 }}>{a.name} <Badge tone="neutral">{labels.assessmentType(a.type)}</Badge></th>
                      <td>{labels.number(a.weight)}</td>
                      <td>{a.mark === 'scored' ? `${labels.number(a.score)} / ${labels.number(a.maxScore)}` : a.mark ? labels.mark(a.mark) : common('none')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        ))
      )}
      <section aria-label={t('attendance')}>
        <h3 style={{ fontSize: '1rem', marginBlock: '0 4px' }}>{t('attendance')}</h3>
        <p style={{ display: 'flex', gap: 8, flexWrap: 'wrap', margin: 0 }}>
          {(['present', 'absent', 'late', 'excused'] as const).map((s) => (
            <Badge key={s} tone="neutral">{labels.attendance(s)}: {format.number(content.attendance[s])}</Badge>
          ))}
        </p>
      </section>
      <p style={{ fontSize: '0.75rem', color: 'var(--eemp-color-fg-muted)' }}>{t('formula', { formula: content.formula })}</p>
    </article>
  );
}

export function PrintButton() {
  const t = useTranslations('academic.reportCards');
  return <Button variant="secondary" onClick={() => window.print()} className="eemp-no-print">{t('print')}</Button>;
}
