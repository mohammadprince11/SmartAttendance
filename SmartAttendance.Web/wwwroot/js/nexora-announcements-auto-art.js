
(function(){
'use strict';
function ready(fn){if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',fn);}else{fn();}}
function normalize(v){return (v||'').toString().replace(/\s+/g,' ').trim().toLowerCase();}
ready(function(){
  var root=document.querySelector('[data-nxr-ann-page]'); if(!root) return;
  var composer=root.querySelector('[data-ann-composer]');
  var openBtn=root.querySelector('[data-ann-open-composer]');
  var closeBtn=root.querySelector('[data-ann-close-composer]');
  var resetBtn=root.querySelector('[data-ann-reset-form]');
  var templateButtons=[].slice.call(root.querySelectorAll('[data-ann-template]'));
  var form=root.querySelector('[data-ann-form]');
  var typeInput=root.querySelector('[data-ann-type]');
  var titleInput=root.querySelector('[data-ann-title]');
  var descInput=root.querySelector('[data-ann-desc]');
  var audienceInput=root.querySelector('[data-ann-audience]');
  var categoryInput=root.querySelector('[data-ann-category-create]');
  var personInput=root.querySelector('[data-ann-person-name]');
  var personRow=root.querySelector('[data-ann-person-row]');
  var pType=root.querySelector('[data-preview-type]');
  var pTitle=root.querySelector('[data-preview-title]');
  var pDesc=root.querySelector('[data-preview-desc]');
  var pAudience=root.querySelector('[data-preview-audience]');
  var pPerson=root.querySelector('[data-preview-person]');
  var pCard=root.querySelector('[data-preview-card]');
  var pAuto=root.querySelector('[data-preview-auto-art]');
  var search=root.querySelector('[data-ann-search]');
  var filterStatus=root.querySelector('[data-ann-status]');
  var filterChannel=root.querySelector('[data-ann-channel]');
  var filterCategory=root.querySelector('[data-ann-category-filter]');
  var rows=[].slice.call(root.querySelectorAll('[data-ann-row]'));
  var counter=root.querySelector('[data-ann-counter]');
  var emptyRow=root.querySelector('[data-ann-empty]');

  var presets={
    custom:{label:'إعلان فارغ',theme:'theme-custom',title:'عنوان الإعلان',desc:'اكتب هنا وصف الإعلان أو الرسالة التي تريد نشرها للموظفين. يتم توليد صورة تصميمية تلقائياً حسب الحالة المختارة.',category:'عام',icon:'✦',kicker:'NEXORA HR',artTitle:'إعلان داخلي',artText:'تصميم تلقائي قابل للنشر على حائط الموظف'},
    death:{label:'حالة وفاة',theme:'theme-death',title:'تعزية ومواساة',desc:'بقلوب مؤمنة بقضاء الله وقدره، نتقدم بأحر التعازي والمواساة. نسأل الله الرحمة والمغفرة وأن يلهم الأهل وذويهم الصبر والسلوان.',category:'تعزية',icon:'◼',kicker:'رسالة تعزية',artTitle:'إنا لله وإنا إليه راجعون',artText:'تصميم احترام ومواساة مناسب لهذه المناسبة'},
    wedding:{label:'زفاف',theme:'theme-wedding',title:'تهنئة بمناسبة الزواج',desc:'نتقدم بأجمل التهاني والتبريكات بهذه المناسبة السعيدة، مع أطيب الأمنيات بحياة مليئة بالفرح والتوفيق.',category:'تهنئة',icon:'❤',kicker:'مناسبة سعيدة',artTitle:'ألف مبارك',artText:'تصميم احتفالي أنيق لتهنئة الزواج'},
    holiday:{label:'عطلة',theme:'theme-holiday',title:'إعلان عطلة رسمية',desc:'يرجى التفضل بالعلم بوجود عطلة رسمية وفق توجيهات الإدارة، وسيتم استئناف الدوام حسب الجدول المعتمد بعد انتهاء العطلة.',category:'تعليمات',icon:'☼',kicker:'تنويه إداري',artTitle:'عطلة رسمية',artText:'تصميم تنبيهي واضح لعرض العطل والتعليمات'},
    birth:{label:'ولادة طفل',theme:'theme-birth',title:'مباركة بمولود جديد',desc:'نبارك بهذه المناسبة السعيدة، ونسأل الله أن يجعله من مواليد السعادة والصحة وأن يقر به أعين والديه.',category:'تهنئة',icon:'★',kicker:'مناسبة عائلية',artTitle:'مبارك المولود',artText:'تصميم لطيف واحتفالي بمناسبة المولود الجديد'}
  };

  function openComposer(){ if(composer) composer.classList.add('open'); }
  function closeComposer(){ if(composer) composer.classList.remove('open'); }

  function setTemplate(type){
    var preset=presets[type]||presets.custom;
    if(typeInput) typeInput.value=type;
    templateButtons.forEach(function(btn){btn.classList.toggle('active', btn.getAttribute('data-ann-template')===type);});
    if(categoryInput && (!categoryInput.value || categoryInput.dataset.autoFill==='1')){categoryInput.value=preset.category; categoryInput.dataset.autoFill='1';}
    if(titleInput && (!titleInput.value || titleInput.dataset.autoFill==='1')){titleInput.value=preset.title; titleInput.dataset.autoFill='1';}
    if(descInput && (!descInput.value || descInput.dataset.autoFill==='1')){descInput.value=preset.desc; descInput.dataset.autoFill='1';}
    var needsPerson=['death','wedding','birth'].indexOf(type)>=0;
    if(personRow) personRow.classList.toggle('show', needsPerson);
    updatePreview();
  }

  function autoArtHtml(type, person){
    var preset=presets[type]||presets.custom;
    var nameBlock='';
    if(person && ['death','wedding','birth'].indexOf(type)>=0){
      nameBlock='<div class="nxr-ann-auto-art-sub">الاسم: '+escapeHtml(person)+'</div>';
    }
    return '<div class="nxr-ann-auto-art-figure"><div class="nxr-ann-auto-art-icon">'+preset.icon+'</div></div>'+
      '<div class="nxr-ann-auto-art-copy">'+
      '<div class="nxr-ann-auto-art-kicker">'+preset.kicker+'</div>'+
      '<div class="nxr-ann-auto-art-head">'+preset.artTitle+'</div>'+
      nameBlock+
      '<div class="nxr-ann-auto-art-sub">'+preset.artText+'</div>'+
      '</div>';
  }

  function escapeHtml(text){
    return (text||'').replace(/[&<>"']/g, function(m){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m];});
  }

  function updatePreview(){
    var type=typeInput?typeInput.value:'custom';
    var preset=presets[type]||presets.custom;
    var title=titleInput&&titleInput.value?titleInput.value:preset.title;
    var desc=descInput&&descInput.value?descInput.value:preset.desc;
    var audience=audienceInput&&audienceInput.value?audienceInput.value:'جميع الموظفين';
    var person=personInput?personInput.value.trim():'';
    if(pCard) pCard.className='nxr-ann-preview-card '+preset.theme;
    if(pType) pType.textContent=preset.label;
    if(pTitle) pTitle.textContent=title;
    if(pDesc) pDesc.textContent=desc;
    if(pAudience) pAudience.textContent='موجّه إلى: '+audience;
    if(pPerson){
      if(person && ['death','wedding','birth'].indexOf(type)>=0){ pPerson.textContent='اسم الشخص: '+person; pPerson.classList.add('show'); }
      else { pPerson.textContent=''; pPerson.classList.remove('show'); }
    }
    if(pAuto) pAuto.innerHTML=autoArtHtml(type, person);
  }

  function resetForm(){ if(form) form.reset(); if(titleInput) titleInput.dataset.autoFill='1'; if(descInput) descInput.dataset.autoFill='1'; if(categoryInput) categoryInput.dataset.autoFill='1'; setTemplate('custom'); }
  if(openBtn) openBtn.addEventListener('click', function(){ openComposer(); setTimeout(function(){ if(titleInput) titleInput.focus(); }, 40); });
  if(closeBtn) closeBtn.addEventListener('click', closeComposer);
  if(resetBtn) resetBtn.addEventListener('click', resetForm);
  templateButtons.forEach(function(btn){ btn.addEventListener('click', function(){ openComposer(); setTemplate(btn.getAttribute('data-ann-template')); }); });
  [titleInput,descInput,audienceInput,categoryInput,personInput].forEach(function(el){ if(!el) return; el.addEventListener('input', updatePreview); el.addEventListener('change', updatePreview); });
  if(typeInput){ typeInput.addEventListener('change', function(){ setTemplate(typeInput.value||'custom'); }); }
  if(form){ form.addEventListener('submit', function(ev){ ev.preventDefault(); alert('تم تجهيز نموذج الإعلان مع صورة تصميمية تلقائية حسب المناسبة. في المرحلة التالية يمكن ربط الحفظ الفعلي بقاعدة البيانات.');}); }

  function applyFilter(){
    var q=normalize(search?search.value:'');
    var s=normalize(filterStatus?filterStatus.value:'');
    var c=normalize(filterChannel?filterChannel.value:'');
    var cat=normalize(filterCategory?filterCategory.value:'');
    var shown=0;
    rows.forEach(function(row){
      var text=normalize(row.innerText||row.textContent||'');
      var rs=normalize(row.getAttribute('data-status'));
      var rc=normalize(row.getAttribute('data-channel'));
      var rcat=normalize(row.getAttribute('data-category'));
      var ok=true;
      if(q && text.indexOf(q)<0) ok=false;
      if(s && rs!==s) ok=false;
      if(c && rc!==c) ok=false;
      if(cat && rcat!==cat) ok=false;
      row.style.display=ok?'':'none';
      if(ok) shown++;
    });
    if(counter) counter.textContent='عرض '+shown+' من '+rows.length;
    if(emptyRow) emptyRow.classList.toggle('show', shown===0);
  }
  [search,filterStatus,filterChannel,filterCategory].forEach(function(el){ if(!el) return; el.addEventListener('input', applyFilter); el.addEventListener('change', applyFilter); });
  resetForm(); updatePreview(); applyFilter();
});
})();
