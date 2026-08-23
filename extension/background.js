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

let accountCache = { at: 0, emails: [], primary: '', available: false };
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
  let available = false;
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
      available = true;
    }
  } catch (error) {
    console.debug('ListAccounts failed', error);
  }

  accountCache = { at: now, emails, primary: emails[0] || '', available };
  return accountCache;
}

async function getPolicy(force = false) {
  const now = Date.now();
  if (!force && policyCache.policy && now - policyCache.at < POLICY_CACHE_MS) {
    return policyCache.policy;
  }

  const response = await callNative({ type: 'getPolicy' });
  if (response && response.ok && response.policy) {
    policyCache = { at: now, policy: response.policy };
  }

  // Keep the last valid snapshot when the service is temporarily unavailable.
  // Clearing it here would also clear declarativeNetRequest rules on the next sync.
  return policyCache.policy;
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
  youtube: ['www.youtube.com', 'm.youtube.com', 'music.youtube.com', 'youtube.com'],
  gemini: ['gemini.google.com'],
  maps: ['maps.google.com', 'www.google.com/maps'],
  translate: ['translate.google.com'],
  keep: ['keep.google.com'],
  news: ['news.google.com'],
  finance: ['www.google.com/finance'],
  groups: ['groups.google.com'],
  one: ['one.google.com']
};

function blockedUrlFor(reason) {
  return chrome.runtime.getURL('blocked.html') + '?reason=' + encodeURIComponent(reason);
}

function urlFilterForHost(host) {
  return host.includes('/') ? `||${host}` : `||${host}/`;
}

function normalizeOrigin(value) {
  try {
    const uri = new URL(String(value || '').trim());
    if (uri.protocol !== 'https:' || uri.username || uri.password || uri.port || uri.search || uri.hash || !uri.hostname) {
      return '';
    }
    return `https://${uri.hostname.toLowerCase()}`;
  } catch (error) {
    return '';
  }
}

function evaluateCachedPolicy(policy, email, service, origin) {
  if (!policy) {
    return { blocked: false, identityKnown: false, reason: 'שירות המדיניות אינו זמין.' };
  }

  const normalizedEmail = String(email || '').trim().toLowerCase();
  const normalizedService = String(service || '').trim().toLowerCase();
  const normalizedOrigin = normalizeOrigin(origin);
  const account = Array.isArray(policy.googleAccounts)
    ? policy.googleAccounts.find(item => String(item.email || '').toLowerCase() === normalizedEmail)
    : null;

  if (!normalizedEmail) {
    return {
      blocked: policy.blockUnknownGoogleSessions === true,
      identityKnown: false,
      reason: 'לא ניתן לזהות את חשבון Google בזמן שהשירות אינו זמין.'
    };
  }

  if (!account) {
    return { blocked: false, identityKnown: true, reason: 'החשבון אינו חסום כרגע.' };
  }

  const siteBlocked = normalizedOrigin && Array.isArray(account.sites)
    && account.sites.some(site => normalizeOrigin(site) === normalizedOrigin);
  const serviceBlocked = Array.isArray(account.services)
    && account.services.some(item => String(item).toLowerCase() === normalizedService);
  const blocked = Boolean(siteBlocked || serviceBlocked);

  return {
    blocked,
    identityKnown: true,
    reason: blocked
      ? 'הגישה נחסמה לפי המדיניות האחרונה הידועה.'
      : 'החשבון אינו חסום כרגע.'
  };
}

function isGoogleHostname(hostname) {
  const host = String(hostname || '').toLowerCase().replace(/\.$/, '');
  return host === 'google.com' || host.endsWith('.google.com');
}

/**
 * Rebuilds the dynamic block rules from the policy plus the accounts signed in RIGHT NOW.
 * If you are not signed in with a restricted account, no rules exist and nothing is blocked -
 * which is exactly how a brother using his own account is left alone.
 */
async function syncRules() {
  const policy = await getPolicy();
  if (!policy) {
    // No valid snapshot has ever been received. Preserve any existing rules rather
    // than replacing them with an empty set during a service outage.
    console.warn('Policy unavailable; keeping existing declarative rules');
    return;
  }

  const accounts = await readSignedInAccounts();
  if (!accounts.available) {
    // An empty result caused by a failed Google request is not evidence that the
    // browser has no accounts. Keep the last rules until identity can be read again.
    console.warn('Google account list unavailable; keeping existing declarative rules');
    return;
  }

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
          condition: { urlFilter: urlFilterForHost(host), resourceTypes: ['main_frame', 'sub_frame'] }
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
          condition: { urlFilter: urlFilterForHost(host), resourceTypes: ['main_frame', 'sub_frame'] }
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
    if (target.protocol === 'https:' && !isGoogleHostname(target.hostname)) {
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
      const cachedDecision = evaluateCachedPolicy(
        policyCache.policy,
        email,
        message.service || '',
        message.origin || ''
      );
      sendResponse(cachedDecision);
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
