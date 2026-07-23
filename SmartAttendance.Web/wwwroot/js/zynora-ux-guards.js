/* ==========================================================================
   ZYNORA — حراس تجربة المستخدم المشتركة
   ثلاث قواعد من قائمة تدقيق UX، مطبَّقة مركزياً بدل تكرارها في 148 صفحة:

   1) إعلان التنبيهات للقارئ الشاشي: التنبيه البصري وحده لا يصل لمستخدم
      قارئ الشاشة — تُضاف role/aria-live لكل رسالة نجاح أو خطأ.
   2) حالة الإرسال: زر يُرسل بلا تغذية راجعة يدفع المستخدم لضغطه مرتين،
      فيُنشئ سجلاً مكرراً — يُعطَّل الزر ويُبدَّل نصه أثناء الإرسال.
   3) التحقق عند مغادرة الحقل: كشف الخطأ عند الإرسال فقط يعني اكتشافه
      بعد ملء النموذج كله — يُتحقق من الحقل المطلوب فور مغادرته.

   لا تعتمد على مكتبة، ومندوبة على المستند فتشمل المحتوى المُحمَّل لاحقاً.
   ========================================================================== */
(function () {
    "use strict";

    // ===== 1) إعلان التنبيهات =====
    // أصناف التنبيهات المستخدمة عبر المشروع (hrms-core + أصناف الصفحات)
    var ALERT_SELECTOR = [
        ".hrms-alert", ".rec-alert", ".ot-alert", ".alert-success", ".alert-danger",
        "[class*='-alert']"
    ].join(",");

    function announceAlerts(root) {
        var scope = root && root.querySelectorAll ? root : document;

        scope.querySelectorAll(ALERT_SELECTOR).forEach(function (alert) {
            if (alert.dataset.zyAnnounced === "1") return;
            alert.dataset.zyAnnounced = "1";

            // الخطأ يقاطع القراءة، والنجاح ينتظر دوره
            var isError = /danger|error|err\b/i.test(alert.className);

            alert.setAttribute("role", isError ? "alert" : "status");
            alert.setAttribute("aria-live", isError ? "assertive" : "polite");
            alert.setAttribute("aria-atomic", "true");
        });
    }

    // ===== 2) حالة الإرسال =====
    var BUSY_TEXT = "جارٍ الحفظ…";

    function markSubmitting(form, submitter) {
        var button = submitter && submitter.tagName === "BUTTON"
            ? submitter
            : form.querySelector("button[type=submit], input[type=submit]");

        if (!button || button.dataset.zyBusy === "1") return;

        // زر التأكيد بنقرتين يُدار بـzynora-confirm — لا نتدخّل قبل التسليح
        if (button.hasAttribute("data-zyconfirm") && button.dataset.zyArmed === "1") return;

        button.dataset.zyBusy = "1";
        button.dataset.zyBusyOriginal = button.innerHTML;
        button.classList.add("zy-busy");
        button.setAttribute("aria-busy", "true");

        if (button.tagName === "BUTTON") {
            button.innerHTML = BUSY_TEXT;
        }

        // التعطيل بعد دورة الحدث حتى لا يُسقَط اسم الزر من البيانات المرسلة
        window.setTimeout(function () {
            button.disabled = true;
        }, 0);

        // شبكة أمان: لو أُلغي الإرسال (تحقق فاشل) يعود الزر لحاله
        window.setTimeout(function () {
            if (button.dataset.zyBusy !== "1") return;
            restoreButton(button);
        }, 12000);
    }

    function restoreButton(button) {
        delete button.dataset.zyBusy;
        button.disabled = false;
        button.classList.remove("zy-busy");
        button.removeAttribute("aria-busy");

        if (button.dataset.zyBusyOriginal != null) {
            button.innerHTML = button.dataset.zyBusyOriginal;
            delete button.dataset.zyBusyOriginal;
        }
    }

    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (!form || form.tagName !== "FORM") return;
        if (form.hasAttribute("data-zy-no-busy")) return;
        if (event.defaultPrevented) return;

        // نموذج غير صالح لن يُرسل — لا تُعطّل زره
        if (typeof form.checkValidity === "function" && !form.checkValidity()) return;

        markSubmitting(form, event.submitter);
    });

    // العودة من الصفحة بزر الرجوع تُعيد الأزرار لحالتها
    window.addEventListener("pageshow", function () {
        document.querySelectorAll("[data-zy-busy='1']").forEach(restoreButton);
    });

    // ===== 3) التحقق عند مغادرة الحقل =====
    function validateField(field) {
        if (!field.willValidate || field.disabled || field.type === "hidden") return;
        if (!field.hasAttribute("required") && field.value === "") return;

        var valid = field.checkValidity();

        field.classList.toggle("zy-invalid", !valid);
        field.setAttribute("aria-invalid", valid ? "false" : "true");

        var holder = field.parentElement;
        if (!holder) return;

        var message = holder.querySelector("[data-zy-field-error]");

        if (valid) {
            if (message) message.remove();
            return;
        }

        if (!message) {
            message = document.createElement("small");
            message.setAttribute("data-zy-field-error", "");
            message.className = "zy-field-error";
            holder.appendChild(message);
        }

        message.textContent = field.validationMessage;
    }

    document.addEventListener("blur", function (event) {
        var field = event.target;
        if (!field || !field.matches) return;
        if (!field.matches("input, select, textarea")) return;
        if (field.closest("[data-zy-no-validate]")) return;

        validateField(field);
    }, true);

    // تصحيح الحقل يمسح خطأه فوراً بدل انتظار مغادرة أخرى
    document.addEventListener("input", function (event) {
        var field = event.target;
        if (!field || !field.classList || !field.classList.contains("zy-invalid")) return;
        if (field.checkValidity()) validateField(field);
    });

    // ===== التشغيل =====
    function init() {
        announceAlerts(document);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }

    // مؤقّت لا requestAnimationFrame: الأخير لا يعمل في تبويب خلفي ولا في بيئة
    // بلا تركيب إطارات، فكان التنبيه المُضاف لاحقاً يبقى بلا إعلان للقارئ الشاشي.
    var announceTimer = null;

    new MutationObserver(function () {
        if (announceTimer) return;
        announceTimer = window.setTimeout(function () {
            announceTimer = null;
            announceAlerts(document);
        }, 60);
    }).observe(document.documentElement, { childList: true, subtree: true });
})();
