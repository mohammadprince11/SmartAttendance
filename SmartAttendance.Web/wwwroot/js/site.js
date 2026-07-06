/* NEXORA UI Utility Core - Optimized JavaScript Engine */
const NEXORA = {
    Ui: {
        BlockButton: function (buttonElement) {
            if (buttonElement) {
                buttonElement.disabled = true;
                buttonElement.setAttribute('data-original-text', buttonElement.innerHTML);
                buttonElement.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Processing...';
            }
        },
        UnblockButton: function (buttonElement) {
            if (buttonElement && buttonElement.hasAttribute('data-original-text')) {
                buttonElement.innerHTML = buttonElement.getAttribute('data-original-text');
                buttonElement.disabled = false;
                buttonElement.removeAttribute('data-original-text');
            }
        }
    },
    Fetch: async function (url, options = {}) {
        const defaultOptions = {
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                'Content-Type': 'application/json'
            }
        };
        const mergedOptions = { ...defaultOptions, ...options };
        try {
            const response = await fetch(url, mergedOptions);
            if (!response.ok) {
                throw new Error('NEXORA Network response was not ok: ' + response.statusText);
            }
            return await response.json();
        } catch (error) {
            console.error('NEXORA Fetch Error:', error);
            throw error;
        }
    }
};

/* Auto-hook Form Submissions to prevent Double Submit */
document.addEventListener('DOMContentLoaded', function () {
    const forms = document.querySelectorAll('form');
    forms.forEach(form => {
        form.addEventListener('submit', function (e) {
            const submitBtn = form.querySelector('button[type="submit"]');
            if (submitBtn) {
                NEXORA.Ui.BlockButton(submitBtn);
            }
        });
    });
});