/**
 * Screen Time Guardian - content script.
 *
 * Second line of defence only. The real blocking is done by declarativeNetRequest
 * in the background worker, which acts before the page loads and cannot be raced.
 * This script catches in-page SPA navigation, where no network request is made.
 */

const SERVICE_BY_HOST = {
  'mail.google.com': 'gmail',
  'chat.google.com': 'chat',
  'calendar.google.com': 'calendar',
  'drive.google.com': 'drive',
  'docs.google.com': 'docs',
  'sheets.google.com': 'docs',
  'slides.google.com': 'docs',
  'meet.google.com': 'meet',
  'photos.google.com': 'photos',
  'gemini.google.com': 'gemini',
  'maps.google.com': 'maps',
  'translate.google.com': 'translate',
  'keep.google.com': 'keep',
  'news.google.com': 'news',
  'groups.google.com': 'groups',
  'one.google.com': 'one',
  'www.google.com': 'search',
  'youtube.com': 'youtube',
  'www.youtube.com': 'youtube',
  'm.youtube.com': 'youtube',
  'music.youtube.com': 'youtube'
};

const MIN_INTERVAL_MS = 5000;
const EMAIL_RE = /[\w.+-]+@[\w-]+(\.[\w-]+)+/;

let lastKey = '';
let lastCheck = 0;
let timer;

/**
 * Reads the ACTIVE account in this tab from the page itself.
 *
 * ListAccounts (in the background worker) returns every account signed into the
 * browser profile, but the active tab may be using a different one - e.g. a
 * restricted sibling account while the parent's account is the profile primary.
 * The DOM carries the active address in several language-independent places:
 * data-email attributes, account-switcher labels, avatar alt text and the
 * YouTube account picker.
 */
function extractAccountEmail() {
  const found = [];
  const remember = (value) => {
    if (!value) return;
    const match = String(value).match(EMAIL_RE);
    if (match) found.push(match[0].toLowerCase());
  };

  // 1. Explicit data-email attributes (Gmail, Drive and many Google apps).
  document.querySelectorAll('[data-email]').forEach((el) => {
    remember(el.getAttribute('data-email'));
  });

  // 2. Account-switcher links / buttons whose label contains the address.
  document.querySelectorAll('a[aria-label*="@"], button[aria-label*="@"], [title*="@"]').forEach((el) => {
    remember((el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('title') || ''));
  });

  // 3. Avatar images with the address in the alt text (Gmail, Google search).
  document.querySelectorAll('img[alt*="@"]').forEach((el) => {
    remember(el.getAttribute('alt'));
  });

  // 4. YouTube account picker and the active account header.
  document.querySelectorAll(
    'ytd-account-item-renderer, ytd-active-account-header-renderer, #account-picker, yt-account-item'
  ).forEach((el) => {
    remember(el.textContent);
  });

  return found.find((email) => EMAIL_RE.test(email)) || '';
}

function currentService() {
  const host = location.hostname;
  const path = location.pathname.toLowerCase();
  if (host === 'www.google.com' || host === 'google.com') {
    if (path.startsWith('/maps')) return 'maps';
    if (path.startsWith('/finance')) return 'finance';
  }
  return SERVICE_BY_HOST[host] || 'google';
}

function currentOrigin() {
  return `${location.protocol}//${location.hostname}`;
}

function showBlocked(reason) {
  const params = new URLSearchParams({
    service: currentService(),
    reason: reason || 'הגישה נחסמה לפי לוח הזמנים.'
  });
  location.replace(`${chrome.runtime.getURL('blocked.html')}?${params}`);
}

function evaluate(force) {
  const now = Date.now();
  const key = `${location.hostname}|${location.pathname}`;

  // Throttle hard. The old MutationObserver fired constantly on Gmail and YouTube.
  // A forced re-check (account chip not rendered yet) bypasses the throttle.
  if (!force && key === lastKey && now - lastCheck < MIN_INTERVAL_MS) {
    return;
  }

  lastKey = key;
  lastCheck = now;

  const email = extractAccountEmail();
  let retried = 0;

  chrome.runtime.sendMessage(
    { type: 'evaluateAccount', email, service: currentService(), origin: currentOrigin() },
    (response) => {
      if (chrome.runtime.lastError) {
        // Extension context invalidated or worker restarting. Do NOT block on this.
        return;
      }

      if (response && response.blocked) {
        showBlocked(response.reason);
        return;
      }

      // The account chip usually renders after the first paint. Poll briefly so a
      // detection that ran too early does not leave the tab unguarded.
      if (!email && !response.identityKnown && retried < 3) {
        retried++;
        setTimeout(() => evaluate(true), 1200 * retried);
      }
    }
  );
}

function schedule() {
  clearTimeout(timer);
  timer = setTimeout(evaluate, 600);
}

// Wait for the document to actually exist before doing anything, so a
// document_start injection does not evaluate against an empty page.
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', evaluate, { once: true });
} else {
  evaluate();
}

// SPA navigation only. No DOM mutation observer: URL changes are what matter.
const pushState = history.pushState;
history.pushState = function (...args) {
  pushState.apply(this, args);
  schedule();
};

const replaceState = history.replaceState;
history.replaceState = function (...args) {
  replaceState.apply(this, args);
  schedule();
};

window.addEventListener('popstate', schedule);
window.addEventListener('hashchange', schedule);
