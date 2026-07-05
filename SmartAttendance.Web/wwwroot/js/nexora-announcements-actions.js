(function(){
'use strict';
function ready(fn){if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',fn);}else{fn();}}
function text(el){return (el?.textContent||'').replace(/\s+/g,' ').trim();}
function make(tag,cls,html){const e=document.createElement(tag);if(cls)e.className=cls;if(html!=null)e.innerHTML=html;return e;}
function findAnnouncementsTable(){const tables=[...document.querySelectorAll('table')];
 return tables.find(t=>text(t.closest('section,div,article')||t).includes('قائمة الإعلانات'))||tables.find(t=>text(t.tHead||t).includes('فئة الإعلان')&&text(t.tHead||t).includes('العنوان'))||null;}
function tableMap(table){const headers=[...table.querySelectorAll('thead th')].map(th=>text(th));
 const idx=(name)=>headers.findIndex(h=>h.includes(name));
 return {category:idx('فئة'), title:idx('العنوان'), sent:idx('تاريخ'), method:idx('طريقة'), status:idx('الحالة'), action:idx('الإجراء')};}
function secondaryLine(cell){const kids=[...cell.childNodes].map(n=>n.textContent||'').join('\n').split(/\n+/).map(s=>s.trim()).filter(Boolean);return kids.length>1?kids.slice(1).join(' • '):'';}
function extractData(row,map){const tds=[...row.querySelectorAll('td')];
 const titleCell=tds[map.title]||null; const catCell=tds[map.category]||null; const sentCell=tds[map.sent]||null; const methodCell=tds[map.method]||null; const statusCell=tds[map.status]||null;
 return {
   title:text(titleCell),
   subtitle: secondaryLine(titleCell),
   category:text(catCell),
   sentDate:text(sentCell),
   method:text(methodCell),
   status:text(statusCell),
   description: secondaryLine(titleCell)||'لا يوجد وصف إضافي حالياً.',
   target: secondaryLine(titleCell)||'سيظهر لاحقاً على حائط الموظف أو ضمن حسابه الشخصي.',
   imageLabel: text(catCell)||'إعلان داخلي'
 };
}
function applyData(row,map,data){const tds=[...row.querySelectorAll('td')];
 if(tds[map.category]) tds[map.category].innerHTML='<span>'+escapeHtml(data.category||'')+'</span>';
 if(tds[map.sent]) tds[map.sent].innerHTML='<span>'+escapeHtml(data.sentDate||'')+'</span>';
 if(tds[map.method]) tds[map.method].innerHTML='<span>'+escapeHtml(data.method||'')+'</span>';
 if(tds[map.status]) tds[map.status].innerHTML='<span>'+escapeHtml(data.status||'')+'</span>';
 if(tds[map.title]){
   const main=escapeHtml(data.title||'');
   const sub=escapeHtml(data.description||data.subtitle||'');
   tds[map.title].innerHTML='<div style="display:grid;gap:4px"><strong>'+main+'</strong><span style="font-size:11px;color:#8fa6bb">'+sub+'</span></div>';
 }
}
function escapeHtml(str){return (str||'').replace(/[&<>"']/g,m=>({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[m]));}
function getCounter(table){return [...document.querySelectorAll('*')].find(el=>/عرض \d+ من \d+/.test(text(el)) && (el.closest('section,div,article')?.contains(table)||true));}
function updateCounter(table){const counter=getCounter(table); if(counter){const count=table.querySelectorAll('tbody tr').length; counter.textContent='عرض '+count+' من '+count;}}
function showToast(message){let toast=document.querySelector('.nxra-ann-toast'); if(!toast){toast=make('div','nxra-ann-toast'); document.body.appendChild(toast);} toast.textContent=message; toast.classList.add('show'); clearTimeout(showToast._t); showToast._t=setTimeout(()=>toast.classList.remove('show'),2200);}
function buildModal(){let backdrop=make('div','nxra-ann-modal-backdrop'); let modal=make('div','nxra-ann-modal');
 modal.innerHTML='\
 <div class="nxra-ann-modal-header">\
  <div><div class="nxra-ann-modal-title">تفاصيل الإعلان</div><div class="nxra-ann-modal-sub">عرض وتعديل أو حذف الإعلان المحدد</div></div>\
  <button type="button" class="nxra-ann-close">✕</button>\
 </div>\
 <div class="nxra-ann-modal-body">\
  <div class="nxra-ann-preview">\
   <div class="nxra-ann-image" data-field="imagePreview">صورة الإعلان\nحسب المناسبة</div>\
   <div class="nxra-ann-meta">\
    <div class="nxra-ann-badge-row">\
      <span class="nxra-ann-badge" data-view="category"></span>\
      <span class="nxra-ann-badge" data-view="status"></span>\
      <span class="nxra-ann-badge" data-view="method"></span>\
    </div>\
    <div class="nxra-ann-desc" data-view="description"></div>\
    <div class="nxra-ann-desc" data-view="target"></div>\
   </div>\
  </div>\
  <div class="nxra-ann-grid">\
   <div class="nxra-ann-field full"><label class="nxra-ann-label">العنوان</label><input class="nxra-ann-input" data-edit="title"></div>\
   <div class="nxra-ann-field"><label class="nxra-ann-label">فئة الإعلان</label><input class="nxra-ann-input" data-edit="category"></div>\
   <div class="nxra-ann-field"><label class="nxra-ann-label">تاريخ الإرسال</label><input class="nxra-ann-input" data-edit="sentDate"></div>\
   <div class="nxra-ann-field"><label class="nxra-ann-label">طريقة النشر</label><input class="nxra-ann-input" data-edit="method"></div>\
   <div class="nxra-ann-field"><label class="nxra-ann-label">الحالة</label><input class="nxra-ann-input" data-edit="status"></div>\
   <div class="nxra-ann-field full"><label class="nxra-ann-label">الوصف</label><textarea class="nxra-ann-textarea" data-edit="description"></textarea></div>\
   <div class="nxra-ann-field full"><label class="nxra-ann-label">إلى من موجّه الإعلان</label><textarea class="nxra-ann-textarea" data-edit="target"></textarea></div>\
  </div>\
 </div>\
 <div class="nxra-ann-actions">\
   <button type="button" class="nxra-ann-btn" data-action="cancel">إغلاق</button>\
   <button type="button" class="nxra-ann-btn danger" data-action="delete">حذف الإعلان</button>\
   <button type="button" class="nxra-ann-btn primary" data-action="save">حفظ التعديلات</button>\
 </div>';
 document.body.append(backdrop,modal);
 function close(){backdrop.classList.remove('open'); modal.classList.remove('open'); modal.dataset.mode=''; modal._row=null;}
 backdrop.addEventListener('click',close); modal.querySelector('.nxra-ann-close').addEventListener('click',close); modal.querySelector('[data-action="cancel"]').addEventListener('click',close);
 return {backdrop,modal,close};
}
ready(function(){
 const table=findAnnouncementsTable(); if(!table) return;
 const map=tableMap(table); const rows=[...table.querySelectorAll('tbody tr')]; if(!rows.length) return;
 const modalParts=buildModal(); const modal=modalParts.modal;
 function setPreview(data){modal.querySelector('[data-view="category"]').textContent=data.category||'إعلان'; modal.querySelector('[data-view="status"]').textContent=data.status||'حالة'; modal.querySelector('[data-view="method"]').textContent=data.method||'نشر'; modal.querySelector('[data-view="description"]').textContent=data.description||'لا يوجد وصف.'; modal.querySelector('[data-view="target"]').textContent='موجّه إلى: '+(data.target||'عام'); modal.querySelector('[data-field="imagePreview"]').textContent=(data.category||'إعلان')+'\n'+(data.title||'');}
 function open(row,mode){const data=extractData(row,map); modal._row=row; modal.dataset.mode=mode; modal.querySelector('.nxra-ann-modal-title').textContent=mode==='view'?'عرض الإعلان':'تعديل الإعلان'; ['title','category','sentDate','method','status','description','target'].forEach(k=>{ const input=modal.querySelector('[data-edit="'+k+'"]'); if(input) input.value=data[k]||''; input && (input.disabled=(mode==='view'));}); setPreview(data); const saveBtn=modal.querySelector('[data-action="save"]'); saveBtn.style.display=(mode==='view')?'none':'inline-flex'; modalParts.backdrop.classList.add('open'); modal.classList.add('open'); }
 function enhanceRow(row){const actionCell=[...row.querySelectorAll('td')][map.action]||row.lastElementChild; if(!actionCell) return; const existingButtons=[...actionCell.querySelectorAll('button,a')]; let wrap=actionCell.querySelector('.nxra-ann-inline-actions'); if(!wrap){wrap=make('div','nxra-ann-inline-actions'); actionCell.innerHTML=''; actionCell.appendChild(wrap);} else {wrap.innerHTML='';}
 const viewBtn=make('button','nxra-ann-mini','عرض'); const editBtn=make('button','nxra-ann-mini','تعديل'); const deleteBtn=make('button','nxra-ann-mini danger','حذف');
 viewBtn.type=editBtn.type=deleteBtn.type='button';
 viewBtn.addEventListener('click',()=>open(row,'view'));
 editBtn.addEventListener('click',()=>open(row,'edit'));
 deleteBtn.addEventListener('click',()=>{if(confirm('هل أنت متأكد من حذف هذا الإعلان؟')){row.remove(); updateCounter(table); showToast('تم حذف الإعلان بنجاح');}});
 wrap.append(viewBtn,editBtn,deleteBtn);
 }
 [...table.querySelectorAll('tbody tr')].forEach(enhanceRow);
 modal.querySelector('[data-action="save"]').addEventListener('click',function(){ const row=modal._row; if(!row) return; const data={}; ['title','category','sentDate','method','status','description','target'].forEach(k=>data[k]=modal.querySelector('[data-edit="'+k+'"]').value.trim()); applyData(row,map,data); enhanceRow(row); showToast('تم حفظ التعديلات'); modalParts.close();});
 [...modal.querySelectorAll('[data-edit]')].forEach(inp=>inp.addEventListener('input',()=>{ const live={}; ['title','category','sentDate','method','status','description','target'].forEach(k=>{const el=modal.querySelector('[data-edit="'+k+'"]'); live[k]=el?el.value.trim():'';}); setPreview(live);}));
 modal.querySelector('[data-action="delete"]').addEventListener('click',function(){ const row=modal._row; if(!row) return; if(confirm('هل تريد حذف الإعلان نهائياً؟')){row.remove(); updateCounter(table); showToast('تم حذف الإعلان نهائياً'); modalParts.close();}});
});
})();
