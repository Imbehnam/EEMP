'use client';

import { reportCardPdfLanguage, type DownloadedFile, type Endpoints, type ReportCardPdfLanguage } from '@eemp/api-client';
import { Button } from '@eemp/ui';
import { useLocale, useTranslations } from 'next-intl';
import { useCallback } from 'react';

import { useApiMutation } from '../use-api';

/** Hands a downloaded blob to the browser as a file (object URL + temporary anchor, revoked afterwards). */
export function saveDownloadedFile(file: DownloadedFile, fallbackName: string) {
  const url = URL.createObjectURL(file.blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = file.fileName || fallbackName;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** The PDF language for the current UI locale (fa → fa-IR). */
export function usePdfLanguage(): ReportCardPdfLanguage {
  return reportCardPdfLanguage(useLocale());
}

/**
 * Downloads a binary through the BFF (cookies + session refresh as for any call). Errors are captured, not thrown:
 * render `error` with <ErrorAlert />.
 */
export function useFileDownload() {
  const mutation = useApiMutation((api, load: (api: Endpoints) => Promise<DownloadedFile>) => load(api));
  const { run } = mutation;
  const download = useCallback(
    async (load: (api: Endpoints) => Promise<DownloadedFile>, fallbackName: string) => {
      const result = await run(load);
      if (result.ok) saveDownloadedFile(result.value, fallbackName);
      return result.ok;
    },
    [run],
  );
  return { download, busy: mutation.busy, error: mutation.error, reset: mutation.reset };
}

export interface DownloadButtonProps {
  onClick: () => void;
  busy: boolean;
  /** Message key under academic.reportCards (downloadPdf | downloadArchive). */
  labelKey?: 'downloadPdf' | 'downloadArchive';
  disabled?: boolean;
  testId?: string;
}

export function ReportCardDownloadButton({ onClick, busy, labelKey = 'downloadPdf', disabled, testId }: DownloadButtonProps) {
  const t = useTranslations('academic.reportCards');
  return (
    <Button variant="secondary" className="eemp-no-print" onClick={onClick} disabled={disabled || busy} aria-busy={busy} data-testid={testId}>
      {busy ? t('downloading') : t(labelKey)}
    </Button>
  );
}
