
(function(){
'use strict';
function ready(fn){if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',fn);}else{fn();}}
function id(){return 'REQ-' + Math.floor(Math.random()*90000+10000);}
function now(){var d=new Date();return d.toLocaleDateString('ar-IQ') + ' ' + d.toLocaleTimeString('ar-IQ',{hour:'2-digit',minute:'2-digit'});}
function esc(v){return (v||'').toString().replace(/[&<>"']/g,function(m){return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m];});}
ready(function(){
 const app=document.querySelector('[data-nxep-app]');
 const tabs=[...document.querySelectorAll('[data-portal-tab]')];
 const panes=[...document.querySelectorAll('[data-portal-pane]')];
 function activate(name){tabs.forEach(t=>t.classList.toggle('active',t.dataset.portalTab===name));panes.forEach(p=>p.classList.toggle('active',p.dataset.portalPane===name));localStorage.setItem('NEXORA.EmployeePortal.Tab',name);}
 tabs.forEach(t=>t.addEventListener('click',()=>activate(t.dataset.portalTab)));
 document.querySelectorAll('[data-jump-tab]').forEach(b=>b.addEventListener('click',()=>activate(b.dataset.jumpTab)));
 const saved=localStorage.getItem('NEXORA.EmployeePortal.Tab'); if(saved && panes.some(p=>p.dataset.portalPane===saved)) activate(saved);
 const toggle=document.querySelector('[data-portal-toggle]'); if(toggle&&app) toggle.addEventListener('click',()=>app.classList.toggle('sidebar-open'));

 const defaultRequests=[
  {ref:'OT26-16775',type:'العمل الإضافي',name:'طلب ساعات العمل الإضافية الاعتيادية',date:'30/05/2026 10:04 ص',status:'موافق عليه'},
  {ref:'PR26-2892',type:'مغادرة',name:'مغادرة شخصية',date:'20/05/2026 12:26 م',status:'موافق عليه'},
  {ref:'LV26-9790',type:'إجازة',name:'إجازة سنوية',date:'17/05/2026 02:25 م',status:'موافق عليه'},
  {ref:'OT26-15242',type:'العمل الإضافي',name:'طلب ساعات العمل الإضافية الاعتيادية',date:'13/05/2026 11:57 م',status:'مرفوض'},
  {ref:'OT26-15241',type:'العمل الإضافي',name:'طلب الوقت الإضافي بأيام العطل',date:'13/05/2026 11:56 م',status:'مرفوض'}
 ];
 let requests=JSON.parse(localStorage.getItem('NEXORA.EmployeePortal.Requests')||'null')||defaultRequests;
 const tbody=document.querySelector('[data-requests-table] tbody');
 const search=document.querySelector('[data-request-search]');
 function statusClass(s){if(s==='موافق عليه')return 'ok'; if(s==='مرفوض')return 'no'; return 'wait';}
 function renderRequests(){if(!tbody)return; const q=(search?.value||'').trim().toLowerCase(); const rows=requests.filter(r=>(r.ref+' '+r.type+' '+r.name+' '+r.status).toLowerCase().includes(q)); tbody.innerHTML=rows.map(r=>'<tr><td>'+esc(r.ref)+'</td><td>'+esc(r.type)+'</td><td>'+esc(r.name)+'</td><td>'+esc(r.date)+'</td><td><span class="nxep-status '+statusClass(r.status)+'">'+esc(r.status)+'</span></td></tr>').join('') || '<tr><td colspan="5" style="text-align:center;height:120px;color:#8fa0b3">لا توجد طلبات</td></tr>';}
 search?.addEventListener('input',renderRequests); renderRequests();
 const modal=document.querySelector('[data-request-modal]'); const backdrop=document.querySelector('[data-request-backdrop]'); const title=document.querySelector('[data-request-modal-title]'); const typeInput=document.querySelector('[data-new-request-type]'); const descInput=document.querySelector('[data-new-request-desc]');
 function openRequest(type){if(!modal||!backdrop)return; title.textContent=type; typeInput.value=type; descInput.value=''; modal.classList.add('open'); backdrop.classList.add('open');}
 function closeRequest(){modal?.classList.remove('open'); backdrop?.classList.remove('open');}
 document.querySelectorAll('[data-open-request]').forEach(b=>b.addEventListener('click',()=>{activate('requests');openRequest(b.dataset.openRequest);}));
 document.querySelectorAll('[data-close-request]').forEach(b=>b.addEventListener('click',closeRequest)); backdrop?.addEventListener('click',closeRequest);
 document.querySelector('[data-save-request]')?.addEventListener('click',()=>{const type=typeInput.value||'طلب'; const req={ref:id(),type:type.includes('إجازة')?'إجازة':type.includes('مغادرة')?'مغادرة':'العمل الإضافي',name:type,date:now(),status:'معلق'}; requests.unshift(req); localStorage.setItem('NEXORA.EmployeePortal.Requests',JSON.stringify(requests)); renderRequests(); closeRequest();});
});
})();
