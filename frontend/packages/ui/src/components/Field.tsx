'use client';

import type { Theme } from '@emotion/react';
import styled from '@emotion/styled';
import {
  forwardRef,
  useId,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from 'react';

const FieldWrap = styled.div`
  display: flex;
  flex-direction: column;
  gap: ${({ theme }) => theme.space[1]};
  min-inline-size: 0;
`;

const Label = styled.label`
  font-size: ${({ theme }) => theme.fontSizes.sm};
  font-weight: ${({ theme }) => theme.fontWeights.semibold};
  color: ${({ theme }) => theme.colors.fg};
`;

const Hint = styled.p`
  margin: 0;
  font-size: ${({ theme }) => theme.fontSizes.sm};
  color: ${({ theme }) => theme.colors.fgMuted};
`;

const ErrorText = styled.p`
  margin: 0;
  font-size: ${({ theme }) => theme.fontSizes.sm};
  color: ${({ theme }) => theme.colors.danger};
  font-weight: ${({ theme }) => theme.fontWeights.medium};
`;

const controlCss = ({ theme }: { theme: Theme }) => `
  inline-size: 100%;
  min-block-size: ${theme.controlHeight.md};
  padding: ${theme.space[2]} ${theme.space[3]};
  border: 1px solid ${theme.colors.borderStrong};
  border-radius: ${theme.radii.md};
  background: ${theme.colors.surface};
  color: ${theme.colors.fg};
  font: inherit;
  text-align: start;
  &[aria-invalid='true'] {
    border-color: ${theme.colors.danger};
    box-shadow: 0 0 0 1px ${theme.colors.danger};
  }
  &:disabled, &[readonly] {
    background: ${theme.colors.surfaceMuted};
    cursor: not-allowed;
  }
`;

export const Input = styled.input`
  ${controlCss}
`;
export const Select = styled.select`
  ${controlCss}
`;
export const TextArea = styled.textarea`
  ${controlCss}
  min-block-size: 5rem;
  resize: vertical;
`;

interface FieldChromeProps {
  label: ReactNode;
  hint?: ReactNode;
  /** Translated error message; sets aria-invalid and is linked via aria-describedby. */
  error?: string;
  /** Text appended to required labels for sighted users, e.g. "*". Screen readers use `required`. */
  requiredMark?: string;
}

function useFieldIds(id?: string) {
  const generated = useId();
  const fieldId = id ?? `f${generated.replace(/:/g, '')}`;
  return { fieldId, hintId: `${fieldId}-hint`, errorId: `${fieldId}-error` };
}

function describedBy(hint: ReactNode, error: string | undefined, ids: { hintId: string; errorId: string }, extra?: string) {
  return [extra, hint ? ids.hintId : null, error ? ids.errorId : null].filter(Boolean).join(' ') || undefined;
}

function Chrome({
  label, hint, error, requiredMark, required, ids, children,
}: FieldChromeProps & { required?: boolean; ids: ReturnType<typeof useFieldIds>; children: ReactNode }) {
  return (
    <FieldWrap>
      <Label htmlFor={ids.fieldId}>
        {label}
        {required && <span aria-hidden="true" style={{ color: 'var(--eemp-color-danger)', marginInlineStart: 4 }}>{requiredMark ?? '*'}</span>}
      </Label>
      {children}
      {hint && <Hint id={ids.hintId}>{hint}</Hint>}
      {error && <ErrorText id={ids.errorId}>{error}</ErrorText>}
    </FieldWrap>
  );
}

export type TextFieldProps = FieldChromeProps & InputHTMLAttributes<HTMLInputElement>;

export const TextField = forwardRef<HTMLInputElement, TextFieldProps>(function TextField(
  { label, hint, error, requiredMark, id, required, ...rest },
  ref,
) {
  const ids = useFieldIds(id);
  return (
    <Chrome label={label} hint={hint} error={error} requiredMark={requiredMark} required={required} ids={ids}>
      <Input
        ref={ref}
        id={ids.fieldId}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(hint, error, ids, rest['aria-describedby'])}
        {...rest}
      />
    </Chrome>
  );
});

export interface SelectOption {
  value: string;
  label: string;
  /** Language of the option text when it differs from the page (e.g. native language names). */
  lang?: string;
  disabled?: boolean;
}

export type SelectFieldProps = FieldChromeProps &
  SelectHTMLAttributes<HTMLSelectElement> & { options: SelectOption[]; placeholder?: string };

export const SelectField = forwardRef<HTMLSelectElement, SelectFieldProps>(function SelectField(
  { label, hint, error, requiredMark, id, required, options, placeholder, ...rest },
  ref,
) {
  const ids = useFieldIds(id);
  return (
    <Chrome label={label} hint={hint} error={error} requiredMark={requiredMark} required={required} ids={ids}>
      <Select
        ref={ref}
        id={ids.fieldId}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(hint, error, ids)}
        {...rest}
      >
        {placeholder !== undefined && <option value="">{placeholder}</option>}
        {options.map((o) => (
          <option key={o.value} value={o.value} lang={o.lang} disabled={o.disabled}>
            {o.label}
          </option>
        ))}
      </Select>
    </Chrome>
  );
});

export type TextAreaFieldProps = FieldChromeProps & TextareaHTMLAttributes<HTMLTextAreaElement>;

export const TextAreaField = forwardRef<HTMLTextAreaElement, TextAreaFieldProps>(function TextAreaField(
  { label, hint, error, requiredMark, id, required, ...rest },
  ref,
) {
  const ids = useFieldIds(id);
  return (
    <Chrome label={label} hint={hint} error={error} requiredMark={requiredMark} required={required} ids={ids}>
      <TextArea
        ref={ref}
        id={ids.fieldId}
        required={required}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(hint, error, ids)}
        {...rest}
      />
    </Chrome>
  );
});

const CheckRow = styled.div`
  display: flex;
  align-items: flex-start;
  gap: ${({ theme }) => theme.space[2]};
  input {
    inline-size: 1.125rem;
    block-size: 1.125rem;
    margin-block-start: 0.2rem;
    accent-color: ${({ theme }) => theme.colors.primary};
  }
  label {
    font-size: ${({ theme }) => theme.fontSizes.md};
  }
`;

export type CheckboxProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { label: ReactNode; hint?: ReactNode };

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(function Checkbox({ label, hint, id, ...rest }, ref) {
  const ids = useFieldIds(id);
  return (
    <CheckRow>
      <input ref={ref} type="checkbox" id={ids.fieldId} aria-describedby={hint ? ids.hintId : undefined} {...rest} />
      <div>
        <label htmlFor={ids.fieldId}>{label}</label>
        {hint && <Hint id={ids.hintId}>{hint}</Hint>}
      </div>
    </CheckRow>
  );
});

export const Fieldset = styled.fieldset`
  border: 1px solid ${({ theme }) => theme.colors.border};
  border-radius: ${({ theme }) => theme.radii.md};
  padding: ${({ theme }) => theme.space[4]};
  margin: 0;
  min-inline-size: 0;
  legend {
    padding-inline: ${({ theme }) => theme.space[2]};
    font-weight: ${({ theme }) => theme.fontWeights.semibold};
  }
`;

/** Responsive two-column form grid built with logical properties. */
export const FormGrid = styled.div`
  display: grid;
  gap: ${({ theme }) => theme.space[4]};
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
`;
