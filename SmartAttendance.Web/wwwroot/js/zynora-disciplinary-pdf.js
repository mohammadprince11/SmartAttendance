// First-page-only background preview. The original PDF and all POST handlers stay unchanged.
(() => {
    'use strict';
    if (window.ZynoraDisciplinaryPdf) return;
    const base = '/vendor/pdfjs-5.6.205/';
    const limit = 20 * 1024 * 1024;
    const states = new WeakMap();
    let library;
    function setStatus(canvas, message) {
        const status = canvas.closest('.nxpen-live-preview')?.querySelector('[data-disciplinary-pdf-status]');
        if (status) status.textContent = message;
    }
    async function readPdf(url, signal) {
        const response = await fetch(url.href, {credentials: 'same-origin', cache: 'no-store', signal});
        if (response.status === 401 || response.status === 403 || response.redirected) throw new Error('access');
        if (response.status === 404) throw new Error('missing');
        if (!response.ok || new URL(response.url).origin !== location.origin) throw new Error('file');
        if (!(response.headers.get('content-type') || '').toLowerCase().includes('application/pdf')) throw new Error('file');
        if (Number(response.headers.get('content-length')) > limit) throw new Error('size');
        const reader = response.body.getReader();
        const chunks = [];
        let length = 0;
        try {
            while (true) {
                const {done, value} = await reader.read();
                if (done) break;
                length += value.length;
                if (length > limit) throw new Error('size');
                chunks.push(value);
            }
        } finally { await reader.cancel().catch(() => {}); }
        const bytes = new Uint8Array(length);
        let offset = 0;
        for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
        return bytes;
    }
    async function render(canvas) {
        if (states.has(canvas)) return;
        const state = {controller: new AbortController(), cancelled: false};
        states.set(canvas, state);
        const timer = setTimeout(() => { state.timedOut = true; state.controller.abort(); void state.task?.destroy().catch(() => {}); }, 30000);
        try {
            const url = new URL(canvas.dataset.disciplinaryPdf, location.href);
            if (url.origin !== location.origin || !/^\/disciplinaryrules(?:\/index)?\/?$/i.test(url.pathname)
                || url.searchParams.get('handler') !== 'A4FormPreview' || url.searchParams.size !== 1 || url.hash) throw new Error('file');
            library ??= import(base + 'pdf.min.mjs');
            const [pdfjs, data] = await Promise.all([library, readPdf(url, state.controller.signal)]);
            if (state.timedOut) throw new Error('timeout');
            if (state.cancelled || !canvas.isConnected) return;
            pdfjs.GlobalWorkerOptions.workerSrc = base + 'pdf.worker.min.mjs';
            state.task = pdfjs.getDocument({data, cMapUrl: base + 'cmaps/', cMapPacked: true,
                standardFontDataUrl: base + 'standard_fonts/', wasmUrl: base + 'wasm/',
                iccUrl: base + 'iccs/', isEvalSupported: false, enableXfa: false, useWasm: false,
                maxImageSize: 16000000, canvasMaxAreaInBytes: 16777216});
            state.task.onPassword = () => { state.password = true; void state.task.destroy().catch(() => {}); };
            const pdf = await state.task.promise;
            const page = await pdf.getPage(1);
            const size = page.getViewport({scale: 1});
            if (!Number.isFinite(size.width) || !Number.isFinite(size.height) || size.width <= 0 || size.height <= 0) throw new Error('page');
            const viewport = page.getViewport({scale: Math.min(1600 / size.width, 2200 / size.height)});
            canvas.width = Math.ceil(viewport.width); canvas.height = Math.ceil(viewport.height);
            await page.render({canvasContext: canvas.getContext('2d'), viewport, background: 'white'}).promise;
            if (state.timedOut) throw new Error('timeout');
            if (state.cancelled || !canvas.isConnected) return;
            canvas.classList.add('nxpen-a4-form-layer');
            canvas.hidden = false;
            setStatus(canvas, pdf.numPages > 1
                ? `تم عرض الصفحة الأولى من ${pdf.numPages} صفحات. طبقات النص تظهر فوقها؛ الملف الأصلي محفوظ كاملاً.`
                : 'تم عرض الفورمة المرفوعة. طبقات النص تظهر فوقها.');
        } catch (error) {
            if (!state.cancelled && canvas.isConnected) setStatus(canvas,
                state.password ? 'الملف محمي بكلمة مرور. ارفع نسخة غير محمية للمعاينة.'
                : state.timedOut ? 'تعذر تحميل المعاينة ضمن الوقت المحدد. أعد فتح المصمم للمحاولة؛ الملف المحفوظ لم يتغير.'
                : error.message === 'access' ? 'تعذر الوصول للفورمة. أعد تسجيل الدخول وتحقق من صلاحيتك للمصمم.'
                : error.message === 'missing' ? 'الفورمة المحفوظة غير متوفرة للمعاينة. أعد رفع الفورمة أو راجع مسؤول النظام.'
                : error.message === 'size' ? 'الملف أكبر من حد المعاينة (20MB). الملف المحفوظ لم يتغير.'
                : 'تعذر عرض PDF. تحقق من سلامة الملف ثم أعد رفعه؛ الملف المحفوظ لم يتغير.');
        } finally {
            clearTimeout(timer);
            await state.task?.destroy().catch(() => {});
        }
    }
    function init(root = document) { root.querySelectorAll('[data-disciplinary-pdf]').forEach(canvas => { void render(canvas); }); }
    function dispose(root) {
        root.querySelectorAll('[data-disciplinary-pdf]').forEach(canvas => {
            const state = states.get(canvas);
            if (state) { state.cancelled = true; state.controller.abort(); void state.task?.destroy().catch(() => {}); }
        });
    }
    window.ZynoraDisciplinaryPdf = {init, dispose};
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => init());
    else init();
})();
