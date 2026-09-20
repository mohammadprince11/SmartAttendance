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
        let previewImage =
            previewDialog.querySelector("[data-review-preview-image]");
        if (!previewImage) {
            const body = previewDialog.querySelector(".sor-preview-body");
            previewImage = document.createElement("img");
            previewImage.setAttribute("data-review-preview-image", "");
            previewImage.className = "sor-preview-image";
            previewImage.alt = "معاينة المستند المحمي";
            previewImage.hidden = true;
            body?.prepend(previewImage);
        }
        const closePreview = () => {
            previewFrame.src = "about:blank";
            previewFrame.hidden = false;
            previewImage.removeAttribute("src");
            previewImage.hidden = true;
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
                    openButton.dataset.previewName || "معاينة المستند";
                if (previewTitle) {
                    previewTitle.textContent = previewName;
                }

                const isImage = /\.(png|jpe?g|webp)$/i.test(previewName);
                if (isImage) {
                    previewFrame.src = "about:blank";
                    previewFrame.hidden = true;
                    previewImage.src = url;
                    previewImage.alt = previewName;
                    previewImage.hidden = false;
                } else {
                    previewImage.removeAttribute("src");
                    previewImage.hidden = true;
                    previewFrame.hidden = false;
                    previewFrame.src = url;
                }

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
            previewFrame.hidden = false;
            previewImage.removeAttribute("src");
            previewImage.hidden = true;
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
