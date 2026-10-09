(() => {
  'use strict';
  const form=document.getElementById('zys-free-compose');if(!form)return;
  const q=id=>document.getElementById('zys-free-'+id);
  const template=document.getElementById('zys-template');
  function syncMode(){
    if(!template)return;
    const free=template.value==='__free';
    document.getElementById('zys-free').hidden=!free;
    document.getElementById('zys-template-compose-wrap').hidden=free;
  }
  if(template){
    template.addEventListener('change',syncMode);
    if(location.hash==='#zys-free'){template.value='__free';template.dispatchEvent(new Event('change'));}
    syncMode();
  }
  const rows=[...form.querySelectorAll('[data-free-language]')];
  const row=code=>rows.find(r=>r.dataset.freeLanguage===code);
  function editLanguage(code){if(q('edit-language'))q('edit-language').value=code;rows.forEach(r=>r.hidden=r.dataset.freeLanguage!==code);}
  function render(){
    const primary=row(q('primary').value), selected=row(q('preview-language').value);
    const selectedComplete=selected?.querySelector('[data-free-title]').value.trim() && selected.querySelector('[data-free-body]').value.trim();
    const active=selectedComplete?selected:primary;
    q('title').textContent=active?.querySelector('[data-free-title]').value||'';
    q('body').textContent=active?.querySelector('[data-free-body]').value||'';
    q('preview').dir=active?.dataset.freeDirection||'ltr';
    rows.forEach(r=>{
      const title=r.querySelector('[data-free-title]'), body=r.querySelector('[data-free-body]');
      const required=r===primary || !!title.value.trim() || !!body.value.trim();
      title.required=required;body.required=required;
    });
  }
  q('primary').addEventListener('change',()=>{editLanguage(q('primary').value);q('preview-language').value=q('primary').value;render()});
  q('edit-language')?.addEventListener('change',()=>editLanguage(q('edit-language').value));
  q('preview-language').value=q('primary').value;
  form.addEventListener('input',render);q('preview-language').addEventListener('change',render);
  let imageUrl=null;
  q('image').addEventListener('change',()=>{
    if(imageUrl)URL.revokeObjectURL(imageUrl);imageUrl=null;
    const file=q('image').files[0];
    if(file && ['image/png','image/jpeg'].includes(file.type) && file.size<=5242880)imageUrl=URL.createObjectURL(file);
    q('image-preview').hidden=!imageUrl;
    if(imageUrl)q('image-preview').src=imageUrl;else q('image-preview').removeAttribute('src');
  });
  form.addEventListener('invalid',e=>{const row=e.target.closest('[data-free-language]');if(row)editLanguage(row.dataset.freeLanguage)},true);
  form.addEventListener('submit',()=>form.querySelector('button[type=submit]').disabled=true);
  render();
})();
