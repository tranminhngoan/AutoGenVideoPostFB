import { useEffect, useState } from 'react';

export type ThemePref = 'light' | 'dark' | 'system';
const KEY = 'studio.theme';

function read(): ThemePref {
  try {
    return (localStorage.getItem(KEY) as ThemePref) || 'system';
  } catch {
    return 'system';
  }
}

function apply(pref: ThemePref) {
  const dark =
    pref === 'dark' || (pref === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
}

export function useTheme() {
  const [pref, setPref] = useState<ThemePref>(read);

  useEffect(() => {
    apply(pref);
    try {
      localStorage.setItem(KEY, pref);
    } catch {
      /* ignore */
    }
    if (pref !== 'system') return;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => apply('system');
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, [pref]);

  return [pref, setPref] as const;
}
