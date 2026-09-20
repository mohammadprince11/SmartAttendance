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

    if (previewDialog && previewFrame) {
        const fitImageDocument = () => {
            try {
                const doc = previewFrame.contentDocument;
                const image = doc?.querySelector("img");
                if (!doc || !image) return;

                doc.documentElement.style.inlineSize = "100%";
                doc.documentElement.style.blockSize = "100%";
                doc.documentElement.style.margin = "0";
                doc.documentElement.style.overflow = "hidden";

                if (doc.body) {
                    doc.body.style.inlineSize = "100%";
                    doc.body.style.blockSize = "100%";
                    doc.body.style.margin = "0";
                    doc.body.style.display = "flex";
                    doc.body.style.alignItems = "center";
                    doc.body.style.justifyContent = "center";
                    doc.body.style.overflow = "hidden";
                }

                image.style.display = "block";
                image.style.inlineSize = "auto";
                image.style.blockSize = "auto";
                image.style.maxInlineSize = "100%";
                image.style.maxBlockSize = "100%";
                image.style.objectFit = "contain";
                image.style.margin = "auto";
            } catch {
                // PDFs and browser-native viewers do not need image fitting.
            }
        };

        previewFrame.addEventListener("load", fitImageDocument);

        const closePreview = () => {
            previewFrame.src = "about:blank";
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

                if (previewTitle) {
                    previewTitle.textContent =
                        openButton.dataset.previewName ||
                        "معاينة المستند";
                }
                previewFrame.src = url;
                previewDialog.showModal();
                return;
            }

            if (event.target.closest("[data-review-preview-close]")) {
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
        });
    }

    const branch = document.getElementById("sor-branch");
    const department = document.getElementById("sor-department");
    if (!branch || !department) {
        return;
    }

    const options = Array.from(department.options);

    const syncDepartments = () => {
        const branchId = branch.value;
        const selected = department.value;
        let selectedStillVisible = false;

        for (const option of options) {
            if (!option.value) {
                option.hidden = false;
                option.disabled = false;
                continue;
            }

            const matches = option.dataset.branch === branchId;
            option.hidden = !matches;
            option.disabled = !matches;

            if (matches && option.value === selected) {
                selectedStillVisible = true;
            }
        }

        if (!selectedStillVisible) {
            department.value = "0";
        }
    };

    branch.addEventListener("change", syncDepartments);
    syncDepartments();

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
