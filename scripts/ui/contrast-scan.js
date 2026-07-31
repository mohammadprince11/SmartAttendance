/*
 * contrast-scan.js — ماسح التباين لترحيل الصفحات لـDesign System v1
 *
 * يُحمَّل كوسم <script> من نفس الأصل ثم يُستدعى `__zyScan()` فيعيد JSON
 * بكل نصّ غير مقروء وكل سطح داكن، مجمّعاً حسب توقيع العنصر.
 * (دالة لا IIFE عمداً: سياسة CSP تمنع eval فلا يصحّ لصق نصّ وتقييمه.)
 *
 * درسان كلّفا وقتاً سابقاً ومبنيّان هنا (لا تحذفهما):
 *  1) تركيب الألفا فوق طبقات الآباء — بدونه 133 إنذاراً وهمياً بدل 31.
 *  2) دعم صيغة color(srgb 0.9 0.97 0.97) بقيم 0..1 — بدونه يُحسب الفاتح داكناً.
 *  3) الأيقونة المحرفية (▾ ◷ ➕ ⏳) ليست نصّاً: حدّها 3:1 لا 4.5:1.
 *     بدون هذا التمييز كان `.nxcs-caret` (3.33:1) يتصدّر نتائج كل صفحة تقريباً
 *     وهو مطابق أصلاً — إنذار وهمي يقود لتعديل لا داعي له.
 */
window.__zyScan = function () {
  const WHITE = { r: 255, g: 255, b: 255, a: 1 };

  function parseColor(str) {
    if (!str) return null;
    str = str.trim();
    if (str === 'transparent') return { r: 0, g: 0, b: 0, a: 0 };

    // color(srgb 0.9 0.97 0.97 / 0.5) — قيم 0..1 وليست 0..255
    let m = str.match(/^color\(\s*srgb\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)(?:\s*\/\s*([\d.]+))?\s*\)$/i);
    if (m) {
      return {
        r: parseFloat(m[1]) * 255,
        g: parseFloat(m[2]) * 255,
        b: parseFloat(m[3]) * 255,
        a: m[4] === undefined ? 1 : parseFloat(m[4]),
      };
    }

    m = str.match(/^rgba?\(([^)]+)\)$/i);
    if (m) {
      const p = m[1].split(/[,\s/]+/).filter(Boolean).map(parseFloat);
      return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
    }

    m = str.match(/^#([0-9a-f]{3,8})$/i);
    if (m) {
      let h = m[1];
      if (h.length === 3 || h.length === 4) h = h.split('').map((c) => c + c).join('');
      return {
        r: parseInt(h.slice(0, 2), 16),
        g: parseInt(h.slice(2, 4), 16),
        b: parseInt(h.slice(4, 6), 16),
        a: h.length === 8 ? parseInt(h.slice(6, 8), 16) / 255 : 1,
      };
    }
    return null;
  }

  // src فوق dst — تركيب ألفا قياسي
  function over(src, dst) {
    const a = src.a + dst.a * (1 - src.a);
    if (a === 0) return { r: 0, g: 0, b: 0, a: 0 };
    return {
      r: (src.r * src.a + dst.r * dst.a * (1 - src.a)) / a,
      g: (src.g * src.a + dst.g * dst.a * (1 - src.a)) / a,
      b: (src.b * src.a + dst.b * dst.a * (1 - src.a)) / a,
      a,
    };
  }

  // الخلفية الفعلية = تركيب خلفيات كل الآباء حتى تكتمل العتامة، وأساسها أبيض
  function effectiveBg(el) {
    const stack = [];
    for (let n = el; n && n.nodeType === 1; n = n.parentElement) {
      const c = parseColor(getComputedStyle(n).backgroundColor);
      if (c && c.a > 0) stack.push(c);
      if (c && c.a >= 1) break;
    }
    let bg = WHITE;
    for (let i = stack.length - 1; i >= 0; i--) bg = over(stack[i], bg);
    return bg;
  }

  const chan = (v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  };
  const lum = (c) => 0.2126 * chan(c.r) + 0.7152 * chan(c.g) + 0.0722 * chan(c.b);
  const ratio = (a, b) => {
    const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p);
    return (x + 0.05) / (y + 0.05);
  };

  function visible(el) {
    const s = getComputedStyle(el);
    if (s.display === 'none' || s.visibility === 'hidden' || parseFloat(s.opacity) === 0) return false;
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  }

  function ownText(el) {
    let t = '';
    for (const n of el.childNodes) if (n.nodeType === 3) t += n.textContent;
    return t.trim();
  }

  const sig = (el) => {
    const cls = (el.getAttribute('class') || '').trim().split(/\s+/).filter(Boolean).join('.');
    return el.tagName.toLowerCase() + (cls ? '.' + cls : '');
  };

  // أيقونة محرفية: محرف أو محرفان بلا حروف ولا أرقام (▾ ◷ ➕ ⏳ ✓ …)
  const isGlyph = (t) => t.length <= 2 && !/[\p{L}\p{N}]/u.test(t);

  const lowText = new Map();
  const glyphs = new Map();
  const darkSurfaces = new Map();
  const scope = document.querySelector('main') || document.body;

  for (const el of scope.querySelectorAll('*')) {
    if (!visible(el)) continue;
    const s = getComputedStyle(el);
    const bg = effectiveBg(el);

    const text = ownText(el);
    if (text) {
      const fg = over(parseColor(s.color) || { r: 0, g: 0, b: 0, a: 1 }, bg);
      const cr = ratio(fg, bg);
      const size = parseFloat(s.fontSize);
      const weight = parseInt(s.fontWeight, 10) || 400;
      const large = size >= 24 || (size >= 18.66 && weight >= 700);
      const glyph = isGlyph(text);
      const need = glyph || large ? 3 : 4.5;
      if (cr < need) {
        const bucket = glyph ? glyphs : lowText;
        const k = sig(el);
        const hit = bucket.get(k) || { selector: k, count: 0, worst: 99, sample: '', fg: '', bg: '' };
        hit.count++;
        if (cr < hit.worst) {
          hit.worst = Math.round(cr * 100) / 100;
          hit.sample = text.slice(0, 40);
          hit.fg = s.color;
          hit.bg = `rgb(${Math.round(bg.r)} ${Math.round(bg.g)} ${Math.round(bg.b)})`;
        }
        bucket.set(k, hit);
      }
    }

    // سطح داكن بقوقعة فاتحة: خلفية معتمة داكنة على مساحة معتبرة
    const own = parseColor(s.backgroundColor);
    if (own && own.a > 0.5) {
      // `bg` يضمّ خلفية العنصر نفسه أصلاً، فتركيبها فوقه ثانيةً يضاعف العتمة
      // ويضخّم عدد «الأسطح الداكنة» زوراً — نركّب فوق خلفية الأب لا فوق bg.
      const parentBg = el.parentElement ? effectiveBg(el.parentElement) : WHITE;
      const solid = over(own, parentBg);
      const r = el.getBoundingClientRect();
      if (lum(solid) < 0.18 && r.width * r.height > 2000) {
        const k = sig(el);
        const hit = darkSurfaces.get(k) || { selector: k, count: 0, bg: s.backgroundColor };
        hit.count++;
        darkSurfaces.set(k, hit);
      }
    }
  }

  const byWorst = (a, b) => a.worst - b.worst;
  return {
    url: location.pathname,
    unreadable: [...lowText.values()].sort(byWorst),
    unreadableTotal: [...lowText.values()].reduce((n, h) => n + h.count, 0),
    glyphsBelow3: [...glyphs.values()].sort(byWorst),
    darkSurfaces: [...darkSurfaces.values()],
  };
};
