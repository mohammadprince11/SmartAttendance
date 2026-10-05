// Regression fixtures use the complete application CSS stack, not just page CSS.
// No application server, authentication, employee records or network access.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const web = path.join(root, 'SmartAttendance.Web');
const read = file => fs.readFileSync(path.join(web, file), 'utf8');
const layout = read('Pages/Shared/_Layout.cshtml');
const shell = [...layout.matchAll(/href="~\/css\/([^"]+)"/g)].map(match => match[1]);
const pages = ['Contracts/Index', 'Contracts/Movements', 'EmployeeDocuments/Index', 'Documents/Requests', 'CompanyDocuments/Index'];
const picker = '<div class="zyep"><label class="zyep-label">الموظف</label><div class="zyep-box"><input type="text" class="zyep-code" placeholder="رمز"><input type="text" class="zyep-name" placeholder="اسم الموظف" readonly><button type="button" class="zyep-open" aria-label="بحث عن موظف"><svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="7"></circle></svg></button><input type="hidden" name="EmployeeId"></div></div>';
const field = '<label>رقم العقد<input type="text" placeholder="اختياري"></label>';
const fixtures = {
  'Contracts/Index': '<form class="nxhs-panel zyp-editor"><h3>إضافة عقد للسجل</h3><div class="zyp-editor-grid">' + picker + field + '</div><footer class="nxhs-actions"><button type="submit">حفظ العقد</button><a class="zyp-move zyu-e3da42537cb5" href="#">تحديثات العقود ←</a></footer></form>',
  'Contracts/Movements': '<form class="nxhs-panel zyp-editor"><h3>حركة جديدة</h3><div class="zyp-editor-grid">' + field + '</div><footer class="nxhs-actions"><button type="submit">تنفيذ الحركة</button><a class="zyp-move zyu-e3da42537cb5" href="#">← السجلّ</a></footer></form>',
  'EmployeeDocuments/Index': '<div class="nxr-document-layout nxr-documents-kayan-layout"><aside class="nxr-card nxr-documents-upload-panel"><form class="nxr-form nxr-documents-upload-form"><div class="nxr-field">' + picker + '</div></form></aside><section class="nxr-card nxr-documents-list-card"><h3>سجل المستندات</h3><div class="nxr-table-wrap nxr-documents-table-wrap is-empty"><table class="nxr-table nxr-documents-table"><thead><tr><th>الموظف</th><th>فتح</th></tr></thead><tbody><tr><td colspan="2" class="nxr-empty"><div class="zy-hr-empty-state" role="status">لا توجد مستندات مرفوعة حالياً.</div></td></tr></tbody></table></div></section></div>',
  'Documents/Requests': '<section class="nxhs-panel"><h2>طلبات الموظفين</h2><form class="zyp-editor-grid zy-hr-filter-form"><label>الحالة<select><option>قيد المراجعة</option></select></label><div class="zy-hr-filter-action"><button class="zyc-add-rule" type="submit">تطبيق</button></div></form></section>',
  'CompanyDocuments/Index': '<section class="nxhs-panel"><h2>فئات وثائق الشركة</h2><form class="zy-hr-category-form"><div class="zyp-editor-grid"><label>اسم فئة جديدة<input></label><label>الوصف<input></label><label>الترتيب<input type="number"></label></div><footer class="nxhs-actions"><button type="submit" class="zyc-add-rule">إضافة فئة</button></footer></form></section>'
};
(async () => {
  const browser = await chromium.launch({channel:'msedge', headless:true});
  let count = 0;
  try {
    for (const view of pages) {
      const pageCss = [...read('Pages/' + view + '.cshtml').matchAll(/href="~\/css\/([^"]+)"/g)].map(match => match[1]);
      // Match the shell's late shared styles, which caused the original clipping.
      const split = shell.indexOf('zynora-components.css');
      const styles = [...shell.slice(0, split), ...pageCss, ...shell.slice(split)].map(file => read('wwwroot/css/' + file)).join('\n');
      for (const theme of ['dark','light']) for (const width of [390,900,1800]) {
        const page = await browser.newPage({viewport:{width,height:1100}});
        await page.route('**/*', route => route.abort());
        await page.setContent('<!doctype html><html lang="ar" dir="rtl" data-ready="true" data-theme="' + theme + '"><head><style>' + styles + '</style></head><body class="zy-app"><main class="zynora-content zy-scope zy-ui-contract"><section class="zy-hr-admin-page ' + (view === 'EmployeeDocuments/Index' ? 'nxr-page nxr-documents-kayan-page' : 'nxhs-page') + '">' + fixtures[view] + '</section></main></body></html>');
        const label = view + '/' + theme + '/' + width;
        await page.waitForTimeout(500); // Let the legacy page entrance animation finish.
        assert.equal(await page.locator('.zy-hr-admin-page').isVisible(), true, label + '/rendered-visible: ' + JSON.stringify(await page.locator('.zy-hr-admin-page').evaluate(el => {const rows=[]; for(let n=el;n;n=n.parentElement){const s=getComputedStyle(n);rows.push({tag:n.tagName,class:n.className,display:s.display,visibility:s.visibility,opacity:s.opacity,rect:n.getBoundingClientRect().toJSON()});}return rows;})));
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, label + '/page-overflow');
        if (view.startsWith('Contracts/')) {
          const button = page.locator('.zyp-move');
          assert.equal(await button.evaluate(el => el.scrollWidth <= el.clientWidth && getComputedStyle(el).whiteSpace === 'nowrap'), true, label + '/text-link-not-clipped');
          assert((await button.boundingBox()).height >= 40, label + '/link-hit-area');
        }
        if (view === 'Contracts/Index' || view === 'EmployeeDocuments/Index') {
          const geometry = await page.locator('.zyep-box').evaluate(el => {
            const box = el.getBoundingClientRect();
            return [...el.querySelectorAll('input:not([type="hidden"]),button')].map(child => {
              const r = child.getBoundingClientRect();
              return {height:r.height, fits:r.top >= box.top - 1 && r.bottom <= box.bottom + 1};
            });
          });
          geometry.forEach(g => { assert(Math.abs(g.height - 44) < 0.5,label+'/picker-height'); assert.equal(g.fits,true,label+'/picker-not-clipped'); });
        }
        if (view === 'EmployeeDocuments/Index') {
          assert.equal(await page.locator('td.nxr-empty').evaluate(el => getComputedStyle(el).display), 'table-cell', label + '/colspan-preserved');
          assert.equal(await page.locator('.is-empty').evaluate(el => getComputedStyle(el).minHeight), '0px', label + '/no-empty-dead-space');
          assert((await page.locator('.zy-hr-empty-state').boundingBox()).height >= 127.5, label + '/empty-state-height');
        }
        if (view === 'Documents/Requests') {
          const filter = await page.locator('.zy-hr-filter-form > label').boundingBox();
          const action = await page.locator('.zy-hr-filter-action').boundingBox();
          assert.equal(await page.locator('.zy-hr-filter-form').evaluate(el => getComputedStyle(el).display), 'flex', label + '/compact-filter');
          if (width >= 900) assert(Math.abs(filter.y + filter.height - action.y - action.height) <= 2, label + '/action-alignment');
        }
        if (view === 'CompanyDocuments/Index') {
          const fields = await page.locator('.zy-hr-category-form > .zyp-editor-grid').boundingBox();
          const footer = await page.locator('.zy-hr-category-form > footer').boundingBox();
          assert(footer.y >= fields.y + fields.height, label + '/category-action-separate-row');
          assert.equal(await page.locator('.zy-hr-category-form .nxhs-actions').evaluate(el => getComputedStyle(el).justifyContent), 'flex-start', label + '/category-rtl-action');
        }
        if (process.env.HR_CLIPPING_SCREENSHOT_DIR && theme === 'dark' && width === 1800) {
          await page.screenshot({path:path.join(process.env.HR_CLIPPING_SCREENSHOT_DIR, 'hr-' + view.replaceAll('/','-') + '.png'), fullPage:true});
        }
        count++;
        await page.close();
      }
    }
    console.log('PASS: ' + count + ' full-shell clipping regression fixtures: contract links, picker geometry, empty colspan, filter and category action alignment.');
  } finally { await browser.close(); }
})().catch(error => {console.error(error); process.exitCode=1;});
