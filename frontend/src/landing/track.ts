import { isMobile } from './facebookLink';

/** Anonymous counters for the public landing page (visit / clicks). A click is not a follow. */

type EventType = 'view' | 'open_page' | 'open_post';

const ENDPOINT = '/api/public/landing/events';
const UTM_KEY = 'landing.utm';

function visitorId(): string {
  try {
    let id = localStorage.getItem('landing.vid');
    if (!id) {
      id = (crypto.randomUUID?.() ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`).slice(0, 64);
      localStorage.setItem('landing.vid', id);
    }
    return id;
  } catch {
    return '';
  }
}

/** UTM tags of the link the visitor came from — kept for the whole visit. */
function utm(): { utmSource?: string; utmMedium?: string; utmCampaign?: string; utmContent?: string } {
  const q = new URLSearchParams(window.location.search);
  const fromUrl = {
    utmSource: q.get('utm_source') ?? undefined,
    utmMedium: q.get('utm_medium') ?? undefined,
    utmCampaign: q.get('utm_campaign') ?? undefined,
    // which helper Page × Group the link was posted in (outreach campaign links)
    utmContent: q.get('utm_content') ?? undefined,
  };
  try {
    if (fromUrl.utmSource || fromUrl.utmMedium || fromUrl.utmCampaign || fromUrl.utmContent) {
      sessionStorage.setItem(UTM_KEY, JSON.stringify(fromUrl));
      return fromUrl;
    }
    return JSON.parse(sessionStorage.getItem(UTM_KEY) ?? '{}');
  } catch {
    return fromUrl;
  }
}

export function track(slug: string, type: EventType, target?: string) {
  const body = JSON.stringify({
    slug,
    type,
    target,
    device: isMobile() ? 'mobile' : 'desktop',
    visitorId: visitorId() || undefined,
    referrer: document.referrer || undefined,
    ...utm(),
  });
  try {
    // sendBeacon survives the page being left right after the click
    if (navigator.sendBeacon?.(ENDPOINT, new Blob([body], { type: 'application/json' }))) return;
  } catch {
    /* fall through */
  }
  fetch(ENDPOINT, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body, keepalive: true }).catch(() => {});
}
