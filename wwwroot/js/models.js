// models.js - Model management logic (Foundry Local only)
const modelsTableBody = document.getElementById('models-table-body');
const btnRefresh = document.getElementById('btn-refresh');
const btnDownloadSelected = document.getElementById('btn-download-selected');
const selectedCountSpan = document.getElementById('selected-count');
const selectAllCheckbox = document.getElementById('select-all');
const downloadProgress = document.getElementById('download-progress');
const downloadBar = document.getElementById('download-bar');
const downloadStatus = document.getElementById('download-status');
const downloadModelName = document.getElementById('download-model-name');

let allModels = [];
let systemRamMb = null;
let currentSort = { key: 'status', dir: 'asc' };

function formatSize(bytes) {
    if (!bytes) return '—';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(1)}&nbsp;GB`;
    const mb = bytes / (1024 * 1024);
    return `${mb.toFixed(0)}&nbsp;MB`;
}

function formatContext(tokens) {
    if (!tokens) return '—';
    if (tokens >= 1048576) return `${(tokens / 1048576).toFixed(0)}M`;
    if (tokens >= 1024) return `${(tokens / 1024).toFixed(0)}K`;
    return tokens.toLocaleString();
}

function formatRam(mb) {
    if (!mb) return '—';
    if (mb >= 1024) return `${(mb / 1024).toFixed(1)}&nbsp;GB`;
    return `${Math.round(mb)}&nbsp;MB`;
}

function canRunBadge(estimatedRamMb) {
    if (!estimatedRamMb || !systemRamMb) return '<span class="badge-status badge-muted" title="Unknown">?</span>';
    const ratio = estimatedRamMb / systemRamMb;
    if (ratio <= 0.5) return '<span class="badge-status badge-success" title="Comfortable — uses less than 50% of RAM">Yes</span>';
    if (ratio <= 0.75) return '<span class="badge-status badge-warning" title="Maybe — uses 50-75% of RAM">Maybe</span>';
    return '<span class="badge-status badge-danger" title="Model likely too large for available RAM">No</span>';
}

function statusBadge(status) {
    const map = {
        'loaded': 'badge-success',
        'downloaded': 'badge-info',
        'available': 'badge-muted'
    };
    const labels = {
        'loaded': 'Loaded',
        'downloaded': 'Downloaded',
        'available': 'Available'
    };
    return `<span class="badge-status ${map[status] || 'badge-muted'}">${labels[status] || status || 'unknown'}</span>`;
}

// MODELS_CACHE_KEY and MODELS_CACHE_TTL_MS are defined globally in site.js

function loadModelsFromCache() {
    try {
        const raw = sessionStorage.getItem(MODELS_CACHE_KEY);
        if (!raw) return null;
        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed.timestamp !== 'number') return null;
        if (Date.now() - parsed.timestamp > MODELS_CACHE_TTL_MS) return null;
        return parsed;
    } catch { return null; }
}

function saveModelsToCache(models, sysRamMb) {
    try {
        sessionStorage.setItem(MODELS_CACHE_KEY, JSON.stringify({
            models,
            systemRamMb: sysRamMb,
            timestamp: Date.now(),
        }));
    } catch { /* quota / disabled */ }
}

async function loadModels({ force = false } = {}) {
    if (!force) {
        const cached = loadModelsFromCache();
        if (cached && Array.isArray(cached.models) && cached.models.length > 0) {
            allModels = cached.models;
            systemRamMb = cached.systemRamMb ?? systemRamMb;
            renderModels();
            return;
        }
    }

    try {
        // Fetch system info and models in parallel
        const [sysRes, modelsRes] = await Promise.all([
            fetch('/api/system-info'),
            fetch('/api/models?provider=foundry')
        ]);
        if (sysRes.ok) {
            const sysInfo = await sysRes.json();
            systemRamMb = sysInfo.totalRamMb;
        }
        allModels = await modelsRes.json();
        renderModels();

        if (Array.isArray(allModels) && allModels.length > 0) {
            saveModelsToCache(allModels, systemRamMb);
        } else {
            // Empty result suggests Foundry Local is unreachable; drop any
            // stale "Connected" cached status so the navbar re-checks.
            window.clearProviderCache?.();
        }
    } catch (err) {
        modelsTableBody.innerHTML = `<tr><td colspan="10" class="text-center py-4" style="color: var(--red);">Error loading models: ${err.message}</td></tr>`;
        window.clearProviderCache?.();
    }
}

function getSortValue(m, key) {
    switch (key) {
        case 'name': return (m.name || m.id || '').toLowerCase();
        case 'status': return { 'loaded': 0, 'downloaded': 1, 'available': 2 }[m.status] ?? 3;
        case 'size': return m.size || 0;
        case 'ram': return m.estimatedRamMb || 0;
        case 'canRun': {
            if (!m.estimatedRamMb || !systemRamMb) return 3;
            const r = m.estimatedRamMb / systemRamMb;
            return r <= 0.5 ? 0 : r <= 0.75 ? 1 : 2;
        }
        case 'device': return (m.parameterSize || '').toLowerCase();
        case 'context': return m.contextWindow || 0;
        default: return 0;
    }
}

function sortModels(models) {
    const { key, dir } = currentSort;
    const mult = dir === 'asc' ? 1 : -1;
    return [...models].sort((a, b) => {
        const va = getSortValue(a, key);
        const vb = getSortValue(b, key);
        if (va < vb) return -1 * mult;
        if (va > vb) return 1 * mult;
        return 0;
    });
}

function updateSortIndicators() {
    document.querySelectorAll('th.sortable .sort-icon').forEach(icon => { icon.textContent = ''; });
    const active = document.querySelector(`th.sortable[data-sort="${currentSort.key}"] .sort-icon`);
    if (active) active.textContent = currentSort.dir === 'asc' ? ' ▲' : ' ▼';
}

function renderModels() {
    if (allModels.length === 0) {
        modelsTableBody.innerHTML = '<tr><td colspan="10" class="text-center py-4" style="color: var(--text-tertiary);">No models found. Check Foundry Local connection.</td></tr>';
        return;
    }

    const sorted = sortModels(allModels);
    updateSortIndicators();

    const NON_CHAT_FAMILIES = ['automatic-speech-recognition'];

    modelsTableBody.innerHTML = sorted.map(m => {
        const isAvailable = m.status === 'available';
        const isChatCapable = !NON_CHAT_FAMILIES.includes((m.family || '').toLowerCase());
        const checkboxId = `chk-${(m.id || '').replace(/[^a-zA-Z0-9]/g, '-')}`;
        return `
        <tr>
            <td>
                ${isAvailable
                    ? `<input type="checkbox" class="check-dark model-checkbox" data-model-id="${m.id}" id="${checkboxId}" />`
                    : ''}
            </td>
            <td>
                <strong>${m.name || m.id}</strong>
                ${m.description ? `<br><small class="text-tertiary" style="font-size: 0.78rem;">${m.description}</small>` : ''}
                ${m.family ? `<br><span class="badge-status badge-muted" style="margin-top: 2px;">${m.family}</span>` : ''}
            </td>
            <td>${statusBadge(m.status)}</td>
            <td><div class="caps-cell">${renderCapBadges(m.capabilities)}</div></td>
            <td class="text-mono">${formatSize(m.size)}</td>
            <td class="text-mono">${formatRam(m.estimatedRamMb)}</td>
            <td>${canRunBadge(m.estimatedRamMb)}</td>
            <td class="text-mono">${m.parameterSize || '—'}</td>
            <td class="text-mono">${formatContext(m.contextWindow)}</td>
            <td>
                <div class="d-flex gap-sm">
                ${isChatCapable ? `<button class="btn-ghost" onclick="openChat('${m.id}')">Chat</button>` : ''}
                ${isAvailable
                    ? `<button class="btn-ghost" onclick="downloadModel('${m.id}')">Download</button>`
                    : `<button class="btn-danger-ghost" onclick="deleteModel('${m.id}')">Remove</button>`}
                </div>
            </td>
        </tr>`;
    }).join('');

    // Attach checkbox listeners
    document.querySelectorAll('.model-checkbox').forEach(cb => {
        cb.addEventListener('change', updateSelectedCount);
    });
    updateSelectedCount();
}

function getSelectedModelIds() {
    return Array.from(document.querySelectorAll('.model-checkbox:checked')).map(cb => cb.dataset.modelId);
}

function updateSelectedCount() {
    const count = getSelectedModelIds().length;
    selectedCountSpan.textContent = count;
    btnDownloadSelected.disabled = count === 0;

    // Update select-all state
    const allCheckboxes = document.querySelectorAll('.model-checkbox');
    if (allCheckboxes.length === 0) {
        selectAllCheckbox.checked = false;
        selectAllCheckbox.indeterminate = false;
    } else if (count === allCheckboxes.length) {
        selectAllCheckbox.checked = true;
        selectAllCheckbox.indeterminate = false;
    } else if (count > 0) {
        selectAllCheckbox.checked = false;
        selectAllCheckbox.indeterminate = true;
    } else {
        selectAllCheckbox.checked = false;
        selectAllCheckbox.indeterminate = false;
    }
}

// Select all toggle
selectAllCheckbox.addEventListener('change', () => {
    const checked = selectAllCheckbox.checked;
    document.querySelectorAll('.model-checkbox').forEach(cb => { cb.checked = checked; });
    updateSelectedCount();
});

// Download a single model
async function downloadModel(modelId) {
    await startDownload(modelId);
}

// Download selected models sequentially
async function downloadSelected() {
    const ids = getSelectedModelIds();
    if (ids.length === 0) return;

    btnDownloadSelected.disabled = true;

    for (let i = 0; i < ids.length; i++) {
        downloadModelName.textContent = `Downloading ${ids[i]} (${i + 1} of ${ids.length})...`;
        await startDownload(ids[i]);
    }

    btnDownloadSelected.disabled = false;
    updateSelectedCount();
}

async function startDownload(modelId) {
    downloadBar.style.transition = 'none';
    downloadBar.style.width = '0%';
    downloadBar.className = 'progress-fill';
    downloadBar.offsetWidth;
    downloadBar.style.transition = '';
    downloadProgress.classList.remove('d-none');
    downloadProgress.scrollIntoView({ behavior: 'smooth', block: 'start' });
    downloadStatus.textContent = 'Connecting to Foundry Local...';
    downloadModelName.textContent = `Downloading ${modelId}...`;
    let failed = false;

    const dlBtn = document.querySelector(`.btn-ghost[onclick="downloadModel('${modelId}')"]`);
    if (dlBtn) {
        dlBtn.disabled = true;
        dlBtn.dataset.origText = dlBtn.textContent;
        dlBtn.innerHTML = '<svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" style="animation: spin 1s linear infinite;"><path d="M8 0a8 8 0 11-4.947 1.703.75.75 0 01.926-1.18A6.5 6.5 0 108 1.5V.25A.25.25 0 018.25 0H8z"/></svg> Downloading...';
    }

    return new Promise(async (resolve) => {
        try {
            const res = await fetch('/api/models/download', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ modelId, provider: 'foundry' })
            });

            const reader = res.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                buffer += decoder.decode(value, { stream: true });
                const lines = buffer.split('\n');
                buffer = lines.pop() || '';

                for (const line of lines) {
                    if (line.startsWith('data: ')) {
                        try {
                            const data = JSON.parse(line.substring(6));
                            console.log('Download SSE:', data);

                            if (data.percent != null && data.percent > 0) {
                                downloadBar.style.width = `${data.percent}%`;
                                if (data.percent >= 100) {
                                    downloadBar.className = 'progress-fill success';
                                }
                            }

                            if (data.status && data.status.startsWith('error')) {
                                failed = true;
                                downloadBar.style.width = '100%';
                                downloadBar.className = 'progress-fill error';
                                downloadStatus.textContent = `Failed: ${data.status}`;
                            } else if (data.status === 'complete' || data.status === 'success') {
                                downloadBar.style.width = '100%';
                                downloadBar.className = 'progress-fill success';
                                downloadStatus.textContent = `${modelId} downloaded successfully`;
                            } else if (data.status) {
                                downloadStatus.textContent = data.status;
                            }
                        } catch (e) { console.warn('Parse error:', line, e); }
                    }
                }
            }
        } catch (err) {
            failed = true;
            downloadStatus.textContent = `Error: ${err.message}`;
            downloadBar.className = 'progress-fill error';
        }

        await new Promise(r => setTimeout(r, 1000));
        await loadModels({ force: true });
        if (!failed) {
            downloadProgress.classList.add('d-none');
        }
        resolve();
    });
}

// Delete a model
function showConfirm(message) {
    return new Promise(resolve => {
        const overlay = document.getElementById('confirm-modal-overlay');
        const text = document.getElementById('confirm-modal-text');
        const btnOk = document.getElementById('confirm-modal-ok');
        const btnCancel = document.getElementById('confirm-modal-cancel');
        text.textContent = message;
        overlay.classList.remove('d-none');

        function cleanup(result) {
            overlay.classList.add('d-none');
            btnOk.removeEventListener('click', onOk);
            btnCancel.removeEventListener('click', onCancel);
            resolve(result);
        }
        function onOk() { cleanup(true); }
        function onCancel() { cleanup(false); }
        btnOk.addEventListener('click', onOk);
        btnCancel.addEventListener('click', onCancel);
    });
}

async function deleteModel(modelId) {
    const confirmed = await showConfirm(`Remove "${modelId}"? This will delete the downloaded model files.`);
    if (!confirmed) return;

    const rmBtn = document.querySelector(`.btn-danger-ghost[onclick="deleteModel('${modelId}')"]`);
    if (rmBtn) {
        rmBtn.disabled = true;
        rmBtn.innerHTML = '<svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" style="animation: spin 1s linear infinite;"><path d="M8 0a8 8 0 11-4.947 1.703.75.75 0 01.926-1.18A6.5 6.5 0 108 1.5V.25A.25.25 0 018.25 0H8z"/></svg> Removing...';
    }

    try {
        const res = await fetch(`/api/models/${encodeURIComponent(modelId)}`, { method: 'DELETE' });
        const data = await res.json();
        if (!res.ok) {
            await showConfirm(data.error || 'Failed to remove model');
        }
    } catch (err) {
        await showConfirm(`Error: ${err.message}`);
    }
    await loadModels({ force: true });
}

// Event listeners
btnRefresh.addEventListener('click', () => loadModels({ force: true }));
btnDownloadSelected.addEventListener('click', downloadSelected);

// Column sort handlers
document.querySelectorAll('th.sortable').forEach(th => {
    th.addEventListener('click', () => {
        const key = th.dataset.sort;
        if (currentSort.key === key) {
            currentSort.dir = currentSort.dir === 'asc' ? 'desc' : 'asc';
        } else {
            currentSort.key = key;
            currentSort.dir = 'asc';
        }
        renderModels();
    });
});

function openChat(modelId) {
    window.location.href = `/?selectModel=${encodeURIComponent(modelId)}`;
}

// Make functions available globally for inline onclick
window.downloadModel = downloadModel;
window.deleteModel = deleteModel;
window.openChat = openChat;

// Init
loadModels();
