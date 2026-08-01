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

    // مجموعة الصفحة الحالية — **تُشتقّ من المسار نفسه**، لا من خاصية `open` ولا من
    // صنف `.active`.
    //
    // 🐞 كلتا الإشارتين تصل **متأخّرة** عن تنفيذ هذا الملف، وقِيس ذلك حيّاً:
    //  · `open` يضبطها `nexora-ui-stabilization-phase1.js` وهو يُحمَّل بعدنا بسطر.
    //  · وصنف `.active` يضيفه السكربت نفسه، ولا يرندره Razor هنا لأن شرطه
    //    `IsUnder("/Violations/Index")` بينما المسار الفعليّ `/Violations`.
    // فكان الشرط يفشل صامتاً والدرج يبقى مغلقاً. والمسار متاح دائماً وبلا ترتيب.
    function isCurrentGroup(group) {
        if (group.hasAttribute("open")) return true;

        var path = window.location.pathname.replace(/\/+$/, "").toLowerCase() || "/";
        if (path === "/") return false;

        var links = group.querySelectorAll(".nexora-nav-group-links a[href]");
        for (var i = 0; i < links.length; i++) {
            var href = (links[i].getAttribute("href") || "").split("?")[0].replace(/\/+$/, "").toLowerCase();
            if (!href || href === "/") continue;
            if (path === href || path.indexOf(href + "/") === 0) return true;
        }

        return false;
    }

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

    // فتح الدرج: يغطّي القائمة تماماً، فيطابق موضعه وعرضه على القائمة الفعلية.
    function openGroup(group, links) {
        closeAll(group);
        group.classList.remove("ky-closing");

        var sidebar = document.querySelector(".nexora-sidebar");
        if (sidebar) {
            var rect = sidebar.getBoundingClientRect();
            links.style.setProperty("--ky-right", (window.innerWidth - rect.right) + "px");
            links.style.setProperty("--ky-w", rect.width + "px");
        }

        group.classList.add("ky-open");
    }

    groups.forEach(function (group) {
        var summary = group.querySelector(":scope > summary");
        var links = group.querySelector(":scope > .nexora-nav-group-links");
        if (!summary || !links) return;

        // مجموعة الصفحة الحالية (يرندرها السيرفر `open`).
        //
        // 🐞 كانت «علامة هادئة بلا انبثاق درج» — والنتيجة أنّك بعد وصولك لصفحةٍ
        // داخل مجموعة لا ترى **أياً** من فروعها: الدرج مخفيّ (`display:none` ما لم
        // يكن `ky-open`)، فالقائمة تعرض العناوين الثلاثة فقط. قيس حيّاً على
        // `/Violations`: المجموعة `open` والدرج `display:none` وثمانية عناصر ظاهرة
        // كلّها جذرية — أي لا «حالات المخالفات» ولا «الإجراءات التأديبية» ولا سبيل
        // للانتقال بينهما إلا بإعادة فتح المجموعة يدوياً.
        //
        // فالدرج يُفتح الآن على مجموعة الصفحة الحالية: ترى أين أنت، وتنتقل بين
        // شاشات القسم بنقرة. وصفّ «رجوع» يعيدك للقائمة الرئيسية.
        //
        // ⚠️ والكشف **بالرابط النشط لا بخاصية `open`**: قِيس أن `open` تصل متأخرة.
        // `nexora-ui-stabilization-phase1.js` يُحمَّل **بعد** هذا الملف وهو الذي
        // يضبط `activeGroup.open = true`، فحين يقرأ هذا السطر الخاصيةَ تكون غائبة
        // ويمرّ بلا أثر — وهو ما جعل الدرج يبقى مغلقاً رغم صحّة كل شيء آخر.
        // أما صنف الرابط النشط فيرندره Razor بالـHTML نفسه، فلا يتعلّق بترتيب أحد.
        if (isCurrentGroup(group)) {
            summary.classList.add("ky-current");
            if (desktop.matches) openGroup(group, links);
        }

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
            openGroup(group, links);
        });
    });

    // 🐞 **سقف الارتفاع يُحرَّر بعد الفتح.**
    //
    // الأنميشن يحتاج رقماً ينتقل إليه، فالـCSS يضع `max-height: 460px` للمفتوح.
    // لكنه سقفٌ مثبَّت: حين يُفتح أكورديونٌ **داخل** أكورديون يزيد المحتوى عن 460
    // فيُقصّ ما فوقه و`overflow: hidden` يبتلعه بصمت. قِيس حيّاً على الإنتاج:
    // «عمليات الموارد البشرية» بارتفاع 460 و`scrollHeight` 460 والسقف 460 — أي
    // مقصوصٌ تماماً عند الحدّ، فبدت «الإجراءات التأديبية» شريحةً مبتورة.
    //
    // فبعد انتهاء حركة الفتح نرفع السقف إلى `none`: تبقى الحركة، ويسقط القصّ.
    // (والإغلاق يعيد قياس الارتفاع الفعليّ أولاً فيبقى متحرّكاً كما هو.)
    function releaseHeight(acc, body) {
        window.setTimeout(function () {
            if (acc.hasAttribute("open")) body.style.maxHeight = "none";
        }, 360);
    }

    // الأكورديونات المفتوحة بالرندر (قسم الصفحة الحالية) تُحرَّر فوراً بلا انتظار
    // حركةٍ لم تحدث أصلاً.
    document.querySelectorAll(".ky-acc[open] > .ky-acc-body").forEach(function (body) {
        body.style.maxHeight = "none";
    });

    // الأكورديون الداخلي: حصري + انميشن إغلاق.
    document.querySelectorAll(".ky-acc > summary").forEach(function (summary) {
        summary.addEventListener("click", function (e) {
            var acc = summary.parentElement;
            var body = acc.querySelector(":scope > .ky-acc-body");
            if (!body) return;
            e.preventDefault();

            if (acc.hasAttribute("open")) {
                // إغلاق بانميشن: من ارتفاعه الفعليّ إلى صفر، ثم أزل open بعد الترانزيشن.
                body.style.maxHeight = body.scrollHeight + "px";
                requestAnimationFrame(function () { body.style.maxHeight = "0"; });
                setTimeout(function () { acc.removeAttribute("open"); body.style.maxHeight = ""; }, 350);
            } else {
                // حصري: أغلق أشقاءه بنفس الدرج.
                var siblings = acc.parentElement.querySelectorAll(":scope > .ky-acc[open]");
                siblings.forEach(function (other) { if (other !== acc) other.removeAttribute("open"); });
                acc.setAttribute("open", "");
                releaseHeight(acc, body);
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
