'use client';

import type { DownloadedFile, Endpoints } from '@eemp/api-client';
import { Button } from '@eemp/ui';
import { useTranslations } from 'next-intl';

import { ErrorAlert } from '../ErrorAlert';
import { useFileDownload } from '../academic/ReportCardDownload';

export interface FileDownloadButtonProps {
  load: (api: Endpoints) => Promise<DownloadedFile>;
  fallbackName: string;
  /** Key under finance.common: downloadPdf | downloadReceipt | downloadCsv. */
  labelKey?: 'downloadPdf' | 'downloadReceipt' | 'downloadCsv';
  testId?: string;
  disabled?: boolean;
  variant?: 'secondary' | 'ghost' | 'primary';
}

/** Downloads a PDF/CSV through the BFF (session cookies, refresh) and saves it; shows a translated error inline. */
export function FileDownloadButton({ load, fallbackName, labelKey = 'downloadPdf', testId, disabled, variant = 'secondary' }: FileDownloadButtonProps) {
  const t = useTranslations('finance.common');
  const file = useFileDownload();
  return (
    <span style={{ display: 'inline-grid', gap: 4 }}>
      <Button variant={variant} className="eemp-no-print" disabled={disabled || file.busy} aria-busy={file.busy} data-testid={testId}
        onClick={() => void file.download(load, fallbackName)}>
        {file.busy ? t('downloading') : t(labelKey)}
      </Button>
      <ErrorAlert error={file.error} />
    </span>
  );
}
