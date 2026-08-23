const params = new URLSearchParams(location.search);
const reason = params.get('reason');
if (reason) {
  document.getElementById('reason').textContent = reason;
}

function tick() {
  const now = new Date();
  document.getElementById('clock').textContent =
    now.toLocaleString('he-IL', { weekday: 'long', hour: '2-digit', minute: '2-digit' });
}

tick();
setInterval(tick, 30_000);
