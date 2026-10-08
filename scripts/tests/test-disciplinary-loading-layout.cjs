// Offline synthetic contract/layout tests only; no live app or database.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, 'SmartAttendance.Web', file), 'utf8');
const model = read('Pages/DisciplinaryRules/Index.cshtml.cs');
const loader = model.slice(model.indexOf('private async Task LoadPageAsync'), model.indexOf('private async Task SeedBodyTextLayersAsync'));
assert(!loader.includes('DisciplinarySchema.EnsureAsync'), 'GET must not run schema/backfill');
const beforeLibrary = loader.slice(0, loader.indexOf('if (Tab != "library") return;'));
for (const call of ['LoadCategoriesAsync', 'LoadViolationTypesAsync', 'LoadPenaltyRulesAsync', 'BuildCatalogJsonAsync', 'EmployeeViolationCases'])
    assert(!beforeLibrary.includes(call), `non-library tabs must not load ${call}`);
assert(loader.includes('if (Categories.Count > 0)'));
assert(loader.includes('if (ViolationTypes.Count == 0) return;'));
assert(read('Pages/DisciplinaryRules/Index.cshtml').includes('data-disciplinary-paper-viewport'));
assert(read('wwwroot/js/zynora-disciplinary-workspace.js').includes('ZynoraDisciplinary?.dispose(previous)'));
const settings = model.slice(model.indexOf('private async Task<DisciplinarySettings> LoadSettingsAsync'), model.indexOf('private async Task<List<ViolationCategory>> LoadCategoriesAsync'));
const queriedKeys = new Set([...settings.matchAll(/N'([^']+)'/g)].map(m => m[1]));
const consumedKeys = new Set([...settings.matchAll(/Get\w+\(map, "([^"]+)"/g)].map(m => m[1]));
assert.deepEqual([...queriedKeys].sort(), [...consumedKeys].sort(), 'designer fetches only consumed settings, not article documents/backups');
assert(settings.includes('WHERE [Key] IN'));
let checks = 12;
const cssFiles = [...read('Pages/Shared/_Layout.cshtml').matchAll(/href="~\/css\/([^"]+)"/g)].map(m => m[1]);
const split = cssFiles.indexOf('zynora-components.css');
const css = [...cssFiles.slice(0, split), 'zynora-workscreen.css', 'zynora-disciplinary-rules.css', 'zynora-disciplinary-designer-preview-safe-v1.css', 'pages/index-387bf545e8.css', ...cssFiles.slice(split)].map(f => read('wwwroot/css/' + f)).join('\n');
const fixture = `<html lang="ar" dir="rtl" data-theme="dark"><head><style>${css}</style></head><body class="zy-app"><main class="zynora-content zy-scope zy-ui-contract"><section class="zyw zy-disciplinary-workspace" data-disciplinary-workspace><section class="zy-card zy-disciplinary-designer" data-disciplinary-designer><div class="nxpen-designer-grid"><div class="nxpen-designer-panel"><button data-designer-tab="text">طبقات</button><details data-layer-details="1"><summary>عنوان</summary><form data-layer-form data-layer-id="1"><input name="xPercent" value="8"><input name="yPercent" value="13"></form></details></div><section class="nxpen-live-preview"><div class="nxpen-a4-viewport" data-disciplinary-paper-viewport><div class="nxpen-a4-paper"><img class="nxpen-a4-form-layer" alt="synthetic" src="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='480' height='679'%3E%3Crect width='480' height='679' fill='white'/%3E%3C/svg%3E"><div id="logo" style="position:absolute;top:0;right:0;width:100%;height:12%">شعار تجريبي</div><div class="nxpen-a4-section nxpen-a4-body"><div id="title" class="nxpen-text-block" data-layer-id="1" data-zy-style="inset-inline-start:8%;top:13%;width:84%;font-size:20px;text-align:center">إشعار مخالفة وجزاء</div><div id="next" class="nxpen-text-block" data-zy-style="inset-inline-start:8%;top:20%;width:84%;font-size:13px">حقل تجريبي</div></div></div></div></section></div></section></section></main></body></html>`;
(async () => {
    const browser = await chromium.launch({channel:'msedge', headless:true});
    try {
        const page = await browser.newPage();
        await page.route('**/*', route => route.abort());
        await page.setContent(fixture);
        await page.addScriptTag({content:read('wwwroot/js/zynora-dynamic-style.js')});
        await page.addScriptTag({content:read('wwwroot/js/zynora-disciplinary-tools.js')});
        for (const width of [1440, 980, 390]) {
            await page.setViewportSize({width, height:1000});
            await page.waitForFunction(() => {
                const v = document.querySelector('[data-disciplinary-paper-viewport]');
                return Math.abs(v.clientWidth - document.querySelector('.nxpen-a4-paper').getBoundingClientRect().width) < 2;
            });
            const boxes = await page.evaluate(() => {
                const rect = id => document.querySelector(id).getBoundingClientRect().toJSON();
                const v = document.querySelector('[data-disciplinary-paper-viewport]');
                return {title:rect('#title'), logo:rect('#logo'), next:rect('#next'), paper:rect('.nxpen-a4-paper'), viewport:rect('[data-disciplinary-paper-viewport]'), scale:Number(v.style.getPropertyValue('--nxpen-preview-scale')), offset:getComputedStyle(document.querySelector('#title')).insetInlineStart, overflow:document.documentElement.scrollWidth > innerWidth};
            });
            assert(!boxes.overflow); checks++;
            assert(boxes.title.top >= boxes.logo.bottom); checks++;
            assert(boxes.title.bottom <= boxes.next.top); checks++;
            assert(Math.abs(boxes.title.height / boxes.scale - 36) < 5, 'title remains a single logical line'); checks++;
            assert(Math.abs(boxes.title.width / boxes.paper.width - .84) < .01); checks++;
            assert(Math.abs(parseFloat(boxes.offset) - 38.4) < .1); checks++;
            assert(Math.abs(boxes.paper.height - boxes.viewport.height) < 2); checks++;
        }
        // Drag percentages remain correct under scaling, without resetting other fields.
        const before = await page.locator('#title').boundingBox();
        await page.mouse.move(before.x + before.width/2, before.y + before.height/2);
        await page.mouse.down(); await page.mouse.move(before.x + before.width/2 - 10, before.y + before.height/2 + 10); await page.mouse.up();
        const xy = await page.locator('#title').evaluate(node => ({x:parseFloat(node.style.right), y:parseFloat(node.style.top)}));
        assert.equal(Number(await page.locator('input[name=xPercent]').inputValue()), xy.x); checks++;
        assert.equal(Number(await page.locator('input[name=yPercent]').inputValue()), xy.y); checks++;
        assert(xy.x > 8 && xy.y > 13); checks++;
        const actual = await page.locator('#title').evaluate(node => {
            const p=node.closest('.nxpen-a4-section').getBoundingClientRect(), r=node.getBoundingClientRect();
            return {x:(p.right-r.right)/p.width*100,y:(r.top-p.top)/p.height*100};
        });
        assert(Math.abs(actual.x-xy.x)<.1 && Math.abs(actual.y-xy.y)<.1); checks++;
        await page.evaluate(() => { const root=document.querySelector('[data-disciplinary-workspace]'); window.ZynoraDisciplinary.dispose(root); window.ZynoraDisciplinary.init(root); });
        assert.equal(await page.locator('.nxpen-layer-dragging').count(), 0); checks++;
        console.log(`Disciplinary loading/layout: ${checks} checks passed (offline synthetic only).`);
    } finally { await browser.close(); }
})().catch(error => {console.error(error);process.exitCode=1;});
