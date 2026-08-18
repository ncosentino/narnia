(function () {
    'use strict';

    var queue = [];
    var active = null;

    function elements() {
        return {
            dialog: document.getElementById('narnia-dialog'),
            form: document.getElementById('narnia-dialog-form'),
            title: document.getElementById('narnia-dialog-title'),
            message: document.getElementById('narnia-dialog-message'),
            inputGroup: document.getElementById('narnia-dialog-input-group'),
            inputLabel: document.getElementById('narnia-dialog-input-label'),
            input: document.getElementById('narnia-dialog-input'),
            cancel: document.getElementById('narnia-dialog-cancel'),
            confirm: document.getElementById('narnia-dialog-confirm'),
            close: document.getElementById('narnia-dialog-close'),
        };
    }

    function request(options) {
        return new Promise(function (resolve) {
            queue.push({ options: options, resolve: resolve });
            showNext();
        });
    }

    function showNext() {
        if (active || queue.length === 0) return;

        var host = elements();
        if (!host.dialog) {
            throw new Error('The Narnia dialog host is unavailable.');
        }

        active = queue.shift();
        var options = active.options;
        host.title.textContent = options.title;
        host.message.textContent = options.message;
        host.inputGroup.hidden = options.kind !== 'prompt';
        host.inputLabel.textContent = options.inputLabel;
        host.input.value = options.defaultValue;
        host.cancel.hidden = options.kind === 'alert';
        host.cancel.textContent = options.cancelLabel;
        host.confirm.textContent = options.confirmLabel;
        host.confirm.classList.toggle('btn-bulk-archive', options.danger === true);
        host.confirm.classList.toggle('launch-btn', options.danger !== true);
        host.dialog.dataset.kind = options.kind;
        host.dialog.showModal();

        queueMicrotask(function () {
            if (options.kind === 'prompt') {
                host.input.focus();
                host.input.select();
            } else {
                host.confirm.focus();
            }
        });
    }

    function settle(accepted) {
        if (!active) return;

        var host = elements();
        var current = active;
        active = null;
        if (host.dialog.open) host.dialog.close();

        if (current.options.kind === 'prompt') {
            current.resolve(accepted ? host.input.value : null);
        } else if (current.options.kind === 'confirm') {
            current.resolve(accepted);
        } else {
            current.resolve();
        }

        queueMicrotask(showNext);
    }

    function normalize(kind, message, defaultValue, options) {
        options = options || {};
        return {
            kind: kind,
            message: String(message ?? ''),
            defaultValue: defaultValue ?? '',
            title: options.title || (
                kind === 'alert'
                    ? 'Narnia'
                    : kind === 'confirm'
                        ? 'Confirm action'
                        : 'Enter a value'),
            inputLabel: options.inputLabel || 'Value',
            confirmLabel: options.confirmLabel || (
                kind === 'alert' ? 'Close' : kind === 'prompt' ? 'Save' : 'Continue'),
            cancelLabel: options.cancelLabel || 'Cancel',
            danger: options.danger === true,
        };
    }

    function initialize() {
        var host = elements();
        if (!host.dialog || host.dialog.dataset.initialized === 'true') return;

        host.dialog.dataset.initialized = 'true';
        host.form.addEventListener('submit', function (event) {
            event.preventDefault();
            settle(true);
        });
        host.cancel.addEventListener('click', function () {
            settle(false);
        });
        host.close.addEventListener('click', function () {
            settle(active?.options.kind === 'alert');
        });
        host.dialog.addEventListener('cancel', function (event) {
            event.preventDefault();
            settle(active?.options.kind === 'alert');
        });
    }

    window.narniaDialog = {
        alert: function (message, options) {
            initialize();
            return request(normalize('alert', message, '', options));
        },
        confirm: function (message, options) {
            initialize();
            return request(normalize('confirm', message, '', options));
        },
        prompt: function (message, defaultValue, options) {
            initialize();
            return request(normalize('prompt', message, defaultValue, options));
        },
    };
})();
