(function () {
    const STORAGE_PREFIX = 'narnia.tableWidths.';
    const DEFAULT_MIN_WIDTH = 72;
    const MAX_WIDTH = 1200;

    function clamp(value, minimum) {
        return Math.max(minimum, Math.min(MAX_WIDTH, Math.round(value)));
    }

    function columnKey(header, index) {
        const explicit = header.getAttribute('data-column-key');
        if (explicit) {
            return explicit;
        }

        const text = (header.textContent || '')
            .replace(/[▲▼]/g, '')
            .trim()
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-|-$/g, '');
        return text || `column-${index}`;
    }

    function readWidths(tableId) {
        try {
            const parsed = JSON.parse(localStorage.getItem(STORAGE_PREFIX + tableId) || '{}');
            if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
                return parsed;
            }

            localStorage.removeItem(STORAGE_PREFIX + tableId);
            return {};
        } catch (error) {
            console.warn(`Narnia: could not read saved column widths for ${tableId}.`, error);
            return {};
        }
    }

    function writeWidths(tableId, widths) {
        try {
            localStorage.setItem(STORAGE_PREFIX + tableId, JSON.stringify(widths));
        } catch (error) {
            console.warn(`Narnia: could not save column widths for ${tableId}.`, error);
        }
    }

    function ensureScrollContainer(table) {
        if (table.parentElement?.classList.contains('narnia-table-scroll')) {
            return table.parentElement;
        }

        const container = document.createElement('div');
        container.className = 'narnia-table-scroll';
        table.parentNode.insertBefore(container, table);
        container.appendChild(table);
        return container;
    }

    function initTable(table) {
        if (table.dataset.narniaResizeInitialized === 'true') {
            return;
        }

        const tableId = table.getAttribute('data-resizable-table');
        const headerRow = table.tHead?.rows[0];
        if (!tableId || !headerRow || headerRow.cells.length < 2) {
            return;
        }

        if (!table.id) {
            table.id = `narnia-table-${tableId}`;
        }

        ensureScrollContainer(table);
        const headers = Array.from(headerRow.cells);
        const naturalWidths = headers.map(header =>
            Math.max(1, Math.ceil(header.getBoundingClientRect().width)));
        const savedWidths = readWidths(tableId);
        const keys = headers.map(columnKey);
        const minimums = headers.map(header => {
            const configured = Number.parseInt(header.getAttribute('data-min-width') || '', 10);
            if (Number.isFinite(configured)) {
                return configured;
            }

            return header.classList.contains('col-select') ? 40 : DEFAULT_MIN_WIDTH;
        });
        const widths = naturalWidths.map((width, index) => {
            const stored = savedWidths[keys[index]];
            return clamp(
                typeof stored === 'number' && Number.isFinite(stored) ? stored : width,
                minimums[index]);
        });

        const colgroup = document.createElement('colgroup');
        const columns = headers.map((_, index) => {
            const column = document.createElement('col');
            column.style.width = `${widths[index]}px`;
            colgroup.appendChild(column);
            return column;
        });
        table.insertBefore(colgroup, table.firstChild);

        function updateTableWidth() {
            const total = widths.reduce((sum, width) => sum + width, 0);
            table.style.width = `${total}px`;
        }

        function resizeColumn(index, width) {
            widths[index] = clamp(width, minimums[index]);
            columns[index].style.width = `${widths[index]}px`;
            const handle = headers[index].querySelector('.narnia-column-resizer');
            if (handle) {
                handle.setAttribute('aria-valuenow', String(widths[index]));
            }
            updateTableWidth();
        }

        function persistColumn(index) {
            const currentWidths = readWidths(tableId);
            currentWidths[keys[index]] = widths[index];
            writeWidths(tableId, currentWidths);
        }

        function resetColumn(index) {
            const currentWidths = readWidths(tableId);
            delete currentWidths[keys[index]];
            resizeColumn(index, naturalWidths[index]);
            writeWidths(tableId, currentWidths);
        }

        headers.forEach((header, index) => {
            if (header.hasAttribute('data-no-resize') || header.querySelector('input[type="checkbox"]')) {
                return;
            }

            header.setAttribute('data-narnia-resizable', 'true');
            const handle = document.createElement('span');
            handle.className = 'narnia-column-resizer';
            handle.setAttribute('role', 'separator');
            handle.setAttribute('aria-orientation', 'vertical');
            handle.setAttribute('aria-label', `Resize ${header.textContent.trim() || `column ${index + 1}`}`);
            handle.setAttribute('aria-controls', table.id);
            handle.setAttribute('aria-valuemin', String(minimums[index]));
            handle.setAttribute('aria-valuemax', String(MAX_WIDTH));
            handle.setAttribute('aria-valuenow', String(widths[index]));
            handle.setAttribute('aria-keyshortcuts', 'ArrowLeft ArrowRight Home Escape');
            handle.title = 'Drag to resize. Use arrow keys to resize, Home for minimum, or Escape to reset.';
            handle.tabIndex = 0;

            handle.addEventListener('pointerdown', event => {
                event.preventDefault();
                event.stopPropagation();
                const pointerId = event.pointerId;
                const startX = event.clientX;
                const startWidth = header.getBoundingClientRect().width;
                let finished = false;
                document.body.classList.add('narnia-resizing-columns');
                handle.setPointerCapture(pointerId);

                function onMove(moveEvent) {
                    if (moveEvent.pointerId !== pointerId) {
                        return;
                    }
                    resizeColumn(index, startWidth + moveEvent.clientX - startX);
                }

                function cleanup(persist) {
                    if (finished) {
                        return;
                    }

                    finished = true;
                    document.removeEventListener('pointermove', onMove);
                    document.removeEventListener('pointerup', onUp);
                    document.removeEventListener('pointercancel', onCancel);
                    handle.removeEventListener('lostpointercapture', onLostCapture);
                    window.removeEventListener('blur', onWindowBlur);
                    document.body.classList.remove('narnia-resizing-columns');
                    if (handle.hasPointerCapture(pointerId)) {
                        handle.releasePointerCapture(pointerId);
                    }
                    if (persist) {
                        persistColumn(index);
                    }
                }

                function onUp(upEvent) {
                    if (upEvent.pointerId === pointerId) {
                        cleanup(true);
                    }
                }

                function cancelResize() {
                    resizeColumn(index, startWidth);
                    cleanup(false);
                }

                function onCancel(cancelEvent) {
                    if (cancelEvent.pointerId === pointerId) {
                        cancelResize();
                    }
                }

                function onLostCapture(lostEvent) {
                    if (lostEvent.pointerId === pointerId) {
                        cancelResize();
                    }
                }

                function onWindowBlur() {
                    cancelResize();
                }

                document.addEventListener('pointermove', onMove);
                document.addEventListener('pointerup', onUp);
                document.addEventListener('pointercancel', onCancel);
                handle.addEventListener('lostpointercapture', onLostCapture);
                window.addEventListener('blur', onWindowBlur);
            });

            handle.addEventListener('keydown', event => {
                const step = event.shiftKey ? 40 : 16;
                if (event.key === 'ArrowLeft') {
                    event.preventDefault();
                    resizeColumn(index, header.getBoundingClientRect().width - step);
                    persistColumn(index);
                } else if (event.key === 'ArrowRight') {
                    event.preventDefault();
                    resizeColumn(index, header.getBoundingClientRect().width + step);
                    persistColumn(index);
                } else if (event.key === 'Home') {
                    event.preventDefault();
                    resizeColumn(index, minimums[index]);
                    persistColumn(index);
                } else if (event.key === 'Escape') {
                    event.preventDefault();
                    resetColumn(index);
                }
            });

            handle.addEventListener('dblclick', event => {
                event.preventDefault();
                event.stopPropagation();
                resetColumn(index);
            });

            header.appendChild(handle);
        });

        updateTableWidth();
        table.dataset.narniaResizeInitialized = 'true';
    }

    function initResizableTables() {
        document.querySelectorAll('table[data-resizable-table]').forEach(initTable);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initResizableTables);
    } else {
        initResizableTables();
    }
})();
