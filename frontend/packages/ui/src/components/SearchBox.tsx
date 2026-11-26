'use client';

import styled from '@emotion/styled';
import { useEffect, useId, useRef, useState } from 'react';

import { Input } from './Field';
import { IconSearch } from './icons';

const Form = styled.form`
  position: relative;
  flex: 1 1 18rem;
  max-inline-size: 28rem;
  & > svg {
    position: absolute;
    inset-inline-start: 0.875rem;
    inset-block-start: 50%;
    transform: translateY(-50%);
    color: var(--eemp-color-fg-muted);
    pointer-events: none;
  }
  input {
    padding-inline-start: 2.625rem;
    border-radius: 9999px;
  }
`;

export interface SearchBoxProps {
  label: string;
  placeholder?: string;
  defaultValue?: string;
  /** Called after typing pauses (debounced) and on submit. */
  onSearch: (value: string) => void;
  debounceMs?: number;
}

export function SearchBox({ label, placeholder, defaultValue = '', onSearch, debounceMs = 350 }: SearchBoxProps) {
  const id = useId();
  const [value, setValue] = useState(defaultValue);
  const callback = useRef(onSearch);
  callback.current = onSearch;
  const first = useRef(true);

  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }
    const handle = setTimeout(() => callback.current(value.trim()), debounceMs);
    return () => clearTimeout(handle);
  }, [value, debounceMs]);

  return (
    <Form
      role="search"
      onSubmit={(e) => {
        e.preventDefault();
        callback.current(value.trim());
      }}
    >
      <label htmlFor={id} className="eemp-visually-hidden">
        {label}
      </label>
      <IconSearch size={18} />
      <Input id={id} type="search" value={value} placeholder={placeholder} onChange={(e) => setValue(e.target.value)} />
    </Form>
  );
}
