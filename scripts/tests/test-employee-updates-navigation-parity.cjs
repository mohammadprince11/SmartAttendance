// Synthetic navigation only. No app server, accounts or employee data.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../../SmartAttendance.Web/wwwroot/js');
const scripts = ['zynora-kayan-nav.js', 'zynora-ui-stabilization-phase1.js'].map(x => fs.readFileSync(path.join(root, x), 'utf8'));
(async () => {
  const browser = await chromium.launch({channel:'msedge',headless:true});
  try {
    const page = await browser.newPage();
    await page.route('**/*', route => route.fulfill({contentType:'text/html',body:`<!doctype html><html dir="rtl"><body>
      <details class="zynora-nav-group" id="people"><summary>أشخاص</summary><div class="zynora-nav-group-links"><a class="zynora-nav-link" href="/EmployeeUpdates">تحديثات الموظف</a></div></details>
      <details class="zynora-nav-group" id="payroll"><summary>الرواتب</summary><div class="zynora-nav-group-links"><a class="zynora-nav-link" href="/EmployeeUpdates?section=financial">المعلومات المالية</a><a class="zynora-nav-link" href="/EmployeeUpdates?section=payment">الدفع</a><a class="zynora-nav-link" href="/Payroll/Runs">مسير الرواتب</a></div></details>
      ${scripts.map(s=>`<script>${s}</script>`).join('')}</body></html>`}));
    for (const [query, group, suffix] of [
      ['', 'people', '/EmployeeUpdates'],
      ['?employeeSelected=true&section=employee-master&employeeId=999', 'people', '/EmployeeUpdates'],
      ['?section=financial', 'payroll', '?section=financial'],
      ['?section=payment', 'payroll', '?section=payment'],
      ['?section=personal', 'people', '/EmployeeUpdates']]) {
      await page.goto('https://synthetic.invalid/EmployeeUpdates'+query);
      assert.equal(await page.locator('summary.ky-current').count(),1);
      assert.equal(await page.locator('.zynora-nav-group.is-active').count(),1);
      assert.equal(await page.locator('a[aria-current="page"]').count(),1);
      assert.equal(await page.locator('summary.ky-current').evaluate(e=>e.parentElement.id),group);
      assert.ok((await page.locator('a[aria-current="page"]').getAttribute('href')).endsWith(suffix));
    }
    console.log('PASS: 5 navigation fixtures, one active module/link; both navigation scripts loaded.');
    const cssRoot = path.resolve(root, '../css');
    const css = ['zynora-theme-contract.css', 'zynora-design-tokens.css', 'zynora-design-system.css',
      'zynora-employee-updates.css', 'pages/employee-updates-identity.css']
      .map(x => fs.readFileSync(path.join(cssRoot,x),'utf8')).join('\n');
    for (const theme of ['dark','light']) for (const width of [320,390,900,1440]) {
      await page.setViewportSize({width,height:700});
      await page.setContent(`<!doctype html><html dir="rtl" data-theme="${theme}"><head><style>${css}</style></head><body class="zy-app">
        <section class="nxupd-page nxupd-page-v14b zy-employee-updates"><section class="nxupd-card"><form id="synthetic"><div class="nxupd-fields">
        <div class="nxupd-field"><label>اختيار تجريبي</label><select name="Choice"><option>A</option><option>B</option></select></div>
        <div class="nxupd-field"><label>علم تجريبي</label><input type="checkbox" name="Flag" value="true" checked><input type="hidden" name="Flag" value="false"></div>
        <div class="nxupd-field"><label>قراءة فقط</label><input readonly value="Synthetic only"></div></div></form></section></section></body></html>`);
      const initial = await page.evaluate(()=>({
        checkbox:getComputedStyle(document.querySelector('[type="checkbox"]')).width,
        flags:new FormData(document.getElementById('synthetic')).getAll('Flag'),
        overflow:document.documentElement.scrollWidth>innerWidth,
        entries:[...new FormData(document.getElementById('synthetic')).keys()]
      }));
      assert.equal(initial.checkbox,'20px');
      assert.equal(initial.overflow,false);
      assert.deepEqual(initial.flags,['true','false']);
      assert.deepEqual(initial.entries,['Choice','Flag','Flag']);
      await page.locator('[type="checkbox"]').uncheck();
      assert.deepEqual(await page.evaluate(()=>new FormData(document.getElementById('synthetic')).getAll('Flag')),['false']);
    }
    console.log('PASS: 8 synthetic field fixtures, RTL responsive grid, 20px checkboxes, checked/unchecked payloads, no readonly submission.');
  } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
