const params=new URLSearchParams(location.search);const key=params.get('key')||'';
const canvas=document.getElementById('wheel');const ctx=canvas.getContext('2d');
const $=id=>document.getElementById(id);
const SPIN_DURATION_MS=5400;
const REVEAL_AFTER_SPUN_MS=SPIN_DURATION_MS+350;
const RESULT_VISIBLE_MS=2500;
let prizes=[],rotation=0,animating=false,soundEnabled=true;
let hideTimer=null,hideForKey=null,revealTimer=null,currentEntryId=null,revealAtMs=0,lastActivityAt=0;

function clearHideTimer(){if(hideTimer)clearTimeout(hideTimer);hideTimer=null;hideForKey=null;}
function clearRevealTimer(){if(revealTimer)clearTimeout(revealTimer);revealTimer=null;}
function scheduleHideAll(key,delay=RESULT_VISIBLE_MS){if(hideTimer&&hideForKey===key)return;clearHideTimer();hideForKey=key;hideTimer=setTimeout(()=>{hideTimer=null;hideForKey=null;hideAll();},delay);}
function hideAll(){clearRevealTimer();$('stage').classList.add('gone');$('queueBox').classList.add('gone');$('result').classList.add('hidden');}
function showStage(){lastActivityAt=Date.now();$('stage').classList.remove('gone');showQueue();}
function showQueue(){lastActivityAt=Date.now();$('queueBox').classList.remove('gone');}
function canReveal(){return !animating&&Date.now()>=revealAtMs;}
function showResult(viewer,prize,entryId){$('result').textContent=`${viewer}: ${prize}`;$('result').classList.remove('hidden');beep();scheduleHideAll(entryId||'current');}
function scheduleRevealCheck(entryId){clearRevealTimer();const wait=Math.max(60,revealAtMs-Date.now()+60);revealTimer=setTimeout(()=>{revealTimer=null;loadQueue();},wait);}

async function loadQueue(){
  const r=await fetch('/api/queue');const q=await r.json();soundEnabled=q.soundEnabled;
  if(!animating)prizes=q.prizes.filter(p=>p.isEnabled&&p.weight>0);
  $('queue').innerHTML=q.summary.length?q.summary.map(x=>`<div class="person">${x.avatarUrl?`<img src="${x.avatarUrl}">`:'<img alt="">'}<div class="n"><strong>${escapeHtml(x.viewerDisplayName)}</strong><br><small>${label(x.lastSource)}</small></div><span class="badge">x${x.pendingSpins+x.awaitingConfirmation}</span></div>`).join(''):'<div class="empty">Sin tiradas pendientes</div>';
  if(q.current){
    showStage();draw();
    if(currentEntryId!==q.current.id){currentEntryId=q.current.id;revealAtMs=q.current.spunAt?new Date(q.current.spunAt).getTime()+REVEAL_AFTER_SPUN_MS:0;}
    if(canReveal()){showResult(q.current.viewerDisplayName,q.current.prizeName,q.current.id);}
    else{$('result').classList.add('hidden');if(!animating)scheduleRevealCheck(q.current.id);}
  }else if(!animating){
    currentEntryId=null;revealAtMs=0;
    if(q.summary.length&&Date.now()-lastActivityAt<4500){showQueue();scheduleHideAll('recent-activity');}
    else hideAll();
  }
  draw();
}
function escapeHtml(s){const d=document.createElement('div');d.textContent=s||'';return d.innerHTML;}
function label(s){return s==='Bits'?'Twitch · Bits':s==='ChannelPoints'?'Twitch · Puntos':'Prueba';}
function segments(){const total=prizes.reduce((a,p)=>a+p.weight,0)||1;let start=-Math.PI/2;return prizes.map(p=>{const span=p.weight/total*Math.PI*2;const x={p,start,end:start+span,mid:start+span/2};start+=span;return x;});}
function draw(){ctx.clearRect(0,0,900,900);const segs=segments();ctx.save();ctx.translate(450,450);ctx.rotate(rotation);segs.forEach(s=>{ctx.beginPath();ctx.moveTo(0,0);ctx.arc(0,0,420,s.start,s.end);ctx.closePath();ctx.fillStyle=s.p.color;ctx.fill();ctx.strokeStyle='rgba(255,255,255,.9)';ctx.lineWidth=5;ctx.stroke();ctx.save();ctx.rotate(s.mid);ctx.textAlign='right';ctx.fillStyle='#fff';ctx.font='bold 34px Segoe UI';ctx.shadowColor='rgba(0,0,0,.7)';ctx.shadowBlur=5;ctx.fillText(s.p.name,395,12);ctx.restore();});ctx.beginPath();ctx.arc(0,0,105,0,Math.PI*2);ctx.fillStyle='#171321';ctx.fill();ctx.lineWidth=8;ctx.strokeStyle='#fff';ctx.stroke();ctx.fillStyle='#fff';ctx.font='bold 30px Segoe UI';ctx.textAlign='center';ctx.fillText('RULETA',0,10);ctx.restore();}
function beep(){if(!soundEnabled)return;try{const a=new(window.AudioContext||window.webkitAudioContext)();const o=a.createOscillator(),g=a.createGain();o.connect(g);g.connect(a.destination);o.frequency.value=880;g.gain.setValueAtTime(.12,a.currentTime);g.gain.exponentialRampToValueAtTime(.001,a.currentTime+.35);o.start();o.stop(a.currentTime+.36);}catch{}}
function showAlert(d){lastActivityAt=Date.now();showQueue();$('alertName').textContent=d.viewer||'';$('alertText').textContent=d.message||'';const img=$('alertAvatar');if(d.avatar){img.src=d.avatar;img.style.display='block';}else img.style.display='none';$('alert').classList.remove('hidden');beep();setTimeout(()=>$('alert').classList.add('hidden'),4500);}

function spin(data){
  clearHideTimer();clearRevealTimer();
  currentEntryId=data.entry.id;
  revealAtMs=Date.now()+REVEAL_AFTER_SPUN_MS;
  prizes=data.prizes.filter(p=>p.isEnabled&&p.weight>0);
  showStage();$('result').classList.add('hidden');
  const segs=segments();const target=segs.find(s=>s.p.name===data.prize.name)||segs[0];if(!target)return;
  const pointer=-Math.PI/2;
  const desired=pointer-target.mid+(Math.random()-.5)*(target.end-target.start)*.45;
  const current=((rotation%(Math.PI*2))+Math.PI*2)%(Math.PI*2);
  let delta=((desired-current)%(Math.PI*2)+Math.PI*2)%(Math.PI*2);delta+=Math.PI*2*6;
  const start=rotation,end=rotation+delta,t0=performance.now();
  animating=true;
  function frame(now){
    const t=Math.min(1,(now-t0)/SPIN_DURATION_MS);const ease=1-Math.pow(1-t,4);
    rotation=start+delta*ease;draw();
    if(t<1){requestAnimationFrame(frame);return;}
    animating=false;rotation=end;draw();
    const wait=revealAtMs-Date.now();
    if(wait>0){clearRevealTimer();revealTimer=setTimeout(()=>{revealTimer=null;showResult(data.entry.viewerDisplayName,data.prize.name,data.entry.id);loadQueue();},wait);}
    else{showResult(data.entry.viewerDisplayName,data.prize.name,data.entry.id);loadQueue();}
  }
  requestAnimationFrame(frame);
}

const es=new EventSource(`/api/events?client=overlay&key=${encodeURIComponent(key)}`);
es.addEventListener('alert',e=>{showAlert(JSON.parse(e.data));loadQueue();});
es.addEventListener('queue',loadQueue);
es.addEventListener('spin',e=>spin(JSON.parse(e.data)));
es.addEventListener('closed',()=>{setTimeout(loadQueue,700);});
es.addEventListener('config',loadQueue);
es.addEventListener('reload',()=>location.reload());
loadQueue();
