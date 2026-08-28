(function () {
    "use strict";

    var root = document.documentElement;
    var culture = root.lang || "ar-IQ";
    if (culture.toLowerCase().startsWith("ar")) return;

    var ignoredParents = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "TEXTAREA", "CODE", "PRE"]);
    var translatedMarker = "data-zy-localized";
    var attributes = ["placeholder", "title", "aria-label"];
    var catalog = Object.create(null);
    var composedKeys = [];
    var templateKeys = [];
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

    function translateTemplate(key) {
        for (var index = 0; index < templateKeys.length; index += 1) {
            var template = templateKeys[index];
            var match = template.expression.exec(key);
            if (!match) continue;

            var translated = catalog[template.key];
            template.placeholders.forEach(function (placeholder, captureIndex) {
                translated = translated.split("{" + placeholder + "}").join(match[captureIndex + 1]);
            });
            return translated;
        }

        return key;
    }

    function isExcluded(node) {
        var element = node instanceof Element ? node : node.parentElement;
        return !!(element && element.closest("[data-zy-no-localize]"));
    }

    function translateComposed(key) {
        var result = key;
        var changed = false;

        composedKeys.forEach(function (source) {
            if (result.indexOf(source) === -1) return;
            result = result.split(source).join(catalog[source]);
            changed = true;
        });

        // Never leave a half Arabic / half translated sentence.  Composition
        // is accepted only when the catalog covered every Arabic fragment;
        // otherwise the original value remains intact until its full key is
        // added to the catalog.
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
        if (translated === key) translated = translateComposed(key);
        return translated !== key ? leading + translated + trailing : value;
    }

    function translateElement(element) {
        if (!(element instanceof Element) || ignoredParents.has(element.tagName) || isExcluded(element)) return;

        attributes.forEach(function (name) {
            if (!element.hasAttribute(name)) return;
            var original = element.getAttribute(name);
            var translated = translateValue(original);
            if (translated !== original) element.setAttribute(name, translated);
        });

        Array.from(element.childNodes).forEach(function (node) {
            if (node.nodeType !== Node.TEXT_NODE) return;
            var translated = translateValue(node.nodeValue);
            if (translated !== node.nodeValue) node.nodeValue = translated;
        });

        element.setAttribute(translatedMarker, "true");
    }

    function translateTree(node) {
        if (isExcluded(node)) return;
        if (node.nodeType === Node.TEXT_NODE) {
            if (node.parentElement && !ignoredParents.has(node.parentElement.tagName)) {
                var translated = translateValue(node.nodeValue);
                if (translated !== node.nodeValue) node.nodeValue = translated;
            }
            return;
        }
        if (!(node instanceof Element) || ignoredParents.has(node.tagName)) return;
        translateElement(node);
        node.querySelectorAll("*").forEach(translateElement);
    }

    fetch("/Culture/Catalog?culture=" + encodeURIComponent(culture) + "&v=20260828-3", {
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
            templateKeys = Object.keys(catalog)
                .filter(function (key) {
                    return arabicText.test(key) && /\{\d+\}/.test(key);
                })
                .map(buildTemplate)
                .sort(function (left, right) { return right.key.length - left.key.length; });
            composedKeys = Object.keys(catalog)
                .filter(function (key) {
                    return arabicText.test(key) && key.indexOf("{") === -1 && key.length > 1;
                })
                .sort(function (left, right) { return right.length - left.length; });
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
                detail: { culture: payload.culture, direction: payload.direction }
            }));
        })
        .catch(function () {
            root.setAttribute("data-zy-localization-ready", "fallback");
        });
})();
