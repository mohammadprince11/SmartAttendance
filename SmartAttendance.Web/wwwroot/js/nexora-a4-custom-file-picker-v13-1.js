// NEXORA A4 Custom File Picker V13.1
(() => {
    function findA4FileInputs() {
        const inputs = Array.from(document.querySelectorAll('input[type="file"]'));

        // This script is linked only on the A4 form page. Still, keep a safe filter.
        return inputs.filter((input) => {
            const haystack = [
                input.name || '',
                input.id || '',
                input.getAttribute('asp-for') || '',
                input.closest('form')?.textContent || '',
                input.closest('section')?.textContent || '',
                document.body?.textContent || ''
            ].join(' ').toLowerCase();

            return haystack.includes('a4') ||
                haystack.includes('فورمة') ||
                haystack.includes('form') ||
                inputs.length === 1;
        });
    }

    function createVisual(input) {
        const wrapper = document.createElement('div');
        wrapper.className = 'nxr-a4-file-picker';

        const visual = document.createElement('div');
        visual.className = 'nxr-a4-file-visual';
        visual.setAttribute('aria-hidden', 'true');

        const icon = document.createElement('span');
        icon.className = 'nxr-a4-file-icon';
        icon.textContent = 'A4';

        const textWrap = document.createElement('span');

        const name = document.createElement('span');
        name.className = 'nxr-a4-file-name';
        name.textContent = 'لم يتم اختيار ملف';

        const hint = document.createElement('small');
        hint.className = 'nxr-a4-file-hint';
        hint.textContent = 'PDF أو صورة بقياس A4';

        textWrap.appendChild(name);
        textWrap.appendChild(hint);

        const button = document.createElement('span');
        button.className = 'nxr-a4-file-button';
        button.textContent = 'اختيار ملف';

        visual.appendChild(icon);
        visual.appendChild(textWrap);
        visual.appendChild(button);

        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);
        wrapper.appendChild(visual);

        input.classList.add('nxr-a4-native-file');

        function updateName() {
            const file = input.files && input.files.length ? input.files[0] : null;

            if (file) {
                name.textContent = file.name;
                wrapper.classList.add('has-file');
                button.textContent = 'تغيير الملف';
            } else {
                name.textContent = 'لم يتم اختيار ملف';
                wrapper.classList.remove('has-file');
                button.textContent = 'اختيار ملف';
            }
        }

        input.addEventListener('change', updateName);
        updateName();
    }

    function enhance() {
        findA4FileInputs().forEach((input) => {
            if (input.dataset.nxrA4CustomPicker === 'true') return;
            input.dataset.nxrA4CustomPicker = 'true';
            createVisual(input);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', enhance);
    } else {
        enhance();
    }
})();
