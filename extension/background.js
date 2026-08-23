/**
 * Screen Time Guardian - background service worker.
 *
 * Three jobs:
 *   1. Identify which Google account is signed in, reliably and language independently.
 *   2. Notice every "Sign in with Google" flow and report the destination site.
 *   3. Keep declarativeNetRequest rules in sync with the policy, so blocking happens
 *      before a page loads instead of racing a content script.
 */

const NATIVE_HOST = 'com.screentimeguardian.host';
const RULE_ID_BASE = 9000;
const ACCOUNT_CACHE_MS = 45_000;
const POLICY_CACHE_MS = 30_000;

let nativePort = null;
let nativeSeq = 0;
const pending = new Map();

let accountCache = { at: 0, emails: [], primary: '' };
let policyCache = { at: 0, policy: null };

// ---------------------------------------------------------------- native host

/**
 * One long lived connection instead of sendNativeMessage.
 * sendNativeMessage spawns a brand new host process for EVERY message; on a busy
 * Gmail tab that meant dozens of process launches per second.
 */
function getPort() {
  if (nativePort) return nativePort;

  try {
    nativePort = chrome.runtime.connectNative(NATIVE_HOST);
  } catch (error) {
    console.warn('Native host unavailable', error);
    return null;
  }

  nativePort.onMessage.addListener((message) => {
    const id = message && message.requestId;
    if (id && pending.has(id)) {
      const { resolve, timer } = pending.get(id);
      clearTimeout(timer);
      pending.delete(id);
      resolve(message);
    }
  });

  nativePort.onDisconnect.addListener(() => {
    nativePort = null;
    for (const [, entry] of pending) {
      clearTimeout(entry.timer);
      entry.resolve({ ok: false, error: 'Native host disconnected' });
    }
    pending.clear();
  });

  return nativePort;
}

function callNative(message, timeoutMs = 4000) {
  return new Promise((resolve) => {
    const port = getPort();
    if (!port) {
      resolve({ ok: false, error: 'Native host unavailable' });
      return;
    }

    const requestId = `r${++nativeSeq}`;
    const timer = setTimeout(() => {
      pending.delete(requestId);
      resolve({ ok: false, error: 'Native host timeout' });
    }, timeoutMs);

    pending.set(requestId, { resolve, timer });

    try {
      port.postMessage({ ...message, requestId });
    } catch (error) {
      clearTimeout(timer);
      pending.delete(requestId);
      resolve({ ok: false, error: String(error) });
    }
  });
}

// ---------------------------------------------------------------- account identity

/**
 * Reads the signed in accounts from Google's own endpoint.
 *
 * The previous version scraped the DOM for aria-label="Google Account:", which does
 * not exist in a Hebrew interface (it reads "חשבון Google:") - so identification
 * silently failed every single time. This endpoint is language independent.
 */
async function readSignedInAccounts() {
  const now = Date.now();
  if (now - accountCache.at < ACCOUNT_CACHE_MS) {
    return accountCache;
  }

  const emails = [];
  try {
    const response = await fetch(
      'https://accounts.google.com/ListAccounts?gpsia=1&source=ChromiumBrowser&json=standard',
      { credentials: 'include', cache: 'no-store' }
    );

    if (response.ok) {
      const text = await response.text();
      const cleaned = text.replace(/^\)\]\}'/, '').trim();
      const data = JSON.parse(cleaned);
      const rows = Array.isArray(data) && Array.isArray(data[1]) ? data[1] : [];
      for (const row of rows) {
        const candidate = Array.isArray(row) ? row.find((v) => typeof v === 'string' && v.includes('@')) : null;
        if (candidate) emails.push(candidate.toLowerCase());
      }
    }
  } catch (error) {
    console.debug('ListAccounts failed', error);
  }

  accountCache = { at: now, emails, primary: emails[0] || '' };
  return accountCache;
}

async function getPolicy(force = false) {
  const now = Date.now();
  if (!force && policyCache.policy && now - policyCache.at < POLICY_CACHE_MS) {
    return policyCache.policy;
  }

  const response = await callNative({ type: 'getPolicy' });
  const policy = response && response.ok && response.policy ? response.policy : null;
  policyCache = { at: now, policy };
  return policy;
}

// ---------------------------------------------------------------- blocking rules

const SERVICE_HOSTS = {
  gmail: ['mail.google.com'],
  drive: ['drive.google.com'],
  docs: ['docs.google.com', 'sheets.google.com', 'slides.google.com'],
  calendar: ['calendar.google.com'],
  chat: ['chat.google.com'],
  meet: ['meet.google.com'],
  photos: ['photos.google.com'],
  search: ['www.google.com'],
  youtube: ['www.youtube.com', 'm.youtube.com', 'music.youtube.com', 'youtube.com']
};

function blockedUrlFor(reason) {
  return chrome.runtime.getURL('blocked.html') + '?reason=' + encodeURIComponent(reason);
}

/**
 * Rebuilds the dynamic block rules from the policy plus the accounts signed in RIGHT NOW.
 * If you are not signed in with a restricted account, no rules exist and nothing is blocked -
 * which is exactly how a brother using his own account is left alone.
 */
async function syncRules() {
  const policy = await getPolicy();
  const accounts = await readSignedInAccounts();

  const existing = await chrome.declarativeNetRequest.getDynamicRules();
  const removeRuleIds = existing.map((rule) => rule.id);
  const addRules = [];
  let nextId = RULE_ID_BASE;

  if (policy && Array.isArray(policy.googleAccounts)) {
    for (const account of policy.googleAccounts) {
      const email = (account.email || '').toLowerCase();
      if (!email || !accounts.emails.includes(email)) {
        continue;
      }

      const hosts = new Set();
      for (const service of account.services || []) {
        for (const host of SERVICE_HOSTS[String(service).toLowerCase()] || []) {
          hosts.add(host);
        }
      }

      for (const host of hosts) {
        addRules.push({
          id: nextId++,
          priority: 1,
          action: { type: 'redirect', redirect: { url: blockedUrlFor(`${host} חסום עבור ${email} לפי לוח הזמנים.`) } },
          condition: { urlFilter: `||${host}/`, resourceTypes: ['main_frame'] }
        });
      }

      for (const site of account.sites || []) {
        let host;
        try {
          host = new URL(site).hostname;
        } catch (error) {
          continue;
        }

        addRules.push({
          id: nextId++,
          priority: 1,
          action: { type: 'redirect', redirect: { url: blockedUrlFor(`${host} חסום עבור ${email} לפי לוח הזמנים.`) } },
          condition: { urlFilter: `||${host}/`, resourceTypes: ['main_frame'] }
        });
      }
    }
  }

  try {
    await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds, addRules });
  } catch (error) {
    console.warn('Could not update dynamic rules', error);
  }
}

// ---------------------------------------------------------------- sign-in discovery

const OAUTH_PATTERNS = [
  '/o/oauth2/auth',
  '/o/oauth2/v2/auth',
  '/signin/oauth',
  '/gsi/select',
  '/gsi/button',
  '/gsi/iframe'
];

/**
 * Every "Sign in with Google" flow passes through accounts.google.com and carries
 * the destination in redirect_uri (or client_id). That is how we learn which
 * third party sites you use your Google account on.
 */
async function inspectGoogleAuth(url) {
  let parsed;
  try {
    parsed = new URL(url);
  } catch (error) {
    return;
  }

  if (parsed.hostname !== 'accounts.google.com') return;
  if (!OAUTH_PATTERNS.some((pattern) => parsed.pathname.startsWith(pattern))) return;

  const params = parsed.searchParams;
  const redirectUri = params.get('redirect_uri') || params.get('origin') || params.get('continue') || '';

  let origin = '';
  try {
    const target = new URL(redirectUri);
    if (target.protocol === 'https:' && !target.hostname.endsWith('google.com')) {
      origin = `${target.protocol}//${target.hostname}`;
    }
  } catch (error) {
    return;
  }

  if (!origin) return;

  const hint = (params.get('login_hint') || '').toLowerCase();
  const accounts = await readSignedInAccounts();
  const email = hint.includes('@') ? hint : accounts.primary;
  if (!email) return;

  await callNative({ type: 'reportDiscovery', origin, email });
}

chrome.webNavigation.onBeforeNavigate.addListener(
  (details) => {
    if (details.frameId !== 0 && details.parentFrameId !== 0) return;
    inspectGoogleAuth(details.url);
  },
  { url: [{ hostEquals: 'accounts.google.com' }] }
);

chrome.webNavigation.onCommitted.addListener(
  () => {
    accountCache.at = 0;
    syncRules();
  },
  { url: [{ hostEquals: 'accounts.google.com' }] }
);

// ---------------------------------------------------------------- messaging + schedule

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || message.type !== 'evaluateAccount') {
    return false;
  }

  (async () => {
    const accounts = await readSignedInAccounts();
    const email = message.email || accounts.primary || '';
    const response = await callNative({
      type: 'accountDecision',
      account: { email, service: message.service || '', origin: message.origin || '' }
    });

    if (!response || response.ok === false) {
      // Fail OPEN when the service simply is not reachable. The previous version
      // failed closed here, which blocked normal browsing whenever the host was down.
      sendResponse({ blocked: false, identityKnown: false, reason: 'שירות המדיניות אינו זמין.' });
      return;
    }

    sendResponse(response);
  })();

  return true;
});

chrome.alarms.create('stg-sync', { periodInMinutes: 1 });
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === 'stg-sync') {
    syncRules();
  }
});

chrome.runtime.onInstalled.addListener(() => syncRules());
chrome.runtime.onStartup.addListener(() => syncRules());
syncRules();
