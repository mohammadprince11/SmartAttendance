// Offline synthetic designer: no application server, database or real records.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, 'SmartAttendance.Web', file), 'utf8');
const pageSource = read('Pages/DisciplinaryRules/Index.cshtml');
const modelSource = read('Pages/DisciplinaryRules/Index.cshtml.cs');
const tools = read('wwwroot/js/zynora-disciplinary-tools.js');
assert(pageSource.includes('if (Model.IsWorkspaceRequest) { Layout = null; }'));
assert(modelSource.includes('Request.Method == "GET"'));
const loader = modelSource.slice(modelSource.indexOf('private async Task LoadPageAsync'),modelSource.indexOf('private async Task SeedDefaultLibraryAsync'));
const designerLoad = loader.match(/if \(Tab == "designer"\)([\s\S]*?)if \(Tab == "templates"\)/)[1];
assert(designerLoad.includes('LoadSettingsAsync') && designerLoad.includes('LoadTextBlocksAsync') && designerLoad.includes('return;'));
assert(!designerLoad.includes('BuildCatalogJsonAsync') && !designerLoad.includes('SalaryItemStore') && !designerLoad.includes('LoadMessageTemplatesAsync'));
assert(!pageSource.includes('zynora-disciplinary-remove-header-footer.js'));
assert(!pageSource.includes('zynora-disciplinary-rules.js'));
assert(!tools.includes('mousedown') && !tools.includes('setTimeout'));
let checks = 7;
const shellCss = [...read('Pages/Shared/_Layout.cshtml').matchAll(/href="~\/css\/([^"]+)"/g)].map(match=>match[1]);
const split = shellCss.indexOf('zynora-components.css');
const css = [...shellCss.slice(0,split),'zynora-workscreen.css','zynora-disciplinary-rules.css','zynora-disciplinary-designer-preview-safe-v1.css','pages/index-387bf545e8.css',...shellCss.slice(split)].map(file=>read('wwwroot/css/'+file)).join('\n');
const forms = [1,2,3].map(id=>`<details name="disciplinary-layer" class="nxpen-edit-item" data-layer-details="${id}"><summary><span>متن</span><strong>نص تجريبي ${id}</strong></summary><form data-layer-form data-layer-id="${id}"><div class="zyw-form-grid"><div><label>من اليمين %</label><input name="xPercent" value="8"></div><div><label>من الأعلى %</label><input name="yPercent" value="10"></div><div class="wide"><label>النص</label><textarea>نص تجريبي</textarea></div></div><button>حفظ طبقة النص</button></form></details>`).join('');
const fixture = `<html dir="rtl" data-theme="dark"><head><style>${css}</style></head><body><section class="zyw zy-disciplinary-workspace" data-disciplinary-workspace><section class="zy-card zy-disciplinary-designer" data-disciplinary-designer><h2>مصمم الفورمة</h2><nav class="zyw-tabs"><button class="zyw-tab" data-designer-tab="background" aria-selected="true">إعدادات الفورمة</button><button class="zyw-tab" data-designer-tab="text">طبقات النص</button></nav><section class="nxpen-designer-grid"><div class="nxpen-designer-panel"><section class="zy-card" data-designer-panel="background"><h3>فورمة A4</h3><input type="file"><button>حفظ الفورمة</button></section><section class="zy-card" data-designer-panel="background"><h3>الهيدر والفوتر</h3><input type="file"></section><details class="zy-card" data-designer-panel="text"><summary>إعدادات متقدمة</summary><button>إنشاء الطبقات</button></details><details class="zy-card" data-designer-panel="text"><summary>إضافة طبقة نص جديدة</summary><textarea></textarea></details><section class="zy-card" data-designer-panel="text"><h3>طبقات النص الحالية</h3><div class="nxpen-edit-list">${forms}</div></section></div><section class="nxpen-live-preview"><header><h3>معاينة الفورمة</h3></header><div class="nxpen-drag-help">اسحب النص ثم احفظ الطبقة</div><div class="nxpen-a4-paper"><div class="nxpen-a4-section nxpen-a4-body"><div class="nxpen-text-block" data-layer-id="1" style="right:8%;top:10%;width:60%">نص تجريبي</div></div></div></section></section></section></section></body></html>`;
(async()=>{
  const browser = await chromium.launch({channel:'msedge',headless:true});
  try {
    const page = await browser.newPage({viewport:{width:1440,height:1000}});
    await page.route('**/*', route=>route.abort());
    await page.setContent(fixture.replace('<html ', '<html lang="ar" data-ready="1" ').replace('<body><section','<body class="zy-app"><main class="zynora-content zy-scope zy-ui-contract"><section').replace('</body>','</main></body>'));
    await page.addScriptTag({content:tools});
    assert.equal(await page.locator('[data-designer-panel=text]:visible').count(),0); checks++;
    assert.equal(await page.locator('[data-designer-panel=background]:visible').count(),2); checks++;
    await page.click('[data-designer-tab=text]');
    assert.equal(await page.locator('[data-designer-panel=background]:visible').count(),0); checks++;
    assert.equal(await page.locator('[data-designer-panel=text]:visible').count(),3); checks++;
    assert.equal(await page.locator('[data-layer-details][open]').count(),0); checks++;
    await page.click('[data-layer-details="1"] summary'); await page.click('[data-layer-details="2"] summary');
    assert.equal(await page.locator('[data-layer-details][open]').count(),1); checks++;
    // Re-init/script replay must not duplicate handlers or reset the chosen panel.
    await page.addScriptTag({content:tools});
    assert.equal(await page.locator('[data-designer-tab=text]').getAttribute('aria-selected'),'true'); checks++;
    await page.locator('.nxpen-text-block').scrollIntoViewIfNeeded();
    const box = await page.locator('.nxpen-text-block').boundingBox(); assert(box?.width>0); checks++;
    await page.mouse.move(box.x+box.width/2,box.y+box.height/2); await page.mouse.down();
    await page.mouse.move(box.x+box.width/2-40,box.y+box.height/2+30,{steps:6}); await page.mouse.up();
    assert.notEqual(await page.locator('[data-layer-id="1"] [name=xPercent]').inputValue(),'8'); checks++;
    const inputX = Number(await page.locator('[data-layer-id="1"] [name=xPercent]').inputValue());
    const styleX = await page.locator('.nxpen-text-block').evaluate(node=>parseFloat(node.style.right));
    assert.equal(styleX,inputX); checks++;
    assert.equal(await page.locator('.nxpen-layer-dragging').count(),0); checks++;
    assert.equal(await page.locator('[data-layer-details="1"]').evaluate(node=>node.open),true); checks++;
    const layout = await page.locator('.nxpen-designer-grid').evaluate(node=>getComputedStyle(node).gridTemplateColumns);
    assert.equal(layout.trim().split(/\s+/).length,2); checks++;
    await page.setViewportSize({width:390,height:844});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true); checks++;
    assert.equal(await page.locator('.nxpen-designer-grid').evaluate(node=>getComputedStyle(node).gridTemplateColumns.trim().split(/\s+/).length),1); checks++;
    for (const theme of ['light','dark']) {
      await page.evaluate(theme=>document.documentElement.dataset.theme=theme,theme);
      assert.equal(await page.locator('[data-designer-tab=text]').isVisible(),true); checks++;
    }
    await page.setViewportSize({width:1440,height:1000});
    await page.click('[data-designer-tab=background]');
    await page.screenshot({path:path.join(root,'docs/verification/disciplinary-designer-organized-20261008.png'),fullPage:true});
    console.log(`Disciplinary designer: ${checks} checks passed (offline synthetic only).`);
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
