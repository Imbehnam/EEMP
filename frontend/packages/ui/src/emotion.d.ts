import '@emotion/react';

import type { Tokens } from './tokens';

declare module '@emotion/react' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  export interface Theme extends Tokens {}
}
