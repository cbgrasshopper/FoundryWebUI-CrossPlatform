// site.js - Global: check Foundry Local status on load and manage reconnect

const STATUS_CACHE_KEY = 'foundrywebui:lastStatus';
const MODELS_CACHE_KEY = 'foundrywebui:lastModels';
const STATUS_CACHE_TTL_MS = 3 * 60 * 1000;          // 3 minutes
const MODELS_CACHE_TTL_MS = 24 * 60 * 60 * 1000;    // 24 hours

const CAPABILITY_MAP = {
    vision:    { label: 'Vision',    css: 'vision',    color: '#58a6ff', title: 'Vision / multimodal image input',              svg: '<svg width="11" height="11" viewBox="0 0 16 16" fill="currentColor"><path d="M8 3.5a4.5 4.5 0 100 9 4.5 4.5 0 000-9zM2 8a6 6 0 1112 0A6 6 0 012 8zm6-2.5a2.5 2.5 0 100 5 2.5 2.5 0 000-5z"/></svg>' },
    tools:     { label: 'Tools',     css: 'tools',     color: '#d29922', title: 'Tool / function calling support',              svg: '<svg width="11" height="11" viewBox="0 0 16 16" fill="currentColor"><path d="M5.433 2.304A4.494 4.494 0 003.5 6c0 1.598.832 3.002 2.09 3.802.518.328.81.88.81 1.478v.72A.75.75 0 007.15 12.75h1.7a.75.75 0 00.75-.75v-.72c0-.598.292-1.15.81-1.478A4.494 4.494 0 0012.5 6a4.5 4.5 0 00-7.067-3.696z"/><path d="M7 14.25a.75.75 0 01.75-.75h.5a.75.75 0 010 1.5h-.5a.75.75 0 01-.75-.75z"/></svg>' },
    reasoning: { label: 'Reasoning', css: 'reasoning', color: '#a371f7', title: 'Chain-of-thought reasoning model',             svg: '<svg width="11" height="11" viewBox="0 0 16 16" fill="currentColor"><path d="M8 0a8 8 0 110 16A8 8 0 018 0zm1.062 4.312a2.5 2.5 0 00-3.458.736.75.75 0 101.26.812 1 1 0 011.988.188c0 .348-.275.634-.648.793a1.75 1.75 0 00-1.102 1.627V9a.75.75 0 001.5 0v-.507a.25.25 0 01.157-.232c.826-.354 1.593-1.107 1.593-2.136a2.5 2.5 0 00-1.29-2.313zM8 11.25a1 1 0 100 2 1 1 0 000-2z"/></svg>' },
    code:      { label: 'Code',      css: 'code',      color: '#3fb950', title: 'Optimized for code generation',                svg: '<svg width="11" height="11" viewBox="0 0 16 16" fill="currentColor"><path d="M4.72 3.22a.75.75 0 011.06 1.06L2.06 8l3.72 3.72a.75.75 0 11-1.06 1.06l-4.25-4.25a.75.75 0 010-1.06l4.25-4.25zm6.56 0a.75.75 0 10-1.06 1.06L13.94 8l-3.72 3.72a.75.75 0 101.06 1.06l4.25-4.25a.75.75 0 000-1.06l-4.25-4.25z"/></svg>' },
    speech:    { label: 'Speech',    css: 'speech',     color: '#f85149', title: 'Speech-to-text / automatic speech recognition', svg: '<svg width="11" height="11" viewBox="0 0 16 16" fill="currentColor"><path d="M8 0a3 3 0 00-3 3v4a3 3 0 006 0V3a3 3 0 00-3-3zM3.5 6.5A.75.75 0 002 6.5v.5a6 6 0 005.25 5.954V15h-1.5a.75.75 0 000 1.5h4.5a.75.75 0 000-1.5h-1.5v-2.046A6 6 0 0014 6.5a.75.75 0 00-1.5 0 4.5 4.5 0 01-9 0z"/></svg>' },
};

const RAM_ICON_SVG = '<svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><path d="M2 4a1 1 0 00-1 1v6a1 1 0 001 1h1v1.5a.5.5 0 001 0V12h1v1.5a.5.5 0 001 0V12h4v1.5a.5.5 0 001 0V12h1v1.5a.5.5 0 001 0V12h1a1 1 0 001-1V5a1 1 0 00-1-1H2zm0 1.5h12v5H2v-5zM3 7v1.5h2V7H3zm3.5 0v1.5h2V7h-2zM10 7v1.5h2V7h-2z"/></svg>';

function renderCapBadges(capabilities) {
    if (!capabilities || capabilities.length === 0) return '<span style="color:var(--text-tertiary);">—</span>';
    return capabilities.map(c => {
        const cap = CAPABILITY_MAP[c];
        if (!cap) return '';
        return `<span class="cap-badge ${cap.css}" title="${cap.title}">${cap.svg} ${cap.label}</span>`;
    }).join('');
}

function renderCapIcons(capabilities) {
    if (!capabilities || capabilities.length === 0) return '';
    return capabilities.map(c => {
        const cap = CAPABILITY_MAP[c];
        if (!cap) return '';
        return `<span class="cap-icon ${cap.css}" title="${cap.title}">${cap.svg}</span>`;
    }).join('');
}

function renderCapTooltip(capabilities) {
    if (!capabilities || capabilities.length === 0) return '';
    const pills = capabilities.map(c => {
        const cap = CAPABILITY_MAP[c];
        if (!cap) return '';
        return `<span class="cap-badge ${cap.css}">${cap.svg} ${cap.label}</span>`;
    }).join('');
    return `<div class="cap-tooltip"><div class="tooltip-caps">${pills}</div></div>`;
}

function renderRamIcon(estimatedRamMb, systemRamMb) {
    if (!estimatedRamMb || !systemRamMb) {
        return `<span class="ram-icon" style="color:var(--text-tertiary);" title="Unknown RAM requirement">${RAM_ICON_SVG}</span>`;
    }
    const ratio = estimatedRamMb / systemRamMb;
    if (ratio <= 0.5) return `<span class="ram-icon" style="color:#3fb950;" title="Comfortable — uses less than 50% of RAM">${RAM_ICON_SVG}</span>`;
    if (ratio <= 0.75) return `<span class="ram-icon" style="color:#d29922;" title="Maybe — uses 50-75% of RAM">${RAM_ICON_SVG}</span>`;
    return `<span class="ram-icon" style="color:#f85149;" title="Model likely too large for available RAM">${RAM_ICON_SVG}</span>`;
}

function loadCachedStatus() {
    try {
        const raw = sessionStorage.getItem(STATUS_CACHE_KEY);
        if (!raw) return null;
        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed.timestamp !== 'number') return null;
        if (Date.now() - parsed.timestamp > STATUS_CACHE_TTL_MS) return null;
        return parsed.statuses;
    } catch { return null; }
}

function saveCachedStatus(statuses) {
    try {
        sessionStorage.setItem(STATUS_CACHE_KEY, JSON.stringify({
            statuses,
            timestamp: Date.now(),
        }));
    } catch { /* storage quota or disabled */ }
}

/**
 * Clears both the status cache and (optionally) the models cache. Callers that
 * discover Foundry Local is unavailable anywhere in the app should invoke this
 * so the next page load doesn't render a stale "Connected" indicator.
 */
function clearProviderCache({ alsoModels = true } = {}) {
    try { sessionStorage.removeItem(STATUS_CACHE_KEY); } catch { }
    if (alsoModels) {
        try { sessionStorage.removeItem(MODELS_CACHE_KEY); } catch { }
    }
}

// Expose for use by per-page scripts (models.js, settings.js, etc.).
window.clearProviderCache = clearProviderCache;

function renderProviderStatus(statuses) {
    const foundry = statuses.find(s => s.provider === 'foundry') || statuses[0];

    const navLight = document.getElementById('foundry-nav-light');
    const navLabel = document.getElementById('foundry-nav-label');
    if (navLight && foundry) {
        navLight.className = `status-dot ${foundry.isAvailable ? 'connected' : 'disconnected'}`;
        if (navLabel) {
            navLabel.title = foundry.isAvailable
                ? `Connected — ${foundry.endpoint || ''}`
                : `Disconnected${foundry.error ? ' — ' + foundry.error : ''}`;
        }
    }

    if (foundry) {
        const indicator = document.getElementById('foundry-status-indicator');
        const endpointDisplay = document.getElementById('foundry-endpoint-display');

        if (indicator) {
            indicator.textContent = foundry.isAvailable ? 'Connected' : 'Disconnected';
            indicator.className = `badge-status ${foundry.isAvailable ? 'badge-success' : 'badge-danger'}`;
        }
        if (endpointDisplay) {
            if (foundry.isAvailable && foundry.endpoint) {
                try {
                    const url = new URL(foundry.endpoint);
                    endpointDisplay.textContent = `port ${url.port || '80'}`;
                } catch {
                    endpointDisplay.textContent = foundry.endpoint;
                }
            } else if (foundry.error) {
                endpointDisplay.textContent = foundry.error;
            } else {
                endpointDisplay.textContent = '';
            }
        }

        const btnStart = document.getElementById('btn-start-foundry');
        if (btnStart) {
            btnStart.style.display = foundry.isAvailable ? 'none' : '';
        }
    }
}

/**
 * Refresh and render Foundry status.
 * @param {{force?: boolean}} options - When `force` is false (default), a recent
 *   cached status from sessionStorage is reused to avoid spamming /api/status on
 *   every page navigation. Reconnect/Start handlers pass `force: true`.
 */
async function checkProviderStatus({ force = false } = {}) {
    if (!force) {
        const cached = loadCachedStatus();
        if (cached) {
            renderProviderStatus(cached);
            return cached;
        }
    }

    try {
        const res = await fetch('/api/status');
        const statuses = await res.json();
        renderProviderStatus(statuses);
        saveCachedStatus(statuses);
        return statuses;
    } catch {
        // Network failure: wipe cached state so next check re-tries cleanly.
        clearProviderCache();
        return [];
    }
}

// Sidebar toggle
document.addEventListener('DOMContentLoaded', () => {
    const sidebar = document.getElementById('sidebar');
    const toggleBtn = document.getElementById('sidebar-toggle');
    if (toggleBtn && sidebar) {
        function syncToggleBtn() {
            const collapsed = sidebar.classList.contains('collapsed');
            toggleBtn.classList.toggle('collapsed', collapsed);
            toggleBtn.title = collapsed ? 'Expand sidebar' : 'Collapse sidebar';
        }
        if (localStorage.getItem('sidebar-collapsed') === 'true') {
            sidebar.classList.add('collapsed');
        }
        syncToggleBtn();
        toggleBtn.addEventListener('click', () => {
            sidebar.classList.toggle('collapsed');
            localStorage.setItem('sidebar-collapsed', sidebar.classList.contains('collapsed'));
            syncToggleBtn();
        });
    }
});

// Reconnect handler
document.addEventListener('DOMContentLoaded', () => {
    const btnReconnect = document.getElementById('btn-reconnect-foundry');
    if (btnReconnect) {
        btnReconnect.addEventListener('click', async () => {
            const indicator = document.getElementById('foundry-status-indicator');
            const endpointDisplay = document.getElementById('foundry-endpoint-display');

            btnReconnect.disabled = true;
            btnReconnect.textContent = 'Reconnecting...';
            if (indicator) {
                indicator.textContent = 'Reconnecting';
                indicator.className = 'badge-status badge-warning';
            }
            if (endpointDisplay) endpointDisplay.textContent = '';

            try {
                const res = await fetch('/api/reconnect?provider=foundry', { method: 'POST' });
                const status = await res.json();

                if (indicator) {
                    indicator.textContent = status.isAvailable ? 'Connected' : 'Disconnected';
                    indicator.className = `badge-status ${status.isAvailable ? 'badge-success' : 'badge-danger'}`;
                }
                if (endpointDisplay) {
                    if (status.isAvailable && status.endpoint) {
                        try {
                            const url = new URL(status.endpoint);
                            endpointDisplay.textContent = `port ${url.port || '80'}`;
                        } catch {
                            endpointDisplay.textContent = status.endpoint;
                        }
                    } else if (status.error) {
                        endpointDisplay.textContent = status.error;
                    }
                }

                // Refresh navbar badge and model list
                await checkProviderStatus({ force: true });
                if (typeof loadModels === 'function') loadModels({ force: true });

            } catch (err) {
                if (indicator) {
                    indicator.textContent = 'Error';
                    indicator.className = 'badge-status badge-danger';
                }
                if (endpointDisplay) endpointDisplay.textContent = err.message;
            }

            btnReconnect.disabled = false;
            btnReconnect.textContent = 'Reconnect';
        });
    }
});

// Start Foundry handler
document.addEventListener('DOMContentLoaded', () => {
    const btnStart = document.getElementById('btn-start-foundry');
    if (!btnStart) return;

    btnStart.addEventListener('click', async () => {
        const indicator = document.getElementById('foundry-status-indicator');
        const endpointDisplay = document.getElementById('foundry-endpoint-display');

        btnStart.disabled = true;
        btnStart.textContent = 'Starting...';
        if (indicator) {
            indicator.textContent = 'Starting';
            indicator.className = 'badge-status badge-warning';
        }
        if (endpointDisplay) endpointDisplay.textContent = '';

        // Poll /api/status concurrently so the indicator updates as soon as
        // Foundry comes up, even though POST may still be in-flight.
        let stopPolling = false;
        let becameAvailable = false;
        const poller = (async () => {
            while (!stopPolling) {
                try {
                    const statuses = await checkProviderStatus({ force: true });
                    const foundry = (statuses || []).find(s => s.provider === 'foundry');
                    if (foundry?.isAvailable) {
                        becameAvailable = true;
                        if (typeof loadModels === 'function') loadModels({ force: true });
                        return;
                    }
                } catch { /* swallow; will retry */ }
                await new Promise(r => setTimeout(r, 1500));
            }
        })();

        try {
            const res = await fetch('/api/foundry/start', { method: 'POST' });
            const payload = await res.json().catch(() => ({}));

            if (!res.ok && endpointDisplay) {
                endpointDisplay.textContent = payload.hint
                    ? `${payload.error} ${payload.hint}`
                    : payload.error || `HTTP ${res.status}`;
            }
        } catch (err) {
            if (endpointDisplay) endpointDisplay.textContent = err.message;
        } finally {
            stopPolling = true;
            await poller;
            await checkProviderStatus({ force: true });
            if (becameAvailable && typeof loadModels === 'function') loadModels({ force: true });
            btnStart.disabled = false;
            btnStart.textContent = 'Start Foundry';
        }
    });
});

// Theme toggle
document.addEventListener('DOMContentLoaded', () => {
    const btn = document.getElementById('theme-toggle');
    if (!btn) return;
    btn.addEventListener('click', () => {
        const current = document.documentElement.getAttribute('data-theme') || 'dark';
        const next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        document.documentElement.setAttribute('data-bs-theme', next);
        localStorage.setItem('theme', next);
    });
});

// Initial check on page load
checkProviderStatus();
