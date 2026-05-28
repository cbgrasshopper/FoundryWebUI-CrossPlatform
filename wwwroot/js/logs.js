// logs.js - Logs page: display Foundry Local service logs

let autoScroll = true;

const LOG_TAG_MAP = { ERR: 'error', FTL: 'error', CRT: 'error', WRN: 'warn', INF: 'info', DBG: 'debug', VRB: 'debug' };

const LOG_LINE_RE = /^(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3}\s+[+-]\d{2}:\d{2})\s+\[(\w+)\]\s+(.*)/s;

function parseLogLine(message) {
    const m = String(message).match(LOG_LINE_RE);
    if (m) {
        return { timestamp: m[1], level: m[2], text: m[3] };
    }
    return null;
}

function detectLevel(entry) {
    const explicit = (entry.level || '').toLowerCase();
    if (explicit) return explicit;
    if (!entry.message) return '';
    const m = entry.message.match(/\[(ERR|INF|WRN|DBG|FTL|VRB|CRT)\]/i);
    return m ? (LOG_TAG_MAP[m[1].toUpperCase()] || '') : '';
}

function escapeRegex(str) {
    return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function highlightText(text, searchTerm) {
    if (!searchTerm) return text;
    const escaped = esc(text);
    const regex = new RegExp(`(${escapeRegex(esc(searchTerm))})`, 'gi');
    return escaped.replace(regex, '<mark>$1</mark>');
}

async function loadLogs() {
    const viewer = document.getElementById('log-viewer');
    const showing = document.getElementById('log-showing');

    try {
        const res = await fetch('/api/logs/foundry?lines=500');
        if (!res.ok) {
            viewer.innerHTML = `<div class="text-danger p-3">Error loading logs: HTTP ${res.status}</div>`;
            return;
        }
        const data = await res.json();
        const entries = data.entries || [];

        const badge = document.getElementById('foundry-count');
        if (badge) badge.textContent = entries.length;

        const levelFilter = document.getElementById('log-level-filter').value;
        const searchRaw = document.getElementById('log-search').value;
        const searchText = searchRaw.toLowerCase();

        const filtered = entries.filter(e => {
            if (levelFilter !== 'all') {
                const entryLevel = detectLevel(e);
                const levels = { 'error': 0, 'critical': 0, 'fatal': 0, 'warn': 1, 'warning': 1, 'info': 2, 'information': 2, 'debug': 3, 'trace': 4, 'verbose': 4 };
                const filterLevel = levels[levelFilter] ?? 2;
                const eLevel = levels[entryLevel] ?? 2;
                if (eLevel > filterLevel) return false;
            }
            if (searchText) {
                const text = JSON.stringify(e).toLowerCase();
                if (!text.includes(searchText)) return false;
            }
            return true;
        });

        if (showing) showing.textContent = `Showing ${filtered.length} of ${entries.length} entries`;

        if (filtered.length === 0) {
            viewer.innerHTML = `<div class="text-muted p-3">${entries.length === 0 ? 'No log entries found.' : 'No entries match the current filter.'}</div>`;
            if (data.logDir) {
                viewer.innerHTML += `<div class="text-muted small p-3">Log directory: ${esc(data.logDir)}</div>`;
            }
            return;
        }

        // Group consecutive continuation lines with their parent entry.
        // A continuation line is one that doesn't start with a timestamp
        // (e.g. stack traces, exception details).
        const grouped = [];
        for (const e of filtered) {
            if (LOG_LINE_RE.test(e.message) || grouped.length === 0) {
                grouped.push({ entry: e, continuations: [] });
            } else {
                grouped[grouped.length - 1].continuations.push(e);
            }
        }

        let currentFile = null;
        const parts = [];
        for (const group of grouped) {
            const { entry, continuations } = group;
            const level = detectLevel(entry);
            let cssClass = '';
            if (level === 'error' || level === 'critical' || level === 'fatal') cssClass = 'log-line-error';
            else if (level === 'warn' || level === 'warning') cssClass = 'log-line-warn';
            else if (level === 'info' || level === 'information') cssClass = 'log-line-info';
            else if (level === 'debug' || level === 'trace' || level === 'verbose') cssClass = 'log-line-debug';

            if (entry.file && entry.file !== currentFile) {
                currentFile = entry.file;
                parts.push(`<div class="log-file-header">${esc(entry.file)}</div>`);
            }

            const parsed = parseLogLine(entry.message);
            let timestampHtml = '';
            let messageText = entry.message;
            if (parsed) {
                timestampHtml = `<span class="log-timestamp">${esc(parsed.timestamp)}</span> <span class="log-level-tag">[${esc(parsed.level)}]</span> `;
                messageText = parsed.text;
            }

            const highlighted = highlightText(messageText, searchRaw);
            parts.push(`<div class="${cssClass}">${timestampHtml}${highlighted}</div>`);

            for (const c of continuations) {
                const contLevel = detectLevel(c);
                let contCss = 'log-line-continuation';
                if (contLevel === 'error' || contLevel === 'critical' || contLevel === 'fatal') contCss += ' log-line-error';
                else if (contLevel === 'warn' || contLevel === 'warning') contCss += ' log-line-warn';
                else if (contLevel === 'info' || contLevel === 'information') contCss += ' log-line-info';
                else if (contLevel === 'debug' || contLevel === 'trace' || contLevel === 'verbose') contCss += ' log-line-debug';

                const contHighlighted = highlightText(c.message, searchRaw);
                parts.push(`<div class="${contCss}">${contHighlighted}</div>`);
            }
        }
        const rendered = parts.join('');

        if (data.logDir && data.logDir !== '(not found)') {
            viewer.innerHTML = `<div class="text-muted small" style="opacity:0.6; margin-bottom:8px;">${esc(data.logDir)}</div>` + rendered;
        } else {
            viewer.innerHTML = rendered;
        }

        if (autoScroll) {
            viewer.scrollTop = viewer.scrollHeight;
        }

    } catch (err) {
        viewer.innerHTML = `<div class="text-danger p-3">Error: ${esc(err.message)}</div>`;
    }
}

function esc(text) {
    if (text === null || text === undefined) return '';
    return String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('btn-refresh-logs')?.addEventListener('click', () => loadLogs());

    const autoScrollBtn = document.getElementById('btn-auto-scroll');
    autoScrollBtn?.addEventListener('click', () => {
        autoScroll = !autoScroll;
        if (autoScroll) {
            autoScrollBtn.style.borderColor = 'var(--accent)';
            autoScrollBtn.style.color = 'var(--accent)';
        } else {
            autoScrollBtn.style.borderColor = '';
            autoScrollBtn.style.color = '';
        }
    });

    document.getElementById('log-level-filter')?.addEventListener('change', () => loadLogs());
    let searchTimeout;
    document.getElementById('log-search')?.addEventListener('input', () => {
        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(() => loadLogs(), 300);
    });

    loadLogs();
    setInterval(() => loadLogs(), 10000);
});
