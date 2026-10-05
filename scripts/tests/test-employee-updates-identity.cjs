/* Isolated fixtures only. Real styles/scripts, synthetic values, no server/DB. */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const read = (folder, file) => fs.readFileSync(path.join(root, 'SmartAttendance.Web/wwwroot', folder, file), 'utf8');
const styles = ['zynora-theme-contract.css', 'zynora-design-tokens.css', 'zynora-design-system.css',
  'zynora-select-system.css', 'zynora-employee-updates.css', 'pages/employee-updates-identity.css',
  'zynora-dropdown-contract.css'].map(file => read('css', file)).join('\n');
const field = (i, financial) => `<div class="nxupd-field" data-field-key="${financial ? 'BasicSalary' : 'Field' + i}">
  <label>حقل تجريبي ${i + 1}</label>${i === 1 ? '<select name="sampleChoice"><option value="a">الخيار الأول</option><option value="b">الخيار الثاني</option></select>' : '<input name="sample' + i + '" value="100" ' + (financial ? 'class="nxupd-accounting-input"' : '') + '>'}
  <small>القيمة الحالية: <b>بيانات اختبار فقط</b></small></div>`;
const stage = `<section class="nxupd-card nxupd-stage-card"><header class="nxupd-card-head"><div><h2>معلومات الموظف</h2><p>بيانات معزولة لا ترتبط بأي موظف.</p></div><span class="nxupd-pill">حركة غير مقفلة</span></header>
  <div class="nxupd-rule-box"><strong>طريقة العمل</strong><span>مراجعة التغييرات قبل اعتماد الحركة.</span></div><form class="nxupd-form">
  <section class="nxupd-movement-meta"><div class="nxupd-field"><label>تاريخ السريان</label><input type="date"><small>توضيح متعدد الأسطر لقياس المساحة دون قص النصوص.</small></div><div class="nxupd-field"><label>بأثر رجعي</label><select><option>تصحيح بيانات فقط</option></select><small>تاريخ ماضٍ وحده لا يكفي: تصحيح الإدخال لا يغيّر الحسابات.</small></div><div class="nxupd-note"><label>ملاحظة الحركة</label><input name="note"></div></section>
  <div class="nxupd-stage-blocks">${[false, true].map(financial => `<section class="nxupd-stage-block" data-section-key="${financial ? 'financial' : 'personal'}"><header><div><span>قسم مستقل</span><h3>${financial ? 'البيانات المالية' : 'البيانات الشخصية'}</h3></div><strong>6 حقول</strong></header><div class="nxupd-fields">${Array.from({length:6}, (_, i) => field(i, financial)).join('')}</div></section>`).join('')}</div>
  <footer class="nxupd-form-footer"><button type="button">إنشاء حركة غير مقفلة</button><a href="#confirm">الذهاب إلى التأكيد</a></footer></form></section>`;
const confirm = `<section class="nxupd-card"><header class="nxupd-card-head"><h2>التأكيد قبل القفل</h2></header><div class="nxupd-batch-list"><article class="nxupd-batch"><header><div><h3>حركة تجريبية</h3><small>تفاصيل للاختبار فقط</small></div><strong>غير مقفلة</strong></header><div class="nxupd-change-table"><table><thead><tr><th>الحقل</th><th>القيمة السابقة</th><th>القيمة الجديدة</th></tr></thead><tbody><tr><td>حقل تجريبي</td><td>أ</td><td>ب</td></tr></tbody></table></div><footer><form data-nxupd-confirm data-nxupd-confirm-tone="danger"><button type="submit" class="danger">حذف الحركة</button></form></footer></article></div></section>`;
const history = `<section class="nxupd-card"><header class="nxupd-card-head"><h2>سجل التغييرات</h2></header><div class="nxupd-history"><article><div class="nxupd-history-dot"></div><div class="nxupd-history-body"><header><strong>حركة تجريبية</strong><small>سجل اختبار</small></header><ul><li><b>حقل تجريبي</b><span>القيمة السابقة</span><i>←</i><strong>القيمة الجديدة</strong></li></ul></div></article></div></section>`;
const modal = `<div class="nxupd-modal-backdrop" data-nxupd-modal hidden><div class="nxupd-modal"><button class="nxupd-modal-close" data-nxupd-modal-cancel>×</button><div class="nxupd-modal-icon" data-nxupd-modal-icon></div><div class="nxupd-modal-copy"><h2 data-nxupd-modal-title></h2><p data-nxupd-modal-message></p></div><footer class="nxupd-modal-actions"><button type="button" data-nxupd-modal-cancel>إلغاء</button><button class="confirm" type="button" data-nxupd-modal-confirm>تأكيد</button></footer></div></div>`;
(async () => {
  const browser = await chromium.launch({channel: process.env.DROPDOWN_BROWSER_CHANNEL || 'msedge', headless:true});
  let checks = 0;
  try {
    for (const theme of ['dark', 'light']) for (const width of [390, 900, 1440]) for (const tab of ['stage', 'confirm', 'history']) {
      const page = await browser.newPage();
      page.on('pageerror', error => console.error('Browser error:', error.message));
      await page.route('**/*', route => route.abort());
      await page.setViewportSize({width, height:1000});
      await page.setContent(`<!DOCTYPE html><html dir="rtl" data-theme="${theme}"><head><style>${styles}</style></head><body class="zy-app"><section class="nxupd-page nxupd-page-v14b zy-employee-updates"><header class="nxupd-hero"><div><h1>تحديثات الموظف</h1><p>إنشاء حركة ومراجعتها قبل الاعتماد.</p></div><div class="nxupd-hero-status"><strong>0</strong><small>حركة غير مقفلة</small></div></header><section class="nxupd-selector-card nxupd-selector-card-single"><label>اختيار الموظف</label><button type="button">اختيار تجريبي</button></section><nav class="nxupd-top-tabs"><a class="active">إدخال حركة جديدة</a><a>التأكيد قبل القفل</a><a>سجل التغييرات</a></nav><main class="nxupd-layout nxupd-layout-single"><section class="nxupd-content">${{stage,confirm,history}[tab]}</section></main></section>${modal}</body></html>`);
      await page.addScriptTag({content:read('js','zynora-employee-updates.js')});
      await page.addScriptTag({content:read('js','zynora-select-system.js')});
      // Allow the shared enhancer's scheduled startup refreshes to settle.
      await page.waitForTimeout(1600);
      const result = await page.evaluate(() => {
        const s = sel => getComputedStyle(document.querySelector(sel));
        return {overflow:document.documentElement.scrollWidth > innerWidth,
          gap:s('.zy-employee-updates').gap, panel:s('.nxupd-card').backgroundColor,
          image:s('.nxupd-card').backgroundImage,
          columns:document.querySelector('.nxupd-fields') ? s('.nxupd-fields').gridTemplateColumns.split(' ').length : 0,
          blocks:document.querySelector('.nxupd-stage-blocks') ? s('.nxupd-stage-blocks').display : '',
          button:document.querySelector('.nxupd-form-footer button') ? s('.nxupd-form-footer button').backgroundColor : '',
          field:document.querySelector('.nxupd-fields input') ? s('.nxupd-fields input').backgroundColor : '',
          finance:document.querySelector('[data-section-key="financial"]') ? s('[data-section-key="financial"]').backgroundImage : ''};
      });
      const label = `${theme}/${width}/${tab}`;
      assert.equal(result.overflow,false,label+'/overflow');
      assert.equal(result.gap,'24px',label+'/spacing');
      assert.equal(result.panel,theme==='dark' ? 'rgb(15, 26, 46)' : 'rgb(255, 255, 255)',label+'/surface');
      assert.equal(result.image,'none',label+'/no-gradient');
      if(tab==='stage') {
        assert.equal(result.columns,width<=760 ? 1 : width<=1100 ? 2 : 3,label+'/columns');
        assert.equal(result.blocks,'grid',label+'/no-masonry');
        assert.equal(result.finance,'none',label+'/financial-surface');
        assert.equal(result.button,'rgb(72, 108, 143)',label+'/brand-button');
        assert.equal(result.field,theme==='dark' ? 'rgb(17, 27, 42)' : 'rgb(255, 255, 255)',label+'/field');
        const trigger = page.locator('.nxupd-fields .nxcs-trigger').first();
        await trigger.scrollIntoViewIfNeeded();
        // The shared component deliberately dismisses its portal on scroll.
        await page.waitForTimeout(150);
        await trigger.click();
        await page.locator('.nxcs-panel .nxcs-option').last().click();
        assert.equal(await page.locator('select[name="sampleChoice"]').first().inputValue(),'b',label+'/selection');
      }
      if(tab==='confirm') {
        await page.locator('.danger').click();
        assert.equal(await page.locator('[data-nxupd-modal]').getAttribute('hidden'),null,label+'/modal-open');
        assert.equal(await page.locator('.nxupd-modal').evaluate(el=>getComputedStyle(el).backgroundImage),'none',label+'/modal-colors');
        await page.keyboard.press('Escape');
        assert.notEqual(await page.locator('[data-nxupd-modal]').getAttribute('hidden'),null,label+'/modal-cancel');
      }
      if(process.env.EMPLOYEE_UPDATES_SCREENSHOT && theme==='dark' && width===1440 && tab==='stage') await page.screenshot({path:process.env.EMPLOYEE_UPDATES_SCREENSHOT,fullPage:true});
      checks++;
      await page.close();
    }
    console.log(`PASS: ${checks} Employee Updates fixtures (three tabs, dark/light, mobile/tablet/desktop, no horizontal overflow, palette, grid, dropdown selection and confirmation cancel).`);
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
