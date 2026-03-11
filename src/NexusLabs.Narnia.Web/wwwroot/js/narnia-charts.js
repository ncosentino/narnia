// Narnia chart initializer — reads <script type="application/json" data-chart-id="...">
// blocks rendered by Blazor Static SSR pages and creates Chart.js instances.

function narniaCopyText(elementId, btn) {
    var el = document.getElementById(elementId);
    if (!el) return;
    navigator.clipboard.writeText(el.textContent.trim()).then(function () {
        btn.textContent = 'Copied!';
        btn.classList.add('copied');
        setTimeout(function () { btn.textContent = 'Copy'; btn.classList.remove('copied'); }, 2000);
    });
}
(function () {
    function initCharts() {
        document.querySelectorAll('script[type="application/json"][data-chart-id]').forEach(function (el) {
            var id = el.getAttribute('data-chart-id');
            var canvas = document.getElementById(id);
            if (!canvas) return;
            try {
                var config = JSON.parse(el.textContent);
                new Chart(canvas, config);
            } catch (e) {
                console.error('Narnia: failed to init chart ' + id, e);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initCharts);
    } else {
        initCharts();
    }
})();

async function narniaSaveOverride(sessionId) {
    const payload = {
        displayName: document.getElementById('ov-display-name').value,
        repository: document.getElementById('ov-repo').value,
        branch: document.getElementById('ov-branch').value,
        notes: document.getElementById('ov-notes').value
    };
    const btn = document.querySelector('.btn-save');
    if (btn) { btn.disabled = true; btn.textContent = 'Saving…'; }
    try {
        const resp = await fetch('/api/sessions/' + encodeURIComponent(sessionId) + '/overrides', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (resp.ok) {
            window.location.reload();
        } else {
            alert('Failed to save overrides (HTTP ' + resp.status + ')');
            if (btn) { btn.disabled = false; btn.textContent = 'Save'; }
        }
    } catch (e) {
        alert('Error saving overrides: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = 'Save'; }
    }
}

async function narniaResetOverride(sessionId) {
    if (!confirm('Reset all overrides for this session? The original session-store values will be shown.')) return;
    const btn = document.querySelector('.btn-reset');
    if (btn) { btn.disabled = true; btn.textContent = 'Resetting…'; }
    try {
        const resp = await fetch('/api/sessions/' + encodeURIComponent(sessionId) + '/overrides', {
            method: 'DELETE'
        });
        if (resp.ok) {
            window.location.reload();
        } else {
            alert('Failed to reset overrides (HTTP ' + resp.status + ')');
            if (btn) { btn.disabled = false; btn.textContent = 'Reset to Original'; }
        }
    } catch (e) {
        alert('Error resetting overrides: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = 'Reset to Original'; }
    }
}
