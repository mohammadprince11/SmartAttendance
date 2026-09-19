(() => {
    const selector = "[data-live-queue]";
    let refreshInFlight = false;
    let refreshTimer = null;

    function panel() {
        return document.querySelector(selector);
    }

    function parseUtc(value) {
        if (!value) return null;
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function formatSeconds(totalSeconds) {
        const seconds = Math.max(0, Math.floor(totalSeconds));
        if (seconds < 60) return `${seconds} ث`;

        const minutes = Math.floor(seconds / 60);
        const remaining = seconds % 60;
        if (minutes < 60) {
            return remaining === 0
                ? `${minutes} د`
                : `${minutes} د ${remaining} ث`;
        }

        const hours = Math.floor(minutes / 60);
        const remainingMinutes = minutes % 60;
        return remainingMinutes === 0
            ? `${hours} س`
            : `${hours} س ${remainingMinutes} د`;
    }

    function updateTimers(root = document) {
        const now = Date.now();

        root.querySelectorAll("[data-elapsed-from]").forEach(element => {
            const started = parseUtc(element.dataset.elapsedFrom);
            if (!started) return;
            element.textContent = formatSeconds((now - started.getTime()) / 1000);
        });

        root.querySelectorAll("[data-countdown-to]").forEach(element => {
            const deadline = parseUtc(element.dataset.countdownTo);
            if (!deadline) return;

            const seconds = (deadline.getTime() - now) / 1000;
            element.textContent = seconds > 0
                ? formatSeconds(seconds)
                : "الآن";
        });
    }

    function setLiveMessage(message, isError = false) {
        const element = document.querySelector("[data-live-message]");
        if (!element) return;
        element.textContent = message;
        element.classList.toggle("is-error", isError);
    }

    function syncLiveFragments(parsed) {
        ["session", "next"].forEach(key => {
            const current = document.querySelector(
                `[data-live-fragment="${key}"]`
            );
            const fresh = parsed.querySelector(
                `[data-live-fragment="${key}"]`
            );

            if (current && fresh) {
                current.replaceWith(fresh);
            }
        });
    }

    async function refreshQueue() {
        const current = panel();
        if (!current || refreshInFlight || document.hidden) return;

        refreshInFlight = true;
        setLiveMessage("جاري تحديث الحالة…");

        try {
            const url = new URL(window.location.href);
            url.searchParams.set("_queueTick", Date.now().toString());

            const response = await fetch(url, {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store",
                headers: {
                    "X-Requested-With": "ZynoraLiveQueue"
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const html = await response.text();
            const parsed = new DOMParser().parseFromString(html, "text/html");
            const fresh = parsed.querySelector(selector);

            if (!fresh) {
                throw new Error("Queue panel missing from refresh response.");
            }

            syncLiveFragments(parsed);
            current.replaceWith(fresh);
            updateTimers(document);

            const seconds = Number.parseInt(
                fresh.dataset.refreshSeconds || "5",
                10
            );
            setLiveMessage(
                `آخر تحديث الآن · التالي خلال ${Number.isFinite(seconds) ? seconds : 5} ثوانٍ`
            );
        } catch {
            setLiveMessage("تعذر تحديث الحالة تلقائياً · سنحاول مجدداً", true);
        } finally {
            refreshInFlight = false;
        }
    }

    function setUploadMessage(message, isError = false) {
        const element = document.querySelector("[data-upload-message]");
        if (!element) return;

        element.hidden = false;
        element.textContent = message;
        element.classList.toggle("is-error", isError);
        element.classList.toggle("is-success", !isError);
    }

    function initAjaxUpload() {
        const form = document.querySelector("[data-ajax-upload]");
        if (!form) return;

        const submit = form.querySelector("[data-upload-submit]");
        const fileInput = form.querySelector("[data-upload-file]");
        const typeSelect = form.querySelector("[data-document-type]");
        let uploadInFlight = false;

        form.addEventListener("submit", async event => {
            event.preventDefault();

            if (uploadInFlight) return;

            if (!fileInput?.files?.length) {
                setUploadMessage("اختر ملف مستند قبل الرفع.", true);
                return;
            }

            const selectedType = typeSelect?.value ?? "";
            uploadInFlight = true;

            if (submit) {
                submit.disabled = true;
                submit.dataset.originalText = submit.textContent ?? "";
                submit.textContent = "جاري الرفع…";
            }

            setUploadMessage("جاري فحص الملف ورفعه…");

            try {
                const response = await fetch(form.action, {
                    method: "POST",
                    body: new FormData(form),
                    credentials: "same-origin",
                    headers: {
                        "X-Requested-With": "ZynoraAjaxUpload"
                    }
                });

                const contentType =
                    response.headers.get("content-type") ?? "";

                if (!contentType.includes("application/json")) {
                    throw new Error("HTTP " + response.status);
                }

                const result = await response.json();

                if (!response.ok || !result.success) {
                    setUploadMessage(
                        result.message ?? "تعذر رفع المستند.",
                        true
                    );
                    return;
                }

                if (fileInput) {
                    fileInput.value = "";
                }

                if (typeSelect && selectedType) {
                    typeSelect.value = selectedType;
                }

                setUploadMessage(
                    result.message ??
                    "تم رفع المستند وإضافته إلى الطابور."
                );

                await refreshQueue();
            } catch {
                setUploadMessage(
                    "تعذر إكمال الرفع بدون إعادة تحميل. أعد المحاولة.",
                    true
                );
            } finally {
                uploadInFlight = false;

                if (submit) {
                    submit.disabled = false;
                    submit.textContent =
                        submit.dataset.originalText ||
                        "رفع وإضافة للطابور";
                }
            }
        });
    }

    function start() {
        const current = panel();
        if (!current) return;

        const seconds = Math.max(
            3,
            Math.min(
                10,
                Number.parseInt(current.dataset.refreshSeconds || "5", 10) || 5
            )
        );

        updateTimers(document);
        initAjaxUpload();
        window.setInterval(() => updateTimers(document), 1000);

        refreshTimer = window.setInterval(refreshQueue, seconds * 1000);

        document.addEventListener("visibilitychange", () => {
            if (!document.hidden) {
                updateTimers(document);
                refreshQueue();
            }
        });

        window.addEventListener("beforeunload", () => {
            if (refreshTimer) {
                window.clearInterval(refreshTimer);
            }
        });
    }

    window.ZynoraSmartOnboardingRefreshQueue = refreshQueue;

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start, { once: true });
    } else {
        start();
    }
})();
