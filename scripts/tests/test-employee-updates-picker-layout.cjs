/* Synthetic employee picker only: no application server or employee data. */
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const cssRoot = path.resolve(__dirname, '../../SmartAttendance.Web/wwwroot/css');
const css = ['zynora-theme-contract.css','zynora-design-tokens.css','zynora-design-system.css',
  'zynora-employee-picker.css','zynora-employee-updates.css','pages/employee-updates-identity.css',
  'zynora-dropdown-contract.css'].map(x => fs.readFileSync(path.join(cssRoot,x),'utf8')).join('\n');
(async () => {
  const browser = await chromium.launch({channel:'msedge',headless:true});
  try {
    const page = await browser.newPage();
    await page.route('**/*',r=>r.abort());
    let checks=0;
    for (const theme of ['dark','light']) for(const width of [320,390,900,1440]) for(const selected of [false,true]) {
      await page.setViewportSize({width,height:240});
      await page.setContent(`<!doctype html><html dir="rtl" data-theme="${theme}"><head><style>${css}</style></head><body class="zy-app"><section class="nxupd-page nxupd-page-v14b zy-employee-updates"><section class="nxupd-selector-card nxupd-selector-card-single"><form class="nxupd-employee-select"><input type="hidden" name="employeeSelected" value="true"><div class="zyep"><label class="zyep-label" for="code">الموظف</label><div class="zyep-box"><input id="code" class="zyep-code" placeholder="رمز" value="${selected?'TEST-01':''}"><input class="zyep-name" readonly placeholder="اسم الموظف" value="${selected?'موظف تجريبي لا يمثل بيانات حقيقية':''}"><button type="button" class="zyep-open" aria-label="بحث عن الموظف"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor"><circle cx="10" cy="10" r="6"/><path d="m15 15 5 5"/></svg></button><input class="zyep-id" type="hidden" value="${selected?'999':''}"></div></div></form></section></section></body></html>`);
      const result=await page.evaluate(()=>{
        const els=['.zyep-code','.zyep-name','.zyep-open'].map(s=>document.querySelector(s));
        return {rects:els.map(e=>{const r=e.getBoundingClientRect();return {x:r.x,right:r.right,height:r.height,y:r.y};}),
          overflow:document.documentElement.scrollWidth>innerWidth,gap:getComputedStyle(document.querySelector('.zyep-box')).gap,
          hidden:getComputedStyle(document.querySelector('.zyep-id')).display,
          readonly:document.querySelector('.zyep-name').readOnly,
          field:getComputedStyle(els[0]).backgroundColor};
      });
      assert.equal(result.overflow,false);
      assert.equal(result.gap,'8px');
      assert.equal(result.hidden,'none');
      assert.equal(result.readonly,true);
      for(const r of result.rects) { assert.equal(r.height,44); assert.equal(r.y,result.rects[0].y); }
      assert.ok(result.rects[0].x>result.rects[1].x && result.rects[1].x>result.rects[2].x);
      assert.ok(result.rects[1].x-result.rects[2].right>=7.9);
      assert.equal(result.field,theme==='dark'?'rgb(17, 27, 42)':'rgb(255, 255, 255)');
      if(process.env.PICKER_SCREENSHOT && theme==='dark' && width===1440 && selected) await page.screenshot({path:process.env.PICKER_SCREENSHOT});
      checks++;
    }
    console.log(`PASS: ${checks} picker fixtures; 44px aligned controls, 8px gaps, RTL, palette, empty/selected states, no overflow.`);
  } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
