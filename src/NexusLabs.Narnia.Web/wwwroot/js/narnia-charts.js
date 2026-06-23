// Narnia chart initializer — reads <script type="application/json" data-chart-id="...">
// blocks rendered by Blazor Static SSR pages and creates Chart.js instances.

function narniaCopyText(elementId, btn) {
    var el = document.getElementById(elementId);
    if (!el) return;
    var origText = btn.textContent;
    navigator.clipboard.writeText(el.textContent.trim()).then(function () {
        btn.textContent = '✅ Copied!';
        btn.classList.add('copied');
        setTimeout(function () { btn.textContent = origText; btn.classList.remove('copied'); }, 2000);
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
        notes: document.getElementById('ov-notes').value,
        localPath: document.getElementById('ov-local-path').value,
        terminalTitle: document.getElementById('ov-terminal-title').value
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

async function narniaToggleArchive(sessionId, archived) {
    try {
        const resp = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/archive`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ archived: archived === 'true' || archived === true }),
        });
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
        window.location.reload();
    } catch (e) {
        alert('Error updating archive status: ' + e.message);
    }
}

async function narniaLaunch(target, sessionId, btn) {
    var origText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Launching…';
    try {
        const resp = await fetch('/api/launch', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sessionId: sessionId, target: target }),
        });
        if (resp.ok) {
            btn.textContent = '✅ Launched!';
            setTimeout(function () { btn.textContent = origText; btn.disabled = false; }, 3000);
        } else {
            var data = await resp.json().catch(function () { return null; });
            alert('Launch failed: ' + (data?.message || data || 'HTTP ' + resp.status));
            btn.textContent = origText;
            btn.disabled = false;
        }
    } catch (e) {
        alert('Error launching: ' + e.message);
        btn.textContent = origText;
        btn.disabled = false;
    }
}

async function narniaSaveSettings() {
    var shellInput = document.getElementById('setting-shell-path');
    if (!shellInput) return;
    var btn = document.querySelector('.btn-save-settings');
    if (btn) { btn.disabled = true; btn.textContent = 'Saving…'; }
    try {
        var resp = await fetch('/api/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ key: 'shell_path', value: shellInput.value }),
        });
        if (resp.ok) {
            if (btn) { btn.textContent = '✅ Saved!'; }
            setTimeout(function () {
                if (btn) { btn.textContent = 'Save'; btn.disabled = false; }
            }, 2000);
        } else {
            alert('Failed to save settings (HTTP ' + resp.status + ')');
            if (btn) { btn.disabled = false; btn.textContent = 'Save'; }
        }
    } catch (e) {
        alert('Error saving settings: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = 'Save'; }
    }
}

async function narniaDetectShell() {
    var btn = document.querySelector('.btn-detect');
    if (btn) { btn.disabled = true; btn.textContent = 'Detecting…'; }
    try {
        var resp = await fetch('/api/settings/detect-shell');
        if (resp.ok) {
            var data = await resp.json();
            var input = document.getElementById('setting-shell-path');
            if (input && data.path) { input.value = data.path; }
            if (btn) { btn.textContent = '✅ Detected!'; }
            setTimeout(function () { if (btn) { btn.textContent = '🔍 Auto-detect'; btn.disabled = false; } }, 2000);
        } else {
            alert('No shell detected on this system');
            if (btn) { btn.disabled = false; btn.textContent = '🔍 Auto-detect'; }
        }
    } catch (e) {
        alert('Error detecting shell: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = '🔍 Auto-detect'; }
    }
}

function narniaToggleAll(masterCheckbox) {
    var checks = document.querySelectorAll('.session-check');
    for (var i = 0; i < checks.length; i++) {
        checks[i].checked = masterCheckbox.checked;
    }
    narniaUpdateBulkBar();
}

function narniaUpdateBulkBar() {
    var checks = document.querySelectorAll('.session-check:checked');
    var bar = document.getElementById('bulk-action-bar');
    var count = document.getElementById('bulk-count');
    if (!bar) return;
    if (checks.length > 0) {
        bar.style.display = '';
        count.textContent = checks.length + ' selected';
    } else {
        bar.style.display = 'none';
    }
}

async function narniaArchiveBulk() {
    var checks = document.querySelectorAll('.session-check:checked');
    if (checks.length === 0) return;
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);

    if (!confirm('Archive ' + ids.length + ' session(s)?')) return;

    var btn = document.querySelector('.btn-bulk-archive');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Archiving…'; }
    try {
        var results = await Promise.all(ids.map(function (id) {
            return fetch('/api/sessions/' + encodeURIComponent(id) + '/archive', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ archived: true }),
            }).then(function (r) { return { id: id, ok: r.ok }; });
        }));
        var failed = results.filter(function (r) { return !r.ok; });
        if (failed.length > 0) {
            alert('Failed to archive ' + failed.length + ' session(s).');
        }
        window.location.reload();
    } catch (e) {
        alert('Error archiving: ' + e.message);
        if (btn) { btn.textContent = '📦 Archive Selected'; btn.disabled = false; }
    }
}

async function narniaLaunchBulk() {
    var checks = document.querySelectorAll('.session-check:checked');
    if (checks.length === 0) return;
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);

    var btn = document.querySelector('.btn-bulk-launch');
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Launching…'; }
    try {
        var resp = await fetch('/api/launch-bulk', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sessionIds: ids }),
        });
        if (resp.ok) {
            var data = await resp.json();
            var msg = '✅ Launched ' + data.launched.length + ' session(s)';
            if (data.failed && data.failed.length > 0) {
                msg += '\n⚠️ Failed: ' + data.failed.map(function (f) { return f.sessionId.substring(0, 8) + ': ' + f.reason; }).join(', ');
            }
            if (btn) { btn.textContent = '✅ Launched!'; }
            setTimeout(function () { if (btn) { btn.textContent = '🚀 Launch Selected'; btn.disabled = false; } }, 3000);
            if (data.failed && data.failed.length > 0) alert(msg);
        } else {
            var errData = await resp.json().catch(function () { return null; });
            alert('Bulk launch failed: ' + (errData || 'HTTP ' + resp.status));
            if (btn) { btn.textContent = '🚀 Launch Selected'; btn.disabled = false; }
        }
    } catch (e) {
        alert('Error launching: ' + e.message);
        if (btn) { btn.textContent = '🚀 Launch Selected'; btn.disabled = false; }
    }
}

// ── Theme (dark/light) ───────────────────────────────────────────────────────
async function narniaSetTheme(theme) {
    var normalized = theme === 'light' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', normalized);
    try {
        await fetch('/api/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ key: 'theme', value: normalized }),
        });
    } catch (e) {
        console.error('Narnia: failed to persist theme', e);
    }
    return normalized;
}

function narniaToggleTheme() {
    var current = document.documentElement.getAttribute('data-theme');
    narniaSetTheme(current === 'light' ? 'dark' : 'light');
}

// ── Terminal windows (recovery console) ──────────────────────────────────────
async function narniaReopenWindow(id, btn) {
    var origText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Reopening…';
    try {
        var resp = await fetch('/api/windows/' + id + '/reopen', { method: 'POST' });
        if (resp.ok) {
            btn.textContent = '✅ Reopened!';
            setTimeout(function () { btn.textContent = origText; btn.disabled = false; }, 3000);
        } else {
            var data = await resp.json().catch(function () { return null; });
            alert('Reopen failed: ' + (data?.message || data || 'HTTP ' + resp.status));
            btn.textContent = origText;
            btn.disabled = false;
        }
    } catch (e) {
        alert('Error reopening: ' + e.message);
        btn.textContent = origText;
        btn.disabled = false;
    }
}

async function narniaNameWindow(id, current) {
    var name = prompt('Name this window (naming it pins it so it is never auto-pruned). Leave blank to clear:', current || '');
    if (name === null) return;
    try {
        var resp = await fetch('/api/windows/' + id + '/name', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name }),
        });
        if (resp.ok) {
            location.reload();
        } else {
            alert('Failed to name window: HTTP ' + resp.status);
        }
    } catch (e) {
        alert('Error naming window: ' + e.message);
    }
}

async function narniaDeleteWindow(id) {
    if (!confirm('Delete this recorded window? This cannot be undone.')) return;
    try {
        var resp = await fetch('/api/windows/' + id, { method: 'DELETE' });
        if (resp.ok) {
            location.reload();
        } else {
            alert('Failed to delete window: HTTP ' + resp.status);
        }
    } catch (e) {
        alert('Error deleting window: ' + e.message);
    }
}

// ── Snapshotter & autostart settings ─────────────────────────────────────────
async function narniaSaveSetting(key, value, el) {
    try {
        var resp = await fetch('/api/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ key: key, value: value }),
        });
        if (!resp.ok) {
            alert('Failed to save setting: HTTP ' + resp.status);
            return false;
        }
        return true;
    } catch (e) {
        alert('Error saving setting: ' + e.message);
        return false;
    }
}

async function narniaSaveSnapshotterConfig(btn) {
    var interval = document.getElementById('setting-snap-interval');
    var retention = document.getElementById('setting-snap-retention');
    if (btn) { btn.disabled = true; btn.textContent = 'Saving…'; }
    var ok = await narniaSaveSetting('snapshotter_interval_seconds', interval.value);
    ok = (await narniaSaveSetting('snapshotter_retention_count', retention.value)) && ok;
    if (btn) {
        btn.textContent = ok ? '✅ Saved!' : 'Save';
        setTimeout(function () { btn.textContent = 'Save'; btn.disabled = false; }, 2500);
    }
}

async function narniaSetAutostart(enabled, el) {
    try {
        var resp = await fetch('/api/autostart', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ enabled: enabled }),
        });
        if (!resp.ok) {
            var data = await resp.json().catch(function () { return null; });
            alert('Failed to update autostart: ' + (data || 'HTTP ' + resp.status));
            if (el) el.checked = !enabled;
        }
    } catch (e) {
        alert('Error updating autostart: ' + e.message);
        if (el) el.checked = !enabled;
    }
}
