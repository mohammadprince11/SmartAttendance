// Offline synthetic fixtures. No application server, database or real employee data.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {execFileSync} = require('node:child_process');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const web = path.join(root, 'SmartAttendance.Web');
const read = p => fs.readFileSync(path.join(web, p), 'utf8');
const source = read('Pages/HrSettings/ApprovalTemplates.cshtml');
const baseline = process.env.HR_SETTINGS_BASELINE
 ? fs.readFileSync(path.join(process.env.HR_SETTINGS_BASELINE,'SmartAttendance.Web/Pages/HrSettings/ApprovalTemplates.cshtml'),'utf8')
 : execFileSync('git', ['show', 'HEAD:SmartAttendance.Web/Pages/HrSettings/ApprovalTemplates.cshtml'], {cwd:root, encoding:'utf8'});
const hasTypedConditions = source.includes('function aptAddCondition(');
const names = s => [...s.replace(/<script[\s\S]*?<\/script>/g,'').matchAll(/\bname="([^"]+)"/g)].map(m=>m[1]).sort();
// GET navigation added Audience. All existing submitted settings stay present exactly once.
assert.deepEqual(names(source).filter(n=>!['Audience','ConditionsJson'].includes(n)), names(baseline).filter(n=>!['Audience','ConditionsJson'].includes(n)));
assert(!source.includes('apt-backdrop'));
assert(source.includes('Model.IsEditor'));
assert(source.includes('asp-route-EditorId="@template.Id"'));
assert(!source.includes('<style'));
const shell = [...read('Pages/Shared/_Layout.cshtml').matchAll(/href="~\/css\/([^"]+)"/g)].map(m=>m[1]);
const pageCss = [...source.matchAll(/href="~\/css\/([^"]+)"/g)].map(m=>m[1]);
const split = shell.indexOf('zynora-components.css');
const styles = [...shell.slice(0,split), ...pageCss, ...shell.slice(split)].map(f=>read('wwwroot/css/'+f)).join('\n');
const saved = {Id:7,Name:'قالب اختبار',IsActive:true,HasConditions:true,CondMinAmount:0,CondMaxAmount:10,
  ConditionsJson:JSON.stringify([{Field:'DaysCount',Operator:'ge',Value:'2'},{Field:'FromDate',Operator:'ge',Value:'2026-10-08'}]),
  Steps:[{ApproverType:'DirectManager',DisplayName:'المدير',StageOrder:1},
  {ApproverType:'Role',RoleName:'HR Officer',DisplayName:'مراجع',StageOrder:2},
  {ApproverType:'Role',RoleName:'HR Manager',DisplayName:'مسؤول',StageOrder:2}],
  Watchers:['test-user'], NotifyJson:'{"Employee":["Approve"]}'};
let behavior = source.match(/@section Scripts\s*{\s*<script>([\s\S]*?)<\/script>/)[1]
  .replace(/var aptConditionFields = [^\n]+;/, 'var aptConditionFields = '+JSON.stringify([{Key:'DaysCount',Label:'عدد أيام الإجازة',Kind:'number'},{Key:'FromDate',Label:'تاريخ بدء الإجازة',Kind:'date'}])+';')
  .replace(/var aptTemplates = [^\n]+;/, 'var aptTemplates = '+JSON.stringify([saved])+';')
  .replaceAll('@Model.Type','LeaveRequest').replaceAll('@Model.CompanyId','1')
  .replace(/@\(Model.EditorId \?\? 0\)/g,'0');
const inputIds = ['Id','Name','NameEn','CondMinAmount','CondMaxAmount','ReminderHours','EscalationDays','CancelLimitDays'];
const selectIds = ['CondBranchId','CondDepartmentId','CondWorkType','CondChangedFieldKey','EscalationTo','EscalationAlternateUser'];
const checks = ['IsActive','HasConditions','AutoReject','CommentReq','AttachReq'];
const controls = inputIds.map(id=>'<label>'+id+'<input id="f_'+id+'" name="'+id+'"></label>').join('')+
  selectIds.map(id=>'<label>'+id+'<select id="f_'+id+'"><option value=""></option><option value="test-user">test-user</option></select></label>').join('')+
  checks.map(id=>'<label class="nxhs-check-line"><input type="checkbox" id="f_'+id+'">'+id+'</label>').join('');
const editor = '<section class="nxhs-panel apt-editor"><h2 id="aptSlideTitle">قالب جديد</h2><form id="aptForm"><div class="apt-form-grid">'+controls+'</div><div id="aptConds" class="apt-form-grid">شروط التطبيق</div><div class="apt-committee-panes"><section class="apt-committee-available"><h4>أصحاب الموافقة المتاحون</h4><select id="aptStepType"><option>DirectManager</option><option>Role</option><option>User</option><option>CommitteeGroup</option><option>ExternalCommittee</option></select>'+['Role','User','CommitteeGroup','ExternalCommittee'].map(t=>'<select id="aptStep'+t+'"><option value="'+(t==='Role'?'HR Officer':t==='User'?'test-user':'9')+'">عنصر تجريبي</option></select>').join('')+'<input type="checkbox" id="aptParallel"></section><section><h4>اللجنة المختارة</h4><div id="aptSteps"></div><p id="aptStepsEmpty">لا توجد لجنة</p></section></div><details class="apt-editor-section"><summary>المشاهدون والإشعارات</summary><select id="f_Watchers" multiple><option value="test-user">مستخدم تجريبي</option></select><input type="checkbox" class="apt-nt" data-a="Employee" data-e="Approve"></details><footer class="nxhs-actions"><button type="submit">حفظ القالب</button></footer></form></section>';
const capabilitySource = read('Infrastructure/Hrms/ApprovalTemplateCapabilities.cs');
const editorWithConditions = editor.replace('<div id="aptConds" class="apt-form-grid">شروط التطبيق</div>',
  '<div id="aptConds" class="apt-form-grid"><section class="full apt-request-conditions"><h4>شروط خاصة بهذا الطلب</h4><input type="hidden" id="f_ConditionsJson"><div id="aptRequestConditions"></div><button type="button" onclick="aptAddCondition()">إضافة شرط</button></section></div>');
const definitions = [...capabilitySource.matchAll(/\["([^"]+)"\] = new\("([^"]+)", "([^"]+)"/g)].map(m=>({key:m[1],module:m[2],description:m[3]}));
const typeNames = Object.fromEntries([...read('Infrastructure/Hrms/ApprovalTemplateStore.cs').matchAll(/new\("([^"\n]+)",\s*"([^"\n]+)",\s*"([^"\n]+)"\)/g)].map(m=>[m[1],m[2]]));
assert.equal(definitions.length,21);
assert.equal(Object.keys(typeNames).length,21);
const modules = ['الأشخاص','الرواتب','الحضور والانصراف','الإجازات','الوثائق'];
const supervisorBlock = read('Infrastructure/Hrms/ApprovalTemplateNavigation.cs').match(/SupervisorTypes[\s\S]*?\{([\s\S]*?)\};/)[1];
const supervisorTypes = new Set([...supervisorBlock.matchAll(/"([^"]+)"/g)].map(m=>m[1]));
assert.equal(supervisorTypes.size,5);
const icon = '<span class="apt-type-icon"><svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M9 5H5v14h14V9M9 5h6l4 4M9 5v4h10M8 13h8M8 16h6"/></svg></span>';
assert(source.includes('aria-pressed="false"'));
assert(source.includes('id="aptCatalogSearchWrap" hidden'));
assert(/class="apt-catalog-module"[^>]+hidden/.test(source));
assert(!/<a\b/.test(source.match(/<nav class="apt-audience-tabs"[\s\S]*?<\/nav>/)[0]));
const catalog = '<form class="apt-company-filter"><input type="hidden" name="Audience" value="All"></form><aside class="nxhs-panel apt-catalog"><nav class="apt-audience-tabs" data-active-audience="All"><button type="button" data-audience="All" aria-pressed="true">كل الموديولات</button><button type="button" data-audience="SelfService" aria-pressed="false">طلبات الخدمة الذاتية</button><button type="button" data-audience="Supervisor" aria-pressed="false">طلبات المشرفين</button></nav><nav class="apt-module-nav">'+modules.map((m,i)=>'<button type="button" aria-controls="apt-module-'+i+'" aria-pressed="false">'+m+'<span data-module-type-count></span></button>').join('')+'</nav><p id="aptCatalogPrompt" class="zy-empty">اختر القسم لعرض أنواع الطلبات الخاصة به.</p><label class="apt-catalog-search" id="aptCatalogSearchWrap" hidden>بحث ضمن القسم المختار<input id="aptCatalogSearch" type="search" disabled></label>'+modules.map((m,i)=>'<section class="apt-catalog-module" id="apt-module-'+i+'" hidden><header class="apt-module-head"><h2>'+m+'</h2><span data-module-summary></span></header><div class="apt-type-grid">'+definitions.filter(d=>d.module===m).map(d=>'<a class="apt-type-card" data-audience="'+(supervisorTypes.has(d.key)?'Supervisor':'SelfService')+'" data-template-count="0" data-label="'+typeNames[d.key]+' '+d.module+' '+d.description+'">'+icon+'<span><strong>'+typeNames[d.key]+'</strong><small class="apt-type-description">'+d.description+'</small><small>0 قالب</small></span></a>').join('')+'</div></section>').join('')+'<p id="aptCatalogEmpty" hidden>لا يوجد نوع يطابق البحث</p></aside>';
(async()=>{
  const browser = await chromium.launch({channel:'msedge',headless:true});
  let checksPassed = 0;
  try {
    for (const theme of ['dark','light']) for (const width of [390,900,1800]) {
      for (const [view,html] of [['catalog',catalog],['catalog-supervisor',catalog.replace('data-active-audience="All"','data-active-audience="Supervisor"').replace('name="Audience" value="All"','name="Audience" value="Supervisor"').replace('data-audience="All" aria-pressed="true"','data-audience="All" aria-pressed="false"').replace('data-audience="Supervisor" aria-pressed="false"','data-audience="Supervisor" aria-pressed="true"')],['catalog-selfservice',catalog.replace('data-active-audience="All"','data-active-audience="SelfService"').replace('name="Audience" value="All"','name="Audience" value="SelfService"').replace('data-audience="All" aria-pressed="true"','data-audience="All" aria-pressed="false"').replace('data-audience="SelfService" aria-pressed="false"','data-audience="SelfService" aria-pressed="true"')],['editor',editor]]) {
        const page = await browser.newPage({viewport:{width,height:1100}});
        let networkRequests=0;
        await page.route('**/*',r=>{networkRequests++; return r.request().isNavigationRequest() ? r.fulfill({status:200,contentType:'text/html',body:'<html></html>'}) : r.abort();});
        await page.goto('http://approval.test/HrSettings/ApprovalTemplates');
        const content = view.startsWith('catalog') ? '<div class="apt-layout apt-catalog-view">'+html+'</div>' : hasTypedConditions ? editorWithConditions : editor;
        await page.setContent('<html dir="rtl" lang="ar" data-ready="true" data-theme="'+theme+'"><head><style>'+styles+'</style></head><body class="zy-app"><main class="zynora-content zy-scope zy-ui-contract"><section class="nxhs-page nxhs-simple zy-hr-admin-page zy-hr-settings-page zy-approval-simple zy-approval-workspace"><header class="nxhs-titlebar"><h1>قوالب الموافقات</h1><p>معاينة ببيانات تجريبية فقط</p></header>'+content+'</section></main></body></html>');
        await page.addScriptTag({content:behavior});
        assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),view+'/'+theme+'/'+width+'/overflow');
        if(view.startsWith('catalog')) {
          assert(await page.locator('.apt-catalog').evaluate(el=>el.getBoundingClientRect().width >= el.parentElement.getBoundingClientRect().width-1));
          assert.equal(await page.locator('.apt-catalog-module:visible').count(),0);
          assert.equal(await page.locator('.apt-type-card:visible').count(),0);
          assert(await page.locator('#aptCatalogPrompt').isVisible());
          assert(await page.locator('#aptCatalogSearch').isDisabled());
          await page.getByRole('button',{name:/^الأشخاص/}).click();
          assert.equal(await page.locator('.apt-catalog-module:visible').count(),1);
          assert.equal(await page.locator('.apt-type-card:visible').count(),view==='catalog-supervisor'?4:view==='catalog-selfservice'?3:7);
          assert.equal(await page.locator('.apt-module-nav button:visible').count(),view==='catalog-supervisor'?2:5);
          // Every entry URL retains the entire catalog for an in-page return to All.
          await page.getByRole('button',{name:'كل الموديولات',exact:true}).click();
          assert.equal(await page.locator('.apt-type-card:visible').count(),7);
          assert(!(await page.locator('#aptCatalogPrompt').isVisible()));
          const payroll = page.getByRole('button',{name:/^الرواتب/});
          await payroll.focus();
          await payroll.press('Enter');
          assert.equal(await payroll.getAttribute('aria-pressed'),'true');
          await page.waitForFunction(()=>{
            const selected=document.querySelector('.apt-module-nav [aria-pressed="true"]');
            const other=document.querySelector('.apt-module-nav [aria-pressed="false"]');
            return getComputedStyle(selected).borderBlockEndWidth !== getComputedStyle(other).borderBlockEndWidth && getComputedStyle(selected).backgroundColor !== getComputedStyle(other).backgroundColor;
          },null,{timeout:3000});
          assert.equal(await page.locator('.apt-module-nav [aria-pressed="true"]').count(),1);
          assert.equal(await page.locator('.apt-catalog-module:visible').count(),1);
          assert.equal(await page.locator('.apt-type-card:visible').count(),4);
          // Searching a different module must not disclose its cards.
          await page.locator('#aptCatalogSearch').fill('وثيقة');
          assert.equal(await page.locator('.apt-type-card:visible').count(),0);
          assert(await page.locator('#aptCatalogEmpty').isVisible());
          const documents = page.getByRole('button',{name:/^الوثائق/});
          await documents.focus();
          await documents.press('Space');
          assert.equal(await page.locator('#aptCatalogSearch').inputValue(),'');
          assert.equal(await page.locator('.apt-catalog-module:visible').count(),1);
          assert.equal(await page.locator('.apt-type-card:visible').count(),1);
          await page.locator('#aptCatalogSearch').fill('غير موجود');
          assert(await page.locator('#aptCatalogEmpty').isVisible());
          await page.locator('#aptCatalogSearch').fill('');
          assert.equal(await page.locator('.apt-type-card:visible').count(),1);
          assert(!(await page.locator('#aptCatalogEmpty').isVisible()));
          await payroll.click();
          assert.equal(await page.locator('.apt-type-card:visible').count(),4);
          await page.evaluate(()=>{window.approvalFixtureMarker='unchanged';});
          const requestsBefore=networkRequests;
          await page.getByRole('button',{name:'طلبات المشرفين',exact:true}).click();
          assert.equal(await page.locator('.apt-type-card:visible').count(),1);
          assert.equal(await page.locator('.apt-module-nav button:visible').count(),2);
          assert.equal(await page.locator('#apt-module-1 [data-module-summary]').textContent(),'1 نوع طلب · 0 قالب');
          assert.equal(await page.locator('.apt-company-filter input[name="Audience"]').inputValue(),'Supervisor');
          assert.equal(new URL(page.url()).searchParams.get('Audience'),'Supervisor');
          await page.getByRole('button',{name:'الأشخاص 4 نوع',exact:true}).click();
          assert.equal(await page.locator('.apt-type-card:visible').count(),4);
          await page.getByRole('button',{name:'طلبات الخدمة الذاتية',exact:true}).press('Enter');
          assert.equal(await page.locator('.apt-type-card:visible').count(),3);
          assert.equal(await page.locator('.apt-module-nav button:visible').count(),5);
          await documents.click();
          await page.getByRole('button',{name:'طلبات المشرفين',exact:true}).click();
          assert.equal(await page.locator('.apt-type-card:visible').count(),0);
          assert(await page.locator('#aptCatalogPrompt').isVisible());
          await page.getByRole('button',{name:'كل الموديولات',exact:true}).press('Space');
          assert.equal(await page.locator('.apt-type-card:visible').count(),0);
          await payroll.click();
          assert.equal(await page.locator('.apt-type-card:visible').count(),4);
          assert.equal(await page.evaluate(()=>window.approvalFixtureMarker),'unchanged');
          assert.equal(networkRequests,requestsBefore,'Audience changes must not make network requests');
          assert.equal(await page.locator('.apt-audience-tabs [aria-pressed="true"]').count(),1);
        } else {
          await page.evaluate(()=>aptOpen(7));
          if (hasTypedConditions) {
            assert.equal(await page.locator('.apt-condition-row').count(),2);
            assert.deepEqual(JSON.parse(await page.locator('#f_ConditionsJson').inputValue()),JSON.parse(saved.ConditionsJson));
          }
          assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'conditions overflow/'+theme+'/'+width);
          assert.equal(await page.locator('#f_Name').inputValue(),'قالب اختبار');
          assert.equal(await page.locator('#f_CondMinAmount').inputValue(),'0');
          assert(await page.locator('.apt-nt').isChecked());
          await page.locator('#aptSteps .apt-step').nth(1).getByRole('button',{name:'تقديم مرحلة الموافقة'}).click();
          assert.deepEqual(await page.locator('#aptSteps [name="StepStage"]').evaluateAll(els=>els.map(e=>e.value)),['1','1','2']);
          assert.deepEqual(await page.locator('#aptSteps [name="StepRole"]').evaluateAll(els=>els.map(e=>e.value)),['HR Officer','HR Manager','']);
          await page.evaluate(()=>aptOpen(0));
          assert.equal(await page.locator('.apt-condition-row').count(),0);
          assert.equal(await page.locator('#f_Name').inputValue(),'');
          assert.equal(await page.locator('#aptSteps .apt-step').count(),0);
          await page.getByRole('button',{name:'حفظ القالب'}).click();
          assert(await page.locator('#aptStepsEmpty').textContent().then(s=>s.includes('واحداً')));
          await page.evaluate(()=>aptAddStep());
          assert.equal(await page.locator('#aptSteps .apt-step').count(),1);
        }
        const screenshotDir=process.env.APPROVAL_SCREENSHOT_DIR;
        if(screenshotDir) {
          fs.mkdirSync(screenshotDir,{recursive:true});
          await page.screenshot({path:path.join(screenshotDir,'approval-'+view+'-'+theme+'-'+width+'.png'),fullPage:true});
        }
        await page.close(); checksPassed++;
      }
    }
    console.log('PASS: submitted field contracts, no-refresh/no-network audience switching from all three entry states, audience counts/company filter/URL synchronization, initially hidden modules, keyboard activation, scoped search/reset, editor rehydration, stage grouping, '+checksPassed+' responsive/theme fixtures.');
  } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
