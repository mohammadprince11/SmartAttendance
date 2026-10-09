// Read-only navigation enhancement. POST handlers, confirmation and antiforgery remain native.
(() => {
    'use strict';
    const selector = '[data-disciplinary-workspace]';
    if (!document.querySelector(selector)) return;
    let activeUrl = location.href;
    let dirty = false;
    let generation = 0;
    let request;
    // Short-lived, document-local GET snapshots only. Never persist tokens or
    // configuration in browser storage, and never cache a POST or failed GET.
    const sections = new Map();
    const cacheLifetime = 30000;
    let cacheEpoch = 0;
    function cacheKey(url) {
        const key = new URL(url.href);
        key.hash = '';
        if (!key.searchParams.has('tab')) key.searchParams.set('tab', 'library');
        if (key.searchParams.get('categoryId') === '0') key.searchParams.delete('categoryId');
        key.searchParams.sort();
        return key.href;
    }
    function invalidate() { sections.clear(); cacheEpoch++; }
    function remember(url, html) {
        if (html.length > 2000000) return;
        const key = cacheKey(url);
        sections.delete(key);
        sections.set(key, {html, expires: Date.now() + cacheLifetime});
        while (sections.size > 4) sections.delete(sections.keys().next().value);
    }
    function cached(url) {
        const key = cacheKey(url), entry = sections.get(key);
        if (!entry || entry.expires <= Date.now()) { sections.delete(key); return null; }
        return entry.html;
    }
    function signature(root) {
        return JSON.stringify(Array.from(root.querySelectorAll('form[method="post"] input, form[method="post"] select, form[method="post"] textarea'),
            field => [field.name, field.type === 'checkbox' ? field.checked : field.type === 'select-multiple'
                ? Array.from(field.selectedOptions, option => option.value) : field.value]));
    }
    let clean = signature(document.querySelector(selector));
    const loaded = new Set(Array.from(document.querySelectorAll('script[data-disciplinary-once]'), s => s.src));
    const scripts = new Set([
        '/js/zynora-disciplinary-tools.js', '/js/zynora-conditions.js', '/js/zynora-grid-editor.js',
        '/js/zynora-a4-custom-file-picker-v13-1.js', '/js/zynora-disciplinary-pdf.js'
    ]);
    const styles = new Set([
        '/css/zynora-conditions.css', '/css/zynora-disciplinary-rules.css',
        '/css/zynora-a4-custom-file-picker-v13-1.css', '/css/zynora-disciplinary-designer-preview-safe-v1.css',
        '/css/pages/index-387bf545e8.css'
    ]);
    const styleLoads = new WeakMap();
    function allowedAsset(url, allowed) {
        // ASP.NET static asset publishing inserts a ten-character fingerprint
        // before the extension. Match only the same explicitly allowed local asset.
        const canonical = url.pathname.replace(/\.[a-z0-9]{10}\.(css|js)$/i, '.$1');
        return url.origin === location.origin && (allowed.has(url.pathname) || allowed.has(canonical));
    }
    function localUrl(value) {
        const url = new URL(value, activeUrl);
        return url.origin === location.origin && url.pathname.replace(/\/$/, '').toLowerCase() === '/disciplinaryrules'
            && !url.searchParams.has('handler') ? url : null;
    }
    function status(root, message) {
        const node = root.querySelector('[data-disciplinary-status]');
        if (node) { node.hidden = !message; node.textContent = message; }
    }
    // The initial library/templates/articles GET is already authorized and loaded.
    // Do not snapshot a running designer (canvas and native file controls).
    const initialRoot = document.querySelector(selector);
    if (!initialRoot.querySelector('[data-disciplinary-designer]')) {
        const links = Array.from(document.querySelectorAll('link[data-disciplinary-style]'), link => link.outerHTML).join('');
        remember(new URL(activeUrl), '<html><head>' + links + '</head><body>' + initialRoot.outerHTML + '</body></html>');
    }
    async function syncStyles(page) {
        const wanted = new Set(Array.from(page.querySelectorAll('link[data-disciplinary-style]'), link => new URL(link.getAttribute('href'), activeUrl).href));
        document.querySelectorAll('link[data-disciplinary-style]').forEach(link => { if (!wanted.has(link.href)) link.remove(); });
        let anchor = document.head.querySelector('[data-disciplinary-style-anchor]');
        if (!anchor) {
            anchor = document.createElement('meta'); anchor.dataset.disciplinaryStyleAnchor = '';
            document.head.appendChild(anchor);
        }
        const pending = [];
        page.querySelectorAll('link[data-disciplinary-style]').forEach(link => {
            const url = new URL(link.getAttribute('href'), activeUrl);
            if (!allowedAsset(url, styles)) return;
            const current = Array.from(document.querySelectorAll('link[data-disciplinary-style]')).find(current => current.href === url.href);
            // Keep the same cascade as a full GET/POST redirect, even when a retained
            // page stylesheet was present before newly requested designer styles.
            if (current) {
                anchor.before(current);
                if (styleLoads.has(current)) pending.push(styleLoads.get(current));
                return;
            }
            const copy = document.createElement('link');
            copy.rel = 'stylesheet'; copy.href = url.href; copy.dataset.disciplinaryStyle = '';
            const loading = new Promise((resolve, reject) => {
                copy.onload = resolve; copy.onerror = () => { copy.remove(); reject(new Error('style')); };
            });
            styleLoads.set(copy, loading); pending.push(loading);
            anchor.before(copy);
        });
        await Promise.all(pending);
    }
    async function initialize(root, ticket) {
        for (const source of root.querySelectorAll('script[data-disciplinary-script]')) {
            if (ticket !== generation) return;
            const url = new URL(source.getAttribute('src'), activeUrl);
            if (!allowedAsset(url, scripts)) continue;
            const once = source.hasAttribute('data-disciplinary-once');
            if (once && loaded.has(url.href)) continue;
            await new Promise((resolve, reject) => {
                const script = document.createElement('script');
                script.src = url.href;
                script.onload = () => { script.remove(); if (once) loaded.add(url.href); resolve(); };
                script.onerror = () => { script.remove(); reject(new Error('script')); };
                document.body.appendChild(script);
            });
        }
        window.ZyConditions?.init(root);
        window.ZynoraDisciplinary?.init(root);
        window.ZynoraDisciplinaryPdf?.init(root);
        window.ZynoraGridEditor?.init(root);
        window.ZynoraRefreshSelectSystem?.();
    }
    async function navigate(url, fromHistory = false) {
        if (cacheKey(url) === cacheKey(new URL(activeUrl)) && !fromHistory) return;
        const previous = document.querySelector(selector);
        if ((dirty || signature(previous) !== clean) && !window.confirm('عندك تغييرات غير محفوظة. تريد مغادرة الشاشة؟')) {
            if (fromHistory) history.pushState(null, '', activeUrl);
            return;
        }
        const ticket = ++generation;
        const epoch = cacheEpoch;
        request?.abort(); request = new AbortController();
        previous.setAttribute('aria-busy', 'true');
        previous.querySelector('main').inert = true;
        status(previous, '');
        let waiting = false;
        const indicator = setTimeout(() => {
            if (ticket !== generation) return;
            waiting = true;
            status(document.querySelector(selector), 'جاري تحميل القسم…');
        }, 250);
        try {
            let html = cached(url);
            const reused = html !== null;
            if (!reused) {
                const response = await fetch(url.href, { credentials: 'same-origin', signal: request.signal,
                    headers: {'X-Zynora-Workspace': 'disciplinary'} });
                if (!response.ok || !localUrl(response.url)) { invalidate(); throw new Error('response'); }
                html = await response.text();
            }
            const page = new DOMParser().parseFromString(html, 'text/html');
            const incoming = page.querySelector(selector);
            if (!incoming) throw new Error('workspace');
            if (ticket !== generation) return;
            await syncStyles(page);
            if (ticket !== generation) return;
            window.ZynoraDisciplinaryPdf?.dispose(previous);
            window.ZynoraDisciplinary?.dispose(previous);
            previous.replaceWith(incoming);
            activeUrl = url.href;
            if (!fromHistory) history.pushState(null, '', activeUrl);
            dirty = false;
            incoming.setAttribute('aria-busy', 'true');
            incoming.querySelector('main').inert = true;
            status(incoming, waiting ? 'جاري تحميل القسم…' : '');
            await initialize(incoming, ticket);
            if (ticket !== generation) return;
            incoming.removeAttribute('aria-busy'); status(incoming, '');
            incoming.querySelector('main').inert = false;
            clean = signature(incoming);
            if (!reused && epoch === cacheEpoch) remember(url, html);
            const focus = url.searchParams.has('openTypeId')
                ? incoming.querySelector('.zyw-acc[open]') : incoming.querySelector('.zyw-tab[aria-selected="true"]');
            if (focus) { focus.setAttribute('tabindex', '-1'); focus.focus({preventScroll: true}); }
        } catch (error) {
            if (ticket !== generation || error.name === 'AbortError') return;
            const root = document.querySelector(selector);
            root.removeAttribute('aria-busy');
            root.querySelector('main').inert = false;
            status(root, 'تعذّر تحميل القسم. حاول مرة ثانية؛ لم يتم إرسال أي تغييرات.');
        } finally {
            clearTimeout(indicator);
            if (ticket === generation) request = null;
        }
    }
    document.addEventListener('input', event => {
        if (event.target.closest(selector) && event.target.closest('form[method="post"]')) dirty = true;
    });
    document.addEventListener('change', event => {
        if (event.target.closest(selector) && event.target.closest('form[method="post"]')) dirty = true;
    });
    document.addEventListener('click', event => {
        if (event.target.closest(selector) && event.target.closest('[data-zyw-add], [data-zyw-remove]')) dirty = true;
        const link = event.target.closest('a[href]');
        if (!link || !link.closest(selector) || event.defaultPrevented || event.button !== 0
            || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || link.hasAttribute('download')
            || (link.target && link.target !== '_self')) return;
        const url = localUrl(link.href);
        if (!url) return;
        event.preventDefault(); navigate(url);
    });
    document.addEventListener('submit', event => {
        const form = event.target;
        if (!form.closest(selector) || form.method.toLowerCase() !== 'get' || event.defaultPrevented) return;
        const url = localUrl(form.action);
        if (!url) return;
        url.search = new URLSearchParams(new FormData(form)).toString();
        event.preventDefault(); navigate(url);
    });
    // Capture before grid handlers/confirmation. Even a prevented write attempt
    // invalidates snapshots conservatively; native antiforgery and POST stay intact.
    document.addEventListener('submit', event => {
        if (event.target.closest(selector) && event.target.method.toLowerCase() === 'post') invalidate();
    }, true);
    document.addEventListener('visibilitychange', () => { if (document.hidden) invalidate(); });
    window.addEventListener('pagehide', invalidate);
    window.addEventListener('popstate', () => {
        const url = localUrl(location.href);
        if (url) navigate(url, true);
    });
})();
