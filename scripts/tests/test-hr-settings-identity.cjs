// Synthetic HR settings fixtures with the complete shell CSS. Never reads application data.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {execFileSync} = require('node:child_process');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const web = path.join(root, 'SmartAttendance.Web');
const read = p => fs.readFileSync(path.join(web, p), 'utf8');
const shell = [...read('Pages/Shared/_Layout.cshtml').matchAll(/href="~\/css\/([^"]+)"/g)].map(m => m[1]);
const views = ['NoticePeriod','SelfServiceSettings','TerminationReasons','NotificationCenter','Lookups','ApprovalTemplates','RequestTypes','LeavePolicies','PeopleAI/Index'];
const contract = s => (s.match(/(?:asp-(?!append-version)[\w-]+|name|id|type|value|method|enctype|required|data-[\w-]+|onchange|onclick)\s*=\s*"[^"]*"/g)||[]).sort();
const fields = '<label>اسم الإعداد<input name="sample" value="إعداد تجريبي"></label><label>الاختيار<select name="choice"><option value="a">الأول</option><option value="b">الثاني</option></select></label>';
const check = '<label class="nxhs-check-line pai-check lp-check"><input type="checkbox" name="enabled">تفعيل الخيار</label><input type="hidden" name="postValue" value="false">';
const actions = '<footer class="nxhs-actions lp-actions pai-actions rtc-actions"><button type="submit" class="rtc-btn primary zy-btn--primary">حفظ الإعدادات</button></footer>';
const radio = '<div class="nxhs-segment"><input type="radio" name="unit" id="day" value="Day" checked><label for="day">يوم</label><input type="radio" name="unit" id="month" value="Month"><label for="month">شهر</label></div>';
const table = '<div class="nxhs-table-wrap hrms-table-wrap"><table class="nxhs-table hrms-table"><thead><tr><th>الاسم</th><th>الحالة</th><th>الإجراء</th></tr></thead><tbody><tr><td>عنصر تجريبي</td><td>فعال</td><td><button type="button">تعديل</button></td></tr></tbody></table></div>';
const fixtures = {
  NoticePeriod: '<form class="nxhs-panel"><header><h2>فترة الإنذار</h2></header><div class="nxhs-duration-row">'+fields+radio+'</div><section class="nxhs-subbox"><h3>الاستثناءات</h3><label class="nxhs-field-row">اختيار<select><option>العطل الرسمية</option></select></label></section>'+check+actions+'</form>',
  SelfServiceSettings: '<form class="nxhs-panel nxhs-self-panel"><section class="nxhs-setting-section"><h2>إعدادات الخدمة الذاتية</h2><div class="nxhs-permission-grid">'+check+'</div><div class="nxhs-field-row">'+fields+'</div></section>'+actions+'</form>',
  TerminationReasons: '<section class="nxhs-panel"><header class="nxhs-search-head"><h2>أسباب الإيقاف</h2></header><form class="nxhs-inline-add">'+fields+check+actions+'</form>'+table+'</section><div class="nxhs-delete-modal" hidden><div class="nxhs-delete-backdrop"></div><section class="nxhs-delete-dialog"><h2>تأكيد</h2></section></div>',
  NotificationCenter: '<section class="nxhs-panel nxhs-notification-panel"><article class="nxhs-notification-item"><header><h3>إشعار تجريبي</h3><form class="nxhs-switch-form"><button type="submit" class="nxhs-switch-button" role="switch" aria-checked="false"><span></span></button></form></header><form class="nxhs-notification-details" hidden><div class="nxhs-notif-grid">'+fields+check+actions+'</div></form></article></section>',
  Lookups: '<div class="hrms-tabs"><button class="hrms-tab active">قائمة</button><button class="hrms-tab">أخرى</button></div><section class="hrms-table-card"><form class="zy-hr-lookup-form"><div class="zy-hr-lookup-fields">'+fields+check+'</div>'+actions+'</form>'+table+'</section>',
  ApprovalTemplates: '<div class="apt-layout"><aside class="nxhs-panel apt-catalog"><h3>الطلبات</h3><a class="active" href="#">طلب تجريبي</a></aside><section class="nxhs-panel"><h2>القوالب</h2>'+table+'<form class="apt-delegation-form">'+fields+check+actions+'</form></section></div><div class="apt-backdrop"></div><aside class="apt-slide"><form class="apt-form-grid">'+fields+check+'<select multiple name="watchers"><option>اختبار</option></select>'+actions+'</form></aside>',
  RequestTypes: '<details class="rtc-newcat"><summary>تبويبة جديدة</summary><form class="rtc-inline">'+fields+actions+'</form></details><section class="rtc-cat"><h2>أنواع الطلبات</h2><details class="rtc-type" open><summary>نوع تجريبي</summary><form class="rtc-form"><div class="rtc-grid">'+fields+'</div><div class="rtc-toggles">'+check+'</div>'+actions+'</form></details></section>',
  LeavePolicies: '<form class="lp-company"><label>الشركة<select><option>شركة اختبار</option></select></label></form><div class="lp-list"><form class="lp-card"><h2>سياسة الإجازات</h2><div class="lp-grid">'+fields+check+'</div>'+actions+'</form></div>',
  'PeopleAI/Index': '<section class="pai-grid"><form class="pai-card"><header class="pai-card-head"><h2>السياسة العامة</h2></header><div class="pai-fields">'+fields+check+'</div>'+actions+'</form><section class="pai-card"><h2>حالة البنية</h2><div class="pai-readiness"><span>مهيأ</span></div></section></section><section class="pai-card"><h2>حقول المراجعة</h2><form class="pai-field-policy-row">'+fields+check+actions+'</form></section>'
};
(async()=>{
  const browser = await chromium.launch({channel:'msedge',headless:true});
  let checks=0;
  try {
    for (const view of views) {
      const sourcePath='SmartAttendance.Web/Pages/HrSettings/'+view+'.cshtml';
      const source=fs.readFileSync(path.join(root,sourcePath),'utf8');
      assert.deepEqual(contract(source),contract(execFileSync('git',['show','97d19f71:'+sourcePath],{cwd:root,encoding:'utf8'})),view+'/form-contract');
      assert(source.includes('zy-hr-admin-page zy-hr-settings-page'),view+'/scope');
      assert(!source.includes('<style'),view+'/no-inline-style');
      const pageCss=[...source.matchAll(/href="~\/css\/([^"]+)"/g)].map(m=>m[1]);
      const split=shell.indexOf('zynora-components.css');
      const styles=[...shell.slice(0,split),...pageCss,...shell.slice(split)].map(f=>read('wwwroot/css/'+f)).join('\n');
      const cls=view==='Lookups'?'hrms-page':view==='RequestTypes'?'rtc-wrap':view==='LeavePolicies'?'lp-page':view==='PeopleAI/Index'?'pai-shell':'nxhs-page nxhs-simple';
      const header=view==='Lookups'?'page-header':view==='RequestTypes'?'rtc-head':view==='LeavePolicies'?'lp-head':view==='PeopleAI/Index'?'pai-head':'nxhs-titlebar';
      for(const theme of ['dark','light']) for(const width of [390,900,1800]) {
        const page=await browser.newPage({viewport:{width,height:1100}});
        await page.route('**/*',r=>r.abort());
        await page.setContent('<!doctype html><html lang="ar" dir="rtl" data-ready="true" data-theme="'+theme+'"><head><style>'+styles+'</style></head><body class="zy-app"><main class="zynora-content zy-scope zy-ui-contract"><section class="'+cls+' zy-hr-admin-page zy-hr-settings-page"><header class="'+header+'"><h1>إعدادات الموارد البشرية</h1></header>'+fixtures[view]+'</section></main></body></html>');
        await page.addScriptTag({content:read('wwwroot/js/zynora-select-system.js')});
        await page.waitForTimeout(500);
        const key=view+'/'+theme+'/'+width;
        assert(await page.locator('.zy-hr-settings-page').isVisible(),key+'/visible');
        assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),key+'/page-overflow');
        const card=page.locator('.nxhs-panel,.hrms-table-card,.rtc-cat,.lp-card,.pai-card').first();
        assert.deepEqual(await card.evaluate(el=>{const s=getComputedStyle(el);return [s.backgroundColor,s.backgroundImage]}),[theme==='dark'?'rgb(15, 26, 46)':'rgb(255, 255, 255)','none'],key+'/card-palette');
        if(view==='NotificationCenter') {
          assert(await page.locator('.nxhs-notification-details').isHidden(),key+'/disabled-details-hidden');
          await page.evaluate(()=>{window.fetch=async()=>new Response('',{status:200})});
          const behavior=[...source.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m=>m[1]).join('\n');
          await page.addScriptTag({content:behavior});
          const toggle=page.locator('.nxhs-switch-button');
          const geometry=()=>toggle.evaluate(el=>{const r=el.getBoundingClientRect(),t=el.querySelector('span').getBoundingClientRect();return {w:r.width,h:r.height,radius:getComputedStyle(el).borderRadius,thumb:t.width,x:t.x-r.x,fits:t.x>=r.x&&t.right<=r.right&&t.y>=r.y&&t.bottom<=r.bottom}});
          const off=await geometry();
          assert.deepEqual([off.w,off.h,off.thumb],[52,28,22],key+'/switch-dimensions');
          assert(off.fits&&parseFloat(off.radius)>=28,key+'/switch-capsule');
          await toggle.click(); await page.waitForTimeout(200);
          assert.equal(await toggle.getAttribute('aria-checked'),'true',key+'/switch-on-accessible');
          const on=await geometry(); assert(on.fits&&Math.abs(on.x-off.x)>=23,key+'/thumb-moves-inside-track');
          assert(await page.locator('.nxhs-notification-details').isVisible(),key+'/details-visible');
        }
        if(view==='RequestTypes') { assert.equal(await page.locator('.rtc-newcat').evaluate(el=>el.open),false,key+'/category-initially-collapsed'); await page.locator('.rtc-newcat > summary').click(); }
        const field=page.locator('input[name="sample"]').first();
        assert(Math.abs((await field.boundingBox()).height-44)<0.5,key+'/field-height');
        assert.equal(await field.evaluate(el=>getComputedStyle(el).backgroundColor),theme==='dark'?'rgb(17, 27, 42)':'rgb(255, 255, 255)',key+'/field-palette');
        assert(await page.locator('input[name="postValue"]').first().isHidden(),key+'/hidden-post-value');
        const cb=page.locator('input[name="enabled"]').first();
        assert(Math.abs((await cb.boundingBox()).width-20)<0.5,key+'/checkbox-size');
        await cb.check(); assert(await cb.isChecked(),key+'/checkbox-working');
        const trigger=page.locator('label').filter({has:page.locator('select[name="choice"]')}).locator('.nxcs-trigger').first();
        await trigger.scrollIntoViewIfNeeded(); await trigger.click();
        await page.locator('.nxcs-panel .nxcs-option').last().click();
        assert.equal(await page.locator('select[name="choice"]').first().inputValue(),'b',key+'/select-working');
        if(view==='NoticePeriod') {
          const segment=page.locator('.nxhs-segment');
          assert(Math.abs((await segment.boundingBox()).height-44)<0.5,key+'/segment-height');
          assert(await segment.evaluate(el=>{const r=el.getBoundingClientRect();return [...el.querySelectorAll('label')].every(l=>{const s=getComputedStyle(l),b=l.getBoundingClientRect();return s.alignItems==='center'&&s.justifyContent==='center'&&b.y>=r.y&&b.bottom<=r.bottom+1&&l.scrollWidth<=l.clientWidth})}),key+'/segment-labels-centered-unclipped');
          await page.locator('label[for="month"]').click();assert(await page.locator('#month').isChecked(),key+'/radio-working');
          await page.locator('#day').focus(); await page.keyboard.press('Space');assert(await page.locator('#day').isChecked(),key+'/radio-keyboard');
        }
        if(view==='ApprovalTemplates') {
          assert(!(await page.locator('.apt-slide').boundingBox()).x || await page.locator('.apt-slide').evaluate(el=>el.getBoundingClientRect().right<=0),key+'/drawer-closed');
          await page.locator('.apt-slide').evaluate(el=>el.classList.add('open')); await page.waitForTimeout(250);
          assert(await page.locator('.apt-slide').evaluate(el=>el.getBoundingClientRect().x>=-1&&el.getBoundingClientRect().right<=innerWidth+1),key+'/drawer-fits');
          assert((await page.locator('select[multiple]').boundingBox()).height>=119.5,key+'/multiple-select '+JSON.stringify(await page.locator('select[multiple]').evaluate(el=>{const s=getComputedStyle(el);return {height:s.height,minHeight:s.minHeight,maxHeight:s.maxHeight,display:s.display}})));
          await page.locator('.apt-slide').evaluate(el=>el.classList.remove('open')); await page.waitForTimeout(250);
          assert(await page.locator('.apt-slide').evaluate(el=>el.getBoundingClientRect().right<=1),key+'/drawer-reclosed');
        }
        if(process.env.HR_SETTINGS_SCREENSHOTS&&theme==='dark'&&width===1800) await page.screenshot({path:path.join(process.env.HR_SETTINGS_SCREENSHOTS,'settings-'+view.replaceAll('/','-')+'.png'),fullPage:true});
        if(view==='NotificationCenter') {
          await page.locator('.nxhs-switch-button').focus(); await page.keyboard.press('Space'); await page.waitForTimeout(200);
          assert.equal(await page.locator('.nxhs-switch-button').getAttribute('aria-checked'),'false',key+'/switch-off-keyboard');
          assert(await page.locator('.nxhs-notification-details').isHidden(),key+'/details-hidden-after-off');
        }
        checks++; await page.close();
      }
    }
    console.log('PASS: '+checks+' full-shell HR settings fixtures; all 9 form contracts preserved; themes, responsive layout, controls, radio, drawer and hidden values.');
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
