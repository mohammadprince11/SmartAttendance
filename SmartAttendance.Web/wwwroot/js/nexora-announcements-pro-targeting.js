
(function(){
'use strict';
function ready(fn){if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',fn);}else{fn();}}
function norm(v){return (v||'').toString().replace(/\s+/g,' ').trim().toLowerCase();}
function esc(t){return (t||'').replace(/[&<>"']/g,function(m){return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m];});}
ready(function(){
  var root=document.querySelector('[data-nxra-page]'); if(!root) return;
  var openBtn=root.querySelector('[data-nxra-open]');
  var closeBtn=root.querySelector('[data-nxra-close]');
  var resetBtn=root.querySelector('[data-nxra-reset]');
  var composer=root.querySelector('[data-nxra-composer]');
  var form=root.querySelector('[data-nxra-form]');
  var templateBtns=[].slice.call(root.querySelectorAll('[data-nxra-template]'));
  var targetBtns=[].slice.call(root.querySelectorAll('[data-target-type]'));
  var typeInput=root.querySelector('[data-nxra-type]');
  var titleInput=root.querySelector('[data-nxra-title]');
  var descInput=root.querySelector('[data-nxra-desc]');
  var catInput=root.querySelector('[data-nxra-category]');
  var personInput=root.querySelector('[data-nxra-person]');
  var personRow=root.querySelector('[data-nxra-person-row]');
  var hiddenTarget=root.querySelector('[data-nxra-target-type]');
  var deptWrap=root.querySelector('[data-target-wrap="department"]');
  var branchWrap=root.querySelector('[data-target-wrap="branch"]');
  var empWrap=root.querySelector('[data-target-wrap="employees"]');
  var mgmtWrap=root.querySelector('[data-target-wrap="management"]');
  var deptSel=root.querySelector('[data-target-department]');
  var branchSel=root.querySelector('[data-target-branch]');
  var empTxt=root.querySelector('[data-target-employees]');
  var mgmtSel=root.querySelector('[data-target-management]');
  var previewCard=root.querySelector('[data-preview-card]');
  var previewType=root.querySelector('[data-preview-type]');
  var previewTitle=root.querySelector('[data-preview-title]');
  var previewDesc=root.querySelector('[data-preview-desc]');
  var previewPerson=root.querySelector('[data-preview-person]');
  var previewArt=root.querySelector('[data-preview-art]');
  var previewAudience=root.querySelector('[data-preview-audience]');
  var previewCat=root.querySelector('[data-preview-category]');
  var targetSummary=root.querySelector('[data-target-summary]');
  var search=root.querySelector('[data-nxra-search]');
  var statusFilter=root.querySelector('[data-nxra-status]');
  var methodFilter=root.querySelector('[data-nxra-method]');
  var categoryFilter=root.querySelector('[data-nxra-category-filter]');
  var rows=[].slice.call(root.querySelectorAll('[data-nxra-row]'));
  var countLabel=root.querySelector('[data-nxra-count]');
  var emptyRow=root.querySelector('[data-nxra-empty]');

  var presets={
    custom:{label:'إعلان فارغ',theme:'theme-custom',emoji:'✦',title:'عنوان الإعلان',desc:'اكتب هنا وصف الإعلان أو الرسالة التي تريد نشرها للموظفين، وسيتم تجهيز التصميم تلقائياً داخل المعاينة.',category:'عام',artKicker:'NEXORA HR',artHead:'إعلان داخلي',artSub:'تصميم إداري مرن قابل للتخصيص حسب الرسالة'},
    death:{label:'وفاة',theme:'theme-death',emoji:'◼',title:'تعزية ومواساة',desc:'بقلوب مؤمنة بقضاء الله وقدره، نتقدم بأحر التعازي والمواساة. نسأل الله الرحمة والمغفرة وأن يلهم الأهل وذويهم الصبر والسلوان.',category:'تعزية',artKicker:'رسالة تعزية',artHead:'إنا لله وإنا إليه راجعون',artSub:'تصميم احترام ومواساة مناسب لمثل هذه المناسبة'},
    wedding:{label:'زفاف',theme:'theme-wedding',emoji:'❤',title:'تهنئة بمناسبة الزواج',desc:'نتقدم بأجمل التهاني والتبريكات بهذه المناسبة السعيدة، مع أطيب الأمنيات بحياة مليئة بالفرح والتوفيق.',category:'تهنئة',artKicker:'مناسبة سعيدة',artHead:'ألف مبارك',artSub:'تصميم احتفالي أنيق لتهنئة الزواج'},
    holiday:{label:'عطلة',theme:'theme-holiday',emoji:'☼',title:'إعلان عطلة رسمية',desc:'يرجى التفضل بالعلم بوجود عطلة رسمية وفق توجيهات الإدارة، وسيتم استئناف الدوام حسب الجدول المعتمد بعد انتهاء العطلة.',category:'تعليمات',artKicker:'تنويه إداري',artHead:'عطلة رسمية',artSub:'تصميم واضح لإيصال تعليمات العطل والدوام'},
    birth:{label:'ولادة طفل',theme:'theme-birth',emoji:'★',title:'مباركة بمولود جديد',desc:'نبارك بهذه المناسبة السعيدة، ونسأل الله أن يجعله من مواليد السعادة والصحة وأن يقر به أعين والديه.',category:'تهنئة',artKicker:'مناسبة عائلية',artHead:'مبارك المولود',artSub:'تصميم لطيف واحتفالي بمناسبة المولود الجديد'}
  };

  function openComposer(){ composer && composer.classList.add('open'); }
  function closeComposer(){ composer && composer.classList.remove('open'); }
  if(openBtn) openBtn.addEventListener('click', function(){ openComposer(); });
  if(closeBtn) closeBtn.addEventListener('click', function(){ closeComposer(); });

  function applyTemplate(type){
    var p=presets[type]||presets.custom;
    if(typeInput) typeInput.value=type;
    templateBtns.forEach(function(btn){ btn.classList.toggle('active', btn.getAttribute('data-nxra-template')===type); });
    if(titleInput && (!titleInput.value || titleInput.dataset.autoFill==='1')){ titleInput.value=p.title; titleInput.dataset.autoFill='1'; }
    if(descInput && (!descInput.value || descInput.dataset.autoFill==='1')){ descInput.value=p.desc; descInput.dataset.autoFill='1'; }
    if(catInput && (!catInput.value || catInput.dataset.autoFill==='1')){ catInput.value=p.category; catInput.dataset.autoFill='1'; }
    var needsPerson=['death','wedding','birth'].indexOf(type)>=0;
    if(personRow) personRow.classList.toggle('show', needsPerson);
    renderPreview();
  }

  function targetLabel(){
    var t=hiddenTarget?hiddenTarget.value:'all';
    if(t==='all') return 'جميع الموظفين';
    if(t==='department') return 'قسم: ' + (deptSel && deptSel.value ? deptSel.value : 'غير محدد');
    if(t==='branch') return 'فرع: ' + (branchSel && branchSel.value ? branchSel.value : 'غير محدد');
    if(t==='employees') return 'موظفون محددون: ' + (empTxt && empTxt.value.trim() ? empTxt.value.trim() : 'لم يتم إدخال أسماء');
    if(t==='management') return 'الإدارة فقط: ' + (mgmtSel && mgmtSel.value ? mgmtSel.value : 'غير محدد');
    return 'جميع الموظفين';
  }

  function setTarget(type){
    if(hiddenTarget) hiddenTarget.value=type;
    targetBtns.forEach(function(btn){ btn.classList.toggle('active', btn.getAttribute('data-target-type')===type); });
    [deptWrap,branchWrap,empWrap,mgmtWrap].forEach(function(el){ if(el) el.classList.remove('show'); });
    if(type==='department' && deptWrap) deptWrap.classList.add('show');
    if(type==='branch' && branchWrap) branchWrap.classList.add('show');
    if(type==='employees' && empWrap) empWrap.classList.add('show');
    if(type==='management' && mgmtWrap) mgmtWrap.classList.add('show');
    if(targetSummary) targetSummary.textContent='توجيه الإعلان: ' + targetLabel();
    renderPreview();
  }

  function artHtml(type, person){
    var p=presets[type]||presets.custom;
    return '<div class="nxra-auto-art-figure"><div class="nxra-auto-art-icon">'+p.emoji+'</div></div>'+
           '<div class="nxra-auto-art-copy"><div class="nxra-auto-art-kicker">'+p.artKicker+'</div>'+
           '<div class="nxra-auto-art-head">'+p.artHead+'</div>'+
           (person && ['death','wedding','birth'].indexOf(type)>=0 ? '<div class="nxra-auto-art-sub">الاسم: '+esc(person)+'</div>' : '')+
           '<div class="nxra-auto-art-sub">'+p.artSub+'</div></div>';
  }

  function renderPreview(){
    var type=typeInput ? typeInput.value : 'custom';
    var p=presets[type]||presets.custom;
    var title=titleInput && titleInput.value ? titleInput.value : p.title;
    var desc=descInput && descInput.value ? descInput.value : p.desc;
    var person=personInput ? personInput.value.trim() : '';
    var cat=catInput && catInput.value ? catInput.value : p.category;
    if(previewCard) previewCard.className='nxra-preview-card '+p.theme;
    if(previewType) previewType.textContent=p.label;
    if(previewTitle) previewTitle.textContent=title;
    if(previewDesc) previewDesc.textContent=desc;
    if(previewCat) previewCat.textContent='الفئة: ' + cat;
    if(previewAudience) previewAudience.textContent=targetLabel();
    if(targetSummary) targetSummary.textContent='توجيه الإعلان: ' + targetLabel();
    if(previewPerson){
      if(person && ['death','wedding','birth'].indexOf(type)>=0){ previewPerson.textContent='اسم الشخص: ' + person; previewPerson.classList.add('show'); }
      else { previewPerson.textContent=''; previewPerson.classList.remove('show'); }
    }
    if(previewArt) previewArt.innerHTML=artHtml(type, person);
  }

  function resetForm(){ if(form) form.reset(); if(titleInput) titleInput.dataset.autoFill='1'; if(descInput) descInput.dataset.autoFill='1'; if(catInput) catInput.dataset.autoFill='1'; applyTemplate('custom'); setTarget('all'); }
  if(resetBtn) resetBtn.addEventListener('click', resetForm);
  templateBtns.forEach(function(btn){ btn.addEventListener('click', function(){ openComposer(); applyTemplate(btn.getAttribute('data-nxra-template')); }); });
  targetBtns.forEach(function(btn){ btn.addEventListener('click', function(){ setTarget(btn.getAttribute('data-target-type')); }); });
  [titleInput,descInput,catInput,personInput,deptSel,branchSel,empTxt,mgmtSel].forEach(function(el){ if(!el) return; el.addEventListener('input', renderPreview); el.addEventListener('change', renderPreview); });
  if(form){ form.addEventListener('submit', function(ev){ ev.preventDefault(); alert('تم تفعيل واجهة إنشاء الإعلان مع توجيه الإعلان داخل الصفحة. الخطوة التالية يمكن ربط الحفظ الفعلي بقاعدة البيانات وإرسال الإعلان لحائط الموظف.'); }); }

  function filterRows(){
    var q=norm(search?search.value:'');
    var s=norm(statusFilter?statusFilter.value:'');
    var m=norm(methodFilter?methodFilter.value:'');
    var c=norm(categoryFilter?categoryFilter.value:'');
    var shown=0;
    rows.forEach(function(row){
      var txt=norm(row.innerText || row.textContent || '');
      var rs=norm(row.getAttribute('data-status'));
      var rm=norm(row.getAttribute('data-method'));
      var rc=norm(row.getAttribute('data-category'));
      var ok=true;
      if(q && txt.indexOf(q)<0) ok=false;
      if(s && rs!==s) ok=false;
      if(m && rm!==m) ok=false;
      if(c && rc!==c) ok=false;
      row.style.display=ok?'':'none';
      if(ok) shown++;
    });
    if(countLabel) countLabel.textContent='عرض ' + shown + ' من ' + rows.length;
    if(emptyRow) emptyRow.classList.toggle('show', shown===0);
  }
  [search,statusFilter,methodFilter,categoryFilter].forEach(function(el){ if(!el) return; el.addEventListener('input', filterRows); el.addEventListener('change', filterRows); });
  resetForm(); renderPreview(); filterRows();
});
})();
