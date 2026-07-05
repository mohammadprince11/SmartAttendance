
(function(){
  'use strict';
  function ready(fn){ if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',fn);} else {fn();} }
  function normalize(v){ return (v||'').toString().replace(/\s+/g,' ').trim().toLowerCase(); }
  ready(function(){
    var root = document.querySelector('[data-nxr-ann-page]');
    if(!root) return;

    var composer = root.querySelector('[data-ann-composer]');
    var openBtn = root.querySelector('[data-ann-open-composer]');
    var closeBtn = root.querySelector('[data-ann-close-composer]');
    var resetBtn = root.querySelector('[data-ann-reset-form]');
    var quickTemplateButtons = Array.from(root.querySelectorAll('[data-ann-template]'));
    var form = root.querySelector('[data-ann-form]');

    var typeInput = root.querySelector('[data-ann-type]');
    var titleInput = root.querySelector('[data-ann-title]');
    var descInput = root.querySelector('[data-ann-desc]');
    var audienceInput = root.querySelector('[data-ann-audience]');
    var categoryInput = root.querySelector('[data-ann-category-create]');
    var personInput = root.querySelector('[data-ann-person-name]');
    var imageInput = root.querySelector('[data-ann-image]');
    var personRow = root.querySelector('[data-ann-person-row]');

    var pType = root.querySelector('[data-preview-type]');
    var pTitle = root.querySelector('[data-preview-title]');
    var pDesc = root.querySelector('[data-preview-desc]');
    var pAudience = root.querySelector('[data-preview-audience]');
    var pPerson = root.querySelector('[data-preview-person]');
    var pImage = root.querySelector('[data-preview-image]');
    var pCard = root.querySelector('[data-preview-card]');

    var search = root.querySelector('[data-ann-search]');
    var filterStatus = root.querySelector('[data-ann-status]');
    var filterChannel = root.querySelector('[data-ann-channel]');
    var filterCategory = root.querySelector('[data-ann-category-filter]');
    var rows = Array.from(root.querySelectorAll('[data-ann-row]'));
    var counter = root.querySelector('[data-ann-counter]');
    var emptyRow = root.querySelector('[data-ann-empty]');

    var presetMap = {
      custom: {
        label: 'إعلان فارغ', theme:'theme-custom', title:'عنوان الإعلان', desc:'اكتب هنا وصف الإعلان أو الرسالة التي تريد نشرها للموظفين. يمكنك تخصيص النص والصورة والجمهور المستهدف.', category:'عام'
      },
      death: {
        label: 'حالة وفاة', theme:'theme-death', title:'تعزية ومواساة', desc:'بقلوب مؤمنة بقضاء الله وقدره، نتقدم بأحر التعازي والمواساة. نسأل الله الرحمة والمغفرة وأن يلهم الأهل وذويهم الصبر والسلوان.', category:'تعزية'
      },
      wedding: {
        label: 'زفاف', theme:'theme-wedding', title:'تهنئة بمناسبة الزواج', desc:'نتقدم بأجمل التهاني والتبريكات بهذه المناسبة السعيدة، مع أطيب الأمنيات بحياة مليئة بالفرح والتوفيق.', category:'تهنئة'
      },
      holiday: {
        label: 'عطلة', theme:'theme-holiday', title:'إعلان عطلة رسمية', desc:'يرجى التفضل بالعلم بوجود عطلة رسمية وفق توجيهات الإدارة، وسيتم استئناف الدوام حسب الجدول المعتمد بعد انتهاء العطلة.', category:'تعليمات'
      },
      birth: {
        label: 'ولادة طفل', theme:'theme-birth', title:'مباركة بمولود جديد', desc:'نبارك بهذه المناسبة السعيدة، ونسأل الله أن يجعله من مواليد السعادة والصحة وأن يقر به أعين والديه.', category:'تهنئة'
      }
    };

    function openComposer(){ if(composer) composer.classList.add('open'); }
    function closeComposer(){ if(composer) composer.classList.remove('open'); }

    function setTemplate(type){
      var preset = presetMap[type] || presetMap.custom;
      if(typeInput) typeInput.value = type;
      quickTemplateButtons.forEach(function(btn){ btn.classList.toggle('active', btn.getAttribute('data-ann-template')===type); });
      if(categoryInput && (!categoryInput.value || categoryInput.dataset.autoFill==='1')){ categoryInput.value = preset.category; categoryInput.dataset.autoFill='1'; }
      if(titleInput && (!titleInput.value || titleInput.dataset.autoFill==='1')){ titleInput.value = preset.title; titleInput.dataset.autoFill='1'; }
      if(descInput && (!descInput.value || descInput.dataset.autoFill==='1')){ descInput.value = preset.desc; descInput.dataset.autoFill='1'; }
      var needsPerson = ['death','wedding','birth'].indexOf(type)>=0;
      if(personRow) personRow.classList.toggle('show', needsPerson);
      updatePreview();
    }

    function updatePreview(){
      var type = typeInput ? typeInput.value : 'custom';
      var preset = presetMap[type] || presetMap.custom;
      var title = titleInput && titleInput.value ? titleInput.value : preset.title;
      var desc = descInput && descInput.value ? descInput.value : preset.desc;
      var audience = audienceInput && audienceInput.value ? audienceInput.value : 'جميع الموظفين';
      var person = personInput ? personInput.value.trim() : '';

      if(pCard){
        pCard.className = 'nxr-ann-preview-card ' + preset.theme;
      }
      if(pType) pType.textContent = preset.label;
      if(pTitle) pTitle.textContent = title;
      if(pDesc) pDesc.textContent = desc;
      if(pAudience) pAudience.textContent = 'موجّه إلى: ' + audience;

      if(pPerson){
        if(person && ['death','wedding','birth'].indexOf(type)>=0){
          var label = type==='death' ? 'اسم الشخص: ' : 'اسم الشخص: ';
          pPerson.textContent = label + person;
          pPerson.classList.add('show');
        } else {
          pPerson.textContent = '';
          pPerson.classList.remove('show');
        }
      }

      if(pImage){
        if(imageInput && imageInput.files && imageInput.files[0]){
          var file = imageInput.files[0];
          var reader = new FileReader();
          reader.onload = function(e){
            pImage.innerHTML = '<img src="'+e.target.result+'" alt="preview" />';
          };
          reader.readAsDataURL(file);
        } else {
          pImage.textContent = 'ستظهر صورة الإعلان هنا عند اختيارها';
        }
      }
    }

    function resetForm(){
      if(form) form.reset();
      if(titleInput) titleInput.dataset.autoFill='1';
      if(descInput) descInput.dataset.autoFill='1';
      if(categoryInput) categoryInput.dataset.autoFill='1';
      setTemplate('custom');
      updatePreview();
    }

    if(openBtn){ openBtn.addEventListener('click', function(){ openComposer(); setTimeout(function(){ if(titleInput) titleInput.focus(); }, 50); }); }
    if(closeBtn){ closeBtn.addEventListener('click', closeComposer); }
    if(resetBtn){ resetBtn.addEventListener('click', resetForm); }

    quickTemplateButtons.forEach(function(btn){
      btn.addEventListener('click', function(){ openComposer(); setTemplate(btn.getAttribute('data-ann-template')); });
    });

    [titleInput, descInput, audienceInput, categoryInput, personInput].forEach(function(el){
      if(!el) return;
      el.addEventListener('input', updatePreview);
      el.addEventListener('change', updatePreview);
    });
    if(imageInput){ imageInput.addEventListener('change', updatePreview); }
    if(typeInput){ typeInput.addEventListener('change', function(){ setTemplate(typeInput.value || 'custom'); }); }

    if(form){
      form.addEventListener('submit', function(ev){
        ev.preventDefault();
        openComposer();
        alert('تم تجهيز نموذج الإعلان. حالياً هذه الواجهة مفعلة للتصميم والإدخال، ويمكن ربط الحفظ الفعلي بالقاعدة في المرحلة التالية.');
      });
    }

    function applyFilter(){
      var q = normalize(search ? search.value : '');
      var s = normalize(filterStatus ? filterStatus.value : '');
      var c = normalize(filterChannel ? filterChannel.value : '');
      var cat = normalize(filterCategory ? filterCategory.value : '');
      var shown = 0;
      rows.forEach(function(row){
        var text = normalize(row.innerText || row.textContent || '');
        var rs = normalize(row.getAttribute('data-status'));
        var rc = normalize(row.getAttribute('data-channel'));
        var rcat = normalize(row.getAttribute('data-category'));
        var ok = true;
        if(q && text.indexOf(q) < 0) ok = false;
        if(s && rs !== s) ok = false;
        if(c && rc !== c) ok = false;
        if(cat && rcat !== cat) ok = false;
        row.style.display = ok ? '' : 'none';
        if(ok) shown++;
      });
      if(counter) counter.textContent = 'عرض ' + shown + ' من ' + rows.length;
      if(emptyRow) emptyRow.classList.toggle('show', shown===0);
    }
    [search, filterStatus, filterChannel, filterCategory].forEach(function(el){ if(!el) return; el.addEventListener('input', applyFilter); el.addEventListener('change', applyFilter); });

    resetForm();
    applyFilter();
  });
})();
