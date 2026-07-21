// سلوك تنقّل كيان (قسم 20 بالدراسة) على قائمتنا:
// - النقر على المجموعة (أشخاص/الحضور/الرواتب) يفتح درج فروع منزلقاً بجانب القائمة
//   (كلاس ky-open — مو خاصية open، حتى لا تتعارض سكربتات الأكورديون القديمة).
// - درج واحد مفتوح؛ النقر خارج القائمة أو Escape يغلق (مثل كيان: اختيار مباشر يغلق).
// - الأكورديون الداخلي (ky-acc) حصري + انميشن إغلاق (details يخفي فوراً، فنؤخر إزالة open).
// - الموبايل (<981px): سلوك <details> الطبيعي.
(function () {
    var groups = Array.prototype.slice.call(document.querySelectorAll(".nexora-nav-group"));
    if (!groups.length) return;

    var desktop = window.matchMedia("(min-width: 981px)");

    function closeAll(except) {
        groups.forEach(function (group) {
            if (group !== except) group.classList.remove("ky-open");
        });
    }

    groups.forEach(function (group) {
        var summary = group.querySelector(":scope > summary");
        var links = group.querySelector(":scope > .nexora-nav-group-links");
        if (!summary || !links) return;

        // مجموعة الصفحة الحالية (رندرها السيرفر open): علامة هادئة بلا انبثاق درج.
        if (group.hasAttribute("open")) summary.classList.add("ky-current");

        summary.addEventListener("click", function (e) {
            if (!desktop.matches) return; // الموبايل: أكورديون طبيعي
            e.preventDefault();
            var willOpen = !group.classList.contains("ky-open");
            closeAll(group);
            group.classList.toggle("ky-open", willOpen);
            if (willOpen) {
                // التصاق تام بالقائمة: نضبط إزاحة تقريبية ثم نقيس الفجوة الفعلية ونعوّضها
                // (القياس المباشر يتأثر بتحجيم المتصفح/شريط التمرير).
                var sidebar = document.querySelector(".nexora-sidebar");
                if (sidebar) {
                    links.style.setProperty("--ky-offset", (window.innerWidth - sidebar.getBoundingClientRect().left) + "px");
                    requestAnimationFrame(function () {
                        var drawerRect = links.getBoundingClientRect();
                        var gap = sidebar.getBoundingClientRect().left - (drawerRect.left + drawerRect.width);
                        if (Math.abs(gap) > 1) {
                            var current = parseFloat(links.style.getPropertyValue("--ky-offset")) || 0;
                            links.style.setProperty("--ky-offset", (current - gap) + "px");
                        }
                    });
                }
            }
        });
    });

    // الأكورديون الداخلي: حصري + انميشن إغلاق.
    document.querySelectorAll(".ky-acc > summary").forEach(function (summary) {
        summary.addEventListener("click", function (e) {
            var acc = summary.parentElement;
            var body = acc.querySelector(":scope > .ky-acc-body");
            if (!body) return;
            e.preventDefault();

            if (acc.hasAttribute("open")) {
                // إغلاق بانميشن: صفّر الارتفاع ثم أزل open بعد الترانزيشن (0.35s).
                body.style.maxHeight = body.scrollHeight + "px";
                requestAnimationFrame(function () { body.style.maxHeight = "0"; });
                setTimeout(function () { acc.removeAttribute("open"); body.style.maxHeight = ""; }, 350);
            } else {
                // حصري: أغلق أشقاءه بنفس الدرج.
                var siblings = acc.parentElement.querySelectorAll(":scope > .ky-acc[open]");
                siblings.forEach(function (other) { if (other !== acc) other.removeAttribute("open"); });
                acc.setAttribute("open", "");
            }
        });
    });

    document.addEventListener("click", function (e) {
        if (!desktop.matches) return;
        // النقر خارج القائمة والدرج يغلق الدرج؛ اختيار رابط داخل الدرج يغلقه أيضاً (مثل كيان).
        var inNav = e.target.closest(".nexora-nav-group");
        if (!inNav) { closeAll(null); return; }
        var link = e.target.closest(".nexora-nav-group-links a.nexora-nav-link");
        if (link && !link.classList.contains("ky-drawer-title")) closeAll(null);
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") closeAll(null);
    });

    desktop.addEventListener("change", function (mq) {
        if (!mq.matches) closeAll(null);
    });
})();
