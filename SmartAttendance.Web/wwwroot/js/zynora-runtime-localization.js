(function () {
    "use strict";

    var root = document.documentElement;
    var culture = root.lang || "ar-IQ";
    if (culture.toLowerCase().startsWith("ar")) return;

    var targetDirection = (root.dir || "").toLowerCase();

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
    var normalizedCatalogKeys = Object.create(null);
    var composedKeys = [];
    var templateKeys = [];
    var templateFragmentKeys = [];
    var arabicText = /[\u0600-\u06ff]/;

    function escapeRegExp(value) {
        return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    }

    function normalizeLookupKey(value) {
        return String(value || "")
            .replace(/\s+/g, " ")
            .trim();
    }

    function isUsableTranslation(source, translated) {
        if (!translated || translated === source) return false;

        // An LTR UI must never be made worse by emitting a half-Arabic
        // translation. This is direction-based rather than English-specific.
        if (targetDirection === "ltr" &&
            arabicText.test(source) &&
            arabicText.test(translated)) {
            return false;
        }

        return true;
    }

    function isUsableCatalogEntry(key) {
        return isUsableTranslation(key, catalog[key]);
    }

    function getExactCatalogTranslation(key) {
        if (Object.prototype.hasOwnProperty.call(catalog, key)) {
            return catalog[key];
        }

        var normalized = normalizeLookupKey(key);
        var mappedKey = normalizedCatalogKeys[normalized];

        if (!mappedKey) return null;
        return catalog[mappedKey];
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

            var translated = applyTemplate(template, match);
            if (isUsableTranslation(key, translated)) {
                return translated;
            }
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
                    var translated = applyTemplate(template, match);

                    if (!isUsableTranslation(match[0], translated)) {
                        return match[0];
                    }

                    changed = true;
                    return translated;
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

            result = result
                .split(source)
                .join(catalog[source]);

            changed = true;
        });

        if (!changed) return key;

        // On an LTR target, composition is accepted only after every Arabic
        // source fragment has been cleared. RTL target languages keep their
        // script and are therefore not subject to this rule.
        if (targetDirection === "ltr" &&
            arabicText.test(key) &&
            arabicText.test(result)) {
            return key;
        }

        return result;
    }

    function translateValue(value) {
        if (!value) return value;

        var leading = value.match(/^\s*/)[0];
        var trailing = value.match(/\s*$/)[0];
        var key = value.trim();

        if (!arabicText.test(key)) return value;

        var exact = getExactCatalogTranslation(key);
        var translated =
            exact !== null &&
            isUsableTranslation(key, exact)
                ? exact
                : key;

        if (translated === key) {
            translated = translateTemplate(key);
        }

        // A template can inject another Arabic catalog key into an otherwise
        // translated sentence. Give that result one clean composition pass.
        if (translated !== key &&
            targetDirection === "ltr" &&
            arabicText.test(translated)) {
            var completed = translateComposed(translated);

            if (completed !== translated &&
                isUsableTranslation(key, completed)) {
                translated = completed;
            } else {
                translated = key;
            }
        }

        if (translated === key) {
            translated = translateComposed(key);
        }

        return translated !== key
            ? leading + translated + trailing
            : value;
    }

    function translateElement(element) {
        if (!(element instanceof Element) ||
            ignoredElements.has(element.tagName) ||
            isExcluded(element)) {
            return;
        }

        attributes.forEach(function (name) {
            if (!element.hasAttribute(name)) return;

            if (name === "value" &&
                !(element instanceof HTMLInputElement &&
                    /^(button|submit|reset)$/i.test(element.type))) {
                return;
            }

            var original = element.getAttribute(name);
            var translated = translateValue(original);

            if (translated !== original) {
                element.setAttribute(name, translated);
            }
        });

        if (!ignoredTextParents.has(element.tagName)) {
            Array.from(element.childNodes).forEach(function (node) {
                if (node.nodeType !== Node.TEXT_NODE) return;

                var translated = translateValue(node.nodeValue);

                if (translated !== node.nodeValue) {
                    node.nodeValue = translated;
                }
            });
        }

        element.setAttribute(translatedMarker, "true");
    }

    function translateTree(node) {
        if (isExcluded(node)) return;

        if (node.nodeType === Node.TEXT_NODE) {
            if (node.parentElement &&
                !ignoredTextParents.has(node.parentElement.tagName)) {
                var translated = translateValue(node.nodeValue);

                if (translated !== node.nodeValue) {
                    node.nodeValue = translated;
                }
            }

            return;
        }

        if (!(node instanceof Element) ||
            ignoredElements.has(node.tagName)) {
            return;
        }

        translateElement(node);
        node.querySelectorAll("*").forEach(translateElement);
    }

    function collectArabicValues() {
        var values = new Set();

        function add(value) {
            if (!value) return;

            var normalized = String(value).trim();

            if (!normalized ||
                normalized.length > 1000 ||
                !arabicText.test(normalized)) {
                return;
            }

            if (values.size >= 500) return;
            values.add(normalized);
        }

        add(document.title);

        document.querySelectorAll("*").forEach(function (element) {
            if (values.size >= 500) return;

            if (!(element instanceof Element) ||
                ignoredElements.has(element.tagName) ||
                isExcluded(element)) {
                return;
            }

            attributes.forEach(function (name) {
                if (values.size >= 500) return;
                if (!element.hasAttribute(name)) return;

                if (name === "value" &&
                    !(element instanceof HTMLInputElement &&
                        /^(button|submit|reset)$/i.test(element.type))) {
                    return;
                }

                add(element.getAttribute(name));
            });

            if (ignoredTextParents.has(element.tagName)) return;

            Array.from(element.childNodes).forEach(function (node) {
                if (values.size >= 500) return;
                if (node.nodeType === Node.TEXT_NODE) {
                    add(node.nodeValue);
                }
            });
        });

        return Array.from(values);
    }

    function fetchBusinessAliases() {
        var values = collectArabicValues();

        if (values.length === 0) {
            return Promise.resolve({
                aliases: Object.create(null)
            });
        }

        return fetch("/Culture/BusinessCatalog", {
            method: "POST",
            cache: "no-store",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                values: values
            })
        })
            .then(function (response) {
                if (!response.ok) {
                    return {
                        aliases: Object.create(null)
                    };
                }

                return response.json();
            })
            .catch(function () {
                // Anonymous surfaces intentionally cannot access tenant business
                // data. UI dictionary localization still proceeds.
                return {
                    aliases: Object.create(null)
                };
            });
    }

    function rebuildMatchers() {
        normalizedCatalogKeys = Object.create(null);

        Object.keys(catalog).forEach(function (key) {
            var normalized = normalizeLookupKey(key);

            if (!normalized) return;

            if (!Object.prototype.hasOwnProperty.call(
                    normalizedCatalogKeys,
                    normalized)) {
                normalizedCatalogKeys[normalized] = key;
                return;
            }

            // A normalized collision must never guess between two source keys.
            if (normalizedCatalogKeys[normalized] !== key) {
                normalizedCatalogKeys[normalized] = null;
            }
        });

        templateKeys = Object.keys(catalog)
            .filter(function (key) {
                return arabicText.test(key) &&
                    /\{\d+\}/.test(key) &&
                    isUsableCatalogEntry(key);
            })
            .map(buildTemplate)
            .sort(function (left, right) {
                return right.key.length - left.key.length;
            });

        templateFragmentKeys = Object.keys(catalog)
            .filter(function (key) {
                return arabicText.test(key) &&
                    /\{\d+\}/.test(key) &&
                    isUsableCatalogEntry(key);
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
                    key.length > 1 &&
                    isUsableCatalogEntry(key);
            })
            .sort(function (left, right) {
                return right.length - left.length;
            });
    }

    fetch("/Culture/Catalog?culture=" +
        encodeURIComponent(culture) +
        "&v=20260907-p5", {
        cache: "no-store",
        credentials: "same-origin",
        headers: {
            "Accept": "application/json"
        }
    })
        .then(function (response) {
            if (!response.ok) {
                throw new Error(
                    "Localization catalog request failed."
                );
            }

            return response.json();
        })
        .then(function (payload) {
            catalog =
                payload.translations ||
                Object.create(null);

            targetDirection =
                (payload.direction ||
                    targetDirection ||
                    "")
                .toLowerCase();

            return fetchBusinessAliases()
                .then(function (businessPayload) {
                    var aliases =
                        businessPayload.aliases ||
                        Object.create(null);

                    Object.keys(aliases).forEach(function (source) {
                        var translated = aliases[source];

                        if (!Object.prototype.hasOwnProperty.call(
                                catalog,
                                source) ||
                            !isUsableTranslation(
                                source,
                                catalog[source])) {
                            catalog[source] = translated;
                        }
                    });

                    return payload;
                });
        })
        .then(function (payload) {
            rebuildMatchers();

            root.dir =
                payload.direction ||
                root.dir;

            document.title =
                translateValue(
                    document.title);

            translateTree(document.body);

            new MutationObserver(function (mutations) {
                mutations.forEach(function (mutation) {
                    mutation.addedNodes.forEach(
                        translateTree);

                    if (mutation.type === "characterData") {
                        translateTree(mutation.target);
                    }

                    if (mutation.type === "attributes") {
                        translateElement(mutation.target);
                    }
                });
            }).observe(document.body, {
                childList: true,
                subtree: true,
                characterData: true,
                attributes: true,
                attributeFilter: attributes
            });

            root.setAttribute(
                "data-zy-localization-ready",
                "true");

            document.dispatchEvent(
                new CustomEvent(
                    "zynora:localization-ready",
                    {
                        detail: {
                            culture: payload.culture,
                            direction: payload.direction
                        }
                    }
                )
            );
        })
        .catch(function () {
            root.setAttribute(
                "data-zy-localization-ready",
                "fallback");
        });
})();