(function () {
    'use strict';

    var allowed = new Set([
        'display', 'background', 'background-color', 'color', 'width', 'height', 'flex',
        'padding', 'margin', 'max-width', 'min-width', 'max-height', 'min-height',
        'position', 'top', 'bottom', 'inset-inline-start', 'inset-inline-end',
        'font-size', 'font-weight', 'line-height', 'text-align', 'border', 'border-radius',
        'transform', 'opacity', '--zy-dynamic-accent'
    ]);
    var unsafe = /url\s*\(|expression\s*\(|javascript:|@import|[<>]/i;

    function apply(element) {
        var source = element.getAttribute('data-zy-style') || '';
        source.split(';').forEach(function (declaration) {
            var colon = declaration.indexOf(':');
            if (colon < 1) return;
            var property = declaration.slice(0, colon).trim().toLowerCase();
            var value = declaration.slice(colon + 1).trim();
            if (!allowed.has(property) || !value || unsafe.test(value)) return;
            element.style.setProperty(property, value);
        });
        element.removeAttribute('data-zy-style');
    }

    function hydrate(root) {
        if (root && root.nodeType === 1 && root.hasAttribute('data-zy-style')) apply(root);
        (root || document).querySelectorAll('[data-zy-style]').forEach(apply);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { hydrate(document); });
    else hydrate(document);

    new MutationObserver(function (records) {
        records.forEach(function (record) {
            record.addedNodes.forEach(function (node) { if (node.nodeType === 1) hydrate(node); });
        });
    }).observe(document.documentElement, { childList: true, subtree: true });
})();
