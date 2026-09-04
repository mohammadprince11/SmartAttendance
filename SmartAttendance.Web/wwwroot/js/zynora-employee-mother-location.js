(function () {
    "use strict";

    function refreshZynoraSelect(select) {
        if (!select) return;

        if (window.ZynoraSelect &&
            typeof window.ZynoraSelect.refresh === "function") {
            window.ZynoraSelect.refresh(select);
        }
        else if (window.ZynoraSelect &&
                 typeof window.ZynoraSelect.refreshAll === "function") {
            window.ZynoraSelect.refreshAll();
        }
    }

    function initialize() {
        const country =
            document.getElementById("Employee_MotherCountry");

        const city =
            document.getElementById("Employee_MotherCity");

        if (!country || !city)
            return;

        /*
         * نخزن كل المدن مرة واحدة.
         * بعد ذلك لا نعتمد على hidden/disabled،
         * بل نبني <select> من جديد حسب البلد المختار فقط.
         */
        const allCities =
            Array.from(city.options)
                .filter(option => option.value)
                .map(option => ({
                    value: option.value,
                    text: option.textContent,
                    country: option.dataset.country || ""
                }));

        const placeholderOption =
            Array.from(city.options)
                .find(option => !option.value);

        const placeholderText =
            placeholderOption
                ? placeholderOption.textContent
                : "اختر المدينة الأم";

        const initialCity =
            city.value || "";

        function createOption(item) {
            const option =
                document.createElement("option");

            option.value =
                item.value;

            option.textContent =
                item.text;

            option.dataset.country =
                item.country;

            return option;
        }

        function rebuildCities(preserveCurrent) {
            const selectedCountry =
                (country.value || "").trim();

            const previousCity =
                preserveCurrent
                    ? (city.value || initialCity || "")
                    : "";

            /*
             * إزالة كل الخيارات القديمة فعلياً.
             * هذا يمنع الـcustom dropdown من عرض مدن دول أخرى.
             */
            city.replaceChildren();

            const placeholder =
                document.createElement("option");

            placeholder.value = "";
            placeholder.textContent =
                placeholderText;

            city.appendChild(placeholder);

            if (!selectedCountry) {
                city.value = "";
                city.disabled = true;

                refreshZynoraSelect(city);
                return;
            }

            const matchingCities =
                allCities.filter(item =>
                    item.country === selectedCountry);

            matchingCities.forEach(item => {
                city.appendChild(
                    createOption(item));
            });

            city.disabled = false;

            /*
             * عند Edit أو validation failure:
             * نحافظ على المدينة القديمة فقط إذا ما زالت
             * تابعة للبلد الحالي.
             */
            const previousStillValid =
                matchingCities.some(item =>
                    item.value === previousCity);

            city.value =
                previousStillValid
                    ? previousCity
                    : "";

            refreshZynoraSelect(city);
        }

        country.addEventListener(
            "change",
            function () {
                /*
                 * تغيير البلد = تصفير المدينة القديمة
                 * وإظهار مدن البلد الجديد فقط.
                 */
                rebuildCities(false);
            });

        /*
         * أول تحميل:
         * يحافظ على المدينة المسجلة للموظف عند Edit.
         */
        rebuildCities(true);
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initialize);
    }
    else {
        initialize();
    }
})();
