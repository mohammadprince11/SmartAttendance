// Offline synthetic browser only: no app, live host, employee records or DB.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const view = read('SmartAttendance.Web/Pages/DisciplinaryRules/Index.cshtml');
const articlesStart = view.indexOf('else if (activeTab == "articles")');
const designerStart = view.indexOf('else if (activeTab == "designer")', articlesStart);
assert(articlesStart >= 0 && designerStart > articlesStart, 'article content section boundaries exist');
const part = view.slice(articlesStart, designerStart);
const formTag = part.match(/<form[^>]+data-disciplinary-articles[^>]*>/)[0];
assert(!formTag.includes('data-zyw-grid'), 'whole document must not drop unchanged rows');
assert(!view.includes('DisciplinaryPolicyPack.Articles'));
assert(!view.includes('asp-page-handler="AddSample"'));
assert(!view.includes('asp-page-handler="MergeDuplicates"'));
const template = part.match(/<template data-zyw-template>([\s\S]*?)<\/template>/)[1];
const code = read('SmartAttendance.Web/wwwroot/js/zynora-grid-editor.js');
const css = read('SmartAttendance.Web/wwwroot/css/zynora-workscreen.css');
let checks = 4;
(async () => {
    const browser = await chromium.launch({channel:'msedge', headless:true});
    try {
        const page = await browser.newPage({viewport:{width:1200,height:850}});
        await page.setContent(`<html dir="rtl"><head><style>${css}</style></head><body><section class="zyw"><article class="zy-card">
            <form method="post" data-disciplinary-articles><input name="articlesVersion" value="version" type="hidden">
            <div class="zyw-table-wrap"><table class="zyw-table"><thead><tr><th>الرقم</th><th>العنوان</th><th>النص</th><th>إزالة</th></tr></thead>
            <tbody data-zyw-rows>${template}</tbody></table></div><template data-zyw-template>${template}</template>
            <button type="button" data-zyw-add>قاعدة جديدة</button><button type="submit">حفظ القواعد</button></form>
            </article></section></body></html>`);
        await page.addScriptTag({content:code});
        await page.locator('[name=articleNumber]').fill('1');
        await page.locator('[name=articleTitle]').fill('قاعدة تجريبية');
        await page.locator('[name=articleText]').fill('سطر أول\nسطر ثانٍ');
        await page.click('[data-zyw-add]');
        assert.equal(await page.locator('[data-zyw-rows] > tr').count(),2); checks++;
        await page.locator('[name=articleNumber]').nth(1).fill('2');
        await page.locator('[name=articleTitle]').nth(1).fill('ثانية');
        await page.locator('[name=articleText]').nth(1).fill('<script>plain text</script>');
        await page.evaluate(() => document.querySelector('form').addEventListener('submit', event => {
            event.preventDefault(); window.savedRows = [...new FormData(event.target).entries()];
        }));
        await page.click('[type=submit]');
        const rows = await page.evaluate(() => window.savedRows);
        assert.deepEqual(rows.filter(([key])=>key==='articleNumber').map(([,value])=>value),['1','2']); checks++;
        assert.deepEqual(rows.filter(([key])=>key==='articleText').map(([,value])=>value),['سطر أول\nسطر ثانٍ','<script>plain text</script>']); checks++;
        assert.equal(await page.locator('input:disabled,textarea:disabled').count(),0); checks++;
        await page.locator('[data-zyw-remove]').first().click();
        await page.click('[type=submit]');
        assert.equal((await page.evaluate(()=>window.savedRows)).filter(([key])=>key==='articleNumber').length,1); checks++;
        await page.setViewportSize({width:390,height:844});
        assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth),true); checks++;
        await page.locator('[data-zyw-remove]').click(); await page.click('[type=submit]');
        assert.equal((await page.evaluate(()=>window.savedRows)).filter(([key])=>key==='articleNumber').length,0); checks++;
        console.log(`Disciplinary user articles: ${checks} checks passed (offline synthetic fixture).`);
    } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
