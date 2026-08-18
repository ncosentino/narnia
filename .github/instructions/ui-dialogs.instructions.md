---
applyTo: "src/NexusLabs.Narnia.Web/**"
description: User-facing dialog and confirmation rules for Narnia's web UI.
---

# In-app dialogs

Browser-native `alert`, `confirm`, and `prompt` dialogs are forbidden. They ignore Narnia's theme,
provide poor accessibility and context, block the browser's event loop, and cannot express the
action clearly.

## Use the shared dialog host

JavaScript interactions use the asynchronous `narniaDialog` API from
`wwwroot/js/narnia-dialog.js`:

- `await narniaDialog.alert(message, options)`
- `await narniaDialog.confirm(message, options)`
- `await narniaDialog.prompt(message, defaultValue, options)`

Callers must remain asynchronous and await the result before continuing. Do not wrap or alias the
browser-native functions.

## Make the action explicit

Confirmations and prompts should provide a workflow-specific title and confirm-button label.
Destructive actions set `danger: true` and name the destructive verb, such as **Delete** or
**Remove sessions**. Error details may use the shared alert variant.

The shared host owns focus, Escape handling, keyboard submission, queuing, backdrop behavior, and
theme-compatible styling. Add capabilities there instead of creating page-specific modal
implementations.
