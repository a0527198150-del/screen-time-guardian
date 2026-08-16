const NATIVE_HOST = 'com.screentimeguardian.host';

function callNative(message) {
  return new Promise((resolve) => {
    chrome.runtime.sendNativeMessage(NATIVE_HOST, message, (response) => {
      if (chrome.runtime.lastError) {
        resolve({
          ok: false,
          error: chrome.runtime.lastError.message || 'Native host unavailable'
        });
        return;
      }

      resolve(response || { ok: false, error: 'Empty native response' });
    });
  });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || message.type !== 'evaluateAccount') {
    return false;
  }

  const service = typeof message.service === 'string' ? message.service : '';
  const email = typeof message.email === 'string' ? message.email : '';

  callNative({
    type: 'accountDecision',
    account: { email, service },
    service
  }).then((response) => {
    if (!response || response.ok === false) {
      sendResponse({
        blocked: true,
        identityKnown: false,
        reason: 'לא ניתן להתחבר לשירות המדיניות.'
      });
      return;
    }

    sendResponse(response);
  });

  return true;
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || message.type !== 'getPolicy') {
    return false;
  }

  callNative({ type: 'getPolicy' }).then(sendResponse);
  return true;
});

chrome.runtime.onInstalled.addListener(() => {
  callNative({ type: 'heartbeat' }).catch(() => undefined);
});
