// Offline, synthetic fixtures only. Actual page form contracts, CSS and workspace JS.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const {execFileSync} = require('node:child_process');
const {chromium} = require('playwright');
const root = path.resolve(__dirname,'../..');
const web = path.join(root,'SmartAttendance.Web');
const read = p => fs.readFileSync(path.join(web,p),'utf8');
const source = read('Pages/HrSettings/PeopleAI/Index.cshtml');
const contract = s => (s.match(/<form\b[\s\S]*?<\/form>/g)||[]).map(form=>(form.match(/(?:asp-(?!append-version)[\w-]+|name|type|value|method|required|onchange)\s*=\s*"[^"]*"/g)||[]).sort());
const baseline = relative => process.env.HR_SETTINGS_BASELINE
 ? fs.readFileSync(path.join(process.env.HR_SETTINGS_BASELINE,relative),'utf8')
 : execFileSync('git',['show','HEAD:'+relative],{cwd:root,encoding:'utf8'});
assert.deepEqual(contract(source),contract(baseline('SmartAttendance.Web/Pages/HrSettings/PeopleAI/Index.cshtml')));
assert.equal(read('Pages/HrSettings/PeopleAI/Index.cshtml.cs').replaceAll('\r\n','\n'),baseline('SmartAttendance.Web/Pages/HrSettings/PeopleAI/Index.cshtml.cs').replaceAll('\r\n','\n'));
assert(!source.includes('<style'));
const nav = source.match(/<nav class="pai-tabs"[\s\S]*?<\/nav>/)[0].replace(/@Model\.[\w.]+/g,'12');
const panels = ['pai-general','pai-types','pai-policies','pai-fields'];
const listClass = ['','pai-doc-type-list','pai-policy-list','pai-field-policy-list'];
const inputs = '<label>اسم العرض<input name="displayLabel" value="عنصر تجريبي طويل" required></label><label>الحالة<select name="requirement"><option value="Required">مطلوب</option><option value="Optional">اختياري</option></select></label><label class="pai-check"><input type="checkbox" name="active" value="true" checked>فعال</label><input type="hidden" name="companyId" value="12"><div class="pai-field-actions"><button class="zy-btn" type="submit">حفظ</button><button class="zy-btn pai-danger" type="submit">إخفاء</button></div>';
const records = i => Array.from({length:18},(_,n)=>'<details class="pai-record" data-pai-record data-pai-search-text="مستند '+n+' NationalId"><summary><span><strong>مستند تجريبي '+n+'</strong><code>NationalId / Field'+n+'</code></span><span class="zy-badge">مطلوب</span><span class="pai-record-hint">تعديل</span></summary><form class="'+(i===1?'pai-doc-type-row':i===2?'pai-policy-row':'pai-field-policy-row')+'">'+inputs+'</form></details>').join('');
const content = panels.map((id,i)=>'<section id="'+id+'" data-pai-panel class="'+(i===0?'pai-grid':'pai-card')+'" aria-labelledby="pai-tab-'+['general','types','policies','fields'][i]+'">'+(i===0?'<form class="pai-card"><header class="pai-card-head"><div><h2>السياسة العامة</h2><p>إعدادات كشف التكرار والمراجعة للشركة.</p></div><label class="pai-check"><input type="checkbox" checked>مفعّل</label></header><div class="pai-fields">'+inputs+'</div><div class="pai-local"><strong>المعالجة المحلية فقط</strong><span>المعالجة السحابية معطّلة</span></div></form><details class="pai-card pai-card--info pai-advanced"><summary class="pai-card-head"><div><h2>التفاصيل التقنية والأمان</h2><p>التخزين المحمي ونطاق الشركات</p></div></summary><div class="pai-readiness"><span>التخزين المحمي</span></div></details>':'<header class="pai-card-head"><div><h2>'+['','أنواع المستندات','سياسات المستندات','حقول المراجعة'][i]+'</h2><p>اختيار السجل لعرض تفاصيله وتعديله.</p></div></header><label class="pai-list-search" data-pai-search-label hidden>بحث<input type="search" data-pai-search></label><p class="zy-empty" data-pai-no-results hidden>لا توجد نتائج مطابقة للبحث</p><div class="'+listClass[i]+'">'+records(i)+'</div><details class="pai-add-section"><summary>إضافة سجل جديد</summary><form class="pai-field-add">'+inputs+'</form></details>')+'</section>').join('');
const shell = [...read('Pages/Shared/_Layout.cshtml').matchAll(/href="~\/css\/([^"]+)"/g)].map(m=>m[1]);
const pageCss = [...source.matchAll(/href="~\/css\/([^"]+)"/g)].map(m=>m[1]);
const split = shell.indexOf('zynora-components.css');
const styles = [...shell.slice(0,split),...pageCss,...shell.slice(split)].map(file=>read('wwwroot/css/'+file)).join('\n');
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  for(const theme of ['dark','light']) for(const width of [390,900,1800]) {
   const page=await browser.newPage({viewport:{width,height:1100}}); let requests=0;
   await page.route('**/*',r=>{requests++;return r.fulfill({status:200,contentType:'text/html',body:'<html></html>'});});
   await page.goto('http://people-ai.test/HrSettings/PeopleAI');
   await page.setContent('<html lang="ar" dir="rtl" data-theme="'+theme+'" data-ready="true"><head><style>'+styles+'</style></head><body class="zy-app"><main class="zynora-content zy-scope zy-ui-contract"><div class="pai-shell zy-hr-admin-page zy-hr-settings-page"><header class="zy-page-header pai-head"><h1>People AI</h1><span class="zy-badge">معالجة محلية فقط</span></header>'+nav+content+'</div></main></body></html>');
   assert.equal(await page.locator('[data-pai-panel]:visible').count(),4,'no-JS fallback');
   await page.addScriptTag({content:read('wwwroot/js/people-ai-workspace.js')});
   const before=requests;
   assert.equal(await page.locator('[data-pai-panel]:visible').count(),1);
   assert(await page.locator('#pai-general').isVisible());
   await page.waitForTimeout(250);
   const tabColors=await page.locator('.pai-tabs').evaluate(nav=>[...nav.querySelectorAll('button')].map(el=>({selected:el.getAttribute('aria-selected'),background:getComputedStyle(el).backgroundColor,border:getComputedStyle(el).borderColor})));
   assert(await page.locator('.pai-tabs').evaluate(el=>parseFloat(getComputedStyle(el).gap)>=8),'uniform tab spacing');
   assert(tabColors[0].background!==tabColors[1].background,theme+'/'+width+'/selected tab must be distinct: '+JSON.stringify(tabColors));
   for(const id of panels.slice(1)) {
    await page.locator('[data-pai-tab="'+id+'"]').click();
    assert.equal(await page.locator('[data-pai-panel]:visible').count(),1);
    assert.equal(await page.locator('#'+id+' [data-pai-record]:visible').count(),8);
    assert.equal(await page.locator('#'+id+' [data-pai-record][open]').count(),0);
    await page.locator('#'+id+' .pai-pagination').getByRole('button',{name:'التالي'}).click();
    assert.equal(await page.locator('#'+id+' [data-pai-record]:visible').count(),8);
    await page.locator('#'+id+' [data-pai-search]').fill('مستند 17');
    assert.equal(await page.locator('#'+id+' [data-pai-record]:visible').count(),1);
    await page.locator('#'+id+' [data-pai-record]:visible summary').click();
    assert(await page.locator('#'+id+' [data-pai-record]:visible summary').evaluate(el=>el.getBoundingClientRect().height>=44),'record summary touch target');
    assert(await page.locator('#'+id+' [data-pai-record]:visible summary').evaluate(el=>parseFloat(getComputedStyle(el).paddingBlockStart)>=12),'record header spacing');
    assert(await page.locator('#'+id+' [data-pai-record]:visible form').isVisible());
    assert.deepEqual(await page.locator('#'+id+' [data-pai-record]:visible form').evaluate(form=>[new FormData(form).get('companyId'),new FormData(form).get('active')]),['12','true']);
    assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),id+'/'+theme+'/'+width+'/overflow');
    if(process.env.PEOPLE_AI_SCREENSHOT_DIR && id==='pai-fields' && theme==='dark' && width===1800) { fs.mkdirSync(process.env.PEOPLE_AI_SCREENSHOT_DIR,{recursive:true}); await page.screenshot({path:path.join(process.env.PEOPLE_AI_SCREENSHOT_DIR,'people-ai-fields.png'),fullPage:true}); }
    await page.locator('#'+id+' [data-pai-search]').fill('missing');
    assert(await page.locator('#'+id+' [data-pai-no-results]').isVisible());
    await page.locator('#'+id+' [data-pai-search]').fill('');
    assert.equal(await page.locator('#'+id+' [data-pai-record]:visible').count(),8);
   }
   await page.locator('[data-pai-tab="pai-fields"]').focus(); await page.keyboard.press('Home');
   assert(await page.locator('#pai-general').isVisible());
   await page.keyboard.press('ArrowLeft'); assert(await page.locator('#pai-types').isVisible());
   assert.equal(requests,before,'tabs/search/pagination must not make network requests');
   await page.locator('[data-pai-tab="pai-general"]').click();
   assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
   if(process.env.PEOPLE_AI_SCREENSHOT_DIR && theme==='dark' && width===1800) await page.screenshot({path:path.join(process.env.PEOPLE_AI_SCREENSHOT_DIR,'people-ai-general.png'),fullPage:true});
   await page.close();
  }
  console.log('PASS: unchanged form/backend contracts; progressive enhancement, no-refresh tabs, keyboard RTL, search/pagination, editable forms and no overflow in 6 responsive/theme fixtures.');
 } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
