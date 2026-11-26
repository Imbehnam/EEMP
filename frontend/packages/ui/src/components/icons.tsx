import type { ReactNode, SVGProps } from 'react';

/** Small decorative line icons (24px grid, currentColor). Always aria-hidden: meaning is carried by text. */
type IconProps = Omit<SVGProps<SVGSVGElement>, 'children'> & { size?: number };

function Svg({ size = 20, children, ...rest }: IconProps & { children: ReactNode }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...rest}
    >
      {children}
    </svg>
  );
}

export const IconInfo = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M12 11v5M12 8h.01" />
  </Svg>
);
export const IconSuccess = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="m8 12.5 2.5 2.5L16 9.5" />
  </Svg>
);
export const IconWarning = (p: IconProps) => (
  <Svg {...p}>
    <path d="M10.3 4.2 2.6 17.5A2 2 0 0 0 4.3 20.5h15.4a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0Z" />
    <path d="M12 9.5v4M12 17h.01" />
  </Svg>
);
export const IconDanger = (p: IconProps) => (
  <Svg {...p}>
    <path d="M8.5 3h7L21 8.5v7L15.5 21h-7L3 15.5v-7Z" />
    <path d="M12 8v5M12 16h.01" />
  </Svg>
);
export const IconEmpty = (p: IconProps) => (
  <Svg {...p}>
    <path d="M3 13h5l1.5 2.5h5L16 13h5" />
    <path d="M5.5 5h13L21 13v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-5Z" />
  </Svg>
);
export const IconLock = (p: IconProps) => (
  <Svg {...p}>
    <rect x="4.5" y="10.5" width="15" height="10" rx="2" />
    <path d="M8 10.5V7.5a4 4 0 0 1 8 0v3" />
  </Svg>
);
export const IconMenu = (p: IconProps) => (
  <Svg {...p}>
    <path d="M4 6h16M4 12h16M4 18h16" />
  </Svg>
);
export const IconClose = (p: IconProps) => (
  <Svg {...p}>
    <path d="M6 6l12 12M18 6 6 18" />
  </Svg>
);
export const IconSearch = (p: IconProps) => (
  <Svg {...p}>
    <circle cx="11" cy="11" r="6.5" />
    <path d="m20 20-4.2-4.2" />
  </Svg>
);
const RIGHT = 'm9 6 6 6-6 6';
const LEFT = 'm15 6-6 6 6 6';

/**
 * Points to the inline end (right in LTR, left in RTL), or the start with towards="start".
 * Both directions are drawn and styles.css shows the one matching the document direction — no CSS
 * transform, which proved unreliable on flex-item SVGs in Chromium's compositor.
 */
export const IconChevron = ({ towards = 'end', ...p }: IconProps & { towards?: 'end' | 'start' }) => (
  <Svg {...p}>
    <path className="eemp-ltr-only" d={towards === 'end' ? RIGHT : LEFT} />
    <path className="eemp-rtl-only" d={towards === 'end' ? LEFT : RIGHT} />
  </Svg>
);

export const toneIcons = {
  neutral: IconInfo,
  info: IconInfo,
  success: IconSuccess,
  warning: IconWarning,
  danger: IconDanger,
} as const;
