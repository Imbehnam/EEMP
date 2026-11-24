import { PERSON_KINDS, type PersonKind } from '@eemp/api-client';
import { notFound } from 'next/navigation';

export function parseKind(value: string): PersonKind {
  if (!(PERSON_KINDS as readonly string[]).includes(value)) notFound();
  return value as PersonKind;
}
