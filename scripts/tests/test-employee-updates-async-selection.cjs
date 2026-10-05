/* Browser fixture only. No real app, accounts, employee records or database. */
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../../SmartAttendance.Web');
const view = fs.readFileSync(path.join(root, 'Pages/EmployeeUpdates/Index.cshtml'), 'utf8');
assert.match(view, /SubmitOnSelect = false/);
assert.match(view, /zynora-employee-updates-selection.js/);
function fixture(id = '') {
  return `<section class="nxupd-page"><header class="nxupd-hero-status"><strong>${id ? 1 : 0}</strong></header>
  <section><form class="nxupd-employee-select" action="/EmployeeUpdates"><input name="employeeSelected" value="true" type="hidden"><input name="tab" value="stage" type="hidden"><input name="section" value="employee-master" type="hidden">
  <div data-zyep><input class="zyep-id" name="employeeId" value="${id}" type="hidden"><input class="zyep-code" value="${id ? 'TEST-'+id : ''}"><input class="zyep-name" value="${id ? 'Synthetic '+id : ''}" readonly><button class="zyep-open" type="button">pick</button></div></form></section>
  <nav class="nxupd-top-tabs"><a href="/EmployeeUpdates?employeeId=${id}">tab ${id}</a></nav>
  <section class="nxupd-content">${id ? `<form data-nxupd-confirm method="post"><input name="employeeId" value="${id}" type="hidden"><input name="__RequestVerificationToken" value="fake-token-${id}" type="hidden"><div class="nxupd-field"><input name="value" value="value-${id}"></div><button>save</button></form>` : '<p>Choose employee</p>'}</section></section>
  <div data-nxupd-modal hidden><div class="nxupd-modal"></div><h2 data-nxupd-modal-title></h2><p data-nxupd-modal-message></p><i data-nxupd-modal-icon></i><button data-nxupd-modal-confirm>confirm</button><button data-nxupd-modal-cancel>cancel</button></div>`;
}
(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage();
    let navigations = 0;
    await page.route('**/*', route => {
      if (route.request().isNavigationRequest()) navigations++;
      return route.fulfill({contentType:'text/html', body:fixture()});
    });
    await page.goto('http://fixture.test/EmployeeUpdates');
    await page.evaluate(() => {
      window.requests = [];
      window.fetch = (url, options) => new Promise(resolve => requests.push({ url: String(url), options, resolve }));
    });
    await page.addScriptTag({path:path.join(root,'wwwroot/js/zynora-employee-picker.js')});
    await page.addScriptTag({path:path.join(root,'wwwroot/js/zynora-employee-updates.js')});
    await page.addScriptTag({path:path.join(root,'wwwroot/js/zynora-employee-updates-selection.js')});
    const choose = id => page.evaluate(id => {
      const picker = document.querySelector('[data-zyep]');
      picker.querySelector('.zyep-id').value = id;
      picker.querySelector('.zyep-code').value = id ? 'TEST-'+id : '';
      picker.querySelector('.zyep-name').value = id ? 'Synthetic '+id : '';
      picker.dispatchEvent(new CustomEvent('zyep:change', { bubbles:true }));
    }, id);
    const reply = (index, html, status=200) => page.evaluate(({index,html,status}) => requests[index].resolve(new Response(html,{status})), {index,html,status});
    // Only a deliberate picker choice starts loading. No default employee.
    assert.equal(await page.evaluate(() => requests.length), 0);
    await choose('1'); await choose('2');
    assert.equal(await page.locator('.nxupd-content').evaluate(e => e.inert && e.hidden), true);
    await reply(1, fixture('2'));
    await page.waitForFunction(() => document.querySelector('.nxupd-content input[name=employeeId]')?.value === '2');
    await reply(0, fixture('1'));
    assert.equal(await page.locator('.nxupd-content input[name=employeeId]').inputValue(), '2');
    assert.equal(await page.locator('input[name=__RequestVerificationToken]').inputValue(), 'fake-token-2');
    assert.match(page.url(), /employeeId=2/);
    assert.equal(await page.evaluate(() => requests[0].options.cache), 'no-store');
    // Delegated field highlighting and confirmation survive the content swap.
    await page.locator('.nxupd-field input').fill('edited');
    await page.locator('.nxupd-field input').dispatchEvent('change');
    assert.equal(await page.locator('.nxupd-field').evaluate(e => e.classList.contains('is-changed')), true);
    await page.locator('.nxupd-content form').evaluate(e => e.requestSubmit());
    assert.equal(await page.locator('[data-nxupd-modal]').evaluate(e => e.hidden), false);
    await page.locator('[data-nxupd-modal-cancel]').click();
    page.once('dialog', dialog => dialog.dismiss());
    await choose('3');
    assert.equal(await page.locator('.zyep-id').inputValue(), '2');
    assert.equal(await page.locator('.nxupd-content').evaluate(e => e.hidden || e.inert), false);
    page.once('dialog', dialog => dialog.accept());
    await choose('3');
    await reply(2, fixture('3'),403);
    await page.waitForFunction(() => document.querySelector('.nxupd-alert')?.textContent.includes('تعذّر'));
    assert.equal(await page.locator('.nxupd-content').evaluate(e => e.hidden && e.inert), true);
    assert.equal(await page.locator('.nxupd-content form').evaluate(form => {
      form.removeAttribute('data-nxupd-confirm');
      return form.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));
    }), false);
    page.once('dialog', dialog => dialog.accept());
    await page.locator('.nxupd-alert button').click();
    // Wrong employee / login-like document is rejected rather than displaying stale forms.
    await reply(3,fixture('4'));
    await page.waitForFunction(() => document.querySelector('.nxupd-alert button'));
    assert.equal(await page.locator('.nxupd-content').evaluate(e => e.hidden && e.inert), true);
    page.once('dialog', dialog => dialog.accept());
    await choose('');
    await reply(4,fixture());
    await page.waitForFunction(() => !document.querySelector('.nxupd-content').hidden);
    assert.equal(await page.locator('.nxupd-content form').count(),0);
    assert.match(page.url(), /employeeSelected=false/);
    // Actual shared picker code resolution also triggers the async path, not form.submit().
    await page.locator('.zyep-code').fill('TEST-5');
    await page.waitForFunction(() => requests.length === 6);
    await page.evaluate(() => requests[5].resolve(new Response(JSON.stringify({items:[{id:5,code:'TEST-5',name:'Synthetic 5'}]}),{headers:{'Content-Type':'application/json'}})));
    await page.waitForFunction(() => requests.length === 7);
    await reply(6,fixture('5'));
    await page.waitForFunction(() => document.querySelector('.nxupd-content input[name=employeeId]')?.value === '5');
    assert.equal(navigations,1);
    console.log('PASS: async selection, stale responses, no navigation, scoped error/mismatch rejection, retry, empty selection, dirty cancellation, fresh token and dynamic confirmation, real picker code lookup.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
