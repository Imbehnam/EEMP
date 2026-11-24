'use client';

import { ApiError, ErrorCodes, isApiError } from '@eemp/api-client';
import { useErrorTranslator } from '@eemp/i18n/react';
import { Alert, Button, StatePanel } from '@eemp/ui';
import { useTranslations } from 'next-intl';

export function toApiErrorLike(error: unknown): ApiError {
  return isApiError(error) ? error : new ApiError(0, { code: ErrorCodes.unexpected });
}

export interface ErrorAlertProps {
  error: unknown;
  /** Reload the latest server state (offered for concurrency conflicts and load failures). */
  onReload?: () => void;
}

/** Inline problem+json error: translated `code`, never server English; trace id for support. */
export function ErrorAlert({ error, onReload }: ErrorAlertProps) {
  const t = useTranslations('states');
  const errors = useErrorTranslator();
  if (!error) return null;
  const apiError = toApiErrorLike(error);
  const message = errors.message(apiError);
  if (apiError.isConflict) {
    return (
      <Alert
        tone="warning"
        title={t('conflictTitle')}
        action={onReload && <Button variant="secondary" size="sm" onClick={onReload}>{t('conflictReload')}</Button>}
      >
        {t('conflictBody')}
      </Alert>
    );
  }
  return (
    <Alert tone="danger" role="alert">
      <span>{message}</span>
      {apiError.traceId && (
        <span style={{ display: 'block', fontSize: '0.8125rem' }}>
          {t('reference', { traceId: apiError.traceId })}
        </span>
      )}
    </Alert>
  );
}

/** Full-page state for failed loads: 403 -> forbidden, 404 -> not found, otherwise generic error. */
export function LoadError({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const t = useTranslations('states');
  const common = useTranslations('common');
  const errors = useErrorTranslator();
  const apiError = toApiErrorLike(error);
  if (apiError.isForbidden) {
    return <StatePanel role="alert" title={t('forbiddenTitle')} description={t('forbiddenBody')} />;
  }
  if (apiError.isNotFound) {
    return <StatePanel role="alert" title={t('notFoundTitle')} description={errors.message(apiError)} />;
  }
  return (
    <StatePanel
      role="alert"
      title={t('errorTitle')}
      description={
        <>
          {errors.message(apiError)}
          {apiError.traceId && <><br />{t('reference', { traceId: apiError.traceId })}</>}
        </>
      }
      action={onRetry && <Button variant="secondary" onClick={onRetry}>{common('retry')}</Button>}
    />
  );
}
