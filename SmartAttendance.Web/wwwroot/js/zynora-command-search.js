(function () {
    "use strict";

    var root = document.querySelector("[data-zy-command-search]");
    if (!root) return;

    var input = root.querySelector("input[type='search']");
    var results = root.querySelector("[role='listbox']");
    if (!input || !results) return;

    var seen = Object.create(null);
    var pages = Array.prototype.slice.call(document.querySelectorAll(".zynora-nav a[href]"))
        .map(function (link) {
            var href = link.getAttribute("href") || "";
            if (!href || href === "#" || seen[href]) return null;
            seen[href] = true;
            var group = link.closest(".zynora-nav-group");
            var summary = group ? group.querySelector(":scope > summary .zynora-nav-text") : null;
            return {
                label: (link.textContent || "").replace(/\s+/g, " ").trim(),
                group: summary ? (summary.textContent || "").trim() : "الرئيسية",
                href: href
            };
        })
        .filter(function (page) { return page && page.label; });

    var visible = [];
    var selected = -1;

    function normalize(value) {
        return (value || "")
            .toLocaleLowerCase("ar")
            .replace(/[إأآ]/g, "ا")
            .replace(/ى/g, "ي")
            .replace(/ة/g, "ه")
            .replace(/[\u064B-\u065F\u0670]/g, "")
            .trim();
    }

    function close() {
        results.hidden = true;
        results.textContent = "";
        input.setAttribute("aria-expanded", "false");
        selected = -1;
    }

    function select(index) {
        if (!visible.length) return;
        selected = (index + visible.length) % visible.length;
        Array.prototype.forEach.call(results.querySelectorAll("[role='option']"), function (item, itemIndex) {
            item.setAttribute("aria-selected", itemIndex === selected ? "true" : "false");
            if (itemIndex === selected) item.scrollIntoView({ block: "nearest" });
        });
    }

    function go(page) {
        if (page && page.href) window.location.assign(page.href);
    }

    function render() {
        var query = normalize(input.value);
        results.textContent = "";
        selected = -1;
        if (!query) {
            close();
            return;
        }

        visible = pages.filter(function (page) {
            return normalize(page.label + " " + page.group + " " + page.href).indexOf(query) >= 0;
        }).slice(0, 10);

        if (!visible.length) {
            var empty = document.createElement("div");
            empty.className = "zy-command-search__empty";
            empty.textContent = "لا توجد صفحة مطابقة ضمن صلاحياتك.";
            results.appendChild(empty);
        } else {
            visible.forEach(function (page, index) {
                var item = document.createElement("button");
                item.type = "button";
                item.className = "zy-command-search__result";
                item.setAttribute("role", "option");
                item.setAttribute("aria-selected", "false");

                var label = document.createElement("b");
                label.textContent = page.label;
                var group = document.createElement("small");
                group.textContent = page.group;
                var path = document.createElement("code");
                path.textContent = page.href.split("?")[0].split("#")[0];
                item.appendChild(label);
                item.appendChild(path);
                item.appendChild(group);
                item.addEventListener("mouseenter", function () { select(index); });
                item.addEventListener("click", function () { go(page); });
                results.appendChild(item);
            });
        }

        results.hidden = false;
        input.setAttribute("aria-expanded", "true");
    }

    input.addEventListener("input", render);
    input.addEventListener("keydown", function (event) {
        if (event.key === "ArrowDown") { event.preventDefault(); select(selected + 1); }
        else if (event.key === "ArrowUp") { event.preventDefault(); select(selected - 1); }
        else if (event.key === "Enter" && visible.length) { event.preventDefault(); go(visible[selected >= 0 ? selected : 0]); }
        else if (event.key === "Escape") { close(); input.blur(); }
    });

    document.addEventListener("keydown", function (event) {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
            event.preventDefault();
            input.focus();
            input.select();
        }
    });

    document.addEventListener("pointerdown", function (event) {
        if (!root.contains(event.target)) close();
    });
})();
