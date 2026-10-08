const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'SmartAttendance.Web/Pages/Employees/Profile.cshtml'), 'utf8');
const start = source.indexOf('function hrmsSetRecordLabels(');
const script = source.slice(start, source.indexOf('</script>', start));
const elements = new Map();
function element(id) {
  if (!elements.has(id)) elements.set(id, { id, value: '', hidden: false, disabled: false, required: false,
    options: [], querySelectorAll() { return this.options.filter(x => x.dataset.legacyOption).map(o => ({ remove: () => { this.options = this.options.filter(x => x !== o); } })); },
    add(option) { this.options.push(option); } });
  return elements.get(id);
}
let editHandler;
const button = { dataset: { type: 'Asset', title: 'Legacy asset', id: '1' }, addEventListener(_, fn) { editHandler = fn; } };
const document = {
  getElementById: element,
  querySelector() { return { reset() { for (const e of elements.values()) e.value = ''; } }; },
  querySelectorAll(selector) { return selector === '.rec-open' ? [button] : [...elements.values()]; }
};
function Option(label, value) { this.text = label; this.value = value; this.dataset = {}; }
const context = vm.createContext({ document, Option, window: {}, hrmsCfToggleRecordGroups() {}, hrmsCfFill() {} });
vm.runInContext(script, context);
context.hrmsOpenRecord('Asset', '', '', '', '', false, false, '');
assert.equal(element('rec-Title').disabled, true);
assert.equal(element('rec-Title').hidden, true);
assert.equal(element('rec-AssetType').disabled, false);
assert.equal(element('rec-AssetType').required, true);
editHandler();
assert.equal(element('rec-AssetType').value, 'Legacy asset');
assert.equal(element('rec-AssetType').options.length, 1);
editHandler();
assert.equal(element('rec-AssetType').options.length, 1);
context.hrmsOpenRecord('Education', '', '', '', '', false, false, '');
assert.equal(element('rec-Title').disabled, false);
assert.equal(element('rec-Title').required, true);
assert.equal(element('rec-AssetType').disabled, true);
assert.equal(element('rec-AssetType-field').hidden, true);
assert.equal(element('rec-AssetType').options.length, 0);
context.hrmsOpenRecord('Asset', '', '', '', '', false, false, '');
assert.equal(element('rec-AssetType').value, '');
assert.match(source, /foreach \(var assetType in Model.AssetTypeOptions\)/);
console.log('Reference asset picker: 14 checks passed.');
