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
                var pointLabels = config.narniaPointLabels;
                if (pointLabels) {
                    delete config.narniaPointLabels;
                    config.options = config.options || {};
                    config.options.plugins = config.options.plugins || {};
                    config.options.plugins.tooltip = config.options.plugins.tooltip || {};
                    config.options.plugins.tooltip.callbacks =
                        config.options.plugins.tooltip.callbacks || {};
                    config.options.plugins.tooltip.callbacks.label = function (context) {
                        var label = pointLabels[context.dataIndex] || 'Session';
                        return label + ': ' +
                            context.parsed.x.toFixed(1) + ' days, ' +
                            context.parsed.y.toFixed(2) + ' MiB';
                    };
                }
                var hrefTemplate = el.getAttribute('data-chart-href-template');
                if (hrefTemplate) {
                    config.options = config.options || {};
                    config.options.onClick = function (_, elements) {
                        if (!elements || elements.length === 0) return;
                        var index = elements[0].index;
                        var label = config.data.labels[index];
                        window.location.assign(
                            hrefTemplate.replace('{label}', encodeURIComponent(label)));
                    };
                    config.options.onHover = function (event, elements) {
                        if (event.native && event.native.target) {
                            event.native.target.style.cursor =
                                elements && elements.length > 0 ? 'pointer' : 'default';
                        }
                    };
                }
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
    const repositoryInput = document.getElementById('ov-repo');
    if (!repositoryInput.checkValidity()) {
        repositoryInput.reportValidity();
        return;
    }

    const payload = {
        displayName: document.getElementById('ov-display-name').value,
        repository: repositoryInput.value,
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
            let message = 'Failed to save overrides (HTTP ' + resp.status + ')';
            const contentType = resp.headers.get('content-type') ?? '';
            if (contentType.includes('json')) {
                const body = await resp.json();
                message = body.errors?.repository?.[0] ?? message;
            }
            alert(message);
            if (btn) { btn.disabled = false; btn.textContent = 'Save'; }
        }
    } catch (e) {
        alert('Error saving overrides: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = 'Save'; }
    }
}

async function narniaResetOverride(sessionId) {
    if (!confirm('Reset session metadata overrides? Favorite and archive state will be preserved.')) return;
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
            if (btn) { btn.disabled = false; btn.textContent = 'Reset Metadata'; }
        }
    } catch (e) {
        alert('Error resetting overrides: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = 'Reset Metadata'; }
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

async function narniaMigrateSession(sessionId, btn) {
    if (!confirm(
        'Recover this Copilot session in place? Narnia will archive the broken event stream, ' +
        'retain the same folder and session ID, and use one bootstrap model response to reseed it.')) {
        return;
    }

    var originalText = btn.textContent;
    btn.disabled = true;
    btn.classList.add('session-migrate-btn--working');
    btn.textContent = '⏳ Recovering session in place…';
    try {
        var response = await fetch(
            '/api/sessions/' + encodeURIComponent(sessionId) + '/migration',
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ confirmMigration: true }),
            });
        var body = await response.json().catch(function () { return null; });
        if (!response.ok) {
            throw new Error(body?.message || 'HTTP ' + response.status);
        }

        btn.textContent = '✅ Recovery complete';
        window.location.assign(
            '/sessions/' + encodeURIComponent(body.replacementSessionId));
    } catch (e) {
        alert('Session recovery failed: ' + e.message);
        btn.disabled = false;
        btn.classList.remove('session-migrate-btn--working');
        btn.textContent = originalText;
    }
}

async function narniaSaveSettings() {
    var shellInput = document.getElementById('setting-shell-path');
    var copilotInput = document.getElementById('setting-copilot-command');
    if (!shellInput) return;
    var btn = document.querySelector('.btn-save-settings');
    if (btn) { btn.disabled = true; btn.textContent = 'Saving…'; }
    try {
        var resp = await fetch('/api/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ key: 'shell_path', value: shellInput.value }),
        });
        if (resp.ok && copilotInput) {
            resp = await fetch('/api/settings', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ key: 'copilot_command', value: copilotInput.value || 'copilot' }),
            });
        }
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

function narniaSelectedSessionIds() {
    var checks = document.querySelectorAll('.session-check:checked');
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);
    return ids;
}

async function narniaArchiveBulk() {
    var ids = narniaSelectedSessionIds();
    if (ids.length === 0) return;

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

async function narniaLaunchSessions(ids, btn) {
    if (ids.length === 0) return;

    var originalText = btn ? btn.textContent : null;
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
            setTimeout(function () {
                if (btn) {
                    btn.textContent = originalText;
                    btn.disabled = false;
                }
            }, 3000);
            if (data.failed && data.failed.length > 0) alert(msg);
        } else {
            var errData = await resp.json().catch(function () { return null; });
            alert('Bulk launch failed: ' + (errData || 'HTTP ' + resp.status));
            if (btn) { btn.textContent = originalText; btn.disabled = false; }
        }
    } catch (e) {
        alert('Error launching: ' + e.message);
        if (btn) { btn.textContent = originalText; btn.disabled = false; }
    }
}

function narniaLaunchBulk() {
    var btn = document.querySelector('#bulk-action-bar .btn-bulk-launch');
    narniaLaunchSessions(narniaSelectedSessionIds(), btn);
}

// ── Session storage ──────────────────────────────────────────────────────────
var narniaStorageCleanupPlan = null;
var narniaStorageCleanupCompleted = false;

function narniaSelectedStorageSessionIds() {
    var checks = document.querySelectorAll('.storage-check:checked');
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);
    return ids;
}

function narniaStorageSelectionChanged() {
    var all = document.querySelectorAll('.storage-check:not(:disabled)');
    var selected = document.querySelectorAll('.storage-check:checked');
    var bar = document.getElementById('storage-cleanup-bar');
    var count = document.getElementById('storage-selected-count');
    var bytes = document.getElementById('storage-selected-bytes');
    var protectedSummary = document.getElementById('storage-selected-protected');
    var selectedBytes = 0;
    var protectedCount = 0;
    for (var i = 0; i < selected.length; i++) {
        selectedBytes += Number(selected[i].dataset.bytes || 0);
        if (selected[i].dataset.protected === 'true') protectedCount++;
    }
    if (bar) bar.style.display = selected.length > 0 ? '' : 'none';
    if (count) count.textContent = selected.length + ' selected';
    if (bytes) bytes.textContent = narniaFormatStorageBytes(selectedBytes) + ' selected';
    if (protectedSummary) {
        protectedSummary.hidden = protectedCount === 0;
        protectedSummary.textContent = protectedCount === 0
            ? ''
            : protectedCount + ' protected — the review plan will explain each protection';
    }
    var master = document.querySelector('.storage-table thead input[type=checkbox]');
    if (master) {
        master.checked = selected.length > 0 && selected.length === all.length;
        master.indeterminate = selected.length > 0 && selected.length < all.length;
    }
}

function narniaToggleAllStorage(master) {
    var checks = document.querySelectorAll('.storage-check:not(:disabled)');
    for (var i = 0; i < checks.length; i++) checks[i].checked = master.checked;
    narniaStorageSelectionChanged();
}

function narniaFormatStorageBytes(bytes) {
    var units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    var value = Math.max(0, Number(bytes) || 0);
    var unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
        value /= 1024;
        unit++;
    }
    return (unit === 0 ? value.toFixed(0) : value.toFixed(2)) + ' ' + units[unit];
}

async function narniaRequestStorageScan(btn) {
    var originalText = btn ? btn.textContent : null;
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Starting scan…'; }
    try {
        var response = await fetch('/api/storage/scan', { method: 'POST' });
        if (!response.ok && response.status !== 409) {
            throw new Error('HTTP ' + response.status);
        }
        await narniaPollStorageScan(btn, originalText);
    } catch (e) {
        alert('Storage scan failed to start: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = originalText; }
    }
}

async function narniaPollStorageScan(btn, originalText) {
    var state = document.getElementById('storage-scan-state');
    for (var attempt = 0; attempt < 1200; attempt++) {
        var response = await fetch('/api/storage/status');
        if (!response.ok) throw new Error('HTTP ' + response.status);
        var status = await response.json();
        if (state) {
            var text = status.status === 'running'
                ? 'Scanner: scanning ' + status.scannedSessions + ' of ' + status.totalSessions + ' sessions'
                : 'Scanner: ' + status.status;
            state.textContent = text;
            state.dataset.status = status.status;
        }
        if (status.status === 'completed' || status.status === 'failed') {
            window.location.reload();
            return;
        }
        await new Promise(function (resolve) { setTimeout(resolve, 1000); });
    }
    if (btn) { btn.disabled = false; btn.textContent = originalText; }
    alert('The storage scan is still running. Refresh the page later to see its progress.');
}

async function narniaPreviewStorageCleanup(btn) {
    var ids = narniaSelectedStorageSessionIds();
    if (ids.length === 0) return;
    var originalText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Validating…';
    try {
        var request = {
            sessionIds: ids,
            overrideProtections: false
        };
        var previewResponse = await fetch('/api/storage/cleanup-preview', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request)
        });
        if (!previewResponse.ok) throw new Error('HTTP ' + previewResponse.status);
        var preview = await previewResponse.json();
        narniaStorageCleanupPlan = { ids: ids, preview: preview };
        narniaStorageCleanupCompleted = false;
        narniaRenderStorageDecisionList(
            'storage-plan-allowed',
            preview.decisions.filter(function (decision) {
                return decision.disposition === 'allowed';
            }),
            'No selected sessions are ready.');
        narniaRenderStorageDecisionList(
            'storage-plan-protected',
            preview.decisions.filter(function (decision) {
                return decision.disposition === 'protected';
            }),
            'No selected sessions are protected.');
        narniaRenderStorageDecisionList(
            'storage-plan-blocked',
            preview.decisions.filter(function (decision) {
                return decision.disposition === 'blocked';
            }),
            'No selected sessions are blocked.');

        var summary = document.getElementById('storage-plan-summary');
        if (summary) {
            summary.textContent =
                ids.length + ' selected · ' +
                preview.allowedCount + ' ready (' +
                narniaFormatStorageBytes(preview.allowedBytes) + ') · ' +
                preview.protectedCount + ' protected · ' +
                preview.blockedCount + ' blocked';
        }
        var overridePanel = document.getElementById('storage-plan-protection-override');
        if (overridePanel) overridePanel.hidden = preview.protectedCount === 0;
        var includeProtected = document.getElementById('storage-plan-include-protected');
        if (includeProtected) includeProtected.checked = false;
        var acknowledgement = document.getElementById('storage-plan-confirm-delete');
        if (acknowledgement) acknowledgement.checked = false;
        var archiveDeleted = document.getElementById('storage-plan-archive');
        if (archiveDeleted) {
            archiveDeleted.checked = true;
            archiveDeleted.disabled = false;
        }
        var resultElement = document.getElementById('storage-cleanup-result');
        if (resultElement) {
            resultElement.hidden = true;
            resultElement.textContent = '';
            resultElement.classList.remove(
                'storage-cleanup-result--success',
                'storage-cleanup-result--error');
        }
        var cancel = document.getElementById('storage-plan-cancel');
        if (cancel) {
            cancel.textContent = 'Cancel';
            cancel.classList.remove('storage-close-complete');
        }
        var deleteButton = document.getElementById('storage-plan-delete');
        if (deleteButton) {
            deleteButton.hidden = false;
            deleteButton.removeAttribute('aria-busy');
            deleteButton.classList.remove('storage-action--working');
        }
        narniaStoragePlanChanged();
        var dialog = document.getElementById('storage-cleanup-dialog');
        if (dialog && typeof dialog.showModal === 'function') dialog.showModal();
    } catch (e) {
        alert('Cleanup preview failed: ' + e.message);
    } finally {
        btn.disabled = false;
        btn.textContent = originalText;
    }
}

function narniaRenderStorageDecisionList(elementId, decisions, emptyText) {
    var list = document.getElementById(elementId);
    if (!list) return;
    list.replaceChildren();
    if (decisions.length === 0) {
        var empty = document.createElement('li');
        empty.className = 'storage-plan-empty';
        empty.textContent = emptyText;
        list.appendChild(empty);
        return;
    }

    for (var i = 0; i < decisions.length; i++) {
        var decision = decisions[i];
        var item = document.createElement('li');
        var title = document.createElement('strong');
        title.textContent = decision.summary || decision.sessionId.substring(0, 8);
        item.appendChild(title);
        var detail = document.createElement('span');
        detail.textContent =
            narniaFormatStorageBytes(decision.estimatedBytes) +
            (decision.reasons.length > 0 ? ' · ' + decision.reasons.join(' · ') : '');
        item.appendChild(detail);
        list.appendChild(item);
    }
}

function narniaStoragePlanChanged() {
    if (!narniaStorageCleanupPlan) return;
    var includeProtected = document.getElementById('storage-plan-include-protected');
    var acknowledgement = document.getElementById('storage-plan-confirm-delete');
    var archiveDeleted = document.getElementById('storage-plan-archive');
    var deleteButton = document.getElementById('storage-plan-delete');
    if (narniaStorageCleanupCompleted) {
        if (includeProtected) includeProtected.disabled = true;
        if (acknowledgement) acknowledgement.disabled = true;
        if (archiveDeleted) archiveDeleted.disabled = true;
        if (deleteButton) {
            deleteButton.disabled = true;
            deleteButton.hidden = true;
        }
        return;
    }
    var preview = narniaStorageCleanupPlan.preview;
    var include = !!(includeProtected && includeProtected.checked);
    var count = preview.allowedCount + (include ? preview.protectedCount : 0);
    var bytes = preview.allowedBytes + (include ? preview.protectedBytes : 0);
    if (deleteButton) {
        deleteButton.disabled = !(acknowledgement && acknowledgement.checked) || count === 0;
        deleteButton.textContent =
            count === 0
                ? 'No sessions can be deleted'
                : 'Delete local data for ' + count + ' session(s) · ' +
                    narniaFormatStorageBytes(bytes);
    }
}

async function narniaExecuteStorageCleanup(btn) {
    if (!narniaStorageCleanupPlan) return;
    var includeProtected = document.getElementById('storage-plan-include-protected');
    var acknowledgement = document.getElementById('storage-plan-confirm-delete');
    var archiveDeleted = document.getElementById('storage-plan-archive');
    if (!(acknowledgement && acknowledgement.checked)) return;
    var originalText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Deleting local data…';
    btn.setAttribute('aria-busy', 'true');
    btn.classList.add('storage-action--working');
    try {
        var response = await fetch('/api/storage/delete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                sessionIds: narniaStorageCleanupPlan.ids,
                overrideProtections: !!(includeProtected && includeProtected.checked),
                confirmLocalDeletion: true,
                archiveDeletedSessions: !!(archiveDeleted && archiveDeleted.checked)
            })
        });
        if (!response.ok) throw new Error('HTTP ' + response.status);
        var result = await response.json();
        var issues = result.results.filter(function (item) { return !!item.error; });
        var message =
            'Deleted ' + result.deletedCount + ' local session(s), approximately ' +
            narniaFormatStorageBytes(result.deletedBytes) + '.';
        if (archiveDeleted && archiveDeleted.checked) {
            message += ' Archived ' + result.archivedCount + ' successfully cleaned session(s) in Narnia.';
        }
        if (issues.length > 0) {
            message += ' ' + issues.length +
                ' session(s) reported a deletion or archive warning; their audit entries include the reason.';
        }
        var resultElement = document.getElementById('storage-cleanup-result');
        if (resultElement) {
            resultElement.textContent = message;
            resultElement.hidden = false;
            resultElement.classList.remove('storage-cleanup-result--error');
            resultElement.classList.add('storage-cleanup-result--success');
        }
        narniaStorageCleanupCompleted = true;
        if (includeProtected) includeProtected.disabled = true;
        if (acknowledgement) acknowledgement.disabled = true;
        if (archiveDeleted) archiveDeleted.disabled = true;
        var cancel = document.getElementById('storage-plan-cancel');
        if (cancel) {
            cancel.textContent = 'Close and refresh';
            cancel.classList.add('storage-close-complete');
        }
        btn.removeAttribute('aria-busy');
        btn.classList.remove('storage-action--working');
        btn.hidden = true;
    } catch (e) {
        var resultElement = document.getElementById('storage-cleanup-result');
        if (resultElement) {
            resultElement.textContent = 'Cleanup failed: ' + e.message;
            resultElement.hidden = false;
            resultElement.classList.remove('storage-cleanup-result--success');
            resultElement.classList.add('storage-cleanup-result--error');
        }
        btn.removeAttribute('aria-busy');
        btn.classList.remove('storage-action--working');
        btn.textContent = originalText;
        btn.disabled = false;
    }
}

function narniaCloseStorageCleanupDialog() {
    var dialog = document.getElementById('storage-cleanup-dialog');
    if (dialog && dialog.open) dialog.close();
    narniaStorageCleanupPlan = null;
    if (narniaStorageCleanupCompleted) {
        window.location.reload();
        return;
    }
    narniaStorageCleanupCompleted = false;
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
async function narniaCreateSessionGroup(sessionIds, btn) {
    if (!sessionIds || sessionIds.length === 0) return;
    var name = prompt('Name this Session Group (' + sessionIds.length + ' session' + (sessionIds.length === 1 ? '' : 's') + '):', '');
    if (name === null) return;
    name = name.trim();
    if (name === '') { alert('A Session Group name is required.'); return; }

    var origText = btn ? btn.textContent : null;
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Saving…'; }
    try {
        var resp = await fetch('/api/session-groups', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name, sessionIds: sessionIds }),
        });
        if (resp.ok) {
            if (btn) {
                btn.textContent = '✅ Saved!';
                setTimeout(function () { btn.textContent = origText; btn.disabled = false; }, 2500);
            } else {
                alert('Saved Session Group "' + name + '".');
            }
        } else {
            var err = await resp.json().catch(function () { return null; });
            alert('Failed to save Session Group: ' + (err || 'HTTP ' + resp.status));
            if (btn) { btn.textContent = origText; btn.disabled = false; }
        }
    } catch (e) {
        alert('Error saving Session Group: ' + e.message);
        if (btn) { btn.textContent = origText; btn.disabled = false; }
    }
}

function narniaSaveSessionsAsSessionGroup(btn) {
    var ids = narniaSelectedSessionIds();
    if (ids.length === 0) return;
    narniaCreateSessionGroup(ids, btn);
}

function narniaOpenSelectedIds() {
    var checks = document.querySelectorAll('.open-check:checked');
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);
    return ids;
}

function narniaOpenSelectionChanged() {
    var all = document.querySelectorAll('.open-check');
    var selected = narniaOpenSelectedIds();
    var btn = document.getElementById('btn-save-open-session-group');
    if (btn) {
        btn.disabled = selected.length === 0;
        btn.textContent = '💾 Save selected as Session Group (' + selected.length + ')';
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

function narniaSaveOpenAsSessionGroup(btn) {
    var ids = narniaOpenSelectedIds();
    if (ids.length === 0) return;
    narniaCreateSessionGroup(ids, btn);
}

async function narniaReopenSessionGroup(id, btn) {
    var oneWindowEl = document.getElementById('session-group-one-window-' + id);
    var separateWindows = !(oneWindowEl && oneWindowEl.checked);

    var origText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Reopening…';
    try {
        var resp = await fetch('/api/session-groups/' + id + '/reopen', {
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
        alert('Error reopening Session Group: ' + e.message);
        btn.textContent = origText;
        btn.disabled = false;
    }
}

async function narniaRenameSessionGroup(id, btn) {
    var current = btn ? (btn.getAttribute('data-name') || '') : '';
    var name = prompt('Rename Session Group:', current);
    if (name === null) return;
    name = name.trim();
    if (name === '') { alert('A Session Group name is required.'); return; }
    try {
        var resp = await fetch('/api/session-groups/' + id + '/rename', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name }),
        });
        if (resp.ok) {
            location.reload();
        } else {
            alert('Failed to rename Session Group: HTTP ' + resp.status);
        }
    } catch (e) {
        alert('Error renaming Session Group: ' + e.message);
    }
}

async function narniaDeleteSessionGroup(id) {
    if (!confirm('Delete this Session Group? The sessions themselves are not affected.')) return;
    try {
        var resp = await fetch('/api/session-groups/' + id, { method: 'DELETE' });
        if (resp.ok) {
            location.reload();
        } else {
            alert('Failed to delete Session Group: HTTP ' + resp.status);
        }
    } catch (e) {
        alert('Error deleting Session Group: ' + e.message);
    }
}

// ── Work collections ─────────────────────────────────────────────────────────
async function narniaCollectionError(resp) {
    var body = await resp.json().catch(function () { return null; });
    if (typeof body === 'string' && body !== '') return body;
    if (body && body.message) return body.message;
    return 'HTTP ' + resp.status;
}

async function narniaCreateCollection(btn) {
    var name = prompt('Name this Collection:', '');
    if (name === null) return;
    name = name.trim();
    if (name === '') { alert('A Collection name is required.'); return; }

    var originalText = btn ? btn.textContent : null;
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Creating…'; }
    try {
        var resp = await fetch('/api/collections', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name, sessionIds: [] }),
        });
        if (!resp.ok) throw new Error(await narniaCollectionError(resp));
        location.reload();
    } catch (e) {
        alert('Failed to create Collection: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = originalText; }
    }
}

async function narniaAddSessionsToCollection(sessionIds, btn) {
    if (!sessionIds || sessionIds.length === 0) return false;

    var originalText = btn ? btn.textContent : null;
    try {
        var listResp = await fetch('/api/collections');
        if (!listResp.ok) throw new Error(await narniaCollectionError(listResp));
        var listData = await listResp.json();
        var collections = listData.collections || [];
        var choices = collections.map(function (collection, index) {
            return '#' + (index + 1) + ' ' + collection.name + ' (' + collection.memberCount + ')';
        });
        var promptText = collections.length === 0
            ? 'No Collections exist yet. Enter a name to create one with these ' + sessionIds.length + ' session(s):'
            : 'Add ' + sessionIds.length + ' session(s) to a Collection.\n\n'
                + choices.join('\n')
                + '\n\nEnter a #number, an existing Collection name, or a new name:';
        var answer = prompt(promptText, '');
        if (answer === null) return false;
        answer = answer.trim();
        if (answer === '') { alert('Choose or name a Collection.'); return false; }

        var normalizedAnswer = answer.toLowerCase();
        var collection = collections.find(function (candidate) {
            return candidate.name.toLowerCase() === normalizedAnswer;
        }) || null;
        if (!collection && /^#\d+$/.test(answer)) {
            var index = Number(answer.substring(1)) - 1;
            if (index < 0 || index >= collections.length) {
                alert('That Collection number does not exist.');
                return false;
            }
            collection = collections[index];
        }

        if (btn) { btn.disabled = true; btn.textContent = '⏳ Saving…'; }
        var resp;
        if (collection) {
            resp = await fetch('/api/collections/' + encodeURIComponent(collection.id) + '/sessions', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ sessionIds: sessionIds }),
            });
        } else {
            resp = await fetch('/api/collections', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: answer, sessionIds: sessionIds }),
            });
        }

        if (!resp.ok) throw new Error(await narniaCollectionError(resp));
        if (btn) {
            btn.textContent = collection
                ? '✅ Added to ' + collection.name
                : '✅ Created ' + answer;
            setTimeout(function () {
                btn.disabled = false;
                btn.textContent = originalText;
            }, 2500);
        }
        return true;
    } catch (e) {
        alert('Failed to update Collection: ' + e.message);
        if (btn) { btn.disabled = false; btn.textContent = originalText; }
        return false;
    }
}

function narniaAddSelectedSessionsToCollection(btn) {
    narniaAddSessionsToCollection(narniaSelectedSessionIds(), btn);
}

async function narniaAddSessionToCollection(sessionId, btn) {
    if (await narniaAddSessionsToCollection([sessionId], btn)) location.reload();
}

async function narniaRenameCollection(id, btn) {
    var current = btn ? (btn.getAttribute('data-name') || '') : '';
    var name = prompt('Rename Collection:', current);
    if (name === null) return;
    name = name.trim();
    if (name === '') { alert('A Collection name is required.'); return; }

    try {
        var resp = await fetch('/api/collections/' + encodeURIComponent(id) + '/rename', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name }),
        });
        if (!resp.ok) throw new Error(await narniaCollectionError(resp));
        location.reload();
    } catch (e) {
        alert('Failed to rename Collection: ' + e.message);
    }
}

async function narniaDeleteCollection(id) {
    if (!confirm('Delete this Collection? The sessions themselves are not affected.')) return;

    try {
        var resp = await fetch('/api/collections/' + encodeURIComponent(id), { method: 'DELETE' });
        if (!resp.ok) throw new Error(await narniaCollectionError(resp));
        location.href = '/collections';
    } catch (e) {
        alert('Failed to delete Collection: ' + e.message);
    }
}

function narniaSelectedCollectionSessionIds() {
    var checks = document.querySelectorAll('.collection-session-check:checked');
    var ids = [];
    for (var i = 0; i < checks.length; i++) ids.push(checks[i].value);
    return ids;
}

function narniaLaunchSelectedCollectionSessions(btn) {
    narniaLaunchSessions(narniaSelectedCollectionSessionIds(), btn);
}

function narniaSaveSelectedCollectionSessionsAsSessionGroup(btn) {
    var sessionIds = narniaSelectedCollectionSessionIds();
    if (sessionIds.length === 0) return;
    narniaCreateSessionGroup(sessionIds, btn);
}

function narniaUpdateCollectionMemberBar() {
    var selected = narniaSelectedCollectionSessionIds();
    var all = document.querySelectorAll('.collection-session-check');
    var bar = document.getElementById('collection-member-action-bar');
    var count = document.getElementById('collection-member-count');
    if (bar) bar.style.display = selected.length > 0 ? '' : 'none';
    if (count) count.textContent = selected.length + ' selected';

    var master = document.getElementById('collection-member-check-all');
    if (master) {
        master.checked = selected.length > 0 && selected.length === all.length;
        master.indeterminate = selected.length > 0 && selected.length < all.length;
    }
}

function narniaToggleAllCollectionMembers(master) {
    var checks = document.querySelectorAll('.collection-session-check');
    for (var i = 0; i < checks.length; i++) checks[i].checked = master.checked;
    narniaUpdateCollectionMemberBar();
}

async function narniaRemoveSelectedCollectionSessions(collectionId, btn) {
    var sessionIds = narniaSelectedCollectionSessionIds();
    if (sessionIds.length === 0) return;
    if (!confirm('Remove ' + sessionIds.length + ' session(s) from this Collection?')) return;

    var originalText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Removing…';
    try {
        var resp = await fetch(
            '/api/collections/' + encodeURIComponent(collectionId) + '/sessions/remove',
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ sessionIds: sessionIds }),
            });
        if (!resp.ok) throw new Error(await narniaCollectionError(resp));
        location.reload();
    } catch (e) {
        alert('Failed to remove sessions: ' + e.message);
        btn.disabled = false;
        btn.textContent = originalText;
    }
}

// ── Scheduled jobs ───────────────────────────────────────────────────────────
(function () {
    function plural(count, singular) {
        return count + ' ' + singular + (count === 1 ? '' : 's');
    }

    function formatTime(value) {
        if (!value) return null;
        var date = new Date(value);
        return Number.isNaN(date.getTime())
            ? null
            : new Intl.DateTimeFormat(undefined, {
                month: 'short',
                day: 'numeric',
                year: 'numeric',
                hour: 'numeric',
                minute: '2-digit',
            }).format(date);
    }

    function scheduleStatus(job) {
        switch (job.health) {
            case 'drift':
                return 'Task missing';
            case 'failed':
                return 'Failed (0x' + Number(job.status.lastResult).toString(16).toUpperCase() + ')';
            case 'running':
                return 'Running now';
            case 'neverrun':
                return 'Never run';
            case 'disabled':
                return 'Disabled';
            case 'succeeded':
                if (!job.status?.nextRunTime) return 'Healthy';
                break;
        }

        var nextRun = formatTime(job.status?.nextRunTime);
        return nextRun ? ('Next ' + nextRun) : 'No upcoming run';
    }

    function appendItem(container, title, status, meta, attention, link) {
        var article = document.createElement('article');
        article.className = 'dashboard-item' + (attention ? ' dashboard-item--attention' : '');

        var header = document.createElement('div');
        header.className = 'dashboard-item-header';

        var titleElement = document.createElement(link ? 'a' : 'span');
        titleElement.className = 'dashboard-item-title';
        titleElement.textContent = title;
        if (link) titleElement.href = link;
        header.appendChild(titleElement);

        if (status) {
            var statusElement = document.createElement('span');
            statusElement.className = 'dashboard-schedule-state';
            statusElement.textContent = status;
            header.appendChild(statusElement);
        }

        article.appendChild(header);
        if (meta) {
            var metaElement = document.createElement('div');
            metaElement.className = 'dashboard-item-meta';
            metaElement.textContent = meta;
            article.appendChild(metaElement);
        }
        container.appendChild(article);
    }

    async function loadDashboardSchedules() {
        var card = document.getElementById('dashboard-schedules-card');
        var value = document.getElementById('dashboard-schedules-value');
        var detail = document.getElementById('dashboard-schedules-detail');
        var panel = document.getElementById('dashboard-schedules-panel');
        var content = document.getElementById('dashboard-schedules-content');
        if (!card || !value || !detail || !panel || !content) return;

        try {
            var response = await fetch('/api/schedules');
            if (!response.ok) throw new Error('HTTP ' + response.status);
            var data = await response.json();
            var jobs = data.jobs || [];
            var untracked = data.untracked || [];
            value.textContent = jobs.length.toLocaleString();
            content.replaceChildren();

            if (!data.schedulerSupported) {
                detail.textContent = 'Live health unavailable';
                var unavailable = document.createElement('p');
                unavailable.className = 'dashboard-empty';
                unavailable.textContent = 'Live scheduler health is unavailable on this platform.';
                content.appendChild(unavailable);
                return;
            }

            var attention = jobs.filter(function (job) { return job.requiresAttention; });
            var attentionCount = attention.length + untracked.length;
            var runningCount = jobs.filter(function (job) { return job.health === 'running'; }).length;
            card.classList.toggle('dashboard-summary-card--attention', attentionCount > 0);
            panel.classList.toggle('dashboard-panel--attention', attentionCount > 0);

            if (attentionCount > 0) {
                detail.textContent =
                    plural(attentionCount, 'item') + (attentionCount === 1 ? ' needs' : ' need') + ' attention';
            } else if (jobs.length === 0) {
                detail.textContent = 'No jobs cataloged';
            } else if (runningCount > 0) {
                detail.textContent = plural(runningCount, 'job') + ' running';
            } else {
                detail.textContent = 'No failures or drift';
            }

            if (jobs.length === 0 && untracked.length === 0) {
                var empty = document.createElement('p');
                empty.className = 'dashboard-empty';
                empty.textContent = 'No scheduled jobs are cataloged.';
                content.appendChild(empty);
                return;
            }

            if (untracked.length > 0) {
                appendItem(
                    content,
                    plural(untracked.length, 'orphaned task'),
                    null,
                    "Present in Task Scheduler but missing from Narnia's catalog.",
                    true,
                    null);
            }

            var highlights = attention.length > 0
                ? attention.sort(function (a, b) {
                    return Date.parse(b.updatedAt || 0) - Date.parse(a.updatedAt || 0);
                })
                : jobs.sort(function (a, b) {
                    if (a.health === 'running' && b.health !== 'running') return -1;
                    if (b.health === 'running' && a.health !== 'running') return 1;
                    return (Date.parse(a.status?.nextRunTime) || Number.MAX_SAFE_INTEGER)
                        - (Date.parse(b.status?.nextRunTime) || Number.MAX_SAFE_INTEGER);
                });

            highlights.slice(0, 3).forEach(function (job) {
                appendItem(
                    content,
                    job.name,
                    scheduleStatus(job),
                    job.cadence || 'Cadence not recorded',
                    job.requiresAttention,
                    '/schedules');
            });
        } catch (error) {
            detail.textContent = 'Live health unavailable';
            content.replaceChildren();
            var failed = document.createElement('p');
            failed.className = 'dashboard-empty';
            failed.textContent = 'Could not load live scheduled-job health.';
            content.appendChild(failed);
            console.error('Narnia: failed to load dashboard schedules', error);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', loadDashboardSchedules);
    } else {
        loadDashboardSchedules();
    }
})();

function narniaScheduleFormBody(register) {
    var days = [];
    var checks = document.querySelectorAll('.sched-day:checked');
    for (var i = 0; i < checks.length; i++) days.push(checks[i].value);
    return {
        name: document.getElementById('sched-name').value.trim(),
        description: document.getElementById('sched-desc').value.trim() || null,
        cwd: document.getElementById('sched-cwd').value.trim() || null,
        prompt: document.getElementById('sched-prompt').value,
        allowFlags: document.getElementById('sched-flags').value.trim() || null,
        copilotArgs: document.getElementById('sched-copilotargs').value.trim() || null,
        cadenceKind: document.getElementById('sched-cadence').value,
        time: document.getElementById('sched-time').value.trim(),
        days: days,
        dayOfMonth: parseInt(document.getElementById('sched-dom').value, 10) || 1,
        skills: narniaCollectSkillRows(),
        register: register,
    };
}

// Appends one skill-row (name + resolution + remove button) to the editor. Called with no
// arguments by "+ Add skill"; called with values when prefilling from an existing job so every
// skill in job.skills round-trips instead of only the first.
function narniaAddSkillRow(skill, resolution) {
    var row = document.createElement('div');
    row.className = 'sched-skill-row';
    row.innerHTML =
        '<input type="text" class="sched-skill-name" placeholder="skill-name" />' +
        '<select class="sched-skill-res"><option value="plugin">plugin</option><option value="repolocal">repo-local</option></select>' +
        '<button type="button" class="btn-bulk-archive" title="Remove skill" onclick="narniaRemoveSkillRow(this)">🗑</button>';
    document.getElementById('sched-skills-list').appendChild(row);
    row.querySelector('.sched-skill-name').value = skill || '';
    row.querySelector('.sched-skill-res').value = resolution || 'plugin';
}

function narniaRemoveSkillRow(btn) {
    btn.closest('.sched-skill-row').remove();
}

// Reads every skill-row in the editor (not just the first) so saving a job never drops skills
// that were present but not the one being edited.
function narniaCollectSkillRows() {
    var skills = [];
    document.querySelectorAll('#sched-skills-list .sched-skill-row').forEach(function (row) {
        var name = row.querySelector('.sched-skill-name').value.trim();
        if (!name) return;
        skills.push({ skill: name, resolution: row.querySelector('.sched-skill-res').value });
    });
    return skills;
}

// Show day-of-week checkboxes only for weekly, day-of-month only for monthly.
function narniaCadenceChanged() {
    var kind = document.getElementById('sched-cadence').value;
    document.getElementById('sched-days-row').style.display = (kind === 'weekly') ? 'flex' : 'none';
    document.getElementById('sched-dom-row').style.display = (kind === 'monthly') ? 'flex' : 'none';
}

async function narniaScheduleSubmit(register) {
    var body = narniaScheduleFormBody(register);
    if (!body.name || !body.prompt || !body.prompt.trim()) { alert('Name and prompt are required.'); return; }
    var editId = document.getElementById('sched-edit-id').value;
    var url = editId ? ('/api/schedules/' + editId) : '/api/schedules';
    var method = editId ? 'PUT' : 'POST';
    try {
        var resp = await fetch(url, {
            method: method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
        });
        var data = await resp.json().catch(function () { return null; });
        if (!resp.ok) { alert('Failed: ' + (data || 'HTTP ' + resp.status)); return; }
        if (register || editId) { location.reload(); }
        else {
            document.getElementById('sched-script-box').value = data.script || '';
            document.getElementById('sched-command').value = data.command || '';
            document.getElementById('sched-copyout').style.display = 'block';
        }
    } catch (e) { alert('Error: ' + e.message); }
}

async function narniaScheduleEdit(id) {
    var resp = await fetch('/api/schedules');
    var data = await resp.json();
    var job = (data.jobs || []).find(function (j) { return j.id === id; });
    if (!job) { alert('Job not found'); return; }
    document.getElementById('sched-edit-id').value = job.id;
    document.getElementById('sched-name').value = job.name || '';
    document.getElementById('sched-desc').value = job.description || '';
    document.getElementById('sched-cwd').value = job.cwd || '';
    document.getElementById('sched-prompt').value = job.prompt || '';
    document.getElementById('sched-flags').value = job.allowFlags || '';
    document.getElementById('sched-copilotargs').value = job.copilotArgs || '';
    document.getElementById('sched-cadence').value = (job.cadenceKind || 'daily').toLowerCase();
    document.getElementById('sched-time').value = job.cadenceTime || '05:00';
    var kind = (job.cadenceKind || 'daily').toLowerCase();
    if (kind === 'monthly') {
        document.getElementById('sched-dom').value = parseInt(job.cadenceDays, 10) || 1;
        document.querySelectorAll('.sched-day').forEach(function (c) { c.checked = false; });
    } else {
        var setDays = (job.cadenceDays || '').split(',');
        document.querySelectorAll('.sched-day').forEach(function (c) { c.checked = setDays.indexOf(c.value) >= 0; });
    }
    narniaCadenceChanged();
    document.getElementById('sched-skills-list').innerHTML = '';
    (job.skills || []).forEach(function (s) { narniaAddSkillRow(s.skill, s.resolution); });
    document.getElementById('sched-form-title').textContent = '✏️ Editing: ' + job.name + ' (click to hide)';
    document.getElementById('sched-form-panel').style.display = 'block';
    document.getElementById('sched-name').scrollIntoView({ behavior: 'smooth', block: 'center' });
}

function narniaToggleScheduleForm() {
    var panel = document.getElementById('sched-form-panel');
    panel.style.display = (panel.style.display === 'none' || !panel.style.display) ? 'block' : 'none';
}

function narniaScheduleResetForm() {
    document.getElementById('sched-edit-id').value = '';
    ['sched-name','sched-desc','sched-cwd','sched-prompt','sched-copilotargs'].forEach(function (i) { document.getElementById(i).value = ''; });
    document.getElementById('sched-skills-list').innerHTML = '';
    document.querySelectorAll('.sched-day').forEach(function (c) { c.checked = false; });
    document.getElementById('sched-dom').value = 1;
    document.getElementById('sched-cadence').value = 'daily';
    narniaCadenceChanged();
    document.getElementById('sched-form-title').textContent = '➕ New scheduled job';
    document.getElementById('sched-copyout').style.display = 'none';
}

async function narniaScheduleAction(id, verb, body) {
    try {
        var resp = await fetch('/api/schedules/' + id + '/' + verb, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : null,
        });
        if (resp.ok) { location.reload(); }
        else { var d = await resp.json().catch(function () { return null; }); alert('Failed: ' + (d || 'HTTP ' + resp.status)); }
    } catch (e) { alert('Error: ' + e.message); }
}

function narniaScheduleEnable(id, enabled) { narniaScheduleAction(id, 'enable', { enabled: enabled }); }
function narniaScheduleRun(id) { if (confirm('Run this job now? It will start a real Copilot session.')) narniaScheduleAction(id, 'run', null); }
async function narniaScheduleDelete(id) {
    if (!confirm('Delete this job? Its scheduled task and generated script will be removed.')) return;
    var resp = await fetch('/api/schedules/' + id, { method: 'DELETE' });
    if (resp.ok) location.reload(); else alert('Delete failed');
}

// btn is the clicked element carrying data-job-name (from a row action), or null (from a health
// badge, which has no name handy) -- either way the id alone is enough to fetch the log.
//
// Polls while the task is confirmed running (per the OS scheduler's live state, not a guess) so a
// user who opens the log for an in-progress run sees it grow in near-real-time instead of a single
// static snapshot with no way to tell running from stuck from failed.
var _narniaScheduleLogPollId = null;
var _narniaScheduleLogViewingJobId = null;

function narniaScheduleStopLogPolling() {
    if (_narniaScheduleLogPollId !== null) { clearTimeout(_narniaScheduleLogPollId); _narniaScheduleLogPollId = null; }
    _narniaScheduleLogViewingJobId = null;
}

async function narniaScheduleViewLog(id, btn) {
    var name = (btn && btn.dataset && btn.dataset.jobName) ? btn.dataset.jobName : null;
    var panel = document.getElementById('sched-log-panel');
    var title = document.getElementById('sched-log-title');
    var meta = document.getElementById('sched-log-meta');
    var content = document.getElementById('sched-log-content');
    title.textContent = name ? ('📄 Log: ' + name) : '📄 Log';
    meta.textContent = 'Loading…';
    content.value = '';
    panel.style.display = 'block';
    panel.scrollIntoView({ behavior: 'smooth', block: 'center' });

    narniaScheduleStopLogPolling();
    _narniaScheduleLogViewingJobId = id;
    await narniaScheduleFetchLogOnce(id, meta, content);
}

async function narniaScheduleFetchLogOnce(id, meta, content) {
    try {
        var resp = await fetch('/api/schedules/' + id + '/log');
        // The user may have switched to viewing a different job's log while this was in flight,
        // or closed the panel (which clears the tracked id) -- either way, drop a stale response.
        if (_narniaScheduleLogViewingJobId !== id) return;
        if (!resp.ok) { meta.textContent = 'Failed to load log: HTTP ' + resp.status; return; }
        var data = await resp.json();
        if (_narniaScheduleLogViewingJobId !== id) return;

        if (data.isRunning) {
            meta.textContent = '● Running — updating every 3s' + (data.found ? '' : ' (starting up…)');
            content.value = data.content || '';
            content.scrollTop = content.scrollHeight;
            _narniaScheduleLogPollId = setTimeout(function () { narniaScheduleFetchLogOnce(id, meta, content); }, 3000);
            return;
        }

        if (!data.found) { meta.textContent = 'This job has never run, so no log exists yet.'; return; }
        meta.textContent = (data.truncated ? 'Showing the most recent portion of ' : '') + data.path;
        content.value = data.content || '';
    } catch (e) {
        if (_narniaScheduleLogViewingJobId !== id) return;
        meta.textContent = 'Error loading log: ' + e.message;
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
            (w.name || ''),
            (w.tabs || [])
                .slice()
                .sort(function (a, b) { return (a.order || 0) - (b.order || 0); })
                .map(function (tab) {
                    return tab.sessionId + ':' + (tab.isFavorite ? '1' : '0');
                })
                .join(',')
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
