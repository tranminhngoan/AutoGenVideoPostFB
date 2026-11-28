/**
 * Opens a Facebook URL, preferring the Facebook app on phones.
 * - Android: an intent URL — opens the app; Chrome itself falls back to the web page when the app is missing.
 * - iOS: plain https facebook.com link — iOS hands it to the app (universal link) when installed, else Safari.
 * - Inside Facebook / Messenger / Instagram's own browser: stay there (already in the app).
 * - Desktop: new tab.
 * Never follows / likes on the visitor's behalf — it only opens the page.
 */
export function openFacebook(webUrl: string) {
  const ua = navigator.userAgent;
  const inAppBrowser = /FBAN|FBAV|FB_IAB|FBIOS|Messenger|Instagram/i.test(ua);
  const android = /Android/i.test(ua);
  const ios = /iPhone|iPad|iPod/i.test(ua) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

  if (inAppBrowser || ios) {
    window.location.href = webUrl;
    return;
  }
  if (android) {
    const fallback = encodeURIComponent(webUrl);
    window.location.href =
      `intent://facewebmodal/f?href=${fallback}#Intent;scheme=fb;package=com.facebook.katana;` +
      `S.browser_fallback_url=${fallback};end`;
    return;
  }
  const win = window.open(webUrl, '_blank', 'noopener,noreferrer');
  if (!win) window.location.href = webUrl;
}

export function isMobile() {
  return /Mobi|Android|iPhone|iPad|iPod/i.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
}
