import { useEffect, useState } from 'react';

export interface Route {
  /** Path segments, e.g. ['videos', '<id>'] */
  segments: string[];
  query: URLSearchParams;
  path: string;
}

function parse(hash: string): Route {
  const raw = hash.replace(/^#/, '') || '/';
  const [pathPart, queryPart = ''] = raw.split('?');
  const segments = pathPart.split('/').filter(Boolean).map(decodeURIComponent);
  return { segments, query: new URLSearchParams(queryPart), path: '/' + segments.join('/') };
}

export function useRoute(): Route {
  const [route, setRoute] = useState(() => parse(window.location.hash));
  useEffect(() => {
    const onChange = () => {
      setRoute(parse(window.location.hash));
      window.scrollTo({ top: 0 });
    };
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);
  return route;
}

export function navigate(path: string) {
  const target = '#' + (path.startsWith('/') ? path : '/' + path);
  if (window.location.hash !== target) window.location.hash = target;
}

export function href(path: string) {
  return '#' + (path.startsWith('/') ? path : '/' + path);
}
