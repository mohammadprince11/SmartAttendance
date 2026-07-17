(() => {
    const canvas = document.getElementById("orgCanvas");
    const search = document.getElementById("orgSearch");

    // Expand/collapse a single node when its toggle button is clicked.
    if (canvas) {
        canvas.addEventListener("click", (event) => {
            const toggle = event.target.closest(".org-node-toggle");
            if (!toggle) {
                return;
            }

            const item = toggle.closest(".org-tree-item");
            if (item) {
                item.classList.toggle("collapsed");
            }
        });
    }

    const setCollapsedAll = (collapsed) => {
        document
            .querySelectorAll(".org-tree-item")
            .forEach((item) => {
                if (item.querySelector(":scope > .org-tree-children")) {
                    item.classList.toggle("collapsed", collapsed);
                }
            });
    };

    const expandBtn = document.querySelector("[data-org-expand]");
    const collapseBtn = document.querySelector("[data-org-collapse]");

    if (expandBtn) {
        expandBtn.addEventListener("click", () => setCollapsedAll(false));
    }
    if (collapseBtn) {
        collapseBtn.addEventListener("click", () => setCollapsedAll(true));
    }

    // Live search: highlights matches across the tree and the unassigned grid,
    // dims non-matches, and reveals the ancestors of any matched tree node.
    if (search) {
        let timer = null;

        const apply = () => {
            const term = search.value.trim().toLowerCase();

            document
                .querySelectorAll(".org-node.org-hit")
                .forEach((node) => node.classList.remove("org-hit"));

            const treeItems = document.querySelectorAll(".org-tree-item");
            const icCards = document.querySelectorAll(".orgchart-ic-card");

            if (!term) {
                treeItems.forEach((item) => item.classList.remove("org-dim"));
                icCards.forEach((card) => card.classList.remove("org-dim"));
                return;
            }

            const matches = (el) =>
                (el.dataset.name || "").includes(term) ||
                (el.dataset.no || "").toLowerCase().includes(term);

            treeItems.forEach((item) => item.classList.add("org-dim"));

            treeItems.forEach((item) => {
                if (!matches(item)) {
                    return;
                }

                const node = item.querySelector(":scope > .org-node");
                if (node) {
                    node.classList.add("org-hit");
                }

                // Reveal the matched node and every ancestor.
                let current = item;
                while (current && current.classList.contains("org-tree-item")) {
                    current.classList.remove("org-dim", "collapsed");
                    current = current.parentElement.closest(".org-tree-item");
                }
            });

            icCards.forEach((card) => {
                card.classList.toggle("org-dim", !matches(card));
            });
        };

        search.addEventListener("input", () => {
            window.clearTimeout(timer);
            timer = window.setTimeout(apply, 120);
        });
    }
})();
