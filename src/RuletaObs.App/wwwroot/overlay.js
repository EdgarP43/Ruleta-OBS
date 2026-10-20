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
let theme={fontFamily:'Segoe UI',centerText:'RULETA',centerBackground:'#171321',centerTextColor:'#ffffff',labelColor:'#ffffff',labelSizePercent:100,resultTextColor:'#ffffff'};
function fontStack(){return `"${(theme.fontFamily||'Segoe UI').replaceAll('"','')}", Arial, sans-serif`;}
function canvasFont(size){return `700 ${size}px ${fontStack()}`;}
const wheelBase=document.createElement('canvas');wheelBase.width=900;wheelBase.height=900;let wheelBaseKey='';

function clearHideTimer(){if(hideTimer)clearTimeout(hideTimer);hideTimer=null;hideForKey=null;}
function clearRevealTimer(){if(revealTimer)clearTimeout(revealTimer);revealTimer=null;}
function scheduleHideAll(key,delay=RESULT_VISIBLE_MS){if(hideTimer&&hideForKey===key)return;clearHideTimer();hideForKey=key;hideTimer=setTimeout(()=>{hideTimer=null;hideForKey=null;hideAll();},delay);}
function hideAll(){clearRevealTimer();$('stage').classList.add('gone');$('queueBox').classList.add('gone');$('result').classList.add('hidden');$('celebration').classList.add('hidden');}
function formatCelebration(viewer,prize,amount,source){return (celebrationText||'').replaceAll('{usuario}',viewer||'').replaceAll('{premio}',prize||'').replaceAll('{cantidad}',amount??'').replaceAll('{origen}',label(source));}
function showStage(){lastActivityAt=Date.now();$('stage').classList.remove('gone');showQueue();}
function showQueue(){lastActivityAt=Date.now();$('queueBox').classList.remove('gone');}
function canRevealEntry(id){return animationState==='idle'&&!queuedSpinIds.has(id)&&Date.now()>=revealAtMs;}

function showResult(viewer,prize,entryId,amount,source){
  if(entryId)displayedEntryIds.add(entryId);
  animationState='showing';activeSpinId=entryId;
  $('result').textContent=`${viewer}: ${prize}`;$('result').classList.remove('hidden');beep();
  const text=formatCelebration(viewer,prize,amount,source);
  const img=$('celebrationImg');
  if(celebrationMediaUrl){img.src=celebrationMediaUrl;img.style.display='inline-block';}else img.style.display='none';
  $('celebrationText').textContent=text;
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
  const r=await fetch('/api/queue');const q=await r.json();soundEnabled=q.soundEnabled;celebrationText=q.celebrationText||'';celebrationMediaUrl=q.celebrationMediaUrl||null;customSoundUrl=q.soundUrl||null;soundVolume=q.soundVolume??80;if(q.theme)theme={...theme,...q.theme};document.body.style.fontFamily=fontStack();$('result').style.fontFamily=fontStack();$('result').style.color=theme.resultTextColor;$('celebrationText').style.fontFamily=fontStack();
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
  segs.forEach(s=>{b.beginPath();b.moveTo(450,450);b.arc(450,450,420,s.start,s.end);b.closePath();b.fillStyle=s.p.color;b.fill();b.strokeStyle='rgba(255,255,255,.9)';b.lineWidth=5;b.stroke();b.save();b.translate(450,450);b.rotate(s.mid);b.textAlign='right';b.fillStyle=theme.labelColor||'#fff';b.font=canvasFont(34*((theme.labelSizePercent||100)/100));b.shadowColor='rgba(0,0,0,.7)';b.shadowBlur=5;b.fillText(s.p.name,395,12);b.restore();});
}
function draw(){
  ctx.clearRect(0,0,900,900);if(!prizes.length)return;ensureWheelBase();
  ctx.save();ctx.translate(450,450);ctx.rotate(rotation);ctx.drawImage(wheelBase,-450,-450);ctx.restore();
  ctx.beginPath();ctx.arc(450,450,105,0,Math.PI*2);ctx.fillStyle=theme.centerBackground||'#171321';ctx.fill();ctx.lineWidth=8;ctx.strokeStyle='#fff';ctx.stroke();
  const centerText=(theme.centerText||'RULETA').toString();const centerSize=centerText.length<=8?30:centerText.length<=14?23:18;
  ctx.fillStyle=theme.centerTextColor||'#fff';ctx.font=canvasFont(centerSize);ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText(centerText,450,450);ctx.textBaseline='alphabetic';
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
  showStage();$('result').classList.add('hidden');$('celebration').classList.add('hidden');
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
loadQueue();
