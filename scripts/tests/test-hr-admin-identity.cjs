/* Synthetic fixtures only: no server, production data, or network. */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const web = path.join(root, 'SmartAttendance.Web');
const views = ['Forms/Submissions', 'Contracts/Index', 'Contracts/Movements', 'EmployeeDocuments/Index',
  'Documents/Requests', 'CompanyDocuments/Index', 'Documents/Templates', 'Acknowledgments/Tracking', 'HrSettings/Index'];
const css = file => fs.readFileSync(path.join(web, 'wwwroot/css', file), 'utf8');
const shared = ['zynora-theme-contract.css', 'zynora-design-tokens.css', 'zynora-design-system.css', 'zynora-select-system.css', 'zynora-employee-picker.css'];
const contract = source => (source.match(/(?:asp-(?!append-version)[\w-]+|name|id|type|value|method|enctype|required|data-(?!zy-style)[\w-]+)\s*=\s*"[^"]*"/g) || []).sort();
const picker = '<div class="zyep"><label class="zyep-label">الموظف</label><div class="zyep-box"><input class="zyep-code" placeholder="رمز"><input class="zyep-name" placeholder="اسم الموظف" readonly><button type="button" class="zyep-open" aria-label="بحث">⌕</button><input type="hidden" class="zyep-id" name="employeeId"></div></div>';
const field = '<label>حقل اختبار<input name="sample" value="بيانات تجريبية"></label><label>اختيار<select name="choice"><option value="a">الأول</option><option value="b">الثاني</option></select></label><label>تفعيل<input type="checkbox" name="enabled"><input type="hidden" name="enabled" value="false"></label>' + picker;
const table = '<div class="zy-table-wrap zy-hr-table-scroll nxr-table-wrap"><table class="zyp-table nxr-table"><thead><tr><th>الموظف</th><th>المستند</th><th>الحالة</th><th>الإجراءات</th></tr></thead><tbody><tr><td>سجل اختبار</td><td>مستند تجريبي</td><td><span class="zyp-rank">حالي</span></td><td><div class="zyp-actions"><button type="button">مراجعة</button></div></td></tr></tbody></table></div>';
const panel = '<section class="nxhs-panel nxr-card"><header><span>السجل</span><h2>قائمة تجريبية</h2><p>بيانات معزولة للتحقق من التصميم فقط.</p></header><form class="zyp-editor-grid">' + field + '</form>' + table + '<footer class="nxhs-actions"><button type="submit">حفظ</button><a href="#">إلغاء</a></footer></section>';
(async () => {
  const browser = await chromium.launch({channel:'msedge', headless:true});
  let checks = 0;
  try {
    for (const view of views) {
      const sourcePath = 'SmartAttendance.Web/Pages/' + view + '.cshtml';
      const source = fs.readFileSync(path.join(root, sourcePath), 'utf8');
      const original = execFileSync('git', ['show', 'HEAD:' + sourcePath], {cwd:root, encoding:'utf8'});
      assert.deepEqual(contract(source), contract(original), view + '/form-and-handler-contract');
      assert(source.includes('zy-hr-admin-page'), view + '/scope');
      assert(source.includes('~/css/pages/hr-admin-identity.css'), view + '/stylesheet');
      assert(!source.includes('<style'), view + '/no-inline-style');
      const oldStyles = [...source.matchAll(/href="~\/css\/([^"]+)"/g)].map(match => match[1]).filter(file => file !== 'pages/hr-admin-identity.css');
      const styles = [...shared, ...oldStyles, 'pages/hr-admin-identity.css', 'zynora-dropdown-contract.css'].map(css).join('\n');
      for (const theme of ['dark', 'light']) for (const width of [320, 390, 900, 1440]) {
        const page = await browser.newPage({viewport:{width, height:1000}});
        await page.route('**/*', route => route.abort());
        const settings = '<section class="nxhs-cards">' + Array.from({length:10}, (_, i) => '<a href="#" class="nxhs-card zy-card"><strong>إعداد تجريبي ' + i + '</strong><small>وصف قصير لإعداد النظام.</small></a>').join('') + '</section>';
        const documents = '<section class="nxr-documents-kpis">' + Array.from({length:5}, () => '<div class="nxr-doc-kpi"><span>مستندات</span><strong>0</strong><small>للاختبار</small></div>').join('') + '</section><div class="nxr-document-layout"><aside class="nxr-card nxr-documents-upload-panel"><h3>رفع مستند</h3><form class="zyp-editor-grid">' + field + '</form><label class="nxr-file-picker" for="file">اختيار ملف</label><input type="file" id="file" class="nxr-file-input"></aside><section class="nxr-card nxr-documents-list-card">' + table + '</section></div>';
        await page.setContent('<!doctype html><html dir="rtl" data-theme="' + theme + '"><head><style>' + styles + '</style></head><body class="zy-app"><main class="nxhs-page nxr-documents-kayan-page zy-hr-admin-page"><header class="nxhs-titlebar"><a href="#">الأشخاص</a><h1>عنوان الصفحة</h1></header>' + (view === 'HrSettings/Index' ? settings : view === 'EmployeeDocuments/Index' ? documents : panel) + '</main></body></html>');
        await page.addScriptTag({content:fs.readFileSync(path.join(web, 'wwwroot/js/zynora-select-system.js'), 'utf8')});
        await page.waitForTimeout(300);
        const result = await page.evaluate(() => {
          const root = document.querySelector('.zy-hr-admin-page');
          const card = root.querySelector('.nxhs-panel, .nxhs-card, .nxr-card');
          const style = getComputedStyle(card);
          const field = root.querySelector('input[name="sample"]');
          const hidden = root.querySelector('input[type="hidden"]');
          const checkbox = root.querySelector('input[type="checkbox"]');
          return {overflow:document.documentElement.scrollWidth > innerWidth, gap:getComputedStyle(root).gap,
            image:style.backgroundImage, surface:style.backgroundColor,
            field:field && getComputedStyle(field).backgroundColor,
            height:field && field.getBoundingClientRect().height,
            hidden:hidden && getComputedStyle(hidden).display,
            check:checkbox && checkbox.getBoundingClientRect().width,
            columns:root.querySelector('.nxhs-cards') && getComputedStyle(root.querySelector('.nxhs-cards')).gridTemplateColumns.split(' ').length};
        });
        const label = view + '/' + theme + '/' + width;
        assert.equal(result.overflow, false, label + '/page-overflow');
        assert.equal(result.gap, width <= 540 ? '16px' : '24px', label + '/uniform-spacing');
        assert.equal(result.image, 'none', label + '/no-legacy-gradient');
        assert.equal(result.surface, theme === 'dark' ? 'rgb(15, 26, 46)' : 'rgb(255, 255, 255)', label + '/card-palette');
        if (view === 'HrSettings/Index') assert.equal(result.columns, width <= 540 ? 1 : width <= 800 ? 2 : 3, label + '/settings-grid');
        else {
          assert.equal(result.height, 44, label + '/field-height');
          assert.equal(result.field, theme === 'dark' ? 'rgb(17, 27, 42)' : 'rgb(255, 255, 255)', label + '/field-palette');
          assert.equal(result.check, 20, label + '/native-checkbox');
          assert.equal(result.hidden, 'none', label + '/hidden-post-value');
          assert.equal(await page.locator('.zyep-box').evaluate(el => el.scrollWidth <= el.clientWidth), true, label + '/picker-overflow');
          await page.locator('input[type="checkbox"]').check();
          assert.equal(await page.locator('input[type="checkbox"]').isChecked(), true);
          const trigger = page.locator('.nxcs-trigger').first();
          await trigger.scrollIntoViewIfNeeded();
          await trigger.click();
          await page.locator('.nxcs-panel .nxcs-option').last().click();
          assert.equal(await page.locator('select[name="choice"]').inputValue(), 'b', label + '/dropdown-behavior');
          if (view === 'EmployeeDocuments/Index') {
            await page.locator('.nxr-file-input').setInputFiles({name:'fixture.txt', mimeType:'text/plain', buffer:Buffer.from('synthetic fixture')});
            assert.equal(await page.locator('.nxr-file-input').evaluate(el => el.files[0].name), 'fixture.txt', label + '/native-file-selection');
          }
        }
        if (process.env.HR_ADMIN_SCREENSHOT && view === 'HrSettings/Index' && theme === 'dark' && width === 1440) await page.screenshot({path:process.env.HR_ADMIN_SCREENSHOT, fullPage:true});
        checks++;
        await page.close();
      }
    }
    console.log('PASS: ' + checks + ' HR page fixtures; all nine form/handler contracts unchanged, dark/light, 320/390/900/1440, palette, spacing, tables, settings grid, checkbox/select/file behavior.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
