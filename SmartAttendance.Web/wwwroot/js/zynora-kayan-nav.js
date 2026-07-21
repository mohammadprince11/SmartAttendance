// سلوك تنقّل كيان (قسم 20 بالدراسة) على قائمتنا:
// - النقر على المجموعة (أشخاص/الحضور/الرواتب) يفتح درج فروع يغطي القائمة نفسها
//   بانزلاق ناعم، مع صف «رجوع» أعلاه يعيد للقائمة الرئيسية.
//   (كلاس ky-open — مو خاصية open، حتى لا تتعارض سكربتات الأكورديون القديمة.)
// - درج واحد مفتوح؛ النقر خارج القائمة أو Escape يغلق (مثل كيان: اختيار مباشر يغلق).
// - الإغلاق بأنميشن خروج (كلاس ky-closing يبقي الدرج مرسوماً حتى انتهاء الحركة).
// - الأكورديون الداخلي (ky-acc) حصري + انميشن إغلاق (details يخفي فوراً، فنؤخر إزالة open).
// - الموبايل (<981px): سلوك <details> الطبيعي.
(function () {
    var groups = Array.prototype.slice.call(document.querySelectorAll(".nexora-nav-group"));
    if (!groups.length) return;

    var desktop = window.matchMedia("(min-width: 981px)");

    function closeGroup(group) {
        if (!group.classList.contains("ky-open")) return;
        group.classList.remove("ky-open");
        // نبقي الدرج مرسوماً أثناء أنميشن الخروج (0.22s) ثم نخفيه
        group.classList.add("ky-closing");
        setTimeout(function () { group.classList.remove("ky-closing"); }, 240);
    }

    function closeAll(except) {
        groups.forEach(function (group) {
            if (group !== except) closeGroup(group);
        });
    }

    groups.forEach(function (group) {
        var summary = group.querySelector(":scope > summary");
        var links = group.querySelector(":scope > .nexora-nav-group-links");
        if (!summary || !links) return;

        // مجموعة الصفحة الحالية (رندرها السيرفر open): علامة هادئة بلا انبثاق درج.
        if (group.hasAttribute("open")) summary.classList.add("ky-current");

        // صف الرجوع أعلى الدرج (الدرج يغطي القائمة، فيحتاج مخرجاً واضحاً).
        var back = document.createElement("button");
        back.type = "button";
        back.className = "ky-back";
        back.innerHTML = '<span class="ky-back-chev" aria-hidden="true"></span><span>رجوع</span>';
        back.addEventListener("click", function (e) {
            e.preventDefault();
            e.stopPropagation();
            closeGroup(group);
        });
        links.insertBefore(back, links.firstChild);

        summary.addEventListener("click", function (e) {
            if (!desktop.matches) return; // الموبايل: أكورديون طبيعي
            e.preventDefault();
            if (group.classList.contains("ky-open")) { closeGroup(group); return; }
            closeAll(group);
            group.classList.remove("ky-closing");
            // الدرج يغطي القائمة تماماً: نطابق موضعه وعرضه على القائمة الفعلية.
            var sidebar = document.querySelector(".nexora-sidebar");
            if (sidebar) {
                var rect = sidebar.getBoundingClientRect();
                links.style.setProperty("--ky-right", (window.innerWidth - rect.right) + "px");
                links.style.setProperty("--ky-w", rect.width + "px");
            }
            group.classList.add("ky-open");
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
