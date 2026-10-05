const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const jquery = fs.readFileSync(path.join(root, 'SmartAttendance.Web/wwwroot/lib/jquery-validation/dist/jquery.validate.js'), 'utf8');
const unobtrusive = fs.readFileSync(path.join(root, 'SmartAttendance.Web/wwwroot/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.js'), 'utf8');
const model = fs.readFileSync(path.join(root, 'SmartAttendance.Web/Pages/Platform/Tenants/Details.cshtml.cs'), 'utf8');

// Exercise the shipped library's actual rules, with a single selected option.
const rangeSource = jquery.match(/rangelength: (function\( value, element, param \) \{[\s\S]*?\n\s*\}),/)[1];
const regexSource = unobtrusive.match(/addMethod\("regex", (function \(value, element, params\) \{[\s\S]*?\n\s*\})\);/)[1];
const range = vm.runInNewContext('(' + rangeSource + ')');
const regex = vm.runInNewContext('(' + regexSource + ')');
const select = { nodeName: 'SELECT' };
const getLengthSource = jquery.match(/getLength: (function\( value, element \) \{[\s\S]*?\n\s*\}),/)[1];
const getLength = vm.runInNewContext('(' + getLengthSource + ')', {
  $: (selector, element) => {
    assert.equal(selector, 'option:selected');
    assert.equal(element, select);
    return { length: 1 };
  }
});
const validator = { optional: () => false, getLength };
assert.equal(getLength.call(validator, 'Custom', select), 1);
assert.equal(range.call(validator, 'Custom', select, [2, 60]), false, 'Reproduce old option-count failure');
const pattern = model.match(/RegularExpression\(@"([^"]+)"[^\n]+\n\s*public string PlanCode/)[1];
for (const code of ['Custom', 'Trial', 'AB', 'A'.repeat(60)]) {
  assert.equal(!!regex.call(validator, code, select, pattern), true);
}
for (const code of ['A', '', 'A'.repeat(61)]) {
  assert.equal(!!regex.call(validator, code, select, pattern), false);
}
console.log('Platform plan validation: old failure reproduced; selected code validation passes.');
