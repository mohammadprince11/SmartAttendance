(() => {
    const params = new URLSearchParams(window.location.search);
    const sessionId = params.get("SessionId") ?? "current";
    const scrollKey =
        `zynora:smart-onboarding-review:scroll:${sessionId}`;

    if ("scrollRestoration" in history) {
        history.scrollRestoration = "manual";
    }

    document
        .querySelectorAll("form[data-preserve-scroll]")
        .forEach(form => {
            form.addEventListener("submit", () => {
                const fieldId = form.dataset.fieldId ?? null;
                sessionStorage.setItem(
                    scrollKey,
                    JSON.stringify({
                        y: window.scrollY,
                        fieldId
                    }));
            });
        });

    const saved = sessionStorage.getItem(scrollKey);
    if (saved) {
        sessionStorage.removeItem(scrollKey);
        try {
            const state = JSON.parse(saved);
            const y = Number(state?.y);
            if (Number.isFinite(y)) {
                requestAnimationFrame(() => {
                    window.scrollTo({
                        top: Math.max(0, y),
                        left: 0,
                        behavior: "auto"
                    });
                });
            }
        } catch {
            // Invalid transient state is safe to ignore.
        }
    }

    const previewDialog =
        document.querySelector("[data-review-preview-dialog]");
    const previewFrame =
        previewDialog?.querySelector("[data-review-preview-frame]");
    const previewTitle =
        previewDialog?.querySelector("[data-review-preview-title]");
    const previewBody =
        previewDialog?.querySelector(".sor-preview-body");

    if (previewDialog && previewFrame && previewBody) {
        let previewViewport =
            previewBody.querySelector(".sor-preview-viewport");
        if (!previewViewport) {
            previewViewport = document.createElement("div");
            previewViewport.className = "sor-preview-viewport";
            previewViewport.hidden = true;
            previewBody.prepend(previewViewport);
        }

        let previewImage =
            previewDialog.querySelector("[data-review-preview-image]");
        if (!previewImage) {
            previewImage = document.createElement("img");
            previewImage.setAttribute("data-review-preview-image", "");
            previewImage.className = "sor-preview-image";
            previewImage.alt = "معاينة المستند المحمي";
            previewImage.hidden = true;
        }
        if (previewImage.parentElement !== previewViewport) {
            previewViewport.append(previewImage);
        }

        let previewToolbar =
            previewBody.querySelector(".sor-preview-toolbar");
        if (!previewToolbar) {
            previewToolbar = document.createElement("div");
            previewToolbar.className = "sor-preview-toolbar";
            previewToolbar.hidden = true;
            previewToolbar.setAttribute("role", "toolbar");
            previewToolbar.setAttribute(
                "aria-label",
                "أدوات التحكم بالمعاينة");
            previewToolbar.innerHTML = [
                '<button type="button" class="zy-btn" data-view-fit title="عرض الصورة كاملة">ملاءمة</button>',
                '<button type="button" class="zy-btn sor-preview-icon-btn" data-view-out title="تصغير">−</button>',
                '<span class="sor-preview-zoom" data-view-zoom>100%</span>',
                '<button type="button" class="zy-btn sor-preview-icon-btn" data-view-in title="تكبير">+</button>',
                '<button type="button" class="zy-btn sor-preview-icon-btn" data-view-left title="تدوير لليسار">↺</button>',
                '<button type="button" class="zy-btn sor-preview-icon-btn" data-view-right title="تدوير لليمين">↻</button>',
                '<button type="button" class="zy-btn" data-view-actual title="الحجم الحقيقي">100%</button>',
                '<button type="button" class="zy-btn sor-preview-icon-btn" data-view-expand title="توسيع المعاينة">⛶</button>',
                '<button type="button" class="zy-btn" data-view-reset title="إعادة ضبط الصورة">إعادة</button>'
            ].join("");
            previewBody.append(previewToolbar);
        }

        const zoomLabel =
            previewToolbar.querySelector("[data-view-zoom]");
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

        const clampScale = value =>
            Math.min(8, Math.max(0.05, value));

        const renderImage = () => {
            previewImage.style.transform =
                `translate(-50%, -50%) translate(${state.panX}px, ${state.panY}px) rotate(${state.rotation}deg) scale(${state.scale})`;
            if (zoomLabel) {
                zoomLabel.textContent =
                    `${Math.round(state.scale * 100)}%`;
            }
        };

        const fitImage = () => {
            if (!previewImage.naturalWidth ||
                !previewImage.naturalHeight) return;

            const width = previewViewport.clientWidth;
            const height = previewViewport.clientHeight;
            if (width <= 0 || height <= 0) return;

            const quarterTurn =
                Math.abs(state.rotation % 180) === 90;
            const visualWidth = quarterTurn
                ? previewImage.naturalHeight
                : previewImage.naturalWidth;
            const visualHeight = quarterTurn
                ? previewImage.naturalWidth
                : previewImage.naturalHeight;

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
            previewViewport.hidden = !enabled;
            previewToolbar.hidden = !enabled;
            previewFrame.hidden = enabled;
            previewBody.classList.toggle(
                "is-image-preview",
                enabled);
            previewBody.classList.toggle(
                "is-document-preview",
                !enabled);
        };

        previewImage.addEventListener("load", () => {
            requestAnimationFrame(fitImage);
        });

        previewToolbar.addEventListener("click", event => {
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
                previewDialog.classList.toggle("is-expanded");
                requestAnimationFrame(() => {
                    if (state.fitMode) fitImage();
                    else renderImage();
                });
            } else if (button.matches("[data-view-reset]")) {
                resetImage();
            }
        });

        previewViewport.addEventListener("wheel", event => {
            if (previewImage.hidden) return;
            event.preventDefault();
            const factor =
                event.deltaY < 0 ? 1.12 : 1 / 1.12;
            setManualScale(state.scale * factor);
        }, { passive: false });

        previewViewport.addEventListener(
            "pointerdown",
            event => {
                if (previewImage.hidden) return;
                state.dragging = true;
                state.fitMode = false;
                state.lastX = event.clientX;
                state.lastY = event.clientY;
                previewViewport.classList.add("is-dragging");
                previewViewport.setPointerCapture(event.pointerId);
            });

        previewViewport.addEventListener(
            "pointermove",
            event => {
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
            previewViewport.classList.remove("is-dragging");
            if (event.pointerId !== undefined &&
                previewViewport.hasPointerCapture(event.pointerId)) {
                previewViewport.releasePointerCapture(event.pointerId);
            }
        };

        previewViewport.addEventListener(
            "pointerup",
            stopDragging);
        previewViewport.addEventListener(
            "pointercancel",
            stopDragging);
        previewViewport.addEventListener(
            "dblclick",
            fitImage);

        const observer = new ResizeObserver(() => {
            if (state.fitMode && !previewViewport.hidden) {
                fitImage();
            }
        });
        observer.observe(previewViewport);

        const closePreview = () => {
            previewFrame.src = "about:blank";
            previewImage.removeAttribute("src");
            previewImage.hidden = true;
            setImageMode(false);
            previewDialog.classList.remove("is-expanded");
            if (previewDialog.open) {
                previewDialog.close();
            }
        };

        document.addEventListener("click", event => {
            const openButton =
                event.target.closest("[data-review-preview-open]");
            if (openButton) {
                const url = openButton.dataset.previewUrl;
                if (!url) return;

                const previewName =
                    openButton.dataset.previewName ||
                    "معاينة المستند";
                if (previewTitle) {
                    previewTitle.textContent = previewName;
                }

                const isImage =
                    /\.(png|jpe?g|webp)$/i.test(previewName);
                if (isImage) {
                    previewFrame.src = "about:blank";
                    setImageMode(true);
                    state.rotation = 0;
                    state.panX = 0;
                    state.panY = 0;
                    state.fitMode = true;
                    previewImage.src = url;
                    previewImage.alt = previewName;
                    previewImage.hidden = false;
                } else {
                    previewImage.removeAttribute("src");
                    previewImage.hidden = true;
                    setImageMode(false);
                    previewFrame.src = url;
                }

                previewDialog.showModal();
                if (isImage && previewImage.complete) {
                    requestAnimationFrame(fitImage);
                }
                return;
            }

            if (event.target.closest(
                    "[data-review-preview-close]")) {
                closePreview();
            }
        });

        previewDialog.addEventListener("click", event => {
            if (event.target === previewDialog) closePreview();
        });
        previewDialog.addEventListener("cancel", event => {
            event.preventDefault();
            closePreview();
        });
        previewDialog.addEventListener("close", () => {
            previewFrame.src = "about:blank";
            previewImage.removeAttribute("src");
            previewImage.hidden = true;
            setImageMode(false);
            previewDialog.classList.remove("is-expanded");
        });
    }

    const branch = document.getElementById("sor-branch");
    const department = document.getElementById("sor-department");
    if (!branch || !department) {
        return;
    }

    const normalizePositiveId = value => {
        const parsed = Number.parseInt(
            String(value ?? "").trim(),
            10);

        return Number.isFinite(parsed) && parsed > 0
            ? parsed
            : 0;
    };

    const departmentOptions = Array.from(department.options);

    const syncDepartments = (resetSelection = false) => {
        const branchId = normalizePositiveId(branch.value);
        const previousValue = resetSelection
            ? "0"
            : String(department.value || "0");

        let previousStillAvailable = false;

        for (const option of departmentOptions) {
            const optionId = normalizePositiveId(option.value);

            // value=0 is always the placeholder.
            if (optionId === 0) {
                option.hidden = false;
                option.disabled = false;
                continue;
            }

            const optionBranchId =
                normalizePositiveId(option.dataset.branch);

            // BranchId=0 means an independent/company-level department.
            const matchesBranch =
                branchId > 0 &&
                (optionBranchId === 0 ||
                 optionBranchId === branchId);

            option.hidden = !matchesBranch;
            option.disabled = !matchesBranch;

            if (matchesBranch &&
                option.value === previousValue) {
                previousStillAvailable = true;
            }
        }

        department.disabled = branchId === 0;
        department.value =
            previousStillAvailable
                ? previousValue
                : "0";

        if (typeof window.ZynoraRefreshSelectSystem === "function") {
            window.ZynoraRefreshSelectSystem();
        }
    };

    branch.addEventListener("change", () => {
        syncDepartments(true);
        department.dispatchEvent(
            new Event("change", { bubbles: true }));
    });

    syncDepartments(false);

    const motherCountry =
        document.getElementById("sor-mother-country");
    const motherCity =
        document.getElementById("sor-mother-city");

    if (motherCountry && motherCity) {
        const cityOptions = Array.from(motherCity.options);

        const syncMotherCities = () => {
            const country = motherCountry.value;
            const selected = motherCity.value;
            let selectedStillVisible = false;

            for (const option of cityOptions) {
                if (!option.value) {
                    option.hidden = false;
                    option.disabled = false;
                    continue;
                }

                const matches =
                    !country ||
                    option.dataset.country === country;

                option.hidden = !matches;
                option.disabled = !matches;

                if (matches && option.value === selected) {
                    selectedStillVisible = true;
                }
            }

            if (!selectedStillVisible) {
                motherCity.value = "";
            }
        };

        motherCountry.addEventListener(
            "change",
            syncMotherCities);
        syncMotherCities();
    }
})();
