import type { SVGProps } from 'react';

type Name = 'grid' | 'users' | 'clipboard' | 'calendar' | 'menu' | 'close' |
  'plus' | 'arrow' | 'search' | 'check' | 'clock' | 'target' | 'logout' |
  'refresh' | 'chevron' | 'phone' | 'mail' | 'filter' | 'briefcase' |
  'edit' | 'warning' | 'info' | 'download' | 'lock' |
  'checklist' | 'file-text' | 'receipt' | 'chart' | 'building' |
  'shield' | 'layers' | 'copy' | 'route' | 'history';

const paths: Record<Name, string> = {
  grid: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
  users: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75',
  clipboard: 'M9 4h6 M9 2h6v4H9z M7 4H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2h-2 M8 12h8 M8 16h6',
  calendar: 'M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M8 15h3',
  menu: 'M4 7h16 M4 12h16 M4 17h16',
  close: 'M18 6 6 18 M6 6l12 12',
  plus: 'M12 5v14 M5 12h14',
  arrow: 'M5 12h14 M13 6l6 6-6 6',
  search: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16 M21 21l-4.35-4.35',
  check: 'M20 6 9 17l-5-5',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20 M12 6v6l4 2',
  target: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20 M12 18a6 6 0 1 0 0-12 6 6 0 0 0 0 12 M12 14a2 2 0 1 0 0-4 2 2 0 0 0 0 4',
  logout: 'M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4 M16 17l5-5-5-5 M21 12H9',
  refresh: 'M20 11a8 8 0 0 0-14-5L3 9 M3 3v6h6 M4 13a8 8 0 0 0 14 5l3-3 M21 21v-6h-6',
  chevron: 'm9 18 6-6-6-6',
  phone: 'M22 16.92v3a2 2 0 0 1-2.18 2A19.79 19.79 0 0 1 2.09 4.18 2 2 0 0 1 4.08 2h3a2 2 0 0 1 2 1.72c.12.96.34 1.9.66 2.81a2 2 0 0 1-.45 2.11L8 9.91a16 16 0 0 0 6.09 6.09l1.27-1.29a2 2 0 0 1 2.11-.45c.91.32 1.85.54 2.81.66a2 2 0 0 1 1.72 2.09z',
  mail: 'M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M22 6l-10 7L2 6',
  filter: 'M4 7h16 M7 12h10 M10 17h4',
  briefcase: 'M3 7h18v13H3z M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M3 12h18',
  edit: 'M12 20h9 M16 4l4 4-11 11-5 1 1-5z',
  warning: 'M12 3 2 21h20L12 3z M12 9v5 M12 18h.01',
  info: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20 M12 11v6 M12 7h.01',
  download: 'M12 3v12 M7 10l5 5 5-5 M5 19h14',
  lock: 'M5 11h14v10H5z M8 11V7a4 4 0 0 1 8 0v4',
  checklist: 'M3 5l2 2 3-3 M11 6h10 M3 12l2 2 3-3 M11 13h10 M3 19l2 2 3-3 M11 20h10',
  'file-text': 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M8 13h8 M8 17h8',
  receipt: 'M4 2h16v20l-4-2-4 2-4-2-4 2V2z M8 7h8 M8 11h8 M8 15h5',
  chart: 'M4 3v18h17 M8 17v-4 M13 17V9 M18 17V5',
  building: 'M3 21V5l9-3 9 3v16 M9 21v-5h6v5 M7 9h.01 M12 9h.01 M17 9h.01 M7 13h.01 M17 13h.01',
  shield: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z',
  layers: 'M12 2 2 8l10 6 10-6-10-6z M2 12l10 6 10-6 M2 16l10 6 10-6',
  copy: 'M8 8h12v12H8z M16 8V4a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h4',
  route: 'M6 19h12 M6 19a2 2 0 1 0 0-4 2 2 0 0 0 0 4 M18 7a2 2 0 1 0 0-4 2 2 0 0 0 0 4 M18 7v5a3 3 0 0 1-3 3H9a3 3 0 0 0-3 3',
  history: 'M3 12a9 9 0 1 0 3-6.7 M3 3v6h6 M12 7v5l3 2'
};

export function Icon({ name, size = 20, ...props }: SVGProps<SVGSVGElement> & { name: Name; size?: number }) {
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
    strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...props}>
    <path d={paths[name]} />
  </svg>;
}
