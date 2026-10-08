// Offline audit regression fixtures. Never connects to an application or database.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const web = path.join(root, 'SmartAttendance.Web');
const read = p => fs.readFileSync(path.join(web, p), 'utf8');
const css = p => read('wwwroot/css/' + p);
const source = p => read('Pages/HrSettings/' + p + '.cshtml');
const shell = [...read('Pages/Shared/_Layout.cshtml').matchAll(/href="~\/css\/([^"]+)"/g)].map(m => m[1]);
const split = shell.indexOf('zynora-components.css');
const styles = [...shell.slice(0, split), 'zynora-hr-settings-v16.css', 'hrms-core.css',
 'pages/hr-admin-identity.css', 'pages/hr-settings-identity.css', 'pages/people-ai-workspace.css', ...shell.slice(split)].map(css).join('\n');
const reasons = source('TerminationReasons');
let row = reasons.match(/@foreach \(var reason[\s\S]*?(<tr>[\s\S]*?<\/tr>)/)[1];
assert.equal((row.match(/<td>/g)||[]).length, 6, 'one real cell per header');
row = row.replaceAll('@reason.Id', '7').replaceAll('@reason.Name', 'سبب تجريبي')
 .replace(/@reason.EndOfServicePercent.ToString\("0"\)/g,'100').replace(/ selected="[^"]*"/g,'')
 .replace(/asp-page-handler="Update"/g,'action="/synthetic-save"');
assert(source('EmployeeCodeSchemaPage').includes('zy-hr-admin-page zy-hr-settings-page'));
assert(!source('EmployeeCodeSchemaPage').includes('كيان'));
assert(!source('SelfServiceSettings').includes('كيان'));
assert(!source('LeavePolicies').includes('class="zy-policy-disclosure" open'));
assert(source('_RequestTypeForm').includes('data-empty-text="بلا شروط — هذا النوع متاح'));
const label = (name) => '<label class="nxhs-field-row"><span>'+name+'</span><select><option>اختيار تجريبي</option></select></label>';
const codeForm = source('EmployeeCodeSchemaPage').match(/<form method="post"[\s\S]*?<\/form>/)[0]
 .replace(/@Model\.[^"\n]+/g,'123');
const notice = '<form class="nxhs-panel nxhs-notice-panel"><section class="nxhs-subbox"><h3>استثناءات</h3>'+['أ','ب','ج'].map(label).join('')+'</section></form>';
const self = '<form class="nxhs-panel nxhs-self-panel">'+['أ','ب','ج','د'].map(n=>'<section class="nxhs-setting-section"><h2>'+n+'</h2><label class="nxhs-check-line"><input type="checkbox">خيار</label></section>').join('')+'<footer class="nxhs-actions"><button>حفظ</button></footer></form>';
const policy = '<details class="zy-policy-disclosure"><summary><span><strong>سياسة تجريبية</strong><small>إجازات</small></span><span class="lp-state">مهيأة</span></summary><form class="lp-card"><div class="lp-grid">'+label('الاستحقاق')+'</div><footer class="lp-actions"><button type="submit">حفظ</button></footer></form></details>';
const approvalSource = source('ApprovalTemplates');
const picker = approvalSource.match(/<div class="zyu-a66c33606303">[\s\S]*?<\/div>/)[0]
 .replace(/@foreach[^\n]+/g,'<option value="test">عنصر تجريبي</option>');
const pickerBehavior = approvalSource.match(/function aptStepTypeChanged\(\) \{[\s\S]*?\n        \}/)[0];
const fixtures = {reasons:'<section class="nxhs-panel"><div class="nxhs-table-wrap"><table class="nxhs-table nxhs-edit-table"><thead><tr>'+['الاسم','الإلزامية','النسبة','الخدمة','الحالة','الإجراء'].map(n=>'<th>'+n+'</th>').join('')+'</tr></thead><tbody>'+row+'</tbody></table></div></section>',
 code:'<section class="hrms-table-card">'+codeForm+'</section>', notice, self, policies:policy+policy,
 picker:'<section class="nxhs-panel apt-editor"><section class="apt-committee-available">'+picker+'</section></section>',
 tabs:'<div class="hrms-tabs"><button class="hrms-tab active">مختار</button><button class="hrms-tab">آخر</button></div><nav class="pai-tabs"><button aria-selected="true">مختار</button><button aria-selected="false">آخر</button></nav>',
 toolbar:'<section class="zy-card zy-notification-toolbar"><form><label>الشركة</label><select><option>شركة اختبار</option></select><button>عرض</button></form><label for="search">البحث عن إشعار</label><input id="search" type="search"><span role="status"></span><p>الحفظ تلقائي للشركة المعروضة فقط.</p></section>',
 categories:'<details class="rtc-cat zy-request-category"><summary><strong>مجموعة تجريبية</strong><span class="rtc-badge">2 نوع</span></summary><div class="rtc-cat-head"><span class="rtc-cat-title">مجموعة تجريبية</span></div><details class="rtc-type"><summary>طلب تجريبي</summary><form><input name="test"></form></details></details>'};
(async()=>{
 const browser = await chromium.launch({channel:'msedge',headless:true});
 let cases = 0;
 try {
  for (const theme of ['dark','light']) for (const width of [390,900,1800]) for (const [view, html] of Object.entries(fixtures)) {
   const page = await browser.newPage({viewport:{width,height:1100}});
   await page.route('**/*',route=>route.abort());
   const cls = 'zy-hr-admin-page zy-hr-settings-page '+(view==='code'?'zy-employee-code-settings':view==='picker'?'zy-approval-workspace':'');
   await page.setContent('<!doctype html><html dir="rtl" lang="ar" data-ready="true" data-theme="'+theme+'"><head><style>'+styles+'</style></head><body class="zy-app"><main class="zynora-content zy-scope zy-ui-contract"><section class="'+cls+'">'+html+'</section></main></body></html>');
   await page.addScriptTag({content:read('wwwroot/js/zynora-select-system.js')});
   const key = view+'/'+theme+'/'+width;
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),key+'/no-page-overflow');
   if (view==='reasons') {
    assert.deepEqual(await page.locator('#reason-7').evaluate(el=>[...new FormData(el).keys()].sort()),['endOfServicePercent','id','isActive','isMandatory','name','requiresSelfService'],key+'/external-controls-still-submit');
    assert(await page.locator('tbody tr').evaluate(row=>[...row.children].every((td,i)=>Math.abs(td.getBoundingClientRect().x-row.closest('table').querySelectorAll('th')[i].getBoundingClientRect().x)<1)),key+'/headers-align');
    const mandatoryTrigger = page.locator('tbody td').nth(1).locator('.nxcs-trigger');
    await mandatoryTrigger.scrollIntoViewIfNeeded();
    // The select system intentionally closes menus on ancestor scroll. Let the
    // horizontal table scroll event finish before opening the menu.
    await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
    await mandatoryTrigger.click();
    await page.getByRole('option', {name:'إلزامي', exact:true}).click();
    assert.equal(await page.locator('#reason-7').evaluate(el=>new FormData(el).get('isMandatory')),'true',key+'/updated-select-submits');
   }
   if (view==='code') {
    const form = page.locator('.zy-code-form');
    assert.equal(await form.locator('input[name]').count(),4);
    assert((await form.locator('button').boundingBox()).width<250,key+'/compact-save');
   }
   if (view==='notice') {
    const y = await page.locator('.nxhs-field-row').evaluateAll(els=>els.map(el=>el.getBoundingClientRect().y));
    assert.equal(y[0]===y[2],width>1100,key+'/compact-columns');
   }
   if (view==='policies') {
    assert.equal(await page.locator('.lp-card:visible').count(),0,key+'/initially-collapsed');
    await page.locator('summary').first().focus(); await page.keyboard.press('Enter');
    assert(await page.locator('.zy-policy-disclosure').first().evaluate(el=>el.open),key+'/keyboard-open');
    assert.equal(await page.locator('.lp-card:visible').count(),1,key+'/keyboard-disclosure');
   }
   if (view==='categories') {
    assert(await page.locator('.rtc-type').isHidden(),key+'/category-initially-collapsed');
    await page.locator('.zy-request-category > summary').focus(); await page.keyboard.press('Enter');
    assert(await page.locator('.rtc-type').isVisible(),key+'/category-keyboard-open');
   }
   if (view==='toolbar') {
    assert((await page.getByRole('button', {name:'عرض', exact:true}).boundingBox()).width<180,key+'/compact-filter-action');
    const positions = await page.locator('.zy-notification-toolbar').evaluate(el=>({form:el.querySelector('form').getBoundingClientRect().y,search:el.querySelector('input').getBoundingClientRect().y}));
    assert.equal(positions.search-positions.form<70,width>650,key+'/responsive-toolbar');
   }
   if (view==='picker') {
    await page.addScriptTag({content:pickerBehavior+'; document.getElementById("aptStepType").addEventListener("change",aptStepTypeChanged); aptStepTypeChanged();'});
    assert.equal(await page.locator('[data-apt-step-choice]:visible').count(),0,key+'/manager-no-unrelated-fields');
    for (const type of ['Role','User','CommitteeGroup','ExternalCommittee']) {
     await page.locator('#aptStepType').selectOption(type);
     assert.equal(await page.locator('[data-apt-step-choice]:visible').count(),1,key+'/one-related-field');
     assert(await page.locator('[data-apt-step-choice="'+type+'"] .nxcs-trigger').isVisible(),key+'/enhanced-field-visible');
    }
   }
   if (view==='tabs') {
    for (const selector of ['.hrms-tab.active','.pai-tabs [aria-selected="true"]']) {
     assert.equal(await page.locator(selector).evaluate(el=>getComputedStyle(el).color),await page.locator('.zy-hr-settings-page').evaluate(el=>getComputedStyle(el).color),key+'/selected-text-readable');
     assert.equal(await page.locator(selector).evaluate(el=>getComputedStyle(el).borderBlockEndWidth),'3px',key+'/selected-indicator');
    }
   }
   if (process.env.HR_AUDIT_SCREENSHOTS && theme==='dark' && width===1800) {fs.mkdirSync(process.env.HR_AUDIT_SCREENSHOTS,{recursive:true}); await page.screenshot({path:path.join(process.env.HR_AUDIT_SCREENSHOTS,view+'.png'),fullPage:true});}
   cases++; await page.close();
  }
  console.log('PASS: '+cases+' offline audit fixtures: columns, POST ownership, compact forms, collapsed policies, enhanced approver visibility, contrast and responsive themes.');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
