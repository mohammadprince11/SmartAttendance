const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../SmartAttendance.Web/wwwroot/js/zynora-employee-capacity.js'), 'utf8');
function fixture(fetchImpl) {
  function element() {
    const classes = new Set();
    return { attributes: new Map([['inert', '']]), events: {}, classList: { add: x => classes.add(x), remove: x => classes.delete(x), contains: x => classes.has(x) }, addEventListener(k, fn) { this.events[k] = fn; }, setAttribute(k,v) { this.attributes.set(k,v); }, removeAttribute(k) { this.attributes.delete(k); }, focus() { this.focused = true; } };
  }
  const trigger = element(), modal = element(), backdrop = element(), close = element(), message = {};
  trigger.href = '/Employees/Create'; trigger.dataset = { capacityUrl: '/Employees?handler=EmployeeCapacity' };
  modal.querySelector = () => close;
  const document = { querySelector: () => trigger, getElementById: id => ({ 'employee-capacity-modal':modal, 'employee-capacity-backdrop':backdrop, 'employee-capacity-message':message })[id], documentElement: { style: { overflow: '' } } };
  const navigations = [];
  vm.runInNewContext(source, { document, fetch:fetchImpl, window: { location: { assign: url => navigations.push(url) } } });
  return { trigger, modal, backdrop, close, message, navigations, click: () => trigger.events.click({ preventDefault() {} }) };
}
(async () => {
  let requests = 0;
  const full = fixture(async (url, options) => { requests++; assert.equal(options.cache, 'no-store'); return { ok:true, json:async () => ({ canAdd:false, message:'10 من 10' }) }; });
  await full.click(); assert.equal(full.message.textContent, '10 من 10'); assert.equal(full.navigations.length, 0); assert(full.modal.classList.contains('zy-open'));
  full.modal.events.keydown({ key:'Escape' }); assert(!full.modal.classList.contains('zy-open')); assert(full.trigger.focused);
  await full.click(); assert.equal(requests, 2);
  const available = fixture(async () => ({ ok:true, json:async () => ({ canAdd:true }) }));
  await available.click(); assert.deepEqual(available.navigations, ['/Employees/Create']);
  const failed = fixture(async () => { throw new Error('offline'); });
  await failed.click(); assert.equal(failed.navigations.length, 0); assert(failed.message.textContent.includes('تعذر التحقق'));
  let release; let concurrentRequests = 0;
  const concurrent = fixture(() => { concurrentRequests++; return new Promise(resolve => { release=resolve; }); });
  const pending = concurrent.click(); await concurrent.click(); assert.equal(concurrentRequests, 1);
  release({ ok:true, json:async () => ({ canAdd:false, message:'full' }) }); await pending;
  assert.equal(concurrent.trigger.attributes.has('aria-busy'), false);
  console.log('PASS: full-limit popup, close/focus, fresh recheck, available navigation, failed check, duplicate-click guard.');
})().catch(error => { console.error(error); process.exitCode=1; });
