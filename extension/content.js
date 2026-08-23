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
  'www.google.com': 'search',
  'youtube.com': 'youtube',
  'www.youtube.com': 'youtube',
  'm.youtube.com': 'youtube',
  'music.youtube.com': 'youtube'
};

const MIN_INTERVAL_MS = 5000;

let lastKey = '';
let lastCheck = 0;
let timer;

function currentService() {
  return SERVICE_BY_HOST[location.hostname] || 'google';
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

function evaluate() {
  const now = Date.now();
  const key = `${location.hostname}|${location.pathname}`;

  // Throttle hard. The old MutationObserver fired constantly on Gmail and YouTube.
  if (key === lastKey && now - lastCheck < MIN_INTERVAL_MS) {
    return;
  }

  lastKey = key;
  lastCheck = now;

  chrome.runtime.sendMessage(
    { type: 'evaluateAccount', service: currentService(), origin: currentOrigin() },
    (response) => {
      if (chrome.runtime.lastError) {
        // Extension context invalidated or worker restarting. Do NOT block on this.
        return;
      }

      if (response && response.blocked) {
        showBlocked(response.reason);
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
