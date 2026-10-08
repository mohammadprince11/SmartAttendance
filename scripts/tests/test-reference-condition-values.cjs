const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(path.resolve(__dirname, '../../SmartAttendance.Web/wwwroot/js/zynora-conditions.js'), 'utf8');
const start = source.indexOf('ZyConditions.prototype.renderValueEditor =');
const editor = source.slice(start, source.indexOf('ZyConditions.prototype.describe =', start));
function element(tag) {
  return { tag, children: [], value: '', events: {}, appendChild(child) { this.children.push(child); if (child.selected) this.value = child.value; },
    addEventListener(name, fn) { this.events[name] = fn; } };
}
function ZyConditions() {}
const context = vm.createContext({ ZyConditions, document: { createElement: element }, el: element });
vm.runInContext(editor, context);
const builder = new ZyConditions();
builder.byKey = { nationality: { kind: 'Lookup', options: [{ value: 'Iraqi', label: 'عراقي' }] } };
let syncs = 0;
builder.sync = () => syncs++;
const rule = { criterion: 'nationality', op: 'Equal', value: 'Legacy value' };
let node = builder.renderValueEditor(rule);
assert.equal(node.value, 'Legacy value');
assert.equal(node.children.length, 3);
node.value = 'Iraqi'; node.events.change();
assert.equal(rule.value, 'Iraqi'); assert.equal(syncs, 1);
node = builder.renderValueEditor(rule);
assert.equal(node.children.length, 2); assert.equal(node.value, 'Iraqi');
rule.op = 'In'; rule.value = 'Iraqi,Legacy value';
node = builder.renderValueEditor(rule);
assert.equal(node.tag, 'input'); assert.equal(node.value, 'Iraqi,Legacy value');
console.log('Reference condition values: 8 checks passed.');
