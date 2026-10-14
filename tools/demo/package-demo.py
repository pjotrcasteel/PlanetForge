"""Package the same main-page prototype modules into an offline playable HTML."""
from pathlib import Path
root = Path(__file__).resolve().parents[2]
web = root / 'src/PlanetForge.Presentation.Web/wwwroot'
model = (web / 'js/formationDemoModel.mjs').read_text().replace('export class ', 'class ')
renderer = (web / 'js/formationDemo.js').read_text().replace("import { FormationDemoWorld } from './formationDemoModel.mjs';", '').replace('export function ', 'function ')
html = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>PlanetForge · playable formation demo</title><style>
*{box-sizing:border-box}body{margin:0;background:#030609;color:#ece7dc;font-family:system-ui,sans-serif;overflow:hidden}
canvas{width:100vw;height:100dvh;display:block}.hud{position:absolute;top:24px;left:24px;max-width:360px;padding:20px;background:#0b1115dd;border:1px solid #ffffff22;border-radius:20px}
small{color:#c8a476;letter-spacing:.18em;font-size:10px}h1{font-size:23px;font-weight:500;margin:10px 0}p{font-size:13px;line-height:1.6;color:#a3abae;margin:8px 0}
.controls{position:absolute;bottom:32px;left:50%;transform:translateX(-50%);display:flex;gap:8px;padding:10px;background:#0b1115ed;border:1px solid #ffffff22;border-radius:20px}
button,select{border:0;border-radius:12px;padding:14px;font-size:15px;background:#1d292d;color:#eee;cursor:pointer}#play{background:#dc9850;color:#17100a;min-width:80px}
button:disabled{opacity:.5}details{max-width:290px;font-size:12px;color:#919da3}summary{cursor:pointer;margin-top:12px}#readout{font-variant-numeric:tabular-nums}
.hint{position:absolute;bottom:112px;text-align:center;width:100%;font-size:11px;letter-spacing:.1em;color:#829094;pointer-events:none}
@media(max-width:600px){.hud{top:14px;left:14px;padding:14px;max-width:270px}h1{font-size:18px}.controls{bottom:20px;gap:5px;padding:8px}button,select{padding:12px 10px;font-size:13px}.hint{bottom:98px}}
</style><canvas id="planet-canvas" aria-label="Interactive planet formation prototype"></canvas>
<div class="hud"><small>PLANETFORGE · PLAYABLE PROTOTYPE</small><h1 id="phase">Magma ocean</h1><p id="readout">Initializing…</p>
<details><summary>Model and controls</summary><p>Lava flows between neighboring cells, cools into basalt and receives material from a finite volcanic reservoir. Crust insulates the surface; a simplified convergence rule raises relief.</p><p>Accelerated, dimensionless rules and exaggerated relief. This is a visual prototype, not validated Earth physics. No atmosphere, oceans or life yet.</p><p>Drag to orbit. Scroll to zoom. Replay uses the same seed and state rules.</p></details></div>
<div class="hint">DRAG TO ORBIT · SCROLL TO EXPLORE</div><div class="controls"><button id="play">Play</button><button id="step">+5 s</button>
<select id="speed" aria-label="Demo time per step"><option value="5">5 s</option><option value="15">15 s</option><option value="30">30 s</option></select><button id="reset">Restart</button></div>
<script type="module">'''
controls = '''
const phase=document.getElementById('phase'),readout=document.getElementById('readout'),play=document.getElementById('play');
let playing=false,busy=false;
const show=s=>{phase.textContent=s.phase;readout.textContent=`Demo T+${Math.floor(s.seconds)} s · crust coverage ${Math.round(s.coverage*100)}%`;};
const run=()=>{if(busy)return;busy=true;try{show(advance(Number(document.getElementById('speed').value)));}catch(e){playing=false;play.textContent='Play';readout.textContent=e.message;}finally{busy=false;}};
try{show(initialize('planet-canvas',24061984));}catch(e){readout.textContent=e.message;document.querySelectorAll('button').forEach(b=>b.disabled=true);}
play.onclick=()=>{playing=!playing;play.textContent=playing?'Pause':'Play';play.setAttribute('aria-pressed',String(playing));};
document.getElementById('step').onclick=()=>{playing=false;play.textContent='Play';run();};
document.getElementById('speed').onchange=e=>document.getElementById('step').textContent='+'+e.target.value+' s';
document.getElementById('reset').onclick=()=>{playing=false;play.textContent='Play';show(restart(24061984));};
setInterval(()=>{if(playing&&!document.hidden)run();},250);
window.addEventListener('pagehide',dispose);
</script></html>'''
(web / 'planetforge-demo.html').write_text(html + model + renderer + controls)
print(web / 'planetforge-demo.html')
