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
    data.templates[0].Definition.DesignIds=['11111111-1111-1111-1111-111111111111'];
    data.catalog={'تاريخ النهاية يجب ألا يسبق البداية.':'Synthetic translated date range','إزالة الحقل':'Synthetic remove field'};
    data.languages=[{Code:'fr-FR',Direction:'rtl'},{Code:'en-US',Direction:'ltr'}];
    const select=(id,options)=>`<select id="zys-${id}">${options.map(v=>`<option value="${v}">${v}</option>`).join('')}</select>`;
    await page.setContent(`<section id="announcement-template-studio"><form id="zys-compose">${select('template',['holiday'])}<div id="zys-fields"></div>${select('design',['builtin:holiday'])}${select('fit',['contain','cover'])}${select('position',['center','top','bottom'])}${select('placement',['above','below','overlay'])}<button type="submit">save</button></form>${select('preview-language',[])}<article id="zys-preview"><h3 id="zys-title"></h3><p id="zys-body"></p><img id="zys-image"></article><p id="zys-errors"></p><form id="zys-builder">${select('edit',['','holiday'])}<input id="zys-key"><input id="zys-name"><input id="zys-revision"><input id="zys-json"><div id="zys-builder-fields"></div><div id="zys-builder-languages"></div><button type="button" id="zys-add-field">field</button><button type="button" id="zys-add-language">language</button></form><script type="application/json" id="zys-data">${JSON.stringify(data)}</script></section>`);
    await page.addScriptTag({content:fs.readFileSync('SmartAttendance.Web/wwwroot/js/zynora-announcement-templates.js','utf8')});
    assert.equal(await page.locator('input[name="Values[startDate]"]').getAttribute('type'),'date');
    assert.equal(await page.locator('input[name="Values[occasion]"]').inputValue(),'Synthetic holiday');
    await page.selectOption('#zys-preview-language','en');
    assert.equal(await page.locator('#zys-title').textContent(),'Holiday Synthetic holiday');
    assert.equal(await page.locator('#zys-preview').getAttribute('dir'),'ltr');
    await page.evaluate(()=>{const option=document.createElement('option');option.value='fr-FR';option.textContent='Synthetic custom direction';document.getElementById('zys-preview-language').append(option);});
    await page.selectOption('#zys-preview-language','fr-FR');
    assert.equal(await page.locator('#zys-preview').getAttribute('dir'),'rtl'); // Direction comes from dictionary metadata, not a hardcoded language list.
    await page.selectOption('#zys-preview-language','en');
    await page.locator('input[name="Values[occasion]"]').fill('<img src=x onerror=alert(1)>');
    assert.equal(await page.locator('#zys-title img').count(),0);
    await page.locator('input[name="Values[endDate]"]').fill('2026-10-01');
    assert.equal(await page.locator('#zys-errors').textContent(),'Synthetic translated date range');
    await page.selectOption('#zys-placement','overlay');
    assert.ok((await page.locator('#zys-preview').getAttribute('class')).includes('zys-overlay'));
    await page.selectOption('#zys-edit','holiday');
    assert.equal(await page.locator('[data-field]').count(),3);
    assert.equal(await page.getByText('Synthetic remove field',{exact:true}).count(),3);
    assert.equal(await page.locator('[data-language]').count(),2);
    await page.click('#zys-add-language');
    await page.locator('[data-language]').last().locator('[data-part=code]').fill('ckb-IQ');
    await page.locator('[data-language]').last().locator('[data-part=title]').fill('Synthetic Kurdish');
    await page.locator('[data-language]').last().locator('[data-part=body]').fill('Synthetic body');
    await page.evaluate(()=>{const form=document.getElementById('zys-builder');form.addEventListener('submit',e=>e.preventDefault());form.dispatchEvent(new Event('submit',{cancelable:true}));});
    const saved=JSON.parse(await page.locator('#zys-json').inputValue());
    assert.ok(saved.Languages['ckb-IQ']);assert.equal(saved.Fields.length,3);
    assert.equal(await page.locator('#zys-image').getAttribute('src'),null);
    assert.equal(await page.locator('#zys-image').isVisible(),false);
    await page.evaluate(()=>{
      const option=document.createElement('option');option.value='11111111-1111-1111-1111-111111111111';
      document.getElementById('zys-design').append(option);
    });
    await page.selectOption('#zys-design','11111111-1111-1111-1111-111111111111');
    await page.locator('#zys-compose').dispatchEvent('input');
    assert.ok((await page.locator('#zys-image').getAttribute('src')).includes('handler=Image'));
    assert.equal(await page.locator('#zys-image').getAttribute('hidden'),null);
    await page.evaluate(()=>{
      const option=document.createElement('option');option.value='';
      document.getElementById('zys-design').prepend(option);
    });
    await page.selectOption('#zys-design','');
    await page.locator('#zys-compose').dispatchEvent('input');
    assert.equal(await page.locator('#zys-image').getAttribute('src'),null);
    assert.equal(await page.locator('#zys-image').isVisible(),false);
    await page.evaluate(()=>{
      const data=JSON.parse(document.getElementById('zys-data').textContent);
      data.templates[0].Definition.Key='newborn-girl';data.templateKey='newborn-girl';
      data.design='builtin:newborn';
      document.getElementById('zys-data').textContent=JSON.stringify(data);
      document.getElementById('zys-template').options[0].value='newborn-girl';
    });
    await page.addScriptTag({content:fs.readFileSync('SmartAttendance.Web/wwwroot/js/zynora-announcement-templates.js','utf8')});
    assert.equal(await page.locator('#zys-design').inputValue(),'');
    assert.equal(await page.locator('#zys-image').getAttribute('src'),null);
    assert.equal(await page.locator('#zys-image').isVisible(),false);
    await page.evaluate(()=>{
      const data=JSON.parse(document.getElementById('zys-data').textContent);
      data.builtinAssets=['zynora-v1-newborn','zynora-v1-birthday'];
      data.defaultAssets={'newborn-girl':'zynora-v1-newborn'};
      data.useDefaultDesign=true;data.design=null;
      document.getElementById('zys-data').textContent=JSON.stringify(data);
      for(const asset of data.builtinAssets){const option=document.createElement('option');option.value='builtin:'+asset;document.getElementById('zys-design').append(option);}
    });
    await page.addScriptTag({content:fs.readFileSync('SmartAttendance.Web/wwwroot/js/zynora-announcement-templates.js','utf8')});
    assert.equal(await page.locator('#zys-design').inputValue(),'builtin:zynora-v1-newborn');
    assert.equal(await page.locator('#zys-image').getAttribute('src'),'/brand/announcement-studio/art/zynora-v1-newborn.png');
    assert.equal(await page.locator('#zys-design option[value="builtin:zynora-v1-birthday"]').count(),0);
    await page.selectOption('#zys-design','');
    await page.locator('#zys-compose').dispatchEvent('input');
    assert.equal(await page.locator('#zys-image').getAttribute('src'),null);
    assert.equal(await page.locator('#zys-image').isVisible(),false);
    assert.deepEqual(errors,[]);
    // Actual stylesheet + generated builder rows: no overlap at desktop/mobile or RTL/LTR.
    await page.addStyleTag({content:fs.readFileSync('SmartAttendance.Web/wwwroot/css/zynora-design-system.css','utf8').replace(/^\uFEFF/,'')});
    await page.addStyleTag({content:fs.readFileSync('SmartAttendance.Web/wwwroot/css/zynora-announcement-templates.css','utf8')});
    await page.evaluate(()=>{
      const root=document.getElementById('announcement-template-studio');root.classList.add('zy-scope','zys');
      document.getElementById('zys-builder').classList.add('zy-card');
      for(const id of ['revision','json'])document.getElementById('zys-'+id).type='hidden';
      const library=document.createElement('div');library.className='zys-library';
      for(let i=0;i<11;i++){const form=document.createElement('form');form.innerHTML='<span>قالب تجريبي بعنوان طويل لاختبار التفعيل</span><button class="zy-btn" type="button">تعطيل</button>';library.append(form);}root.append(library);
    });
    assert.equal(await page.locator('#zys-builder').evaluate(e=>getComputedStyle(e).display),'grid');
    assert.equal(await page.locator('[data-language]').first().evaluate(e=>getComputedStyle(e).display),'grid');
    for(const width of [1263,768,390])for(const direction of ['rtl','ltr']){
      await page.setViewportSize({width,height:1100});await page.evaluate(d=>document.documentElement.dir=d,direction);
      const problems=await page.evaluate(()=>{
        const problems=[];
        for(const row of document.querySelectorAll('[data-language],.zys-builder-row,.zys-library>form')){
          const elements=[...row.children].filter(e=>e.tagName==='LABEL'||e.tagName==='BUTTON'||e.tagName==='SPAN');
          const bounds=elements.map(e=>e.getBoundingClientRect());
          for(let i=0;i<bounds.length;i++)for(let j=i+1;j<bounds.length;j++){
            const a=bounds[i],b=bounds[j];if(Math.min(a.right,b.right)-Math.max(a.left,b.left)>1&&Math.min(a.bottom,b.bottom)-Math.max(a.top,b.top)>1)problems.push('overlap');
          }
          if(row.scrollWidth>row.clientWidth+2)problems.push('overflow');
        }return problems;
      });assert.deepEqual(problems,[],`${width} ${direction}`);
    }
    await page.setViewportSize({width:1263,height:1100});await page.evaluate(()=>document.documentElement.dir='rtl');
    await page.locator('#zys-builder').screenshot({path:'work/announcement-builder-layout.png'});
    console.log('PASS: builder layout at three widths, RTL/LTR, no overlap or row overflow.');
    console.log('PASS: actual-script DOM smoke (date inputs, restored values, languages, escaped text, ranges, layout, custom language serialization).');
  } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
