// Exercise the actual Razor condition-editor functions with synthetic data, offline.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root,'SmartAttendance.Web/Pages/HrSettings/ApprovalTemplates.cshtml'),'utf8');
const behavior = source.slice(source.indexOf('        function aptSyncConditions()'),source.indexOf('        function aptOpen(id)'));
const fields = [{Key:'Hours',Label:'الساعات',Kind:'number'}, {Key:'FromDate',Label:'التاريخ',Kind:'date'},
  {Key:'FinancialKind',Label:'النوع',Kind:'select',Options:[{Value:'1',Label:'قرض'},{Value:'2',Label:'سلفة'}]}];
(async()=>{
  const browser = await chromium.launch({channel:'msedge',headless:true});
  try {
    for (const width of [390,900,1800]) {
      const page = await browser.newPage({viewport:{width,height:1000}});
      await page.setContent('<html dir="rtl"><body><input type="hidden" id="f_ConditionsJson"><div id="aptRequestConditions"></div></body></html>');
      await page.addScriptTag({content:'var aptConditionFields='+JSON.stringify(fields)+';'+behavior});
      await page.evaluate(()=>aptAddCondition({Field:'Hours',Operator:'gt',Value:'2'}));
      assert.deepEqual(JSON.parse(await page.locator('#f_ConditionsJson').inputValue()),[{Field:'Hours',Operator:'gt',Value:'2'}]);
      assert.equal(await page.locator('[data-part="value"]').getAttribute('type'),'number');
      await page.locator('[data-part="field"]').selectOption('FromDate');
      assert.equal(await page.locator('[data-part="value"]').getAttribute('type'),'date');
      await page.locator('[data-part="value"]').fill('2026-10-08');
      assert.equal(JSON.parse(await page.locator('#f_ConditionsJson').inputValue())[0].Value,'2026-10-08');
      await page.locator('[data-part="field"]').selectOption('FinancialKind');
      assert.equal(await page.locator('[data-part="operator"]').inputValue(),'eq');
      assert(await page.locator('[data-part="operator"] option[value="gt"]').evaluate(option=>option.disabled));
      await page.locator('[data-part="value"]').selectOption('2');
      assert.deepEqual(JSON.parse(await page.locator('#f_ConditionsJson').inputValue()),[{Field:'FinancialKind',Operator:'eq',Value:'2'}]);
      await page.getByRole('button',{name:'إزالة'}).click();
      assert.equal(await page.locator('#f_ConditionsJson').inputValue(),'[]');
      await page.evaluate(()=>{for(let i=0;i<21;i++)aptAddCondition();});
      assert.equal(await page.locator('.apt-condition-row').count(),20);
      await page.close();
    }
    console.log('PASS: typed condition editor round-trip, field changes, enum operators, removal and 20-row limit at 3 viewport sizes.');
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
