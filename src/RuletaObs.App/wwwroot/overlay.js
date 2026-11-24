const params=new URLSearchParams(location.search);const key=params.get('key')||'';
const canvas=document.getElementById('wheel');const ctx=canvas.getContext('2d');
const $=id=>document.getElementById(id);
const SPIN_DURATION_MS=5400;
const REVEAL_AFTER_SPUN_MS=SPIN_DURATION_MS+350;
const RESULT_VISIBLE_MS=2500;
let prizes=[],rotation=0,soundEnabled=true;
let hideTimer=null,hideForKey=null,revealTimer=null,alertTimer=null;
let currentEntryId=null,revealAtMs=0,lastActivityAt=0;
let animationState='idle'; // idle | spinning | showing
let activeSpinId=null;
const queuedSpins=[];const queuedSpinIds=new Set();const displayedEntryIds=new Set();
let loadInFlight=false,loadDirty=false;
let celebrationText='',celebrationMediaUrl=null,customSoundUrl=null,soundVolume=80;
let theme={fontFamily:'Luckiest Guy',centerText:'RULETA',centerBackground:'#3d2b7c',centerTextColor:'#ffffff',labelColor:'#ffffff',labelSizePercent:100,resultTextColor:'#ffffff'};
function fontStack(){return `"${(theme.fontFamily||'Luckiest Guy').replaceAll('"','')}", Fredoka, Arial, sans-serif`;}
function canvasFont(size){return `400 ${size}px ${fontStack()}`;}
function isNearBlack(c){
  if(!c||typeof c!=='string')return false;
  const m=c.trim().match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i);if(!m)return false;
  let h=m[1];if(h.length===3)h=h.split('').map(x=>x+x).join('');
  const r=parseInt(h.slice(0,2),16),g=parseInt(h.slice(2,4),16),b=parseInt(h.slice(4,6),16);
  return (r+g+b)/3<40;
}
function strokeBubbleText(c,text,x,y,fill){
  const sizeMatch=String(c.font).match(/(\d+(?:\.\d+)?)px/);
  const size=sizeMatch?Number(sizeMatch[1]):24;
  c.lineJoin='round';c.miterLimit=2;c.lineWidth=Math.max(6,size*0.18);
  c.strokeStyle='#000';c.strokeText(text,x,y);
  c.fillStyle=fill||'#fff';c.fillText(text,x,y);
}
const wheelBase=document.createElement('canvas');wheelBase.width=900;wheelBase.height=900;let wheelBaseKey='';

function clearHideTimer(){if(hideTimer)clearTimeout(hideTimer);hideTimer=null;hideForKey=null;}
function clearRevealTimer(){if(revealTimer)clearTimeout(revealTimer);revealTimer=null;}
function scheduleHideAll(key,delay=RESULT_VISIBLE_MS){if(hideTimer&&hideForKey===key)return;clearHideTimer();hideForKey=key;hideTimer=setTimeout(()=>{hideTimer=null;hideForKey=null;hideAll();},delay);}
function hideAll(){clearRevealTimer();$('stage').classList.add('gone');$('queueBox').classList.add('gone');$('celebrationBox').classList.add('gone');$('result').classList.add('hidden');$('celebration').classList.add('hidden');}
function formatCelebration(viewer,prize,amount,source){return (celebrationText||'').replaceAll('{usuario}',viewer||'').replaceAll('{premio}',prize||'').replaceAll('{cantidad}',amount??'').replaceAll('{origen}',label(source));}
function showStage(){lastActivityAt=Date.now();$('stage').classList.remove('gone');showQueue();}
function showQueue(){lastActivityAt=Date.now();$('queueBox').classList.remove('gone');}
function canRevealEntry(id){return animationState==='idle'&&!queuedSpinIds.has(id)&&Date.now()>=revealAtMs;}

function applyResultStyle(el){
  if(!el)return;
  const color=theme.resultTextColor||'#ffffff';
  el.style.setProperty('color',color,'important');
  el.style.setProperty('font-family',fontStack(),'important');
  el.style.setProperty('text-shadow','0 2px 8px rgba(0,0,0,.85),0 0 14px rgba(196,181,253,.5)','important');
  el.style.setProperty('-webkit-text-stroke','0','important');
}
function showResult(viewer,prize,entryId,amount,source){
  if(entryId)displayedEntryIds.add(entryId);
  animationState='showing';activeSpinId=entryId;
  showQueue();$('celebrationBox').classList.remove('gone');
  // Nunca usar textContent en #result: borraría los michis.
  const textEl=$('resultText');
  if(textEl){textEl.textContent=`${viewer}: ${prize}`;applyResultStyle(textEl);}
  else{
    // Fallback si OBS tiene HTML viejo en caché
    const r=$('result');
    [...r.querySelectorAll(':not(.michi):not(.michi-emoji)')].forEach(n=>{if(n.id!=='resultText')n.remove();});
    let span=$('resultText');
    if(!span){span=document.createElement('span');span.id='resultText';r.appendChild(span);}
    span.textContent=`${viewer}: ${prize}`;applyResultStyle(span);
  }
  applyResultStyle($('resultText'));
  $('result').classList.remove('hidden');beep();
  const text=formatCelebration(viewer,prize,amount,source);
  const img=$('celebrationImg');
  if(celebrationMediaUrl){img.src=celebrationMediaUrl;img.style.display='inline-block';}else img.style.display='none';
  $('celebrationText').textContent=text;applyResultStyle($('celebrationText'));
  if(celebrationMediaUrl||text)$('celebration').classList.remove('hidden');else $('celebration').classList.add('hidden');
  clearHideTimer();hideForKey=entryId;
  hideTimer=setTimeout(()=>{hideTimer=null;hideForKey=null;finishActiveSpin();},RESULT_VISIBLE_MS);
}
async function finishActiveSpin(){
  hideAll();animationState='idle';activeSpinId=null;currentEntryId=null;revealAtMs=0;
  await playNextQueuedSpin();
}
async function playNextQueuedSpin(){
  while(queuedSpins.length&&animationState==='idle'){
    const next=queuedSpins.shift();queuedSpinIds.delete(next.entry.id);
    try{
      const r=await fetch('/api/queue');const q=await r.json();
      if(!q.current||q.current.id!==next.entry.id)continue; // Ya se confirmó/canceló antes de animarla.
    }catch{continue;}
    startSpin(next);return;
  }
  loadQueue();
}
function enqueueSpin(data){
  const id=data.entry.id;
  if(id===activeSpinId||queuedSpinIds.has(id)||displayedEntryIds.has(id))return;
  if(animationState!=='idle'){queuedSpins.push(data);queuedSpinIds.add(id);showQueue();return;}
  startSpin(data);
}

async function loadQueue(){
  if(loadInFlight){loadDirty=true;return;}
  loadInFlight=true;
  try{do{loadDirty=false;await loadQueueOnce();}while(loadDirty);}
  finally{loadInFlight=false;}
}
async function loadQueueOnce(){
  const r=await fetch('/api/queue');const q=await r.json();soundEnabled=q.soundEnabled;celebrationText=q.celebrationText||'';celebrationMediaUrl=q.celebrationMediaUrl||null;customSoundUrl=q.soundUrl||null;soundVolume=q.soundVolume??80;if(q.theme)theme={...theme,...q.theme};
  // Evita texto casi negro guardado por error en configs viejas
  if(isNearBlack(theme.resultTextColor))theme.resultTextColor='#ffffff';
  if(isNearBlack(theme.centerTextColor))theme.centerTextColor='#ffffff';
  if(isNearBlack(theme.labelColor))theme.labelColor='#ffffff';
  document.body.style.fontFamily=fontStack();
  const resultColor=theme.resultTextColor||'#ffffff';
  $('resultText').style.setProperty('color',resultColor,'important');
  $('resultText').style.fontFamily=fontStack();
  $('celebrationText').style.setProperty('color',resultColor,'important');
  $('celebrationText').style.fontFamily=fontStack();
  $('alertName').style.setProperty('color',resultColor,'important');
  // Contorno canvas: blanco con sombra, sin relleno negro
  theme.resultTextColor=resultColor;
  if(animationState!=='spinning')prizes=q.prizes.filter(p=>p.isEnabled&&p.weight>0);
  $('queue').innerHTML=q.summary.length?q.summary.map(x=>`<div class="person">${x.avatarUrl?`<img src="${x.avatarUrl}">`:'<img alt="">'}<div class="n"><strong>${escapeHtml(x.viewerDisplayName)}</strong><br><small>${label(x.lastSource)}</small></div><span class="badge">x${x.pendingSpins+x.awaitingConfirmation}</span></div>`).join(''):'<div class="empty">Sin tiradas pendientes</div>';
  if(q.current){
    if(displayedEntryIds.has(q.current.id)&&animationState==='idle'&&!queuedSpinIds.has(q.current.id)){hideAll();draw();return;}
    showStage();draw();
    if(currentEntryId!==q.current.id){currentEntryId=q.current.id;revealAtMs=q.current.spunAt?new Date(q.current.spunAt).getTime()+REVEAL_AFTER_SPUN_MS:0;}
    const blockedByAnimation=animationState!=='idle'||queuedSpinIds.has(q.current.id);
    if(!blockedByAnimation&&canRevealEntry(q.current.id)){
      showResult(q.current.viewerDisplayName,q.current.prizeName,q.current.id,q.current.amount,q.current.source);
    }else{
      $('result').classList.add('hidden');
      if(animationState!=='showing')$('celebrationBox').classList.add('gone');
      if(animationState==='idle'&&!queuedSpinIds.has(q.current.id)){clearRevealTimer();revealTimer=setTimeout(()=>{revealTimer=null;loadQueue();},Math.max(60,revealAtMs-Date.now()+60));}
    }
  }else if(animationState==='idle'){
    currentEntryId=null;revealAtMs=0;
    if(q.summary.length&&Date.now()-lastActivityAt<4500){showQueue();scheduleHideAll('recent-activity');}
    else hideAll();
  }
  draw();
}
function escapeHtml(s){const d=document.createElement('div');d.textContent=s||'';return d.innerHTML;}
function label(s){return s==='Bits'?'Twitch · Bits':s==='ChannelPoints'?'Twitch · Puntos':'Prueba';}
function segments(){const total=prizes.reduce((a,p)=>a+p.weight,0)||1;let start=-Math.PI/2;return prizes.map(p=>{const span=p.weight/total*Math.PI*2;const x={p,start,end:start+span,mid:start+span/2};start+=span;return x;});}
function ensureWheelBase(){
  const keyNow=JSON.stringify([prizes,theme.fontFamily,theme.labelColor,theme.labelSizePercent]);if(keyNow===wheelBaseKey)return;wheelBaseKey=keyNow;
  const b=wheelBase.getContext('2d');b.clearRect(0,0,900,900);const segs=segments();
  // aro exterior cósmico
  b.beginPath();b.arc(450,450,428,0,Math.PI*2);b.strokeStyle='#8b5cf6';b.lineWidth=12;b.stroke();
  b.beginPath();b.arc(450,450,436,0,Math.PI*2);b.strokeStyle='#c4b5fd';b.lineWidth=5;b.stroke();
  segs.forEach(s=>{b.beginPath();b.moveTo(450,450);b.arc(450,450,420,s.start,s.end);b.closePath();b.fillStyle=s.p.color;b.fill();b.strokeStyle='rgba(245,243,255,.88)';b.lineWidth=4;b.stroke();b.save();b.translate(450,450);b.rotate(s.mid);b.textAlign='right';b.textBaseline='middle';b.font=canvasFont(34*((theme.labelSizePercent||100)/100));strokeBubbleText(b,(s.p.name||'').toUpperCase(),395,0,theme.labelColor||'#fff');b.restore();});
}
function draw(){
  ctx.clearRect(0,0,900,900);if(!prizes.length)return;ensureWheelBase();
  ctx.save();ctx.translate(450,450);ctx.rotate(rotation);ctx.drawImage(wheelBase,-450,-450);ctx.restore();
  // brillo del centro
  const glow=ctx.createRadialGradient(450,450,70,450,450,130);
  glow.addColorStop(0,'rgba(167,139,250,.35)');glow.addColorStop(1,'rgba(167,139,250,0)');
  ctx.beginPath();ctx.arc(450,450,130,0,Math.PI*2);ctx.fillStyle=glow;ctx.fill();
  ctx.beginPath();ctx.arc(450,450,105,0,Math.PI*2);ctx.fillStyle=theme.centerBackground||'#3d2b7c';ctx.fill();
  ctx.lineWidth=8;ctx.strokeStyle='#a78bfa';ctx.stroke();
  ctx.beginPath();ctx.arc(450,450,96,0,Math.PI*2);ctx.lineWidth=2;ctx.strokeStyle='#c4b5fd';ctx.stroke();
  const centerText=(theme.centerText||'RULETA').toString().toUpperCase();const centerSize=centerText.length<=8?32:centerText.length<=14?24:18;
  ctx.font=canvasFont(centerSize);ctx.textAlign='center';ctx.textBaseline='middle';
  strokeBubbleText(ctx,centerText,450,450,theme.centerTextColor||'#fff');
  ctx.textBaseline='alphabetic';
}
function beep(){if(!soundEnabled)return;if(customSoundUrl){try{const audio=new Audio(customSoundUrl);audio.volume=Math.min(1,Math.max(0,soundVolume/100));audio.play().catch(()=>synthBeep());return;}catch{}}synthBeep();}
function synthBeep(){try{const a=new(window.AudioContext||window.webkitAudioContext)();const o=a.createOscillator(),g=a.createGain();o.connect(g);g.connect(a.destination);o.frequency.value=880;g.gain.setValueAtTime(.12,a.currentTime);g.gain.exponentialRampToValueAtTime(.001,a.currentTime+.35);o.start();o.stop(a.currentTime+.36);}catch{}}
function showAlert(d){
  lastActivityAt=Date.now();showQueue();
  if(alertTimer)clearTimeout(alertTimer);
  $('alertName').textContent=d.viewer||'';$('alertText').textContent=d.message||'';
  const img=$('alertAvatar');if(d.avatar){img.src=d.avatar;img.style.display='block';}else img.style.display='none';
  const media=$('alertMedia');if(celebrationMediaUrl){media.src=celebrationMediaUrl;media.style.display='block';}else media.style.display='none';
  $('alert').classList.remove('hidden');beep();
  alertTimer=setTimeout(()=>$('alert').classList.add('hidden'),4500);
}

function startSpin(data){
  clearHideTimer();clearRevealTimer();
  animationState='spinning';activeSpinId=data.entry.id;currentEntryId=data.entry.id;
  revealAtMs=Date.now()+REVEAL_AFTER_SPUN_MS;
  prizes=data.prizes.filter(p=>p.isEnabled&&p.weight>0);wheelBaseKey='';
  showStage();$('result').classList.add('hidden');$('celebration').classList.add('hidden');$('celebrationBox').classList.add('gone');
  const segs=segments();const target=segs.find(s=>s.p.name===data.prize.name)||segs[0];
  if(!target){animationState='idle';activeSpinId=null;return;}
  const pointer=-Math.PI/2;
  const desired=pointer-target.mid+(Math.random()-.5)*(target.end-target.start)*.45;
  const current=((rotation%(Math.PI*2))+Math.PI*2)%(Math.PI*2);
  let delta=((desired-current)%(Math.PI*2)+Math.PI*2)%(Math.PI*2);delta+=Math.PI*2*6;
  const start=rotation,end=rotation+delta,t0=performance.now();
  function frame(now){
    const t=Math.min(1,(now-t0)/SPIN_DURATION_MS);const ease=1-Math.pow(1-t,4);
    rotation=start+delta*ease;draw();
    if(t<1){requestAnimationFrame(frame);return;}
    rotation=end;draw();
    const wait=revealAtMs-Date.now();
    if(wait>0){clearRevealTimer();revealTimer=setTimeout(()=>{revealTimer=null;showResult(data.entry.viewerDisplayName,data.prize.name,data.entry.id,data.entry.amount,data.entry.source);},wait);}
    else showResult(data.entry.viewerDisplayName,data.prize.name,data.entry.id,data.entry.amount,data.entry.source);
  }
  requestAnimationFrame(frame);
}

const es=new EventSource(`/api/events?client=overlay&key=${encodeURIComponent(key)}`);
es.addEventListener('alert',e=>{showAlert(JSON.parse(e.data));loadQueue();});
es.addEventListener('queue',loadQueue);
es.addEventListener('spin',e=>enqueueSpin(JSON.parse(e.data)));
es.addEventListener('closed',()=>{setTimeout(loadQueue,700);});
es.addEventListener('config',loadQueue);
es.addEventListener('reload',()=>location.reload());
(async()=>{
  try{await document.fonts.load('400 34px "Luckiest Guy"');await document.fonts.load('700 30px Fredoka');}catch{}
  wheelBaseKey='';loadQueue();
})();
