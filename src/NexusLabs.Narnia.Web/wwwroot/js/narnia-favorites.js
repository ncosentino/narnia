async function narniaSetFavorite(sessionId, favorite, button) {
    const originalText = button.textContent;
    button.disabled = true;

    try {
        const response = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/favorite`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ favorite }),
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        window.location.reload();
    } catch (error) {
        button.disabled = false;
        button.textContent = originalText;
        alert('Error updating favorite status: ' + error.message);
    }
}
