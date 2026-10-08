// Offline synthetic PDF rendering. No app, real files, accounts or database.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const http = require('node:http');
const {chromium} = require('playwright');
const {PDFDocument, rgb} = require('pdf-lib');
const root = path.resolve(__dirname, '../..');
const web = path.join(root, 'SmartAttendance.Web/wwwroot');
const html = name => `<!doctype html><html lang="ar" dir="rtl" data-theme="dark"><head><meta charset="utf-8">
<link rel="stylesheet" href="/css/zynora-disciplinary-rules.css"><style nonce="fixture">.nxpen-a4-paper{width:420px;height:594px}.nxpen-text-block{right:8%;top:10%;width:60%}</style></head>
<body><section data-disciplinary-workspace data-disciplinary-designer><section class="nxpen-live-preview"><p data-disciplinary-pdf-status role="status">تحميل</p>
<div class="nxpen-a4-paper"><canvas data-disciplinary-pdf="/DisciplinaryRules?handler=A4FormPreview" hidden></canvas>
<div class="nxpen-a4-section nxpen-a4-body"><div class="nxpen-text-block" data-layer-id="1">نص تجريبي</div></div></div>
<form data-layer-form data-layer-id="1"><input name="xPercent" value="8"><input name="yPercent" value="10"></form></section></section>
<script src="/js/zynora-disciplinary-tools.js"></script><script src="/js/zynora-disciplinary-pdf.js"></script></body></html>`;
(async () => {
  const pdf = await PDFDocument.create();
  for (let i=0;i<2;i++) { const p=pdf.addPage([595,842]); p.drawRectangle({x:0,y:0,width:595,height:842,color:rgb(0.2,0.6,0.8)}); p.drawText('SYNTHETIC FORM '+(i+1),{x:50,y:700,size:25}); }
  const data = Buffer.from(await pdf.save());
  let requests=0, external=0, checks=0, activeCase='valid';
  const server = http.createServer((req,res) => {
    const url = new URL(req.url,'http://localhost');
    if (url.pathname === '/') {
      activeCase=url.searchParams.get('case')||'valid';
      res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self' 'nonce-fixture'; worker-src 'self'; connect-src 'self'; font-src 'self' data:; object-src 'none'");
      res.setHeader('Content-Type','text/html'); return res.end(html(url.searchParams.get('case')||'valid'));
    }
    if (url.pathname.startsWith('/uploads/')) {res.statusCode=404;return res.end();}
    if (url.pathname==='/DisciplinaryRules' && url.searchParams.get('handler')==='A4FormPreview') {
      requests++; res.setHeader('Content-Type','application/pdf');
      if(activeCase==='missing') {res.statusCode=404;return res.end();}
      if(activeCase==='access') {res.statusCode=403;return res.end();}
      if (activeCase==='invalid') return res.end('not a PDF');
      if (activeCase==='large') {res.setHeader('Content-Length',21*1024*1024);return res.end();}
      return res.end(data);
    }
    const asset = path.resolve(web,'.'+url.pathname);
    if (!asset.startsWith(web+path.sep) || !fs.existsSync(asset)) {res.statusCode=404;return res.end();}
    res.setHeader('Content-Type',asset.endsWith('.mjs')||asset.endsWith('.js')?'text/javascript':asset.endsWith('.css')?'text/css':'application/octet-stream');
    res.end(fs.readFileSync(asset));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const origin = `http://127.0.0.1:${server.address().port}`;
  const browser=await chromium.launch({channel:'msedge',headless:true});
  try {
    const page=await browser.newPage({viewport:{width:900,height:800}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    page.on('request',req=>{if(!req.url().startsWith(origin))external++;});
    await page.goto(origin);
    assert.equal(await page.evaluate(async()=> (await fetch('/uploads/disciplinary-forms/a4-form_20000101000000000.pdf')).status),404);checks++;
    await page.waitForFunction(()=>document.querySelector('[data-disciplinary-pdf]').classList.contains('nxpen-a4-form-layer'));
    assert.match(await page.locator('[role=status]').textContent(),/الأولى من 2/);checks++;
    assert.equal(await page.locator('canvas').isVisible(),true);checks++;
    const sample=await page.locator('canvas').evaluate(c=>[...c.getContext('2d').getImageData(10,10,1,1).data]);
    assert(sample[2]>sample[0] && sample[3]===255);checks++;
    assert.equal(await page.locator('canvas').evaluate(c=>getComputedStyle(c).position),'absolute');checks++;
    assert.equal(await page.locator('canvas').evaluate(c=>getComputedStyle(c).objectFit),'contain');checks++;
    assert.equal(await page.locator('.nxpen-text-block').evaluate(n=>getComputedStyle(n).pointerEvents),'auto');checks++;
    const box=await page.locator('.nxpen-text-block').boundingBox();
    await page.mouse.move(box.x+20,box.y+10);await page.mouse.down();await page.mouse.move(box.x+50,box.y+40,{steps:4});await page.mouse.up();
    assert.notEqual(await page.locator('[name=xPercent]').inputValue(),'8');checks++;
    await page.evaluate(()=>window.ZynoraDisciplinaryPdf.init(document));
    assert.equal(requests,1);checks++;
    await page.screenshot({path:path.join(root,'docs/verification/disciplinary-pdf-preview-20261008.png')});
    // Same re-entrant initialization used after workspace AJAX navigation.
    await page.evaluate(async()=>{
      const response=await fetch('/');
      const incoming=new DOMParser().parseFromString(await response.text(),'text/html').querySelector('[data-disciplinary-workspace]');
      const previous=document.querySelector('[data-disciplinary-workspace]');
      window.ZynoraDisciplinaryPdf.dispose(previous);previous.replaceWith(incoming);
      window.ZynoraDisciplinary.init(incoming);window.ZynoraDisciplinaryPdf.init(incoming);
    });
    await page.waitForFunction(()=>document.querySelector('canvas').classList.contains('nxpen-a4-form-layer'));
    assert.equal(requests,2);checks++;
    assert.match(await page.locator('[role=status]').textContent(),/الأولى من 2/);checks++;
    for (const kind of ['invalid','large','missing','access']) {
      await page.goto(origin+'/?case='+kind);
      await page.waitForFunction(()=>/تعذر|أكبر|غير متوفرة/.test(document.querySelector('[role=status]').textContent));
      assert.equal(await page.locator('canvas').isVisible(),false);checks++;
      assert.equal(await page.locator('.nxpen-text-block').isVisible(),true);checks++;
    }
    await page.goto(origin);
    await page.evaluate(()=>{window.ZynoraDisciplinaryPdf.dispose(document);document.querySelector('canvas').remove();});
    assert.equal(await page.locator('canvas').count(),0);checks++;
    assert.deepEqual(errors,[]);checks++;
    assert.equal(external,0);checks++;
    assert(fs.readFileSync(path.join(web,'js/zynora-disciplinary-workspace.js'),'utf8').includes("'/js/zynora-disciplinary-pdf.js'"));checks++;
    console.log(`Disciplinary PDF preview: ${checks} checks passed; first-page canvas, drag, CSP, errors, disposal, local-only.`);
  } finally {await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;});
