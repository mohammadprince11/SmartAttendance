(() => {
  'use strict';
  const root = document.getElementById('announcement-template-studio');
  if (!root) return;
  const q = id => document.getElementById(`zys-${id}`);
  const data = JSON.parse(q('data').textContent);
  const templates = data.templates;
  const text = (tag, value) => { const e = document.createElement(tag); e.textContent = value; return e; };
  const control = (type, value = '') => { const e = document.createElement('input'); e.type = type; e.value = value; return e; };
  const labeled = (title, e) => { const l = text('label', title); l.append(e); return l; };
  const select = options => { const e = document.createElement('select'); options.forEach(([v,l]) => {const o=text('option',l);o.value=v;e.append(o)}); return e; };
  let current;
  function populate(restore = false) {
    current = templates.find(x => x.Definition.Key === q('template').value)?.Definition;
    q('fields').replaceChildren(); q('preview-language').replaceChildren();
    if (!current) { q('errors').textContent='لا توجد قوالب فعالة. فعّل قالباً أو أنشئ قالباً جديداً.';q('compose').querySelector('button[type=submit]').disabled=true;return; }
    current.Fields.forEach(f => {const input=control(f.Type,restore?(data.values[f.Key]||''):'');input.name=`Values[${f.Key}]`;input.required=f.Required;input.maxLength=500;q('fields').append(labeled(f.Label+(f.Required?' *':' — اختياري'),input));});
    Object.keys(current.Languages).forEach(lang => {const o=text('option',lang);o.value=lang;q('preview-language').append(o)});
    [...q('design').options].forEach(o => {o.hidden=!o.value.startsWith('builtin:') && current.DesignIds.length>0 && !current.DesignIds.includes(o.value)});
    const builtin={birthday:'newborn','newborn-boy':'newborn','newborn-girl':'newborn','employee-of-month':'promotion',anniversary:'promotion',retirement:'farewell'}[current.Key]||current.Key;
    q('design').value=`builtin:${builtin}`;
    if(!q('design').value)q('design').selectedIndex=0;
    if(restore){if(data.design)q('design').value=data.design;q('fit').value=data.fit;q('position').value=data.position;q('placement').value=data.placement;}
    render();
  }
  function render() {
    if(!current)return;
    const values={};q('fields').querySelectorAll('input').forEach(e=>values[e.name.slice(7,-1)]=e.value);
    if(values.joiningDate){const j=new Date(values.joiningDate+'T00:00:00Z'),now=new Date(data.today+'T00:00:00Z');let years=now.getUTCFullYear()-j.getUTCFullYear();if(now.getUTCMonth()<j.getUTCMonth()||(now.getUTCMonth()===j.getUTCMonth()&&now.getUTCDate()<j.getUTCDate()))years--;values.years=String(Math.max(0,years));}
    const t=current.Languages[q('preview-language').value]||Object.values(current.Languages)[0];
    const replace=s=>s.replace(/\{([^{}]+)\}/g,(_,key)=>values[key]||'…');
    q('title').textContent=replace(t.Title);q('body').textContent=replace(t.Body);
    q('preview').dir=q('preview-language').value.match(/^(ar|ku|ckb|fa|he)/)?'rtl':'ltr';
    q('preview').className=`zys-visual zys-${q('fit').value} zys-${q('position').value} zys-${q('placement').value}`;
    const d=q('design').value;q('image').src=d.startsWith('builtin:')?`/brand/announcement-studio/art/${d.slice(8)}.png`:`/Engagement/Studio?handler=Image&CompanyId=${data.companyId}&id=${encodeURIComponent(d)}`;
    q('errors').textContent=values.startDate && values.endDate && values.endDate<values.startDate?'تاريخ النهاية يجب ألا يسبق البداية.':'';
  }
  q('template').addEventListener('change',()=>populate());q('compose').addEventListener('input',render);q('preview-language').addEventListener('change',render);
  q('compose').addEventListener('submit',e=>{if(q('errors').textContent){e.preventDefault();return}q('compose').querySelector('button[type=submit]').disabled=true;});
  const builder=q('builder');
  if(builder){
    function fieldRow(f={Key:'',Label:'',Type:'text',Required:true}){
      const row=document.createElement('div');row.className='zys-builder-row';row.dataset.field='true';
      const key=control('text',f.Key),label=control('text',f.Label),type=select([['text','نص'],['date','تاريخ'],['month','شهر وسنة'],['number','رقم']]),required=control('checkbox');
      key.dataset.part='key';label.dataset.part='label';type.dataset.part='type';type.value=f.Type;required.dataset.part='required';required.checked=f.Required;
      const remove=text('button','إزالة الحقل');remove.type='button';remove.className='zy-btn';remove.onclick=()=>row.remove();
      row.append(labeled('رمز الحقل',key),labeled('اسم الحقل',label),labeled('نوع الحقل',type),labeled('مطلوب',required),remove);q('builder-fields').append(row);
    }
    function languageRow(lang='ar',t={Title:'',Body:''}){
      const row=document.createElement('div');row.dataset.language='true';const code=control('text',lang),title=control('text',t.Title),body=document.createElement('textarea');body.value=t.Body;body.rows=4;
      code.dataset.part='code';title.dataset.part='title';body.dataset.part='body';const remove=text('button','إزالة اللغة');remove.type='button';remove.className='zy-btn';remove.onclick=()=>row.remove();
      row.append(labeled('رمز اللغة (ar / en / ku أو لغة جديدة)',code),labeled('العنوان',title),labeled('النص',body),remove);q('builder-languages').append(row);
    }
    q('edit').addEventListener('change',()=>{const t=templates.find(t=>t.Definition.Key===q('edit').value);q('key').value=t?.Definition.Key||'';q('key').readOnly=!!t;q('name').value=t?.Definition.Name||'';q('revision').value=t?.Revision||'00000000-0000-0000-0000-000000000000';q('builder-fields').replaceChildren();q('builder-languages').replaceChildren();(t?.Definition.Fields||[]).forEach(fieldRow);Object.entries(t?.Definition.Languages||{ar:{Title:'',Body:''}}).forEach(([l,v])=>languageRow(l,v));root.querySelectorAll('[data-zys-design-id]').forEach(e=>e.checked=t?.Definition.DesignIds.includes(e.dataset.zysDesignId)||false);});
    q('add-field').onclick=()=>fieldRow();q('add-language').onclick=()=>languageRow('');q('edit').dispatchEvent(new Event('change'));
    builder.addEventListener('submit',e=>{
      const definition={Key:q('key').value,Name:q('name').value,Fields:[],Languages:{},DesignIds:[]};
      q('builder-fields').querySelectorAll('[data-field]').forEach(row=>{const part=n=>row.querySelector(`[data-part=${n}]`);definition.Fields.push({Key:part('key').value,Label:part('label').value,Type:part('type').value,Required:part('required').checked})});
      let duplicate=false;q('builder-languages').querySelectorAll('[data-language]').forEach(row=>{const part=n=>row.querySelector(`[data-part=${n}]`).value;const code=part('code').trim();if(definition.Languages[code])duplicate=true;definition.Languages[code]={Title:part('title'),Body:part('body')};});
      root.querySelectorAll('[data-zys-design-id]:checked').forEach(x=>definition.DesignIds.push(x.dataset.zysDesignId));
      if(duplicate){e.preventDefault();alert('رمز اللغة مكرر.');return}q('json').value=JSON.stringify(definition);
    });
  }
  if(data.templateKey)q('template').value=data.templateKey;
  populate(true);
})();
