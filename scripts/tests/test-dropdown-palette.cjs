/* Disposable browser fixture only: no server, accounts or database access. */
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const css = name => fs.readFileSync(path.join(root, 'SmartAttendance.Web/wwwroot/css', name), 'utf8');
const common = ['zynora-theme-contract.css', 'zynora-design-tokens.css',
  'zynora-design-system.css', 'zynora-select-system.css'];
const cases = [
  ['filters', '', []],
  ['create', 'employee-create-form', ['zynora-employee-create-designlab-v1.css']],
  ['edit', 'employee-edit-form', ['zynora-employee-edit-create-parity.css']],
  ['position', 'positionForm', ['positions.css', 'zynora-legacy-bridge.css']],
  ['onboarding', '', ['pages/smart-onboarding-review.css']]
];
(async () => {
  const browser = await chromium.launch({ channel: process.env.DROPDOWN_BROWSER_CHANNEL || 'msedge', headless: true });
  let checks = 0;
  try {
    const page = await browser.newPage();
    await page.route('**/*', route => route.abort());
    for (const theme of ['dark', 'light']) {
      for (const width of [420, 1280]) {
        await page.setViewportSize({ width, height: 800 });
        for (const [name, id, extras] of cases) {
          const styles = [...common, ...extras, 'zynora-dropdown-contract.css'].map(css).join('\n');
          await page.setContent(`<html dir="rtl" data-theme="${theme}"><head><style>${styles}</style></head>
            <body class="zy-app"><div class="zynora-profile-theme-v2 sor-shell"><form id="${id}" class="sor-final-form">
              <div class="nxcs-select"><button type="button" class="nxcs-trigger"><span class="nxcs-value">خيار</span><span class="nxcs-caret">⌄</span></button></div>
              <div class="nx-search-select"><button class="nx-search-select__button">خيار</button><div class="nx-search-select__panel"><button class="nx-search-select__option is-selected">خيار</button></div></div>
              <div class="nxex-csel"><button class="nxex-csel-trigger">خيار</button><div class="nxex-csel-list"><button class="nxex-csel-opt" aria-selected="true">خيار</button></div></div>
              <div class="nxr-searchable-wrapper"><input class="nxr-search-input"><div class="nxr-search-dropdown"><div class="nxr-option" aria-selected="true">خيار</div></div></div>
              <select multiple><option selected>خيار</option></select>
            </form></div><div class="nxcs-panel"><button class="nxcs-option is-selected">خيار</button><button class="nxcs-option">آخر</button></div>
            <style>html body.zy-app #${id || 'unused'} .nxcs-trigger { background: magenta !important; border-radius: 1px !important; }</style>
            </body></html>`);
          const result = await page.evaluate(() => {
            const style = sel => { const s = getComputedStyle(document.querySelector(sel)); return { bg: s.backgroundColor, height: s.height, radius: s.borderRadius, font: s.fontFamily }; };
            return {
              fields: ['.nxcs-trigger', '.nx-search-select__button', '.nxex-csel-trigger', '.nxr-search-input'].map(style),
              panels: ['.nxcs-panel', '.nx-search-select__panel', '.nxex-csel-list', '.nxr-search-dropdown', 'select[multiple]'].map(style),
              options: ['.nxcs-option.is-selected', '.nx-search-select__option.is-selected', '.nxex-csel-opt', '.nxr-option'].map(style),
              active: getComputedStyle(document.documentElement).getPropertyValue('--zy-dd-option-active').trim()
            };
          });
          const label = `${name}/${theme}/${width}`;
          for (const field of result.fields) {
            assert.equal(field.bg, theme === 'dark' ? 'rgb(17, 27, 42)' : 'rgb(255, 255, 255)', label);
            assert.equal(field.height, '44px', label);
            assert.equal(field.radius, '12px', label);
            assert.match(field.font, /Tajawal/, label);
          }
          for (const panel of result.panels) assert.equal(panel.bg, theme === 'dark' ? 'rgb(15, 26, 41)' : 'rgb(255, 255, 255)', label);
          for (const option of result.options) {
            if (theme === 'dark') assert.equal(option.bg, 'rgb(32, 55, 79)', label);
            else assert.notEqual(option.bg, 'rgba(0, 0, 0, 0)', label);
          }
          const before = result.options[0].bg;
          await page.locator('.nxcs-option:not(.is-selected)').focus();
          assert.equal(await page.locator('.nxcs-option:not(.is-selected)').evaluate(el => getComputedStyle(el).backgroundColor), before, label + '/focus');
          checks++;
        }
      }
    }
    console.log(`PASS: ${checks} isolated palette/geometry scenarios (dark/light, RTL, desktop/mobile, legacy ID overrides, searchable variants and native multi-select).`);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
