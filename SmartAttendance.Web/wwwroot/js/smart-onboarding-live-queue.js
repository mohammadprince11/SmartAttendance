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

    function initPreviewDialog() {
        const dialog = document.querySelector("[data-preview-dialog]");
        const frame = dialog?.querySelector("[data-preview-frame]");
        const title = dialog?.querySelector("[data-preview-title]");
        const body = dialog?.querySelector(".so-preview-body");

        if (!dialog || !frame || !body) return;

        let viewport = body.querySelector(".so-preview-viewport");
        if (!viewport) {
            viewport = document.createElement("div");
            viewport.className = "so-preview-viewport";
            viewport.hidden = true;
            body.prepend(viewport);
        }

        let image = dialog.querySelector("[data-preview-image]");
        if (!image) {
            image = document.createElement("img");
            image.setAttribute("data-preview-image", "");
            image.className = "so-preview-image";
            image.alt = "معاينة المستند المحمي";
            image.hidden = true;
        }
        if (image.parentElement !== viewport) {
            viewport.append(image);
        }

        let toolbar = body.querySelector(".so-preview-toolbar");
        if (!toolbar) {
            toolbar = document.createElement("div");
            toolbar.className = "so-preview-toolbar";
            toolbar.hidden = true;
            toolbar.setAttribute("role", "toolbar");
            toolbar.setAttribute("aria-label", "أدوات التحكم بالمعاينة");
            toolbar.innerHTML = [
                '<button type="button" class="zy-btn" data-view-fit title="عرض الصورة كاملة">ملاءمة</button>',
                '<button type="button" class="zy-btn so-preview-icon-btn" data-view-out title="تصغير">−</button>',
                '<span class="so-preview-zoom" data-view-zoom>100%</span>',
                '<button type="button" class="zy-btn so-preview-icon-btn" data-view-in title="تكبير">+</button>',
                '<button type="button" class="zy-btn so-preview-icon-btn" data-view-left title="تدوير لليسار">↺</button>',
                '<button type="button" class="zy-btn so-preview-icon-btn" data-view-right title="تدوير لليمين">↻</button>',
                '<button type="button" class="zy-btn" data-view-actual title="الحجم الحقيقي">100%</button>',
                '<button type="button" class="zy-btn so-preview-icon-btn" data-view-expand title="توسيع المعاينة">⛶</button>',
                '<button type="button" class="zy-btn" data-view-reset title="إعادة ضبط الصورة">إعادة</button>'
            ].join("");
            body.append(toolbar);
        }

        const zoomLabel = toolbar.querySelector("[data-view-zoom]");
        const state = {
            scale: 1,
            rotation: 0,
            panX: 0,
            panY: 0,
            dragging: false,
            lastX: 0,
            lastY: 0,
            fitMode: true
        };

        const clampScale = value => Math.min(8, Math.max(0.05, value));

        const renderImage = () => {
            image.style.transform =
                `translate(-50%, -50%) translate(${state.panX}px, ${state.panY}px) rotate(${state.rotation}deg) scale(${state.scale})`;
            if (zoomLabel) {
                zoomLabel.textContent = `${Math.round(state.scale * 100)}%`;
            }
        };

        const fitImage = () => {
            if (!image.naturalWidth || !image.naturalHeight) return;
            const width = viewport.clientWidth;
            const height = viewport.clientHeight;
            if (width <= 0 || height <= 0) return;

            const quarterTurn = Math.abs(state.rotation % 180) === 90;
            const visualWidth = quarterTurn
                ? image.naturalHeight
                : image.naturalWidth;
            const visualHeight = quarterTurn
                ? image.naturalWidth
                : image.naturalHeight;

            state.scale = clampScale(Math.min(
                width / visualWidth,
                height / visualHeight
            ) * 0.98);
            state.panX = 0;
            state.panY = 0;
            state.fitMode = true;
            renderImage();
        };

        const setManualScale = scale => {
            state.scale = clampScale(scale);
            state.fitMode = false;
            renderImage();
        };

        const resetImage = () => {
            state.rotation = 0;
            state.panX = 0;
            state.panY = 0;
            state.fitMode = true;
            fitImage();
        };

        const setImageMode = enabled => {
            viewport.hidden = !enabled;
            toolbar.hidden = !enabled;
            frame.hidden = enabled;
            body.classList.toggle("is-image-preview", enabled);
            body.classList.toggle("is-document-preview", !enabled);
        };

        image.addEventListener("load", () => {
            requestAnimationFrame(fitImage);
        });

        toolbar.addEventListener("click", event => {
            const button = event.target.closest("button");
            if (!button) return;

            if (button.matches("[data-view-fit]")) {
                fitImage();
            } else if (button.matches("[data-view-in]")) {
                setManualScale(state.scale * 1.2);
            } else if (button.matches("[data-view-out]")) {
                setManualScale(state.scale / 1.2);
            } else if (button.matches("[data-view-left]")) {
                state.rotation = (state.rotation - 90) % 360;
                fitImage();
            } else if (button.matches("[data-view-right]")) {
                state.rotation = (state.rotation + 90) % 360;
                fitImage();
            } else if (button.matches("[data-view-actual]")) {
                state.panX = 0;
                state.panY = 0;
                setManualScale(1);
            } else if (button.matches("[data-view-expand]")) {
                dialog.classList.toggle("is-expanded");
                requestAnimationFrame(() => {
                    if (state.fitMode) fitImage();
                    else renderImage();
                });
            } else if (button.matches("[data-view-reset]")) {
                resetImage();
            }
        });

        viewport.addEventListener("wheel", event => {
            if (image.hidden) return;
            event.preventDefault();
            const factor = event.deltaY < 0 ? 1.12 : 1 / 1.12;
            setManualScale(state.scale * factor);
        }, { passive: false });

        viewport.addEventListener("pointerdown", event => {
            if (image.hidden) return;
            state.dragging = true;
            state.fitMode = false;
            state.lastX = event.clientX;
            state.lastY = event.clientY;
            viewport.classList.add("is-dragging");
            viewport.setPointerCapture(event.pointerId);
        });

        viewport.addEventListener("pointermove", event => {
            if (!state.dragging) return;
            state.panX += event.clientX - state.lastX;
            state.panY += event.clientY - state.lastY;
            state.lastX = event.clientX;
            state.lastY = event.clientY;
            renderImage();
        });

        const stopDragging = event => {
            if (!state.dragging) return;
            state.dragging = false;
            viewport.classList.remove("is-dragging");
            if (event.pointerId !== undefined &&
                viewport.hasPointerCapture(event.pointerId)) {
                viewport.releasePointerCapture(event.pointerId);
            }
        };

        viewport.addEventListener("pointerup", stopDragging);
        viewport.addEventListener("pointercancel", stopDragging);
        viewport.addEventListener("dblclick", fitImage);

        const observer = new ResizeObserver(() => {
            if (state.fitMode && !viewport.hidden) {
                fitImage();
            }
        });
        observer.observe(viewport);

        const closePreview = () => {
            frame.src = "about:blank";
            image.removeAttribute("src");
            image.hidden = true;
            setImageMode(false);
            dialog.classList.remove("is-expanded");
            if (dialog.open) {
                dialog.close();
            }
        };

        document.addEventListener("click", event => {
            const openButton = event.target.closest("[data-preview-open]");
            if (openButton) {
                const url = openButton.dataset.previewUrl;
                if (!url) return;

                const previewName =
                    openButton.dataset.previewName || "معاينة المستند";
                if (title) {
                    title.textContent = previewName;
                }

                const isImage = /\.(png|jpe?g|webp)$/i.test(previewName);
                if (isImage) {
                    frame.src = "about:blank";
                    setImageMode(true);
                    state.rotation = 0;
                    state.panX = 0;
                    state.panY = 0;
                    state.fitMode = true;
                    image.src = url;
                    image.alt = previewName;
                    image.hidden = false;
                } else {
                    image.removeAttribute("src");
                    image.hidden = true;
                    setImageMode(false);
                    frame.src = url;
                }

                dialog.showModal();
                if (isImage && image.complete) {
                    requestAnimationFrame(fitImage);
                }
                return;
            }

            if (event.target.closest("[data-preview-close]")) {
                closePreview();
            }
        });

        dialog.addEventListener("click", event => {
            if (event.target === dialog) {
                closePreview();
            }
        });

        dialog.addEventListener("cancel", event => {
            event.preventDefault();
            closePreview();
        });

        dialog.addEventListener("close", () => {
            frame.src = "about:blank";
            image.removeAttribute("src");
            image.hidden = true;
            setImageMode(false);
            dialog.classList.remove("is-expanded");
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
        initPreviewDialog();
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
