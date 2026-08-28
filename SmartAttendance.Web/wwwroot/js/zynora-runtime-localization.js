(function () {
    "use strict";

    var root = document.documentElement;
    var culture = root.lang || "ar-IQ";
    if (culture.toLowerCase().startsWith("ar")) return;

    var ignoredParents = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "TEXTAREA", "CODE", "PRE"]);
    var translatedMarker = "data-zy-localized";
    var attributes = ["placeholder", "title", "aria-label"];
    var catalog = Object.create(null);

    function translateValue(value) {
        if (!value) return value;
        var leading = value.match(/^\s*/)[0];
        var trailing = value.match(/\s*$/)[0];
        var key = value.trim();
        return Object.prototype.hasOwnProperty.call(catalog, key)
            ? leading + catalog[key] + trailing
            : value;
    }

    function translateElement(element) {
        if (!(element instanceof Element) || ignoredParents.has(element.tagName)) return;

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

    fetch("/Culture/Catalog?culture=" + encodeURIComponent(culture), {
        credentials: "same-origin",
        headers: { "Accept": "application/json" }
    })
        .then(function (response) {
            if (!response.ok) throw new Error("Localization catalog request failed.");
            return response.json();
        })
        .then(function (payload) {
            catalog = payload.translations || Object.create(null);
            root.dir = payload.direction || root.dir;
            translateTree(document.body);

            new MutationObserver(function (mutations) {
                mutations.forEach(function (mutation) {
                    mutation.addedNodes.forEach(translateTree);
                });
            }).observe(document.body, { childList: true, subtree: true });

            root.setAttribute("data-zy-localization-ready", "true");
            document.dispatchEvent(new CustomEvent("zynora:localization-ready", {
                detail: { culture: payload.culture, direction: payload.direction }
            }));
        })
        .catch(function () {
            root.setAttribute("data-zy-localization-ready", "fallback");
        });
})();
