const query = new URLSearchParams(window.location.search);
const service = query.get('service') || 'השירות';
const reason = query.get('reason') || 'הגישה נחסמה לפי לוח הזמנים.';
const message = document.getElementById('message');
if (message) {
  message.textContent = `${service}: ${reason}`;
}
