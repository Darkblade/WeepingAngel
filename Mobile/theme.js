(()=>{
const names=['Gold','Autumn','Steel','Security','Forest','Ocean','Galaxy','Contrast','Contrast Light','Comfort'];
const aliases={Dark:'Steel',System:'Steel',Cyber:'Galaxy',darkgold:'Gold','dark-gold':'Gold',blackgold:'Gold','black-gold':'Gold',bluesteel:'Steel','blue-steel':'Steel',highsecurity:'Security','high-security':'Security'};
const read=(key,fallback)=>{try{return localStorage.getItem(key)||fallback;}catch{return fallback;}};
const write=(key,value)=>{try{localStorage.setItem(key,value);}catch{}};
let preference=new URLSearchParams(location.search).get('theme')||read('dontblink-theme','Steel');
function apply(name){name=aliases[name]||name;if(!names.includes(name))name='Steel';preference=name;write('dontblink-theme',name);document.documentElement.dataset.theme=name;
 document.querySelectorAll('[data-theme-choice]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.themeChoice===name)));
 const select=document.getElementById('theme');if(select)select.value=name;
}
function accessibility(){const root=document.documentElement;root.style.setProperty('--text-scale',read('dontblink-text-size','1'));root.dataset.textLarge=String(Number(read('dontblink-text-size','1'))>1);for(const key of ['spacing','comfortable','clutter'])root.dataset[key]=read('dontblink-'+key,'false');root.dataset.motion=read('dontblink-motion','system');root.dataset.talkSide=read('dontblink-talk-side','right');}
apply(preference);accessibility();
document.addEventListener('DOMContentLoaded',()=>{
 const grid=document.querySelector('.theme-grid');if(grid){grid.replaceChildren();for(const name of names){const b=document.createElement('button');b.className='theme-choice';b.dataset.themeChoice=name;b.textContent=name;grid.append(b);}}
 document.querySelectorAll('[data-preference]').forEach(control=>{const key=control.dataset.preference;control.value=read('dontblink-'+key,control.value);if(control.type==='checkbox')control.checked=read('dontblink-'+key,'false')==='true';control.onchange=()=>{write('dontblink-'+key,control.type==='checkbox'?String(control.checked):control.value);accessibility();};});
 document.querySelectorAll('[data-theme-choice]').forEach(b=>b.onclick=()=>apply(b.dataset.themeChoice));
 const select=document.getElementById('theme');if(select)select.onchange=()=>apply(select.value);
 apply(preference);
});
addEventListener('storage',event=>{if(event.key==='dontblink-theme')apply(event.newValue||'Steel');});
window.DontBlinkTheme={apply,names};
})();
