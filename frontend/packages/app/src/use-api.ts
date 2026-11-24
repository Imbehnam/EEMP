'use client';

import type { Endpoints } from '@eemp/api-client';
import { useCallback, useEffect, useRef, useState } from 'react';

import { useSession } from './session';

export interface QueryState<T> {
  data: T | undefined;
  error: unknown;
  loading: boolean;
  /** Re-run the loader (e.g. after a mutation or a concurrency conflict). */
  reload: () => void;
  setData: (data: T) => void;
}

/**
 * Loads data through the BFF. `key` identifies the inputs (e.g. `${page}|${search}`): the loader
 * re-runs whenever it changes. Stale responses from superseded keys are ignored.
 */
export function useApiQuery<T>(key: string, load: (api: Endpoints) => Promise<T>): QueryState<T> {
  const { api } = useSession();
  const loader = useRef(load);
  loader.current = load;
  const [tick, setTick] = useState(0);
  const [state, setState] = useState<{ data: T | undefined; error: unknown; loading: boolean }>({
    data: undefined,
    error: undefined,
    loading: true,
  });

  useEffect(() => {
    let active = true;
    setState((s) => ({ ...s, loading: true, error: undefined }));
    loader
      .current(api)
      .then((data) => active && setState({ data, error: undefined, loading: false }))
      .catch((error: unknown) => active && setState((s) => ({ ...s, error, loading: false })));
    return () => {
      active = false;
    };
  }, [api, key, tick]);

  const reload = useCallback(() => setTick((t) => t + 1), []);
  const setData = useCallback((data: T) => setState({ data, error: undefined, loading: false }), []);
  return { ...state, reload, setData };
}

export type MutationResult<R> = { ok: true; value: R } | { ok: false; error: unknown };

export interface MutationState<A extends unknown[], R> {
  run: (...args: A) => Promise<MutationResult<R>>;
  busy: boolean;
  error: unknown;
  reset: () => void;
}

/** Wraps a mutation; errors are captured (not thrown) so forms can render translated problems. */
export function useApiMutation<A extends unknown[], R>(fn: (api: Endpoints, ...args: A) => Promise<R>): MutationState<A, R> {
  const { api } = useSession();
  const ref = useRef(fn);
  ref.current = fn;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(undefined);
  const run = useCallback(
    async (...args: A): Promise<MutationResult<R>> => {
      setBusy(true);
      setError(undefined);
      try {
        return { ok: true, value: await ref.current(api, ...args) };
      } catch (e) {
        setError(e);
        return { ok: false, error: e };
      } finally {
        setBusy(false);
      }
    },
    [api],
  );
  const reset = useCallback(() => setError(undefined), []);
  return { run, busy, error, reset };
}
