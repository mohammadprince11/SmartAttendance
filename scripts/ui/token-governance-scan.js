/*
 * token-governance-scan.js — من يحكمه المختبر؟ ماسح تغطية التوكنات.
 *
 * يُحمَّل كوسم <script> من نفس الأصل ثم يُستدعى `__zyGovScan(pages)` فيعيد JSON
 * بجرد كل ودجت مرئيّ عبر الصفحات: أيّها يحكمه `zynora-tokens.css` وأيّها سائب،
 * ومع السائب أسماء عائلاته وأشكاله المثبّتة بالكود.
 * (دالة لا IIFE عمداً: سياسة CSP تمنع eval فلا يصحّ لصق نصّ وتقييمه — نفس
 *  اصطلاح `contrast-scan.js`.)
 *
 * **لماذا؟** «وحّد تصميم الشاشات كلها» سؤالٌ بلا نهاية ما لم يصر قائمةً منتهية.
 * المختبر لا يحكم إلا ما تسمّيه طبقة الاستهلاك بالاسم، والباقي يبقى بأرقامه
 * المثبّتة (71 ملف CSS · 6735 !important). هذا الماسح يحوّل السؤال إلى جدول:
 * كم عنصراً محكوماً وكم سائباً، ومن أين يبدأ العمل لأعلى أثر.
 *
 * دروس مبنيّة هنا (لا تحذفها):
 *  1) المطابقة بـ`Set` من `querySelectorAll` لكل محدِّد مرّةً واحدة — لا
 *     `el.matches()` لكل عنصر بكل محدِّد (كان ذلك 700 ألف نداء لست صفحات).
 *  2) خصائص الاختصار (`border-radius` `gap` `padding-inline`) تعود **فارغة** من
 *     CSSOM حين تحوي `var()` (قيمة معلّقة الاستبدال)، فتُقرأ طبقة الاستهلاك من
 *     نصّ الملف بـ`fetch` لا من `document.styleSheets`.
 *  3) العنصر المخفيّ أو صفر العرض يُستبعد: الجرد عمّا يُرى لا عمّا بالـDOM.
 *  4) **الحقول الأصلية المبتلَعة تُستبعد بالارتفاع لا بالصنف.** الودجتات عندنا
 *     (المنسدلة · الكلاندر) تُبقي `<select>`/`<input>` الأصليّ حيّاً بارتفاع 1px
 *     تحت الغلاف — لا `display:none` — فمرّ 31 منها بأول تشغيلٍ وظهرت «حقولاً
 *     سائبة»، فبدت تغطية الحقول 57% وهي 100%. أيّ عنصرٍ بارتفاع ≤2px ليس ودجتاً
 *     مرئياً، واستثناؤه بالاسم كان سيحتاج قائمةً تبيت مع كل ودجت جديد.
 */
window.__zyGovScan = async function (pages) {
  const TOKENS_FILE = /zynora-tokens/;

  /** محدِّدات طبقة الاستهلاك — من نصّ الملف لا من CSSOM (الدرس 2). */
  async function consumerSelectors() {
    const href = [...document.styleSheets]
      .map(s => s.href)
      .find(h => h && TOKENS_FILE.test(h));
    if (!href) throw new Error('zynora-tokens.css غير محمَّل بهذه الصفحة');

    const css = (await (await fetch(href)).text()).replace(/\/\*[\s\S]*?\*\//g, '');
    const out = [];
    for (const rule of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
      const selector = rule[1].trim();
      if (selector.startsWith('@') || selector.startsWith(':root')) continue;
      if (/var\(\s*--zy-/.test(rule[2])) out.push(selector);
    }
    return out;
  }

  /** دور الودجت من وسمه وأصنافه — التصنيف الذي يُقاس عليه التوحيد. */
  function role(el) {
    const tag = el.tagName;
    const cls = String(el.className || '');

    if (tag === 'BUTTON' || /(^|\s|-)btn/i.test(cls)) return 'أزرار';
    if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return 'حقول';
    if (/badge|pill|chip|tag/i.test(cls)) return 'شارات';
    if (/card|panel/i.test(cls)) return 'بطاقات';
    if (tag === 'TH' || tag === 'TD') return 'خلايا جداول';
    if (/tab(s|-)/i.test(cls)) return 'تبويبات';
    return null;
  }

  function loadPage(url) {
    return new Promise(resolve => {
      const frame = document.createElement('iframe');
      frame.style.cssText =
        'position:fixed;left:-99999px;width:1440px;height:900px;border:0';
      document.body.appendChild(frame);
      frame.onload = () => resolve(frame);
      window.setTimeout(() => resolve(frame), 11000);
      frame.src = url;
    });
  }

  const selectors = await consumerSelectors();
  const tally = {};
  const families = {};

  for (const page of pages) {
    const frame = await loadPage(page);
    const doc = frame.contentDocument;
    const view = frame.contentWindow;

    if (doc && view) {
      // الدرس 1: مجموعةٌ واحدة لكل المحكومين بدل مطابقةٍ لكل عنصر.
      const governed = new Set();
      selectors.forEach(selector => {
        try {
          doc.querySelectorAll(selector).forEach(el => governed.add(el));
        } catch (_) { /* محدِّد لا يدعمه المتصفّح */ }
      });

      doc.querySelectorAll('.nexora-content *').forEach(el => {
        const widget = role(el);
        if (!widget) return;

        const style = view.getComputedStyle(el);
        const box = el.getBoundingClientRect();
        // الدرسان 3 و4: الجرد عمّا يُرى، والحقل المبتلَع ارتفاعه 1px لا صفر.
        if (style.display === 'none' || box.width === 0 || box.height <= 2) return;

        const bucket = (tally[widget] ||= { governed: 0, loose: 0 });
        if (governed.has(el)) {
          bucket.governed++;
          return;
        }

        bucket.loose++;
        const shape = [
          Math.round(el.getBoundingClientRect().height),
          style.paddingInlineStart,
          style.fontSize,
          style.borderRadius,
        ].join(' / ');

        String(el.className || '')
          .split(/\s+/)
          .filter(cls => /^(zhr|zxd|nxp|nxupd|nxr|att|zy|nx|hrms|zyp|zyep)[-_]/.test(cls))
          .forEach(cls => {
            const key = widget + ' · .' + cls;
            const entry = (families[key] ||= { count: 0, shapes: {} });
            entry.count++;
            entry.shapes[shape] = (entry.shapes[shape] || 0) + 1;
          });
      });
    }

    frame.remove();
  }

  const ranked = Object.entries(families)
    .sort((a, b) => b[1].count - a[1].count)
    .map(([key, value]) => ({
      family: key,
      count: value.count,
      shapes: Object.entries(value.shapes).sort((a, b) => b[1] - a[1]),
    }));

  return {
    legend: 'الشكل = ارتفاع / حشوة / خطّ / استدارة',
    pagesScanned: pages.length,
    tally,
    looseFamilies: ranked,
  };
};
