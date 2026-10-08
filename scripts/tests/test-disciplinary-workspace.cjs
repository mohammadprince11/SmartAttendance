// Offline synthetic browser fixtures only: no application, employee data or database.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, 'SmartAttendance.Web', file), 'utf8');
const source = read('Pages/DisciplinaryRules/Index.cshtml');
const baseline = process.env.DISCIPLINARY_BASELINE_VIEW
  ? fs.readFileSync(process.env.DISCIPLINARY_BASELINE_VIEW, 'utf8')
  : require('node:child_process').execFileSync('git', ['show', 'HEAD:SmartAttendance.Web/Pages/DisciplinaryRules/Index.cshtml'], {cwd:root, encoding:'utf8'});
const names = text => [...text.matchAll(/<(?:input|select|textarea|button)\b[^>]*\bname="([^"]+)"/g)].map(m => m[1]).sort();
const articleFields = new Set(['articleNumber', 'articleTitle', 'articleText', 'articlesVersion']);
assert.deepEqual(names(source).filter(name => !articleFields.has(name)), names(baseline), 'existing library/designer fields retained; user article editor is additive');
assert(source.includes('data-disciplinary-workspace'));
assert(read('Pages/DisciplinaryRules/_WorkspaceStyles.cshtml').includes('data-disciplinary-style'));
let checks = 3;
const conditions = read('wwwroot/js/zynora-conditions.js');
const cacheSource = conditions.slice(conditions.indexOf('var sharedCache ='), conditions.indexOf('function el('));
let catalogNode = {textContent:'first'};
const cacheContext = require('node:vm').createContext({document:{getElementById:()=>catalogNode}});
require('node:vm').runInContext(cacheSource, cacheContext);
assert.equal(cacheContext.sharedCriteria(),'first'); checks++;
catalogNode = {textContent:'refreshed'};
assert.equal(cacheContext.sharedCriteria(),'refreshed', 'new workspace uses refreshed criteria'); checks++;
const css = read('wwwroot/css/zynora-workscreen.css') + read('wwwroot/css/pages/index-387bf545e8.css');
function html(tab='library', query='') {
  const links = ['library','articles','templates','designer'].map(key => `<a class="zyw-tab" href="/DisciplinaryRules?tab=${key}" aria-selected="${key===tab}">${key}</a>`).join('');
  const catalog = '<script type="application/json" id="zy-criteria-catalog">[]</script>';
  const library = `<form method="get" action="/DisciplinaryRules"><input name="tab" value="library" type="hidden"><input id="search" name="search" value="${query}"><button>بحث</button></form>
  <form method="post" data-zyw-grid><table><tbody data-zyw-rows><tr><td><input name="rowId" value="1"><input name="rowName" value="فئة تجريبية"></td></tr></tbody></table><button id="save">حفظ</button></form>
  <a id="ladder" href="/DisciplinaryRules?tab=library&openTypeId=7">سلّم الجزاءات</a>${catalog}
  <script data-disciplinary-script data-disciplinary-once src="/js/zynora-grid-editor.js"></script>`;
  return `<html dir="rtl"><head><style>:root{--zy-space-4:24px;--zy-space-3:16px;--zy-space-2:8px;--zy-border:#aaa;--zy-text:#eee;--zy-text-muted:#ccc;} ${css}</style>
  <link data-disciplinary-style rel="stylesheet" href="/css/${tab==='designer'?'zynora-disciplinary-rules':'zynora-conditions'}.css"></head>
  <body><aside id="shell">shell</aside><section class="zyw zy-disciplinary-workspace" data-disciplinary-workspace>
  <header><h1>لائحة المخالفات</h1></header><p data-disciplinary-status hidden role="status"></p><nav class="zyw-tabs">${links}</nav>
  <main><section class="zy-disciplinary-content"><article>قسم ${tab}</article><article>${tab==='library'?library:'<form method="post"><input id="edit" name="name"><label class="zyw-checks"><input type="checkbox" id="check">فعال</label><button>حفظ</button></form>'}</article></section></main></section></body></html>`;
}
(async () => {
  const browser = await chromium.launch({channel:'msedge',headless:true});
  try {
    const page = await browser.newPage({viewport:{width:1280,height:850}});
    let gets = 0, gridLoads = 0;
    await page.route('https://disciplinary.test/**', async route => {
      const url = new URL(route.request().url());
      if (url.pathname.startsWith('/js/')) {
        gridLoads++;
        return route.fulfill({contentType:'application/javascript',body:read('wwwroot'+url.pathname)});
      }
      if (url.pathname.startsWith('/css/')) return route.fulfill({contentType:'text/css',body:''});
      gets++;
      if (url.searchParams.get('search')==='failure') return route.fulfill({status:500,body:'error'});
      if (url.searchParams.get('tab')==='articles') await new Promise(r=>setTimeout(r,150));
      await route.fulfill({contentType:'text/html',body:html(url.searchParams.get('tab')||'library',url.searchParams.get('search')||'')});
    });
    await page.goto('https://disciplinary.test/DisciplinaryRules');
    await page.addScriptTag({content:read('wwwroot/js/zynora-disciplinary-workspace.js')});
    await page.evaluate(() => { window.documentSentinel = 17; document.querySelector('#shell').sentinel = 99; });
    async function choose(tab) {
      await page.click(`.zyw-tab[href$="=${tab}"]`);
      await page.waitForFunction(tab=> document.querySelector(`[aria-selected="true"]`).textContent===tab && !document.querySelector('[data-disciplinary-workspace]').hasAttribute('aria-busy'),tab);
      assert.equal(await page.evaluate(()=>window.documentSentinel),17); checks++;
      assert.equal(await page.evaluate(()=>document.querySelector('#shell').sentinel),99); checks++;
    }
    await choose('templates'); await choose('designer'); await choose('library');
    assert.equal(gridLoads,1); checks++;
    await choose('templates'); await choose('library');
    assert.equal(gridLoads,1, 'delegated grid script loaded once'); checks++;
    await page.locator('#search').fill('بحث');
    await page.click('form[method=get] button');
    await page.waitForFunction(()=>location.search.includes('search=')&&!document.querySelector('[aria-busy=true]'));
    assert.equal(new URL(page.url()).searchParams.get('search'),'بحث'); checks++;
    await page.click('#ladder'); await page.waitForURL('**/*openTypeId=7');
    await page.waitForFunction(()=>!document.querySelector('[aria-busy=true]'));
    assert.equal(await page.evaluate(()=>window.documentSentinel),17); checks++;
    // Submitting an unchanged grid must not leave its editable inputs disabled.
    page.once('dialog', dialog=>dialog.accept());
    await page.click('#save');
    assert.equal(await page.locator('input[name=rowName]').isDisabled(),false); checks++;
    await choose('templates'); await page.locator('#edit').fill('تعديل غير محفوظ');
    const before = gets; page.once('dialog',dialog=>dialog.dismiss());
    await page.click('.zyw-tab[href$="=articles"]');
    assert.equal(gets,before); checks++;
    assert.equal(await page.locator('#edit').inputValue(),'تعديل غير محفوظ'); checks++;
    page.once('dialog',dialog=>dialog.accept()); await choose('library');
    await page.locator('#search').fill('failure'); await page.click('form[method=get] button');
    await page.waitForFunction(()=>document.querySelector('[data-disciplinary-status]').textContent.includes('تعذّر'));
    assert.equal(await page.evaluate(()=>window.documentSentinel),17); checks++;
    assert.equal(await page.locator('#search').inputValue(),'failure'); checks++;
    await choose('templates');
    await page.goBack(); await page.waitForFunction(()=>document.querySelector('[aria-selected=true]').textContent==='library'&&!document.querySelector('[aria-busy=true]'));
    assert.equal(await page.evaluate(()=>window.documentSentinel),17); checks++;
    // Latest navigation wins, including out-of-order responses.
    await page.click('.zyw-tab[href$="=articles"]'); await page.click('.zyw-tab[href$="=templates"]');
    await page.waitForFunction(()=>document.querySelector('[aria-selected=true]').textContent==='templates'&&!document.querySelector('[aria-busy=true]'));
    assert.equal(new URL(page.url()).searchParams.get('tab'),'templates'); checks++;
    await page.setViewportSize({width:390,height:844});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true); checks++;
    assert.equal(await page.locator('#check').evaluate(node=>getComputedStyle(node).width),'18px'); checks++;
    const boxes = await page.locator('.zy-disciplinary-content > article').evaluateAll(nodes=>nodes.map(node=>node.getBoundingClientRect().toJSON()));
    assert(boxes[1].top>boxes[0].bottom); checks++;
    console.log(`Disciplinary workspace: ${checks} checks passed (offline synthetic data only).`);
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
