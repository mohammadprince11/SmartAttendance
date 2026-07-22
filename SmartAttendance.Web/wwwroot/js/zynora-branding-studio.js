(function () {
    "use strict";

    var studio = document.querySelector(".brand-studio");
    if (!studio) {
        return;
    }

    var previewUrl = studio.getAttribute("data-preview-url");
    var previewPane = document.getElementById("brand-preview");
    var validationBox = document.getElementById("brand-validation");
    var ladder = document.getElementById("brand-ladder");
    var styleTag = null;
    var debounceTimer = null;

    function fields() {
        return {
            primary: readField("PrimaryHex"),
            secondary: readField("SecondaryHex"),
            accent: readField("AccentHex")
        };
    }

    function readField(name) {
        var el = studio.querySelector('[data-color-field="' + name + '"] [data-role="hex"]');
        return el ? el.value.trim() : "";
    }

    // Keep the colour picker and the hex text box in sync, both directions.
    studio.querySelectorAll("[data-color-field]").forEach(function (group) {
        var picker = group.querySelector('[data-role="picker"]');
        var hex = group.querySelector('[data-role="hex"]');
        if (!picker || !hex) {
            return;
        }

        picker.addEventListener("input", function () {
            hex.value = picker.value.toUpperCase();
            schedulePreview();
        });

        hex.addEventListener("input", function () {
            var v = normalizeHex(hex.value);
            if (v) {
                picker.value = v;
            }
            schedulePreview();
        });
    });

    function normalizeHex(value) {
        if (!value) {
            return null;
        }
        var v = value.trim();
        if (v.charAt(0) !== "#") {
            v = "#" + v;
        }
        if (/^#[0-9a-fA-F]{6}$/.test(v)) {
            return v;
        }
        if (/^#[0-9a-fA-F]{3}$/.test(v)) {
            return "#" + v[1] + v[1] + v[2] + v[2] + v[3] + v[3];
        }
        return null;
    }

    function schedulePreview() {
        renderLadder();
        if (debounceTimer) {
            window.clearTimeout(debounceTimer);
        }
        debounceTimer = window.setTimeout(runPreview, 220);
    }

    function runPreview() {
        var f = fields();
        var primary = normalizeHex(f.primary);
        if (!primary || !previewUrl) {
            return;
        }

        var url = previewUrl +
            "&primary=" + encodeURIComponent(primary) +
            "&secondary=" + encodeURIComponent(normalizeHex(f.secondary) || "") +
            "&accent=" + encodeURIComponent(normalizeHex(f.accent) || "");

        fetch(url, { headers: { "X-Requested-With": "fetch" } })
            .then(function (r) { return r.json(); })
            .then(applyPreview)
            .catch(function () { /* keep last good preview */ });
    }

    function applyPreview(data) {
        if (!data) {
            return;
        }

        showValidation(data.level, data.messages);

        if (data.level === "Block" || !data.css) {
            return;
        }

        // Scope the compiled :root override to the preview pane only.
        var scoped = data.css.replace(":root{", "#brand-preview{");
        if (!styleTag) {
            styleTag = document.createElement("style");
            styleTag.id = "brand-preview-style";
            document.head.appendChild(styleTag);
        }
        styleTag.textContent = scoped;
    }

    function showValidation(level, messages) {
        if (!validationBox) {
            return;
        }
        var cls = level === "Block" ? "v-block" : (level === "Warn" ? "v-warn" : "v-pass");
        var text = (messages && messages.length) ? messages.join(" · ") : "";
        validationBox.innerHTML = '<span class="' + cls + '">' + escapeHtml(text) + "</span>";
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
        });
    }

    // Client-side lightness ladder purely for at-a-glance feedback.
    function renderLadder() {
        if (!ladder) {
            return;
        }
        var primary = normalizeHex(readField("PrimaryHex"));
        if (!primary) {
            return;
        }
        var hsl = hexToHsl(primary);
        var steps = [0.92, 0.82, 0.70, 0.58, 0.46, hsl.l, 0.30, 0.22, 0.14];
        ladder.innerHTML = "";
        steps.forEach(function (l) {
            var span = document.createElement("span");
            span.style.background = hslToHex(hsl.h, hsl.s, l);
            ladder.appendChild(span);
        });
    }

    function hexToHsl(hex) {
        var r = parseInt(hex.substr(1, 2), 16) / 255;
        var g = parseInt(hex.substr(3, 2), 16) / 255;
        var b = parseInt(hex.substr(5, 2), 16) / 255;
        var max = Math.max(r, g, b), min = Math.min(r, g, b);
        var h = 0, s, l = (max + min) / 2, d = max - min;
        if (d === 0) {
            s = 0;
        } else {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max === r) { h = (g - b) / d + (g < b ? 6 : 0); }
            else if (max === g) { h = (b - r) / d + 2; }
            else { h = (r - g) / d + 4; }
            h /= 6;
        }
        return { h: h, s: s, l: l };
    }

    function hslToHex(h, s, l) {
        var r, g, b;
        if (s === 0) {
            r = g = b = l;
        } else {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = hue(p, q, h + 1 / 3);
            g = hue(p, q, h);
            b = hue(p, q, h - 1 / 3);
        }
        return "#" + [r, g, b].map(function (x) {
            return ("0" + Math.round(x * 255).toString(16)).slice(-2);
        }).join("");
    }

    function hue(p, q, t) {
        if (t < 0) { t += 1; }
        if (t > 1) { t -= 1; }
        if (t < 1 / 6) { return p + (q - p) * 6 * t; }
        if (t < 1 / 2) { return q; }
        if (t < 2 / 3) { return p + (q - p) * (2 / 3 - t) * 6; }
        return p;
    }

    // Make the preview nav interactive: clicking a tab activates it (showing the
    // brand's active state on any section) and updates the sample card text.
    var nav = document.getElementById("bp-nav");
    if (nav) {
        var cardTitle = document.getElementById("bp-card-title");
        var cardText = document.getElementById("bp-card-text");

        var views = Array.prototype.slice.call(
            document.querySelectorAll("#brand-preview .bp-view"));

        function activateTab(tab) {
            nav.querySelectorAll(".bp-nav-item").forEach(function (t) {
                t.classList.toggle("is-active", t === tab);
            });
            if (cardTitle) {
                cardTitle.textContent = tab.getAttribute("data-bp-title") || cardTitle.textContent;
            }
            if (cardText) {
                cardText.textContent = tab.getAttribute("data-bp-text") || cardText.textContent;
            }
            var view = tab.getAttribute("data-bp-view");
            views.forEach(function (v) {
                v.hidden = v.getAttribute("data-bp-view") !== view;
            });
        }

        nav.addEventListener("click", function (e) {
            var tab = e.target.closest(".bp-nav-item");
            if (tab) {
                activateTab(tab);
            }
        });

        nav.addEventListener("keydown", function (e) {
            if (e.key === "Enter" || e.key === " ") {
                var tab = e.target.closest(".bp-nav-item");
                if (tab) {
                    e.preventDefault();
                    activateTab(tab);
                }
            }
        });
    }

    // Initial paint.
    renderLadder();
    runPreview();
})();
