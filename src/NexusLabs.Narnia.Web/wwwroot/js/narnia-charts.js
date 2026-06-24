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

function narniaSelectedClosedIds() {
    var checks = document.querySelectorAll('.closed-check:checked');
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);
    return ids;
}

function narniaClosedSelectionChanged() {
    var all = document.querySelectorAll('.closed-check');
    var selected = narniaSelectedClosedIds();
    var btn = document.getElementById('btn-reopen-selected');
    if (btn) {
        btn.disabled = selected.length === 0;
        btn.textContent = '🚀 Reopen selected (' + selected.length + ')';
    }
    var master = document.getElementById('closed-check-all');
    if (master) {
        master.checked = selected.length > 0 && selected.length === all.length;
        master.indeterminate = selected.length > 0 && selected.length < all.length;
    }
}

function narniaToggleAllClosed(master) {
    var checks = document.querySelectorAll('.closed-check');
    for (var i = 0; i < checks.length; i++) checks[i].checked = master.checked;
    narniaClosedSelectionChanged();
}

async function narniaReopenSelected(btn) {
    var ids = narniaSelectedClosedIds();
    if (ids.length === 0) return;
    var oneWindowEl = document.getElementById('closed-one-window');
    var separateWindows = !(oneWindowEl && oneWindowEl.checked);

    var origText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Reopening…';
    try {
        var resp = await fetch('/api/windows/reopen', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ids: ids, separateWindows: separateWindows }),
        });
        if (resp.ok) {
            var data = await resp.json();
            btn.textContent = '✅ Reopened ' + (data.reopened || 0);
            setTimeout(function () { location.reload(); }, 1200);
        } else {
            var err = await resp.json().catch(function () { return null; });
            alert('Reopen failed: ' + (err?.message || err || 'HTTP ' + resp.status));
            btn.textContent = origText;
            btn.disabled = false;
        }
    } catch (e) {
        alert('Error reopening selection: ' + e.message);
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

// ── Session groups ───────────────────────────────────────────────────────────
// Shared create flow: prompt for a name, then POST the chosen session ids as a new group.
async function narniaCreateGroup(sessionIds, btn) {
    if (!sessionIds || sessionIds.length === 0) return;
    var name = prompt('Name this group (' + sessionIds.length + ' session' + (sessionIds.length === 1 ? '' : 's') + '):', '');
    if (name === null) return;
    name = name.trim();
    if (name === '') { alert('A group name is required.'); return; }

    var origText = btn ? btn.textContent : null;
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Saving…'; }
    try {
        var resp = await fetch('/api/groups', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name, sessionIds: sessionIds }),
        });
        if (resp.ok) {
            if (btn) {
                btn.textContent = '✅ Saved!';
                setTimeout(function () { btn.textContent = origText; btn.disabled = false; }, 2500);
            } else {
                alert('Saved group "' + name + '".');
            }
        } else {
            var err = await resp.json().catch(function () { return null; });
            alert('Failed to save group: ' + (err || 'HTTP ' + resp.status));
            if (btn) { btn.textContent = origText; btn.disabled = false; }
        }
    } catch (e) {
        alert('Error saving group: ' + e.message);
        if (btn) { btn.textContent = origText; btn.disabled = false; }
    }
}

// Manual curation entry point: the checked sessions on the Sessions list.
function narniaSaveSessionsAsGroup(btn) {
    var checks = document.querySelectorAll('.session-check:checked');
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);
    if (ids.length === 0) return;
    narniaCreateGroup(ids, btn);
}

// Snapshot entry point: the checked sessions in the "Open now" list on the Windows page.
function narniaOpenSelectedIds() {
    var checks = document.querySelectorAll('.open-check:checked');
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);
    return ids;
}

function narniaOpenSelectionChanged() {
    var all = document.querySelectorAll('.open-check');
    var selected = narniaOpenSelectedIds();
    var btn = document.getElementById('btn-save-open-group');
    if (btn) {
        btn.disabled = selected.length === 0;
        btn.textContent = '💾 Save selected as group (' + selected.length + ')';
    }
    var master = document.getElementById('open-check-all');
    if (master) {
        master.checked = selected.length > 0 && selected.length === all.length;
        master.indeterminate = selected.length > 0 && selected.length < all.length;
    }
}

function narniaToggleAllOpen(master) {
    var checks = document.querySelectorAll('.open-check');
    for (var i = 0; i < checks.length; i++) checks[i].checked = master.checked;
    narniaOpenSelectionChanged();
}

function narniaSaveOpenAsGroup(btn) {
    var ids = narniaOpenSelectedIds();
    if (ids.length === 0) return;
    narniaCreateGroup(ids, btn);
}

// Groups page: reopen an entire group, honoring its per-group window-mode toggle.
async function narniaReopenGroup(id, btn) {
    var oneWindowEl = document.getElementById('group-one-window-' + id);
    var separateWindows = !(oneWindowEl && oneWindowEl.checked);

    var origText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Reopening…';
    try {
        var resp = await fetch('/api/groups/' + id + '/reopen', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ separateWindows: separateWindows }),
        });
        if (resp.ok) {
            var data = await resp.json();
            btn.textContent = '✅ Reopened ' + (data.reopened || 0);
            setTimeout(function () { btn.textContent = origText; btn.disabled = false; }, 3000);
        } else {
            var err = await resp.json().catch(function () { return null; });
            alert('Reopen failed: ' + (err?.message || err || 'HTTP ' + resp.status));
            btn.textContent = origText;
            btn.disabled = false;
        }
    } catch (e) {
        alert('Error reopening group: ' + e.message);
        btn.textContent = origText;
        btn.disabled = false;
    }
}

async function narniaRenameGroup(id, btn) {
    var current = btn ? (btn.getAttribute('data-name') || '') : '';
    var name = prompt('Rename group:', current);
    if (name === null) return;
    name = name.trim();
    if (name === '') { alert('A group name is required.'); return; }
    try {
        var resp = await fetch('/api/groups/' + id + '/rename', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name }),
        });
        if (resp.ok) {
            location.reload();
        } else {
            alert('Failed to rename group: HTTP ' + resp.status);
        }
    } catch (e) {
        alert('Error renaming group: ' + e.message);
    }
}

async function narniaDeleteGroup(id) {
    if (!confirm('Delete this group? The sessions themselves are not affected.')) return;
    try {
        var resp = await fetch('/api/groups/' + id, { method: 'DELETE' });
        if (resp.ok) {
            location.reload();
        } else {
            alert('Failed to delete group: HTTP ' + resp.status);
        }
    } catch (e) {
        alert('Error deleting group: ' + e.message);
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

// ── Terminal Windows live refresh ────────────────────────────────────────────
// The UI is static server-rendered, so the windows page would otherwise never
// reflect a window opening or closing until a manual reload. This watcher polls
// the windows API and reloads only when the open/closed set actually changes
// (identity, status, tab count, name, pin, occurrence) — never on the snapshotter's
// routine last-seen heartbeat, so a steady state does not flash.
function narniaWindowsSignature(data) {
    var all = (data.open || []).concat(data.closed || []);
    all.sort(function (a, b) { return a.id < b.id ? -1 : (a.id > b.id ? 1 : 0); });
    return all.map(function (w) {
        return [
            w.id,
            w.status,
            (w.tabs ? w.tabs.length : 0),
            (w.pinned ? 1 : 0),
            (w.occurrenceCount || 0),
            (w.name || '')
        ].join(':');
    }).join('|');
}

(function () {
    var POLL_MS = 8000;

    function startWindowsWatch() {
        var root = document.getElementById('windows-root');
        if (!root) return;

        var baseline = root.getAttribute('data-signature') || '';
        var live = document.getElementById('windows-live-indicator');

        setInterval(async function () {
            try {
                var resp = await fetch('/api/windows', { headers: { 'Accept': 'application/json' } });
                if (!resp.ok) return;
                var data = await resp.json();
                if (narniaWindowsSignature(data) !== baseline) {
                    // Don't yank the page out from under an in-progress multi-select — wait until
                    // the user clears their selection, then pick up the change on a later tick.
                    if (document.querySelector('.closed-check:checked, .open-check:checked')) return;
                    if (live) live.textContent = '↻ updating…';
                    location.reload();
                }
            } catch (e) {
                // Transient (server restarting, etc.) — try again on the next tick.
            }
        }, POLL_MS);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startWindowsWatch);
    } else {
        startWindowsWatch();
    }
})();
