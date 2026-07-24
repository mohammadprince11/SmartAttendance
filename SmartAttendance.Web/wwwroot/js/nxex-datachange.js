/* تعديل البيانات (صفحة مستقلة): زر تغيير الصورة يفتح منتقي الملفات + معاينة + اسم الملف.
 * الكلندر والقوائم تعمل عبر ملفاتها — لا حاجة لكود إضافي هنا. */
(() => {
    const input = document.getElementById('dcPhotoInput') || document.querySelector('[data-dc-photo-input]');
    const btn = document.getElementById('dcPhotoBtn');
    const preview = document.querySelector('[data-dc-photo-preview]');
    const nameEl = document.querySelector('[data-dc-photo-name]');
    if (!input) return;

    if (btn) btn.addEventListener('click', () => input.click());

    input.addEventListener('change', () => {
        const file = input.files && input.files[0];
        if (!file) return;
        if (preview) {
            const reader = new FileReader();
            reader.onload = e => { preview.src = e.target.result; };
            reader.readAsDataURL(file);
        }
        if (nameEl) nameEl.textContent = file.name;
    });
})();
