const SERVICE_BY_HOST = {
  'mail.google.com': 'gmail',
  'chat.google.com': 'chat',
  'calendar.google.com': 'calendar',
  'drive.google.com': 'drive',
  'docs.google.com': 'docs',
  'meet.google.com': 'meet',
  'youtube.com': 'youtube',
  'www.youtube.com': 'youtube'
};

let lastState = '';
let evaluationTimer;

function currentService() {
  return SERVICE_BY_HOST[window.location.hostname] || 'google';
}

function extractEmail() {
  const candidates = [
    ...document.querySelectorAll('[aria-label^="Google Account:"]'),
    ...document.querySelectorAll('[data-email]'),
    ...document.querySelectorAll('[data-email-address]')
  ];

  for (const element of candidates) {
    const text = [
      element.getAttribute('aria-label') || '',
      element.getAttribute('data-email') || '',
      element.getAttribute('data-email-address') || '',
      element.getAttribute('title') || ''
    ].join(' ');
    const match = text.match(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i);
    if (match) {
      return match[0].toLowerCase();
    }
  }

  return '';
}

function showBlocked(response) {
  const params = new URLSearchParams({
    service: currentService(),
    reason: response?.reason || 'הגישה נחסמה לפי לוח הזמנים.'
  });
  window.location.replace(`${chrome.runtime.getURL('blocked.html')}?${params}`);
}

function evaluate() {
  const service = currentService();
  const email = extractEmail();
  const state = `${service}|${email}|${window.location.pathname}`;
  if (state === lastState) {
    return;
  }
  lastState = state;

  chrome.runtime.sendMessage(
    { type: 'evaluateAccount', service, email },
    (response) => {
      if (chrome.runtime.lastError) {
        showBlocked({ reason: 'לא ניתן לאמת את מצב החסימה.' });
        return;
      }

      if (response?.blocked) {
        showBlocked(response);
      }
    }
  );
}

function scheduleEvaluation() {
  window.clearTimeout(evaluationTimer);
  evaluationTimer = window.setTimeout(evaluate, 250);
}

evaluate();
new MutationObserver(scheduleEvaluation).observe(document.documentElement, {
  subtree: true,
  childList: true,
  attributes: true,
  attributeFilter: ['aria-label', 'data-email', 'data-email-address']
});
window.addEventListener('popstate', scheduleEvaluation);
window.addEventListener('hashchange', scheduleEvaluation);
