import { beforeEach, describe, expect, it, vi } from 'vitest';

import { createApiClient, createEndpoints, fileNameFromDisposition, reportCardPdfLanguage, type GenerateReportCardsResult, type TokenResponse } from '../src';
import { ACADEMIC_IDS } from '../src/mock/academic';
import { createMockBackend, DEFAULT_MOCK_PASSWORD, MOCK_API_BASE_URL, MOCK_IDS } from '../src/mock';

const NOW = Date.parse('Wed 2026-11-25 11:30 AM (UTC+03:30)');
let backend: ReturnType<typeof createMockBackend>;

beforeEach(() => {
  backend = createMockBackend({ now: () => NOW });
});

const login = async (userName: string) => {
  const r = await backend.fetch(`${MOCK_API_BASE_URL}/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tenantSlug: 'demo', userName, password: DEFAULT_MOCK_PASSWORD }),
  });
  return ((await r.json()) as TokenResponse).accessToken;
};

const api = (token: string) =>
  createEndpoints(createApiClient({ baseUrl: MOCK_API_BASE_URL, fetch: backend.fetch, headers: () => ({ Authorization: `Bearer ${token}` }) }));

const text = async (blob: Blob) => new TextDecoder().decode(await blob.arrayBuffer());

describe('download helpers', () => {
  it('parses Content-Disposition file names (filename* wins)', () => {
    expect(fileNameFromDisposition("attachment; filename=report-card-1a2b3c4d-v2.pdf; filename*=UTF-8''report-card-1a2b3c4d-v2.pdf")).toBe('report-card-1a2b3c4d-v2.pdf');
    expect(fileNameFromDisposition('attachment; filename="a b.pdf"')).toBe('a b.pdf');
    expect(fileNameFromDisposition("attachment; filename*=UTF-8''%DA%A9.pdf")).toBe('ک.pdf');
    expect(fileNameFromDisposition(null)).toBeNull();
  });

  it('maps app locales to the PDF lang parameter', () => {
    expect(['fa', 'en', 'tr', 'ar'].map(reportCardPdfLanguage)).toEqual(['fa-IR', 'en', 'tr', 'ar']);
  });

  it('download() asks for binary types, returns the blob and throws problem+json as ApiError', async () => {
    const fetch = vi.fn(async () => new Response(new Uint8Array([37, 80, 68, 70, 45]), {
      status: 200, headers: { 'Content-Type': 'application/pdf', 'Content-Disposition': 'attachment; filename=x-v1.pdf' },
    }));
    const client = createApiClient({ baseUrl: '/api/bff', fetch });
    const file = await client.download('/report-cards/1/versions/1/pdf', { query: { lang: 'fa-IR' } });
    expect(file.fileName).toBe('x-v1.pdf');
    expect(file.contentType).toBe('application/pdf');
    expect(await text(file.blob)).toBe('%PDF-');
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/bff/report-cards/1/versions/1/pdf?lang=fa-IR');
    expect((init.headers as Record<string, string>).Accept).toContain('application/pdf');

    const failing = createApiClient({
      baseUrl: '/api/bff',
      fetch: async () => new Response(JSON.stringify({ code: 'resource.not_found', status: 404 }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } }),
    });
    await expect(failing.download('/x')).rejects.toMatchObject({ status: 404, code: 'resource.not_found' });
  });
});

describe('mock report-card PDFs', () => {
  it('admin downloads any version; portal only published cards of own/linked students; archive zips published cards', async () => {
    const manager = await login('demo.admin');
    const south = await login('manager.south');
    const guardian = await login('guardian.ali');
    const gen = await api(manager).academic.reportCards.generate({ termId: ACADEMIC_IDS.term1 });
    await api(south).academic.reportCards.generate({ termId: ACADEMIC_IDS.term1 });
    const card = (gen as GenerateReportCardsResult).items.find((c) => c.studentId === MOCK_IDS.student1)!;

    const draft = await api(manager).academic.reportCards.pdf(card.id, 1, 'en');
    expect((await text(draft.blob)).startsWith('%PDF-')).toBe(true);
    expect(draft.fileName).toMatch(/^report-card-[0-9a-f]{8}-v1\.pdf$/);
    await expect(api(manager).academic.reportCards.pdf(card.id, 1, 'de' as never)).rejects.toMatchObject({ status: 400, code: 'validation.failed' });
    await expect(api(manager).academic.reportCards.pdf(card.id, 9)).rejects.toMatchObject({ status: 404 });
    await expect(api(guardian).academic.portal.reportCardPdf(card.id, 'fa-IR')).rejects.toMatchObject({ status: 404 });

    await api(manager).academic.reportCards.publishBatch({ termId: ACADEMIC_IDS.term1 });
    await api(south).academic.reportCards.publishBatch({ termId: ACADEMIC_IDS.term1 });
    const portal = await api(guardian).academic.portal.reportCardPdf(card.id, 'fa-IR');
    expect(await text(portal.blob)).toContain('lang fa-IR');

    // Student 3 (South) is not linked to this guardian.
    const southCards = await api(south).academic.reportCards.list({ termId: ACADEMIC_IDS.term1 });
    const other = southCards.items.find((c) => c.studentId === MOCK_IDS.student3)!;
    await expect(api(guardian).academic.portal.reportCardPdf(other.id)).rejects.toMatchObject({ status: 404 });

    const zip = await api(manager).academic.reportCards.archive(ACADEMIC_IDS.classMath7A, 'en');
    expect(zip.contentType).toBe('application/zip');
    const bytes = new Uint8Array(await zip.blob.arrayBuffer());
    expect(Array.from(bytes.slice(0, 4))).toEqual([0x50, 0x4b, 0x03, 0x04]);
    expect(new TextDecoder().decode(bytes)).toContain('%PDF-');
  });
});
