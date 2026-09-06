(function () {
    "use strict";

    var root = document.documentElement;
    var culture = root.lang || "ar-IQ";
    if (culture.toLowerCase().startsWith("ar")) return;

    // Elements whose contents must never be rewritten.
    // TEXTAREA is intentionally NOT here: its UI attributes (placeholder/title/
    // aria-label) are localizable, while its value/text content is user data.
    var ignoredElements = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "CODE", "PRE"]);
    var ignoredTextParents = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "TEXTAREA", "CODE", "PRE"]);
    var translatedMarker = "data-zy-localized";
    var attributes = [
        "placeholder",
        "title",
        "aria-label",
        "aria-description",
        "alt",
        "data-sidebar-label",
        "data-ky-title",
        "data-title",
        "data-tooltip",
        "data-original-title",
        "data-bs-original-title",
        "value"
    ];
    var catalog = Object.create(null);
    var composedKeys = [];
    var templateKeys = [];
    var templateFragmentKeys = [];
    var arabicText = /[\u0600-\u06ff]/;

    function escapeRegExp(value) {
        return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    }

    function buildTemplate(key) {
        var placeholders = [];
        var cursor = 0;
        var expression = "^";
        var matcher = /\{(\d+)\}/g;
        var match;

        while ((match = matcher.exec(key)) !== null) {
            expression += escapeRegExp(key.slice(cursor, match.index));
            expression += "([\\s\\S]+?)";
            placeholders.push(Number(match[1]));
            cursor = match.index + match[0].length;
        }

        expression += escapeRegExp(key.slice(cursor)) + "$";
        return {
            key: key,
            expression: new RegExp(expression),
            placeholders: placeholders
        };
    }

    function buildTemplateFragment(key) {
        var placeholders = [];
        var cursor = 0;
        var expression = "";
        var matcher = /\{(\d+)\}/g;
        var match;

        while ((match = matcher.exec(key)) !== null) {
            expression += escapeRegExp(key.slice(cursor, match.index));
            // Runtime placeholders here are dates, counts, labels or names.
            // Bound the capture so a fragment cannot swallow an entire page.
            expression += "([\\s\\S]{1,160}?)";
            placeholders.push(Number(match[1]));
            cursor = match.index + match[0].length;
        }

        expression += escapeRegExp(key.slice(cursor));

        var literal = key.replace(/\{\d+\}/g, "");
        var segments = key.split(/\{\d+\}/g);

        return {
            key: key,
            expression: new RegExp(expression, "g"),
            placeholders: placeholders,
            literalLength: literal.length,
            stableStart: (segments[0] || "").trim().length >= 2,
            stableEnd: (segments[segments.length - 1] || "").trim().length >= 1
        };
    }

    function applyTemplate(template, match) {
        var translated = catalog[template.key];

        template.placeholders.forEach(function (placeholder, captureIndex) {
            translated = translated
                .split("{" + placeholder + "}")
                .join(match[captureIndex + 1]);
        });

        return translated;
    }

    function translateTemplate(key) {
        for (var index = 0; index < templateKeys.length; index += 1) {
            var template = templateKeys[index];
            var match = template.expression.exec(key);
            if (!match) continue;
            return applyTemplate(template, match);
        }

        return key;
    }

    function translateTemplateFragments(value) {
        var result = value;
        var changed = false;

        templateFragmentKeys.forEach(function (template) {
            result = result.replace(
                template.expression,
                function () {
                    var match = Array.prototype.slice.call(arguments);
                    changed = true;
                    return applyTemplate(template, match);
                });
        });

        return changed ? result : value;
    }

    function isExcluded(node) {
        var element = node instanceof Element ? node : node.parentElement;
        return !!(element && element.closest("[data-zy-no-localize]"));
    }

    function translateComposed(key) {
        var result = translateTemplateFragments(key);
        var changed = result !== key;

        composedKeys.forEach(function (source) {
            if (result.indexOf(source) === -1) return;
            result = result.split(source).join(catalog[source]);
            changed = true;
        });

        // Do not introduce a NEW half-translated result. Composition is accepted
        // only if it fully clears the Arabic source fragments. Exact translations
        // are still allowed to target another Arabic-script language.
        return changed && !arabicText.test(result) ? result : key;
    }

    function translateValue(value) {
        if (!value) return value;

        var leading = value.match(/^\s*/)[0];
        var trailing = value.match(/\s*$/)[0];
        var key = value.trim();

        if (!arabicText.test(key)) return value;

        var translated = Object.prototype.hasOwnProperty.call(catalog, key)
            ? catalog[key]
            : translateTemplate(key);

        // Important for templates such as:
        //   "تسجيل بصمة الآن — {0}" -> "Punch now — دخول"
        // The outer template is translated, but the injected value still needs
        // one more catalog/composition pass.
        if (translated !== key && arabicText.test(translated)) {
            var completed = translateComposed(translated);
            if (completed !== translated) translated = completed;
        }

        if (translated === key) {
            translated = translateComposed(key);
        }

        return translated !== key
            ? leading + translated + trailing
            : value;
    }

    function translateElement(element) {
        if (!(element instanceof Element) || ignoredElements.has(element.tagName) || isExcluded(element)) return;

        attributes.forEach(function (name) {
            if (!element.hasAttribute(name)) return;
            if (name === "value" && !(element instanceof HTMLInputElement && /^(button|submit|reset)$/i.test(element.type))) return;

            var original = element.getAttribute(name);
            var translated = translateValue(original);
            if (translated !== original) element.setAttribute(name, translated);
        });

        if (!ignoredTextParents.has(element.tagName)) {
            Array.from(element.childNodes).forEach(function (node) {
                if (node.nodeType !== Node.TEXT_NODE) return;
                var translated = translateValue(node.nodeValue);
                if (translated !== node.nodeValue) node.nodeValue = translated;
            });
        }

        element.setAttribute(translatedMarker, "true");
    }

    function translateTree(node) {
        if (isExcluded(node)) return;

        if (node.nodeType === Node.TEXT_NODE) {
            if (node.parentElement && !ignoredTextParents.has(node.parentElement.tagName)) {
                var translated = translateValue(node.nodeValue);
                if (translated !== node.nodeValue) node.nodeValue = translated;
            }
            return;
        }

        if (!(node instanceof Element) || ignoredElements.has(node.tagName)) return;

        translateElement(node);
        node.querySelectorAll("*").forEach(translateElement);
    }

    function collectArabicValues() {
        var values = new Set();

        function add(value) {
            if (!value) return;
            var normalized = String(value).trim();
            if (!normalized || normalized.length > 1000 || !arabicText.test(normalized)) return;
            if (values.size >= 500) return;
            values.add(normalized);
        }

        add(document.title);

        document.querySelectorAll("*").forEach(function (element) {
            if (values.size >= 500) return;
            if (!(element instanceof Element) || ignoredElements.has(element.tagName) || isExcluded(element)) return;

            attributes.forEach(function (name) {
                if (values.size >= 500) return;
                if (!element.hasAttribute(name)) return;
                if (name === "value" && !(element instanceof HTMLInputElement && /^(button|submit|reset)$/i.test(element.type))) return;
                add(element.getAttribute(name));
            });

            if (ignoredTextParents.has(element.tagName)) return;

            Array.from(element.childNodes).forEach(function (node) {
                if (values.size >= 500) return;
                if (node.nodeType === Node.TEXT_NODE) add(node.nodeValue);
            });
        });

        return Array.from(values);
    }

    function fetchBusinessAliases() {
        var values = collectArabicValues();
        if (values.length === 0) {
            return Promise.resolve({ aliases: Object.create(null) });
        }

        return fetch("/Culture/BusinessCatalog", {
            method: "POST",
            cache: "no-store",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ values: values })
        })
            .then(function (response) {
                if (!response.ok) return { aliases: Object.create(null) };
                return response.json();
            })
            .catch(function () {
                // Anonymous surfaces (login/verify) intentionally cannot access
                // tenant business data. UI dictionary localization still proceeds.
                return { aliases: Object.create(null) };
            });
    }

    function rebuildMatchers() {
        templateKeys = Object.keys(catalog)
            .filter(function (key) {
                return arabicText.test(key) && /\{\d+\}/.test(key);
            })
            .map(buildTemplate)
            .sort(function (left, right) {
                return right.key.length - left.key.length;
            });

        templateFragmentKeys = Object.keys(catalog)
            .filter(function (key) {
                return arabicText.test(key) && /\{\d+\}/.test(key);
            })
            .map(buildTemplateFragment)
            .filter(function (template) {
                return template.literalLength >= 6 &&
                    template.stableStart &&
                    template.stableEnd;
            })
            .sort(function (left, right) {
                return right.key.length - left.key.length;
            });

        composedKeys = Object.keys(catalog)
            .filter(function (key) {
                return arabicText.test(key) &&
                    key.indexOf("{") === -1 &&
                    key.length > 1;
            })
            .sort(function (left, right) {
                return right.length - left.length;
            });
    }

    fetch("/Culture/Catalog?culture=" + encodeURIComponent(culture) + "&v=20260907-p4", {
        cache: "no-store",
        credentials: "same-origin",
        headers: { "Accept": "application/json" }
    })
        .then(function (response) {
            if (!response.ok) throw new Error("Localization catalog request failed.");
            return response.json();
        })
        .then(function (payload) {
            catalog = payload.translations || Object.create(null);

            return fetchBusinessAliases()
                .then(function (businessPayload) {
                    var aliases = businessPayload.aliases || Object.create(null);

                    Object.keys(aliases).forEach(function (source) {
                        if (!Object.prototype.hasOwnProperty.call(catalog, source)) {
                            catalog[source] = aliases[source];
                        }
                    });

                    return payload;
                });
        })
        .then(function (payload) {
            rebuildMatchers();

            root.dir = payload.direction || root.dir;
            document.title = translateValue(document.title);
            translateTree(document.body);

            new MutationObserver(function (mutations) {
                mutations.forEach(function (mutation) {
                    mutation.addedNodes.forEach(translateTree);
                    if (mutation.type === "characterData") translateTree(mutation.target);
                    if (mutation.type === "attributes") translateElement(mutation.target);
                });
            }).observe(document.body, {
                childList: true,
                subtree: true,
                characterData: true,
                attributes: true,
                attributeFilter: attributes
            });

            root.setAttribute("data-zy-localization-ready", "true");

            document.dispatchEvent(new CustomEvent("zynora:localization-ready", {
                detail: {
                    culture: payload.culture,
                    direction: payload.direction
                }
            }));
        })
        .catch(function () {
            root.setAttribute("data-zy-localization-ready", "fallback");
        });
})();