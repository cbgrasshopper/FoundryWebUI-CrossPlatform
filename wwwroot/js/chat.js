// chat.js - Chat interface logic
const chatMessages = document.getElementById('chat-messages');
const chatInput = document.getElementById('chat-input');
const btnSend = document.getElementById('btn-send');
const btnStop = document.getElementById('btn-stop');
const btnNewChat = document.getElementById('btn-new-chat');
const promptSelect = document.getElementById('prompt-select');
const sendText = document.getElementById('send-text');
const sendSpinner = document.getElementById('send-spinner');
const showThinkingToggle = document.getElementById('show-thinking');
const maxTokensSlider = document.getElementById('max-tokens-slider');
const maxTokensValue = document.getElementById('max-tokens-value');

const CHAT_STATE_KEY = 'foundrywebui:chatState';

let conversation = [];
let abortController = null;
let modelMaxTokens = {};

function saveChatState() {
    if (!selectedModel.id && conversation.length === 0) return;
    try {
        const existing = sessionStorage.getItem(CHAT_STATE_KEY);
        const parsed = existing ? JSON.parse(existing) : {};
        const modelId = selectedModel.id || parsed.selectedModelId || '';
        const provider = selectedModel.provider || parsed.selectedProvider || 'foundry';
        sessionStorage.setItem(CHAT_STATE_KEY, JSON.stringify({
            conversation,
            selectedModelId: modelId,
            selectedProvider: provider,
            maxTokens: maxTokensSlider ? parseInt(maxTokensSlider.value) : 2048,
            thinking: showThinkingToggle ? showThinkingToggle.checked : false,
            systemPromptId: promptSelect ? promptSelect.value : '',
        }));
    } catch { /* quota */ }
}

function loadChatState() {
    try {
        const raw = sessionStorage.getItem(CHAT_STATE_KEY);
        if (!raw) return null;
        return JSON.parse(raw);
    } catch { return null; }
}

// Max tokens slider display
if (maxTokensSlider) {
    maxTokensSlider.addEventListener('input', () => {
        maxTokensValue.textContent = maxTokensSlider.value;
    });
}

// ── Custom Model Dropdown ─────────────────────────────
const modelSelectEl = document.getElementById('model-select');
const triggerEl = modelSelectEl.querySelector('.custom-select-trigger');
const triggerText = triggerEl.querySelector('.trigger-text');
const optionsEl = modelSelectEl.querySelector('.custom-select-options');

let selectedModel = { id: '', provider: 'foundry' };
let previousSelectedModel = { id: '', provider: 'foundry' };
let highlightedIndex = -1;
let dropdownModels = [];
let isDownloading = false;
const NON_CHAT_FAMILIES = ['automatic-speech-recognition'];

const escHtml = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

function formatSizeShort(bytes) {
    if (!bytes) return '';
    const gb = bytes / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(1)}&nbsp;GB`;
    const mb = bytes / (1024 * 1024);
    return `${Math.round(mb)}&nbsp;MB`;
}

function openDropdown() {
    modelSelectEl.classList.add('open');
    highlightedIndex = dropdownModels.findIndex(m => m.id === selectedModel.id);
    updateHighlight();
}

function closeDropdown() {
    modelSelectEl.classList.remove('open');
    highlightedIndex = -1;
}

function renderTriggerContent(m, { dimmed = false } = {}) {
    const sizeStr = formatSizeShort(m.size);
    const capIcons = renderCapIcons(m.capabilities);
    const ramIcon = renderRamIcon(m.estimatedRamMb, chatSystemRamMb);
    const style = dimmed ? ' style="opacity:0.5;"' : '';
    triggerText.innerHTML = `<span class="d-flex align-items-center gap-sm"${style}>` +
        `<span class="trigger-name">${escHtml(m.name || m.id)}</span>` +
        `<span class="d-flex align-items-center gap-sm" style="flex-shrink:0;">${capIcons}` +
        `${sizeStr ? `<span class="opt-size">${sizeStr}</span>` : ''}` +
        `${ramIcon}</span></span>`;
}

function selectModelById(id, { skipDownloadCheck = false, isRestore = false } = {}) {
    const m = dropdownModels.find(m => m.id === id);
    if (!m) return;

    const isReady = m.status === 'downloaded' || m.status === 'loaded';

    if (!isReady && !skipDownloadCheck) {
        if (selectedModel.id) previousSelectedModel = { ...selectedModel };
        closeDropdown();
        renderTriggerContent(m, { dimmed: true });
        promptDownload(m);
        return;
    }

    if (selectedModel.id) previousSelectedModel = { ...selectedModel };
    selectedModel = { id: m.id, provider: m.provider || 'foundry' };
    renderTriggerContent(m);
    optionsEl.querySelectorAll('.custom-select-option').forEach(el => {
        el.classList.toggle('selected', el.dataset.modelId === id);
    });
    closeDropdown();
    hideBanner();
    btnSend.disabled = false;
    updateMaxTokensSlider();
    if (!isRestore) resetChatSettings();
}

function revertModelSelection() {
    if (previousSelectedModel.id) {
        const prev = dropdownModels.find(m => m.id === previousSelectedModel.id);
        if (prev) {
            selectedModel = { ...previousSelectedModel };
            renderTriggerContent(prev);
            optionsEl.querySelectorAll('.custom-select-option').forEach(el => {
                el.classList.toggle('selected', el.dataset.modelId === previousSelectedModel.id);
            });
            btnSend.disabled = false;
            return;
        }
    }
    triggerText.innerHTML = 'Select a model';
    btnSend.disabled = true;
}

// ── Download-from-Chat ───────────────────────────────
const dlBanner = document.getElementById('chat-download-banner');
const dlText = document.getElementById('chat-dl-text');
const dlBar = document.getElementById('chat-dl-bar');
const dlRetryBtn = document.getElementById('chat-dl-retry');
const dlDismissBtn = document.getElementById('chat-dl-dismiss');

function hideBanner() {
    dlBanner.classList.add('d-none');
    dlRetryBtn.classList.add('d-none');
    dlDismissBtn.classList.add('d-none');
    isDownloading = false;
}

function promptDownload(model) {
    dlBar.style.transition = 'none';
    dlBar.style.width = '0%';
    dlBar.className = 'progress-fill';
    dlBar.offsetWidth;
    dlBar.style.transition = '';
    dlText.textContent = `"${model.name || model.id}" needs to be downloaded before you can chat with it.`;
    dlBanner.classList.remove('d-none');
    dlRetryBtn.classList.remove('d-none');
    dlRetryBtn.textContent = 'Download';
    dlDismissBtn.classList.remove('d-none');
    btnSend.disabled = true;

    const onRetry = () => startChatDownload(model);
    const onDismiss = () => {
        hideBanner();
        revertModelSelection();
        dlRetryBtn.removeEventListener('click', onRetry);
        dlDismissBtn.removeEventListener('click', onDismiss);
    };

    dlRetryBtn.onclick = onRetry;
    dlDismissBtn.onclick = onDismiss;
}

async function startChatDownload(model) {
    isDownloading = true;
    dlRetryBtn.classList.add('d-none');
    dlDismissBtn.classList.add('d-none');
    dlBar.style.transition = 'none';
    dlBar.style.width = '0%';
    dlBar.className = 'progress-fill';
    dlBar.offsetWidth;
    dlBar.style.transition = '';
    dlText.textContent = `Downloading ${model.name || model.id}...`;
    btnSend.disabled = true;

    let failed = false;
    try {
        const res = await fetch('/api/models/download', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ modelId: model.id, provider: 'foundry' })
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
                if (!line.startsWith('data: ')) continue;
                try {
                    const data = JSON.parse(line.substring(6));
                    if (data.percent != null && data.percent > 0) {
                        dlBar.style.width = `${data.percent}%`;
                        if (data.percent >= 100) dlBar.className = 'progress-fill success';
                    }
                    if (data.status && data.status.startsWith('error')) {
                        failed = true;
                        dlBar.style.width = '100%';
                        dlBar.className = 'progress-fill error';
                        dlText.textContent = `Download failed: ${data.status.replace(/^error:\s*/, '')}`;
                    } else if (data.status === 'complete' || data.status === 'success') {
                        dlBar.style.width = '100%';
                        dlBar.className = 'progress-fill success';
                        dlText.textContent = `${model.name || model.id} downloaded successfully`;
                    } else if (data.status) {
                        dlText.textContent = `Downloading ${model.name || model.id} — ${data.status}`;
                    }
                } catch { /* ignore parse errors */ }
            }
        }
    } catch (err) {
        failed = true;
        dlBar.style.width = '100%';
        dlBar.className = 'progress-fill error';
        dlText.textContent = `Download failed: ${err.message}`;
    }

    isDownloading = false;

    if (failed) {
        dlRetryBtn.classList.remove('d-none');
        dlRetryBtn.innerHTML = '<svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor"><path d="M8 3a5 5 0 11-4.546 2.914.5.5 0 00-.908-.418A6 6 0 108 2v1z"/><path d="M8 4.466V.534a.25.25 0 00-.41-.192L5.23 2.308a.25.25 0 000 .384l2.36 1.966A.25.25 0 008 4.466z"/></svg> Retry';
        dlRetryBtn.onclick = () => startChatDownload(model);
        dlDismissBtn.classList.remove('d-none');
        dlDismissBtn.onclick = () => { hideBanner(); revertModelSelection(); };
    } else {
        model.status = 'downloaded';
        await loadModels({ force: true });
        selectModelById(model.id, { skipDownloadCheck: true });
        setTimeout(() => hideBanner(), 2000);
    }
}

function updateHighlight() {
    const opts = optionsEl.querySelectorAll('.custom-select-option');
    opts.forEach((el, i) => el.classList.toggle('highlighted', i === highlightedIndex));
    if (highlightedIndex >= 0 && opts[highlightedIndex]) {
        opts[highlightedIndex].scrollIntoView({ block: 'nearest' });
    }
}

triggerEl.addEventListener('click', () => {
    modelSelectEl.classList.contains('open') ? closeDropdown() : openDropdown();
});

triggerEl.addEventListener('keydown', (e) => {
    const opts = optionsEl.querySelectorAll('.custom-select-option');
    if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        if (modelSelectEl.classList.contains('open')) {
            if (highlightedIndex >= 0 && opts[highlightedIndex]) {
                selectModelById(opts[highlightedIndex].dataset.modelId);
            }
        } else {
            openDropdown();
        }
    } else if (e.key === 'Escape') {
        closeDropdown();
    } else if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (!modelSelectEl.classList.contains('open')) { openDropdown(); return; }
        highlightedIndex = Math.min(highlightedIndex + 1, opts.length - 1);
        updateHighlight();
    } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        highlightedIndex = Math.max(highlightedIndex - 1, 0);
        updateHighlight();
    }
});

document.addEventListener('click', (e) => {
    if (!modelSelectEl.contains(e.target)) closeDropdown();
});

// ── Thinking Markers ──────────────────────────────────
const THINKING_MARKERS = [
    { start: '<|channel|>analysis', end: '<|message|>' },
    { start: '<think>', end: '</think>' }
];

function parseThinkingAndAnswer(text) {
    for (const marker of THINKING_MARKERS) {
        const startIdx = text.indexOf(marker.start);
        if (startIdx === -1) continue;
        const afterStart = startIdx + marker.start.length;
        const endIdx = text.indexOf(marker.end, afterStart);
        if (endIdx !== -1) {
            const thinking = text.substring(afterStart, endIdx).trim();
            const answer = text.substring(endIdx + marker.end.length).trim();
            return { thinking, answer, hasThinking: true };
        } else {
            const thinking = text.substring(afterStart).trim();
            return { thinking, answer: '', hasThinking: true, thinkingInProgress: true };
        }
    }
    return { thinking: '', answer: text, hasThinking: false };
}

// ── Load Models ───────────────────────────────────────
let chatSystemRamMb = null;

function loadChatModelsFromCache() {
    try {
        const raw = sessionStorage.getItem(MODELS_CACHE_KEY);
        if (!raw) return null;
        const parsed = JSON.parse(raw);
        if (!parsed || typeof parsed.timestamp !== 'number') return null;
        if (Date.now() - parsed.timestamp > MODELS_CACHE_TTL_MS) return null;
        return parsed;
    } catch { return null; }
}

async function loadModels({ force = false } = {}) {
    if (!force) {
        const cached = loadChatModelsFromCache();
        if (cached && Array.isArray(cached.models) && cached.models.length > 0) {
            chatSystemRamMb = cached.systemRamMb ?? chatSystemRamMb;
            cached.models.forEach(m => {
                if (m.maxOutputTokens) modelMaxTokens[m.id] = m.maxOutputTokens;
            });
            dropdownModels = cached.models;
            renderDropdownOptions(cached.models);
            return;
        }
    }

    try {
        const [sysRes, catalogRes] = await Promise.all([
            fetch('/api/system-info'),
            fetch('/api/models?provider=foundry')
        ]);

        if (sysRes.ok) {
            const sysInfo = await sysRes.json();
            chatSystemRamMb = sysInfo.totalRamMb;
        }

        let catalog = [];
        if (catalogRes.ok) {
            catalog = await catalogRes.json();
            catalog.forEach(m => {
                if (m.maxOutputTokens) modelMaxTokens[m.id] = m.maxOutputTokens;
            });
        }

        dropdownModels = catalog;
        renderDropdownOptions(catalog);

        if (catalog.length > 0) {
            try {
                sessionStorage.setItem(MODELS_CACHE_KEY, JSON.stringify({
                    models: catalog,
                    systemRamMb: chatSystemRamMb,
                    timestamp: Date.now(),
                }));
            } catch { /* quota / disabled */ }
        } else {
            window.clearProviderCache?.();
        }

    } catch (err) {
        triggerText.textContent = 'Error loading models';
        btnSend.disabled = true;
        window.clearProviderCache?.();
    }
}

function renderDropdownOptions(models) {
    const chatModels = models.filter(m => !NON_CHAT_FAMILIES.includes((m.family || '').toLowerCase()));
    dropdownModels = chatModels;

    if (chatModels.length === 0) {
        optionsEl.innerHTML = '';
        triggerText.textContent = 'No models available';
        btnSend.disabled = true;
        return;
    }

    if (!selectedModel.id) {
        triggerText.textContent = 'Select a model';
        btnSend.disabled = true;
    }

    const ready = chatModels.filter(m => m.status === 'downloaded' || m.status === 'loaded');
    const available = chatModels.filter(m => m.status !== 'downloaded' && m.status !== 'loaded');

    function renderOption(m) {
        const isReady = m.status === 'downloaded' || m.status === 'loaded';
        const sizeStr = formatSizeShort(m.size);
        const capIcons = renderCapIcons(m.capabilities);
        const ramIcon = renderRamIcon(m.estimatedRamMb, chatSystemRamMb);
        const tooltip = renderCapTooltip(m.capabilities);
        const readyIcon = isReady
            ? '<svg width="12" height="12" viewBox="0 0 16 16" fill="var(--green)" style="flex-shrink:0;"><path d="M13.78 4.22a.75.75 0 010 1.06l-7.25 7.25a.75.75 0 01-1.06 0L2.22 9.28a.75.75 0 011.06-1.06L6 10.94l6.72-6.72a.75.75 0 011.06 0z"/></svg>'
            : '<svg width="12" height="12" viewBox="0 0 16 16" fill="var(--text-tertiary)" style="flex-shrink:0;opacity:0.5;"><path d="M7.47 10.78a.75.75 0 001.06 0l3.75-3.75a.75.75 0 00-1.06-1.06L8 9.19 4.78 5.97a.75.75 0 00-1.06 1.06l3.75 3.75zM3.75 4a.75.75 0 000 1.5h8.5a.75.75 0 000-1.5h-8.5z"/></svg>';
        return `<div class="custom-select-option${isReady ? ' opt-ready' : ' opt-available'}" data-model-id="${escHtml(m.id)}" data-provider="${escHtml(m.provider || 'foundry')}">
            ${readyIcon}
            <span class="opt-name">${escHtml(m.name || m.id)}</span>
            <span class="opt-meta">
                ${capIcons}
                ${sizeStr ? `<span class="opt-size">${sizeStr}</span>` : ''}
                ${ramIcon}
            </span>
            ${tooltip}
        </div>`;
    }

    let html = ready.map(renderOption).join('');
    if (ready.length > 0 && available.length > 0) {
        html += '<div class="opt-separator"></div>';
    }
    html += available.map(renderOption).join('');
    optionsEl.innerHTML = html;

    optionsEl.querySelectorAll('.custom-select-option').forEach(el => {
        el.addEventListener('click', () => selectModelById(el.dataset.modelId));
    });

    const params = new URLSearchParams(window.location.search);
    const selectModel = params.get('selectModel');
    if (selectModel) {
        const match = chatModels.find(m => m.id === selectModel)
            || chatModels.find(m => m.id.split(':')[0] === selectModel.split(':')[0]);
        if (match) {
            selectModelById(match.id);
            chatInput.focus();
        }
        history.replaceState(null, '', '/');
    } else {
        const saved = loadChatState();
        if (saved && saved.selectedModelId) {
            const savedModel = chatModels.find(m => m.id === saved.selectedModelId);
            if (saved.conversation && saved.conversation.length > 0) {
                conversation = saved.conversation;
                renderMessages();
            }
            restoreChatSettings(saved);
            if (savedModel) {
                const isReady = savedModel.status === 'downloaded' || savedModel.status === 'loaded';
                if (isReady) {
                    selectModelById(saved.selectedModelId, { skipDownloadCheck: true, isRestore: true });
                } else {
                    renderTriggerContent(savedModel, { dimmed: true });
                    optionsEl.querySelectorAll('.custom-select-option').forEach(el => {
                        el.classList.toggle('selected', el.dataset.modelId === saved.selectedModelId);
                    });
                    btnSend.disabled = true;
                    promptDownload(savedModel);
                }
            }
        }
    }
}

function updateMaxTokensSlider() {
    if (!maxTokensSlider) return;
    const limit = modelMaxTokens[selectedModel.id] || 4096;
    maxTokensSlider.max = limit;
    if (parseInt(maxTokensSlider.value) > limit) {
        maxTokensSlider.value = limit;
    }
    maxTokensValue.textContent = maxTokensSlider.value;
}

function resetChatSettings() {
    if (maxTokensSlider) {
        maxTokensSlider.value = 2048;
        maxTokensValue.textContent = '2048';
        updateMaxTokensSlider();
    }
    if (showThinkingToggle) {
        showThinkingToggle.checked = false;
    }
    if (promptSelect) {
        const defaultOpt = Array.from(promptSelect.options).find(o => o.dataset.content && o.selected) ||
                           Array.from(promptSelect.options).find(o => o.textContent === 'Default') ||
                           promptSelect.options[0];
        if (defaultOpt) promptSelect.value = defaultOpt.value;
    }
}

function restoreChatSettings(saved) {
    if (maxTokensSlider && saved.maxTokens) {
        maxTokensSlider.value = saved.maxTokens;
        maxTokensValue.textContent = saved.maxTokens;
    }
    if (showThinkingToggle && saved.thinking !== undefined) {
        showThinkingToggle.checked = saved.thinking;
    }
    if (promptSelect && saved.systemPromptId !== undefined) {
        promptSelect.value = saved.systemPromptId;
    }
}

// ── System Prompts ────────────────────────────────────
async function loadSystemPrompts() {
    try {
        const res = await fetch('/api/system-prompts');
        const prompts = await res.json();
        promptSelect.innerHTML = '<option value="">None</option>';
        prompts.forEach(p => {
            const opt = document.createElement('option');
            opt.value = p.id;
            opt.textContent = p.name;
            opt.dataset.content = p.content;
            if (p.isDefault) opt.selected = true;
            promptSelect.appendChild(opt);
        });
    } catch (err) {
        console.warn('Failed to load system prompts:', err);
    }
}

function getSystemPromptContent() {
    const opt = promptSelect.selectedOptions[0];
    return opt && opt.dataset.content ? opt.dataset.content : null;
}

// ── Render Messages ───────────────────────────────────
function renderMessages() {
    if (conversation.length === 0) {
        chatMessages.innerHTML = `
            <div class="chat-welcome">
                <h4>FoundryWebUI-X</h4>
                <p>Select a model and start chatting</p>
            </div>`;
        return;
    }

    const showThinking = showThinkingToggle && showThinkingToggle.checked;

    chatMessages.innerHTML = conversation.map((msg, i) => {
        const isUser = msg.role === 'user';
        const contextWarning = msg.contextExceeded
            ? `<div class="alert-inline mt-2">
                 <span>Context limit reached — start a new chat to continue.</span>
               </div>`
            : '';

        if (isUser) {
            return `
                <div class="d-flex mb-3 justify-content-end">
                    <div class="msg-bubble msg-user">
                        <div class="msg-label">You</div>
                        <div class="message-content">${formatContent(msg.content)}</div>
                    </div>
                </div>`;
        }

        if (msg.isError) {
            return `
                <div class="d-flex mb-3 justify-content-start">
                    <div class="msg-bubble msg-error">
                        <div class="msg-label">Error</div>
                        <div class="message-content">${formatContent(msg.content)}</div>
                    </div>
                </div>`;
        }

        const parsed = parseThinkingAndAnswer(msg.content);
        let html = '';

        if (parsed.hasThinking && showThinking && parsed.thinking) {
            html += `
                <div class="d-flex mb-2 justify-content-start">
                    <div class="msg-bubble msg-thinking">
                        <div class="msg-label">Thinking</div>
                        <div class="message-content thinking-content">${formatContent(parsed.thinking)}</div>
                        ${parsed.thinkingInProgress ? '<div style="color: var(--yellow); font-size: 0.8rem; margin-top: 4px;"><em>Still thinking...</em></div>' : ''}
                    </div>
                </div>`;
        }

        if (parsed.answer || !parsed.hasThinking) {
            const displayContent = parsed.hasThinking ? parsed.answer : msg.content;
            const label = parsed.hasThinking ? 'Answer' : 'Assistant';
            html += `
                <div class="d-flex mb-3 justify-content-start">
                    <div class="msg-bubble msg-assistant">
                        <div class="msg-label">${label}</div>
                        <div class="message-content">${formatContent(displayContent)}</div>
                        ${contextWarning}
                    </div>
                </div>`;
        } else if (parsed.hasThinking && !parsed.answer && !showThinking) {
            html += `
                <div class="d-flex mb-3 justify-content-start">
                    <div class="msg-bubble msg-assistant">
                        <div class="msg-label">Assistant</div>
                        <div class="message-content" style="color: var(--text-tertiary);"><em>Thinking...</em></div>
                    </div>
                </div>`;
        }

        return html;
    }).join('');

    chatMessages.scrollTop = chatMessages.scrollHeight;
    saveChatState();
}

function formatContent(text) {
    return text
        .replace(/```(\w*)\n([\s\S]*?)```/g, '<pre><code class="language-$1">$2</code></pre>')
        .replace(/`([^`]+)`/g, '<code>$1</code>')
        .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
        .replace(/\n/g, '<br>');
}

// ── Send Message ──────────────────────────────────────
async function sendMessage() {
    const text = chatInput.value.trim();
    if (!text || !selectedModel.id || isDownloading) return;

    const provider = selectedModel.provider;

    conversation.push({ role: 'user', content: text });
    conversation.push({ role: 'assistant', content: '⏳ Thinking...' });
    const thinkingIdx = conversation.length - 1;
    renderMessages();

    chatInput.value = '';
    setLoading(true);

    abortController = new AbortController();
    let receivedContent = false;

    try {
        console.log(`[chat] Sending to /api/chat?provider=${provider}, model=${selectedModel.id}`);
        const chatMessages_arr = conversation.filter((m, i) => i < thinkingIdx).map(m => ({role: m.role, content: m.content}));
        const sysPrompt = getSystemPromptContent();
        if (sysPrompt) {
            chatMessages_arr.unshift({ role: 'system', content: sysPrompt });
        }
        const maxTokens = maxTokensSlider ? parseInt(maxTokensSlider.value) : 4096;
        const res = await fetch(`/api/chat?provider=${provider}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                model: selectedModel.id,
                messages: chatMessages_arr,
                stream: true,
                temperature: 0.7,
                max_tokens: maxTokens
            }),
            signal: abortController.signal
        });

        console.log(`[chat] Response status: ${res.status} ${res.statusText}`);

        if (!res.ok) {
            conversation[thinkingIdx].content = `Unable to reach Foundry Local (HTTP ${res.status}). Check the Logs page for details.`;
            conversation[thinkingIdx].isError = true;
            renderMessages();
            setLoading(false);
            abortController = null;
            return;
        }

        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { done, value } = await reader.read();
            if (done) {
                console.log('[chat] Stream ended');
                break;
            }

            const chunk = decoder.decode(value, { stream: true });
            buffer += chunk;
            const lines = buffer.split('\n');
            buffer = lines.pop() || '';

            for (const line of lines) {
                if (!line.trim()) continue;

                if (line.startsWith('data: ')) {
                    const dataStr = line.substring(6);
                    try {
                        const data = JSON.parse(dataStr);
                        if (!receivedContent && (data.content || data.error)) {
                            conversation[thinkingIdx].content = '';
                            receivedContent = true;
                        }
                        if (data.content) {
                            conversation[thinkingIdx].content += data.content;
                        }
                        if (data.error) {
                            conversation[thinkingIdx].isError = true;
                            if (data.error === 'context_length_exceeded') {
                                conversation[thinkingIdx].content += 'Context limit reached — the conversation is too long for this model. Start a new chat or use a model with a larger context window.';
                                conversation[thinkingIdx].contextExceeded = true;
                            } else if (data.error === 'connection_closed') {
                                conversation[thinkingIdx].content += 'Connection lost — Foundry Local closed the connection. This usually means the max tokens setting exceeds the model\'s capacity. Try lowering Max Tokens.';
                            } else {
                                conversation[thinkingIdx].content += data.error;
                            }
                        }
                        renderMessages();
                    } catch (parseErr) {
                        console.warn('[chat] Failed to parse:', dataStr, parseErr);
                    }
                } else if (line.startsWith('event: ')) {
                    console.log('[chat] Event type:', line.substring(7));
                }
            }
        }

        if (!receivedContent) {
            console.warn('[chat] No content received from stream');
            conversation[thinkingIdx].content = 'No response received. The model may still be loading — try again in a moment.';
            conversation[thinkingIdx].isError = true;
            renderMessages();
        }
    } catch (err) {
        console.error('[chat] Error:', err);
        if (err.name !== 'AbortError') {
            conversation[thinkingIdx].content = 'Unable to connect to Foundry Local. Check that the service is running.';
            conversation[thinkingIdx].isError = true;
            renderMessages();
        }
    }

    setLoading(false);
    abortController = null;
}

function setLoading(loading) {
    btnSend.classList.toggle('d-none', loading);
    btnStop.classList.toggle('d-none', !loading);
    chatInput.disabled = loading;
    sendText.classList.toggle('d-none', loading);
    sendSpinner.classList.toggle('d-none', !loading);
}

// ── Event Listeners ───────────────────────────────────
btnSend.addEventListener('click', sendMessage);
btnStop.addEventListener('click', () => {
    if (abortController) abortController.abort();
});
btnNewChat.addEventListener('click', () => {
    conversation = [];
    try { sessionStorage.removeItem(CHAT_STATE_KEY); } catch {}
    renderMessages();
});
chatInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
    }
});
if (maxTokensSlider) {
    maxTokensSlider.addEventListener('change', saveChatState);
}
if (promptSelect) {
    promptSelect.addEventListener('change', saveChatState);
}
if (showThinkingToggle) {
    showThinkingToggle.addEventListener('change', () => { renderMessages(); saveChatState(); });
}

// Init
loadModels();
loadSystemPrompts();
