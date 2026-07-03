(() => {
    const DEFAULT_PAGE_SIZE = 10;
    const PAGE_SIZE_KEY = "smartAttendance.defaultPageSize";
    const PAGE_SIZES = [10, 25, 50, 100, "All"];
    const floatingGroups = [];

    document.addEventListener("DOMContentLoaded", () => {
        setupAllTables();
        bindFloatingEvents();
        updateFloatingControls();
    });

    function setupAllTables() {
        const tables = document.querySelectorAll("table.data-table");

        tables.forEach((table) => {
            if (table.dataset.paginationReady === "true") {
                return;
            }

            if (table.dataset.noPagination === "true") {
                return;
            }

            const tbody = table.querySelector("tbody");
            if (!tbody) {
                return;
            }

            const allRows = Array.from(tbody.querySelectorAll("tr"));

            if (!allRows.length) {
                return;
            }

            const isEmptyTable = allRows.length === 1 && isEmptyMessageRow(allRows[0]);
            if (isEmptyTable) {
                return;
            }

            table.dataset.paginationReady = "true";

            const tableCard = table.closest(".table-card") || table.parentElement;
            const searchForm = tableCard ? tableCard.querySelector(".search-form") : null;
            const canUseLiveClientSearch = searchForm ? isClientSideSearchForm(searchForm) : false;
            const searchInput = canUseLiveClientSearch ? searchForm.querySelector("input[type='text'], input[type='search']") : null;
            const clearButtonOrLink = canUseLiveClientSearch ? findClearElement(searchForm) : null;

            let currentPage = 1;
            let pageSize = getStoredPageSize();
            let currentQuery = searchInput ? searchInput.value.trim().toLowerCase() : "";

            const wrapper = document.createElement("div");
            wrapper.className = "sa-pagination-wrapper";

            const left = document.createElement("div");
            left.className = "sa-pagination-left";

            const label = document.createElement("span");
            label.className = "sa-pagination-label";
            label.textContent = "Rows per page";

            const select = document.createElement("select");
            select.className = "sa-page-size";

            PAGE_SIZES.forEach(size => {
                const option = document.createElement("option");
                option.value = String(size);
                option.textContent = String(size);
                select.appendChild(option);
            });

            select.value = String(pageSize);

            const info = document.createElement("span");
            info.className = "sa-pagination-info";

            left.appendChild(label);
            left.appendChild(select);
            left.appendChild(info);

            const right = document.createElement("div");
            right.className = "sa-pagination-right";

            const firstBtn = createButton("First");
            const prevBtn = createButton("Prev");
            const pageNumber = document.createElement("span");
            pageNumber.className = "sa-page-number";
            const nextBtn = createButton("Next");
            const lastBtn = createButton("Last");

            right.appendChild(firstBtn);
            right.appendChild(prevBtn);
            right.appendChild(pageNumber);
            right.appendChild(nextBtn);
            right.appendChild(lastBtn);

            wrapper.appendChild(left);
            wrapper.appendChild(right);

            table.parentNode.insertBefore(wrapper, table);

            const noResultsRow = document.createElement("tr");
            noResultsRow.className = "sa-no-results-row";
            noResultsRow.style.display = "none";

            const noResultsCell = document.createElement("td");
            noResultsCell.colSpan = getColumnCount(table);
            noResultsCell.textContent = "No matching results found.";

            noResultsRow.appendChild(noResultsCell);
            tbody.appendChild(noResultsRow);

            function getFilteredRows() {
                if (!currentQuery) {
                    return allRows;
                }

                return allRows.filter(row =>
                    normalizeText(row.innerText).includes(normalizeText(currentQuery)));
            }

            function render() {
                const filteredRows = getFilteredRows();
                const totalRows = filteredRows.length;
                const allSelected = String(pageSize) === "All";
                const numericPageSize = allSelected ? Math.max(totalRows, 1) : Number(pageSize);
                const totalPages = Math.max(1, Math.ceil(Math.max(totalRows, 1) / numericPageSize));

                if (currentPage > totalPages) {
                    currentPage = totalPages;
                }

                const startIndex = allSelected ? 0 : (currentPage - 1) * numericPageSize;
                const endIndex = allSelected ? totalRows : Math.min(startIndex + numericPageSize, totalRows);

                const visibleSet = new Set(filteredRows.slice(startIndex, endIndex));

                allRows.forEach((row) => {
                    row.style.display = visibleSet.has(row) ? "" : "none";
                });

                noResultsRow.style.display = totalRows === 0 ? "" : "none";

                const from = totalRows === 0 ? 0 : startIndex + 1;
                const to = totalRows === 0 ? 0 : endIndex;

                info.textContent = currentQuery
                    ? `Showing ${from}-${to} of ${totalRows} filtered`
                    : `Showing ${from}-${to} of ${totalRows}`;

                pageNumber.textContent = `${currentPage} / ${totalPages}`;

                firstBtn.disabled = currentPage <= 1 || allSelected || totalRows === 0;
                prevBtn.disabled = currentPage <= 1 || allSelected || totalRows === 0;
                nextBtn.disabled = currentPage >= totalPages || allSelected || totalRows === 0;
                lastBtn.disabled = currentPage >= totalPages || allSelected || totalRows === 0;

                updateFloatingControls();
            }

            select.addEventListener("change", () => {
                pageSize = select.value === "All" ? "All" : Number(select.value);
                localStorage.setItem(PAGE_SIZE_KEY, String(pageSize));
                currentPage = 1;
                render();
            });

            firstBtn.addEventListener("click", () => {
                currentPage = 1;
                render();
            });

            prevBtn.addEventListener("click", () => {
                currentPage = Math.max(1, currentPage - 1);
                render();
            });

            nextBtn.addEventListener("click", () => {
                const totalPages = getTotalPages(getFilteredRows().length, pageSize);
                currentPage = Math.min(totalPages, currentPage + 1);
                render();
            });

            lastBtn.addEventListener("click", () => {
                currentPage = getTotalPages(getFilteredRows().length, pageSize);
                render();
            });

            /*
             * Important:
             * Live client-side search must NOT hijack server action forms.
             * Pages like Attendance Processing, Import, Setup, Reports use date/file/select/post forms.
             * If we preventDefault on those forms, buttons like Process/Preview/Import will not send the selected dates to the server.
             */
            if (canUseLiveClientSearch && searchForm && searchInput) {
                searchForm.addEventListener("submit", (event) => {
                    event.preventDefault();
                    currentQuery = searchInput.value.trim().toLowerCase();
                    currentPage = 1;
                    render();
                });

                searchInput.addEventListener("input", () => {
                    currentQuery = searchInput.value.trim().toLowerCase();
                    currentPage = 1;
                    render();
                });

                if (clearButtonOrLink) {
                    clearButtonOrLink.addEventListener("click", (event) => {
                        event.preventDefault();
                        searchInput.value = "";
                        currentQuery = "";
                        currentPage = 1;
                        render();
                    });
                }
            }

            setupFloatingGroup(tableCard, searchForm, wrapper);
            render();
        });
    }

    function isClientSideSearchForm(form) {
        if (!form) {
            return false;
        }

        const method = (form.getAttribute("method") || "get").toLowerCase();

        if (method === "post") {
            return false;
        }

        if (form.querySelector("input[type='date'], input[type='file'], input[type='number'], textarea, select")) {
            return false;
        }

        const buttonsText = Array.from(form.querySelectorAll("button, a"))
            .map(x => normalizeText(x.innerText))
            .join(" ");

        const serverActionWords = [
            "process",
            "preview",
            "import",
            "upload",
            "assign",
            "generate",
            "save",
            "create",
            "update",
            "delete",
            "export"
        ];

        if (serverActionWords.some(word => buttonsText.includes(word))) {
            return false;
        }

        const textInputs = Array.from(form.querySelectorAll("input[type='text'], input[type='search']"));

        return textInputs.length === 1;
    }

    function setupFloatingGroup(tableCard, searchForm, paginationWrapper) {
        if (!tableCard || !paginationWrapper) {
            return;
        }

        const controls = [];

        if (searchForm) {
            controls.push(searchForm);
        }

        controls.push(paginationWrapper);

        const placeholders = controls.map(control => {
            const placeholder = document.createElement("div");
            placeholder.className = "sa-floating-placeholder";
            control.parentNode.insertBefore(placeholder, control);
            return placeholder;
        });

        floatingGroups.push({
            tableCard,
            controls,
            placeholders
        });
    }

    function bindFloatingEvents() {
        window.addEventListener("scroll", updateFloatingControls, { passive: true });
        window.addEventListener("resize", updateFloatingControls);
        document.addEventListener("input", updateFloatingControls);
    }

    function updateFloatingControls() {
        const topbar = document.querySelector(".topbar");
        const topOffset = (topbar ? topbar.getBoundingClientRect().height : 58) + 8;

        floatingGroups.forEach(group => {
            const cardRect = group.tableCard.getBoundingClientRect();
            const cardBottom = cardRect.bottom;
            const cardTop = cardRect.top;

            const totalControlsHeight = group.controls.reduce((sum, control) => {
                return sum + getElementOuterHeight(control);
            }, 0);

            const shouldFloat =
                cardTop < topOffset &&
                cardBottom > topOffset + totalControlsHeight + 80;

            if (!shouldFloat) {
                group.controls.forEach((control, index) => {
                    releaseControl(control, group.placeholders[index]);
                });
                return;
            }

            let currentTop = topOffset;

            group.controls.forEach((control, index) => {
                fixControl(control, group.placeholders[index], cardRect.left, cardRect.width, currentTop);
                currentTop += getElementOuterHeight(control);
            });
        });
    }

    function fixControl(control, placeholder, left, width, top) {
        if (!control.classList.contains("sa-fixed-control")) {
            placeholder.style.height = `${getElementOuterHeight(control)}px`;
            placeholder.style.display = "block";
            control.classList.add("sa-fixed-control");
        }

        control.style.top = `${top}px`;
        control.style.left = `${left}px`;
        control.style.width = `${width}px`;
    }

    function releaseControl(control, placeholder) {
        placeholder.style.display = "none";
        placeholder.style.height = "0px";

        control.classList.remove("sa-fixed-control");
        control.style.top = "";
        control.style.left = "";
        control.style.width = "";
    }

    function getElementOuterHeight(element) {
        const style = window.getComputedStyle(element);
        const marginTop = parseFloat(style.marginTop) || 0;
        const marginBottom = parseFloat(style.marginBottom) || 0;

        return element.getBoundingClientRect().height + marginTop + marginBottom;
    }

    function createButton(text) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "sa-page-btn";
        button.textContent = text;
        return button;
    }

    function getStoredPageSize() {
        const stored = localStorage.getItem(PAGE_SIZE_KEY);

        if (!stored) {
            return DEFAULT_PAGE_SIZE;
        }

        if (stored === "All") {
            return "All";
        }

        const parsed = Number(stored);

        if ([10, 25, 50, 100].includes(parsed)) {
            return parsed;
        }

        return DEFAULT_PAGE_SIZE;
    }

    function getTotalPages(totalRows, pageSize) {
        if (pageSize === "All") {
            return 1;
        }

        return Math.max(1, Math.ceil(Math.max(totalRows, 1) / Number(pageSize)));
    }

    function normalizeText(value) {
        return (value || "")
            .toString()
            .toLowerCase()
            .replace(/\s+/g, " ")
            .trim();
    }

    function getColumnCount(table) {
        const headerCells = table.querySelectorAll("thead th");
        if (headerCells && headerCells.length > 0) {
            return headerCells.length;
        }

        const firstRowCells = table.querySelectorAll("tbody tr:first-child td");
        return firstRowCells.length || 1;
    }

    function isEmptyMessageRow(row) {
        const text = normalizeText(row.innerText);
        return text.includes("no records") ||
            text.includes("no employees") ||
            text.includes("no data") ||
            text.includes("no preview") ||
            text.includes("no matching") ||
            text.includes("not found.");
    }

    function findClearElement(form) {
        const candidates = Array.from(form.querySelectorAll("a, button"));

        return candidates.find(element =>
            normalizeText(element.innerText) === "clear" ||
            normalizeText(element.innerText) === "cancel" ||
            normalizeText(element.innerText) === "reset");
    }
})();
