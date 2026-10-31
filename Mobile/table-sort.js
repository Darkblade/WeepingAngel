(()=>{
const numeric=new Intl.Collator(undefined,{numeric:true,sensitivity:'base'});
const compare=(a,b)=>{const x=a.trim(),y=b.trim();if(/^-?\d+(\.\d+)?$/.test(x)&&/^-?\d+(\.\d+)?$/.test(y))return Number(x)-Number(y);return numeric.compare(x,y);};
function setup(table){const body=table.tBodies[0];if(!body)return;const headers=[...table.tHead.rows[0].cells];let column=-1,descending=false;let observer;const key='weeping-angel-sort-'+table.id;
function apply(){observer?.disconnect();if(column>=0){const rows=[...body.rows];if(rows.length&&rows.every(row=>row.cells.length===headers.length)){const ordered=rows.map((row,index)=>({row,index,value:row.cells[column].dataset.sortValue??row.cells[column].textContent}));ordered.sort((a,b)=>(descending?-1:1)*compare(a.value,b.value)||a.index-b.index);for(const item of ordered)body.append(item.row);}}headers.forEach((h,i)=>{if(h.dataset.sortable==='true'){h.setAttribute('aria-sort',i===column?(descending?'descending':'ascending'):'none');h.querySelector('button').dataset.direction=i===column?(descending?'down':'up'):'';}});observer?.observe(body,{childList:true});}
for(const [index,header] of headers.entries()){if(!header.hasAttribute('data-sort-key'))continue;header.dataset.sortable='true';header.setAttribute('aria-sort','none');const label=header.textContent,button=document.createElement('button');button.type='button';button.className='column-sort';button.textContent=label;button.setAttribute('aria-label','Sort by '+label);header.replaceChildren(button);button.onclick=()=>{descending=column===index?!descending:false;column=index;try{localStorage.setItem(key,JSON.stringify({column,descending}));}catch{}apply();};}
try{const saved=JSON.parse(localStorage.getItem(key));if(Number.isInteger(saved?.column)&&headers[saved.column]?.dataset.sortable==='true'){column=saved.column;descending=!!saved.descending;}}catch{}
observer=new MutationObserver(apply);apply();
}
document.addEventListener('DOMContentLoaded',()=>document.querySelectorAll('table[data-sortable-table]').forEach(setup));
})();
