// Offline synthetic upload/redirect only. No app server, real files or database.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(file==='wwwroot/js/zynora-disciplinary-workspace.js' && process.env.DISCIPLINARY_WORKSPACE_SCRIPT
 ? process.env.DISCIPLINARY_WORKSPACE_SCRIPT : path.join(root, 'SmartAttendance.Web', file), 'utf8');
const assets = ['zynora-disciplinary-rules.css','zynora-a4-custom-file-picker-v13-1.css','zynora-disciplinary-designer-preview-safe-v1.css','pages/index-387bf545e8.css'];
const publicAsset = asset => process.env.DISCIPLINARY_FINGERPRINTED==='true' ? asset.replace(/\.(css|js)$/,'.a1b2c3d4e5.$1') : asset;
const publishNames = html => html.replace(/(href|src)="(\/(?:css|js)\/[^"?]+\.(?:css|js))"/g,(_,attr,url)=>`${attr}="${publicAsset(url)}"`);
if (!process.env.DISCIPLINARY_WORKSPACE_SCRIPT) {
 const vm=require('node:vm'), context=vm.createContext({location:{origin:'https://disciplinary.test'}});
 vm.runInContext(read('wwwroot/js/zynora-disciplinary-workspace.js').match(/function allowedAsset\([\s\S]*?\n    }/)[0],context);
 const allowed=new Set(['/js/zynora-disciplinary-tools.js']);
 assert(context.allowedAsset(new URL('https://disciplinary.test/js/zynora-disciplinary-tools.a1b2c3d4e5.js'),allowed));
 assert(context.allowedAsset(new URL('https://disciplinary.test/js/zynora-disciplinary-tools.js'),allowed));
 assert(!context.allowedAsset(new URL('https://other.test/js/zynora-disciplinary-tools.a1b2c3d4e5.js'),allowed));
 assert(!context.allowedAsset(new URL('https://disciplinary.test/js/unlisted.a1b2c3d4e5.js'),allowed));
 assert(!context.allowedAsset(new URL('https://disciplinary.test/other/zynora-disciplinary-tools.a1b2c3d4e5.js'),allowed));
}
const cssTags = tab => (tab==='designer'?assets:['pages/index-387bf545e8.css']).map(asset=>`<link rel="stylesheet" data-disciplinary-style href="/css/${asset}">`).join('')+'<meta data-disciplinary-style-anchor>';
const body = tab => `<section class="zyw zy-disciplinary-workspace" data-disciplinary-workspace><p data-disciplinary-status hidden></p><nav><a class="zyw-tab" href="/DisciplinaryRules?tab=library" aria-selected="${tab==='library'}">library</a><a class="zyw-tab" href="/DisciplinaryRules?tab=designer" aria-selected="${tab==='designer'}">designer</a></nav><main>${tab==='designer'?`<section class="zy-disciplinary-designer" data-disciplinary-designer><button data-designer-tab="background">settings</button><button data-designer-tab="text">text</button><div class="nxpen-designer-grid"><section class="nxpen-designer-panel" data-designer-panel="background"><form method="post" action="/DisciplinaryRules?handler=SaveA4Form" enctype="multipart/form-data"><input type="hidden" name="__RequestVerificationToken" value="synthetic"><input type="file" name="a4FormFile"><button id="save">save A4</button></form></section><section class="nxpen-live-preview"><p class="zy-disciplinary-pdf-status">PDF status</p><div class="nxpen-a4-paper"><div class="nxpen-a4-section nxpen-a4-body"><div class="nxpen-text-block" data-layer-id="1" style="top:10%;right:8%">synthetic text</div></div></div></section></div></section>`:'<p>library</p>'}</main><script data-disciplinary-script data-disciplinary-once src="/js/zynora-disciplinary-tools.js"></script>${tab==='designer'?'<script data-disciplinary-script src="/js/zynora-a4-custom-file-picker-v13-1.js" defer></script>':''}</section>`;
const full = tab => `<html lang="ar" dir="rtl"><head><style>:root{--zy-surface:#172232;--zy-text:#eef;--zy-text-muted:#abc;--zy-border:#345;--zy-primary-soft:#234;--zy-space-2:8px;--zy-space-3:16px;--zy-space-4:24px;--zy-radius-lg:16px}</style>${cssTags(tab)}<style id="shell-after">.shell{color:blue}</style></head><body><aside class="shell">shell</aside>${body(tab)}<script src="/js/zynora-disciplinary-workspace.js" defer></script></body></html>`;
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage({viewport:{width:1440,height:1000}}); let posts=0, headers;
  await page.route('https://disciplinary.test/**',async route=>{
   const request=route.request(), url=new URL(request.url());
   const originalPath=url.pathname.replace(/\.[a-z0-9]{10}\.(css|js)$/i,'.$1');
   if(url.pathname.startsWith('/css/')) { await new Promise(resolve=>setTimeout(resolve,80)); return route.fulfill({contentType:'text/css',body:read('wwwroot'+originalPath)}); }
   if(url.pathname.startsWith('/js/')) return route.fulfill({contentType:'application/javascript',body:read('wwwroot'+originalPath)});
   if(request.method()==='POST') { posts++; assert(request.postData().includes('synthetic')); return route.fulfill({status:303,headers:{location:'/DisciplinaryRules?tab=designer'}}); }
   headers=request.headers(); const tab=url.searchParams.get('tab')||'library';
   return route.fulfill({contentType:'text/html',body:publishNames(headers['x-zynora-workspace']?cssTags(tab)+body(tab):full(tab))});
  });
  await page.goto('https://disciplinary.test/DisciplinaryRules?tab=library');
  await page.evaluate(()=>window.shellSentinel=42);
  await page.click('a[href$="=designer"]');
  await page.waitForFunction(()=>document.querySelector('.nxr-a4-file-picker')&&!document.querySelector('[aria-busy=true]'),null,{timeout:5000});
  assert.equal(await page.evaluate(()=>window.shellSentinel),42);
  assert.equal(headers['x-zynora-workspace'],'disciplinary');
  const snapshot=()=>page.evaluate(()=>({
   styles:[...document.querySelectorAll('link[data-disciplinary-style]')].map(node=>new URL(node.href).pathname),
   paper:{display:getComputedStyle(document.querySelector('.nxpen-a4-paper')).display,width:document.querySelector('.nxpen-a4-paper').getBoundingClientRect().width},
   lastBeforeAnchor:document.querySelector('[data-disciplinary-style-anchor]').previousElementSibling.href,
   shellAfter:!!(document.querySelector('[data-disciplinary-style-anchor]').compareDocumentPosition(document.querySelector('#shell-after'))&Node.DOCUMENT_POSITION_FOLLOWING),
   picker:document.querySelectorAll('.nxr-a4-file-picker').length
  }));
  const before=await snapshot();
  assert.deepEqual(before.styles,assets.map(asset=>publicAsset('/css/'+asset))); assert.equal(before.paper.display,'grid'); assert(before.paper.width>300);
  assert(before.lastBeforeAnchor.endsWith(publicAsset('/css/pages/index-387bf545e8.css'))); assert(before.shellAfter); assert.equal(before.picker,1);
  await page.locator('input[type=file]').setInputFiles({name:'synthetic.pdf',mimeType:'application/pdf',buffer:Buffer.from('%PDF-1.4 synthetic')});
  await Promise.all([page.waitForURL('**/*tab=designer'),page.click('#save')]);
  await page.waitForFunction(()=>document.querySelector('.nxr-a4-file-picker')&&window.ZynoraDisciplinary);
  const after=await snapshot(); assert.deepEqual(after,before); assert.equal(posts,1); assert.equal(await page.evaluate(()=>window.shellSentinel),undefined);
  const boxes=await page.evaluate(()=>({note:document.querySelector('.zy-disciplinary-pdf-status').getBoundingClientRect().toJSON(),paper:document.querySelector('.nxpen-a4-paper').getBoundingClientRect().toJSON()}));
  assert(boxes.note.bottom<=boxes.paper.top); assert(!read('Pages/DisciplinaryRules/Index.cshtml').includes('class="nxpen-a4-pdf-note"'));
  assert(read('Pages/DisciplinaryRules/Index.cshtml.cs').includes('تم حفظ فورمة A4 بنجاح.'));
  console.log('Disciplinary save design: 15 checks passed; AJAX and native POST redirect have matching styles and picker (offline only).');
 } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
