// Isolated DOM smoke test of the actual browser script; not a production or full application E2E test.
const { chromium } = require('playwright');
const fs = require('node:fs');
const assert = require('node:assert/strict');
(async () => {
  const browser = await chromium.launch({ headless: true, ...(process.env.ZYNORA_TEST_BROWSER ? {executablePath:process.env.ZYNORA_TEST_BROWSER}: {}) });
  try {
    const page = await browser.newPage();
    const errors=[]; page.on('pageerror',e=>errors.push(e.message));
    const data={companyId:1,today:'2026-10-09',templateKey:'holiday',values:{occasion:'Synthetic holiday',startDate:'2026-10-10',endDate:'2026-10-12'},design:'builtin:holiday',fit:'contain',position:'center',placement:'above',templates:[{Definition:{Key:'holiday',Name:'Holiday',DesignIds:[],Fields:[{Key:'occasion',Label:'Occasion',Type:'text',Required:true},{Key:'startDate',Label:'Start',Type:'date',Required:true},{Key:'endDate',Label:'End',Type:'date',Required:true}],Languages:{ar:{Title:'عطلة {occasion}',Body:'من {startDate} إلى {endDate}'},en:{Title:'Holiday {occasion}',Body:'From {startDate} to {endDate}'}}},Revision:'00000000-0000-0000-0000-000000000000'}]};
    const select=(id,options)=>`<select id="zys-${id}">${options.map(v=>`<option value="${v}">${v}</option>`).join('')}</select>`;
    await page.setContent(`<section id="announcement-template-studio"><form id="zys-compose">${select('template',['holiday'])}<div id="zys-fields"></div>${select('design',['builtin:holiday'])}${select('fit',['contain','cover'])}${select('position',['center','top','bottom'])}${select('placement',['above','below','overlay'])}<button type="submit">save</button></form>${select('preview-language',[])}<article id="zys-preview"><h3 id="zys-title"></h3><p id="zys-body"></p><img id="zys-image"></article><p id="zys-errors"></p><form id="zys-builder">${select('edit',['','holiday'])}<input id="zys-key"><input id="zys-name"><input id="zys-revision"><input id="zys-json"><div id="zys-builder-fields"></div><div id="zys-builder-languages"></div><button type="button" id="zys-add-field">field</button><button type="button" id="zys-add-language">language</button></form><script type="application/json" id="zys-data">${JSON.stringify(data)}</script></section>`);
    await page.addScriptTag({content:fs.readFileSync('SmartAttendance.Web/wwwroot/js/zynora-announcement-templates.js','utf8')});
    assert.equal(await page.locator('input[name="Values[startDate]"]').getAttribute('type'),'date');
    assert.equal(await page.locator('input[name="Values[occasion]"]').inputValue(),'Synthetic holiday');
    await page.selectOption('#zys-preview-language','en');
    assert.equal(await page.locator('#zys-title').textContent(),'Holiday Synthetic holiday');
    assert.equal(await page.locator('#zys-preview').getAttribute('dir'),'ltr');
    await page.locator('input[name="Values[occasion]"]').fill('<img src=x onerror=alert(1)>');
    assert.equal(await page.locator('#zys-title img').count(),0);
    await page.locator('input[name="Values[endDate]"]').fill('2026-10-01');
    assert.ok((await page.locator('#zys-errors').textContent()).length>0);
    await page.selectOption('#zys-placement','overlay');
    assert.ok((await page.locator('#zys-preview').getAttribute('class')).includes('zys-overlay'));
    await page.selectOption('#zys-edit','holiday');
    assert.equal(await page.locator('[data-field]').count(),3);
    assert.equal(await page.locator('[data-language]').count(),2);
    await page.click('#zys-add-language');
    await page.locator('[data-language]').last().locator('[data-part=code]').fill('ckb-IQ');
    await page.locator('[data-language]').last().locator('[data-part=title]').fill('Synthetic Kurdish');
    await page.locator('[data-language]').last().locator('[data-part=body]').fill('Synthetic body');
    await page.evaluate(()=>{const form=document.getElementById('zys-builder');form.addEventListener('submit',e=>e.preventDefault());form.dispatchEvent(new Event('submit',{cancelable:true}));});
    const saved=JSON.parse(await page.locator('#zys-json').inputValue());
    assert.ok(saved.Languages['ckb-IQ']);assert.equal(saved.Fields.length,3);
    assert.deepEqual(errors,[]);
    console.log('PASS: actual-script DOM smoke (date inputs, restored values, languages, escaped text, ranges, layout, custom language serialization).');
  } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
