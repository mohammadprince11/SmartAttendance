(() => {
    'use strict';
    const dialog = document.getElementById('zy-poll-create');
    const options = dialog?.querySelector('[data-poll-options]');
    if (!options) return;
    const add = dialog.querySelector('[data-poll-add]');
    const primary = dialog.querySelector('#poll-primary-language');
    const form = dialog.querySelector('form');
    const error = dialog.querySelector('[data-poll-language-error]');
    const languageInputs = () => Array.from(dialog.querySelectorAll('[data-poll-language]'));
    const languageMeta = () => Array.from(dialog.querySelectorAll('[data-poll-language-meta]'));
    function syncLanguages() {
        const name = primary.dataset.primaryName || '';
        dialog.querySelectorAll('[data-poll-primary-name]').forEach(span => span.textContent = name);
        dialog.querySelectorAll('[data-poll-language-row]').forEach(row => row.hidden = row.dataset.pollLanguageRow === primary.value);
        languageInputs().forEach(input => input.disabled = input.dataset.pollLanguage === primary.value);
        const active = new Set(languageInputs().filter(input => !input.disabled && input.value.trim()).map(input => input.dataset.pollLanguage));
        languageMeta().forEach(input => input.disabled = !active.has(input.dataset.pollLanguageMeta));
        dialog.querySelectorAll('[data-poll-language-toggle]').forEach(button => button.hidden = Number(primary.dataset.languageCount) < 2);
    }
    function syncOptions() {
        const rows = Array.from(options.children);
        rows.forEach((row, index) => {
            row.querySelector('[data-poll-remove]').disabled = rows.length <= 2;
            const caption = row.querySelector('[data-poll-option-caption]');
            caption.textContent = caption.dataset.captionPrefix + ' ' + (index + 1);
            row.querySelector('.zy-poll-option-main input').setAttribute('aria-label', caption.textContent);
            row.querySelectorAll('[data-poll-language]').forEach(input => input.setAttribute('aria-label', caption.textContent + ' — ' + input.closest('label').querySelector('span').textContent));
        });
        add.disabled = rows.length >= 12;
    }
    dialog.addEventListener('click', event => {
        const toggle = event.target.closest('[data-poll-language-toggle]');
        if (!toggle) return;
        const panel = toggle.closest('[data-poll-field]').querySelector('[data-poll-field-languages]');
        panel.hidden = !panel.hidden;
        toggle.setAttribute('aria-expanded', String(!panel.hidden));
    });
    dialog.addEventListener('input', event => {
        if (event.target.matches('[data-poll-language]')) { error.hidden = true; syncLanguages(); }
    });
    add.addEventListener('click', () => {
        if (options.children.length >= 12) return;
        const row = options.firstElementChild.cloneNode(true);
        row.querySelectorAll('input').forEach(input => input.value = '');
        row.querySelectorAll('.field-validation-error,.zy-field-error').forEach(node => node.remove());
        row.querySelector('[data-poll-field-languages]').hidden = true;
        row.querySelector('[data-poll-language-toggle]').setAttribute('aria-expanded', 'false');
        options.append(row); syncOptions(); syncLanguages();
        row.querySelector('.zy-poll-option-main input').focus();
    });
    options.addEventListener('click', event => {
        const remove = event.target.closest('[data-poll-remove]');
        if (remove && options.children.length > 2) {
            // Translation controls live in their option row, preserving correspondence on deletion.
            remove.closest('.zy-poll-option').remove(); syncOptions(); syncLanguages();
        }
    });
    syncOptions(); syncLanguages();
    form.addEventListener('submit', event => {
        if (form.dataset.submitting) { event.preventDefault(); return; }
        const inputs = languageInputs();
        const active = new Set(inputs.filter(input => input.dataset.pollLanguage !== primary.value && input.value.trim()).map(input => input.dataset.pollLanguage));
        const missing = inputs.find(input => active.has(input.dataset.pollLanguage) && !input.value.trim());
        if (missing) {
            event.preventDefault(); error.hidden = false;
            const field = missing.closest('[data-poll-field]');
            field.querySelector('[data-poll-field-languages]').hidden = false;
            field.querySelector('[data-poll-language-toggle]').setAttribute('aria-expanded', 'true');
            missing.focus(); return;
        }
        // Collapse never discards values; only complete, used languages are posted.
        inputs.forEach(input => input.disabled = !active.has(input.dataset.pollLanguage));
        languageMeta().forEach(input => input.disabled = !active.has(input.dataset.pollLanguageMeta));
        form.dataset.submitting = 'true';
        form.querySelector('button[type=submit]').disabled = true;
    });
})();
