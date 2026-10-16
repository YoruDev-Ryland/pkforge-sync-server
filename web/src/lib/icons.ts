// The UI icons (inline SVG, colored by currentColor and the theme tokens).
export const ICON = {
  info: `<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="6.4" stroke="currentColor" stroke-width="1.4"/><path d="M8 7.2v3.8" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/><circle cx="8" cy="4.9" r=".95" fill="currentColor"/></svg>`,
  ball: `<svg viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="10" fill="var(--danger)"/><path d="M2 12h20v0a10 10 0 0 1-20 0z" fill="var(--surface)"/><circle cx="12" cy="12" r="10" stroke="var(--text)" stroke-width="2"/><path d="M2 12h7m6 0h7" stroke="var(--text)" stroke-width="2"/><circle cx="12" cy="12" r="3" fill="var(--surface)" stroke="var(--text)" stroke-width="2"/></svg>`,
  down: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M8 2v8m-3.5-3.5L8 10l3.5-3.5M3 13.5h10"/></svg>`,
  pen: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M11 2.5l2.5 2.5L6 12.5H3.5V10z"/></svg>`,
  left: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M10 3L5 8l5 5"/></svg>`,
  right: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M6 3l5 5-5 5"/></svg>`,
  check: `<svg viewBox="0 0 12 12" fill="none" stroke="var(--accent-ink)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2.5 6.2l2.3 2.3 4.7-5"/></svg>`,
  phone: `<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.6"><rect x="2" y="5" width="16" height="10" rx="3"/><circle cx="6" cy="10" r="1.3"/><path d="M13 9h2m-1-1v2"/></svg>`,
  plus: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M8 3.5v9M3.5 8h9"/></svg>`,
  x: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M4 4l8 8M12 4l-8 8"/></svg>`,
  sun: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><circle cx="8" cy="8" r="3"/><path d="M8 1.5v1.5M8 13v1.5M1.5 8H3M13 8h1.5M3.4 3.4l1 1M11.6 11.6l1 1M3.4 12.6l1-1M11.6 4.4l1-1"/></svg>`,
  moon: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"><path d="M13.5 9.5A5.5 5.5 0 0 1 6.5 2.5a5.5 5.5 0 1 0 7 7z"/></svg>`,
  auto: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6"><circle cx="8" cy="8" r="6"/><path d="M8 2a6 6 0 0 1 0 12z" fill="currentColor"/></svg>`,
  undo: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M5.5 3.5 2.5 6.5l3 3"/><path d="M2.5 6.5h7a3.5 3.5 0 0 1 0 7H7"/></svg>`,
  web: `<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.6"><rect x="2.5" y="3.5" width="15" height="11" rx="2"/><path d="M7 17.5h6"/></svg>`,
} as const;

/** Move category marks (physical, special, status), with their labels. */
export const CATEGORY: Record<string, [svg: string, label: string]> = {
  physical: [`<svg viewBox="0 0 16 12"><rect width="16" height="12" rx="3" fill="#d9583e"/><path d="M8 2l1.3 2.6 2.7-.9-1 2.6 2.4 1.5-2.8.4.3 2.8L8 9.3 5.1 11l.3-2.8L2.6 7.8 5 6.3 4 3.7l2.7.9z" fill="#ffd36a"/></svg>`, 'Physical'],
  special: [`<svg viewBox="0 0 16 12"><rect width="16" height="12" rx="3" fill="#4a68c8"/><circle cx="8" cy="6" r="3.6" fill="none" stroke="#fff" stroke-width="1.3"/><circle cx="8" cy="6" r="1.4" fill="#fff"/></svg>`, 'Special'],
  status: [`<svg viewBox="0 0 16 12"><rect width="16" height="12" rx="3" fill="#9a9a9a"/><circle cx="8" cy="6" r="3.4" fill="none" stroke="#fff" stroke-width="1.3"/><path d="M8 2.6v6.8" stroke="#fff" stroke-width="1.3"/></svg>`, 'Status'],
};

/** A friendship heart, filled or outlined. */
export const heart = (full: boolean) => `<svg viewBox="0 0 16 16"><path d="M8 14.2 2.1 8.4a3.4 3.4 0 0 1 5.9-4.6 3.4 3.4 0 0 1 5.9 4.6z" fill="${full ? '#e0567a' : 'none'}" stroke="${full ? '#e0567a' : 'var(--line)'}" stroke-width="1.4"/></svg>`;
