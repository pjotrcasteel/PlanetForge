import { FormationDemoWorld } from './formationDemoModel.mjs';
let instance;
export function initialize(canvasId, seed) {
    dispose();
    const canvas = document.getElementById(canvasId), gl = canvas.getContext('webgl2', { antialias: true, alpha: false });
    if (!gl) throw new Error('The playable demo requires WebGL 2.');
    const vertex = `#version 300 es
    precision highp float;
    layout(location=0) in vec2 grid;
    uniform sampler2D field; uniform vec2 rotation; uniform float aspect; uniform float zoom;
    out vec3 normal; out vec3 point; out vec2 uv;
    vec3 turn(vec3 p) {
        float c=cos(rotation.x),s=sin(rotation.x); p=vec3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z);
        c=cos(rotation.y);s=sin(rotation.y);return vec3(p.x,c*p.y-s*p.z,s*p.y+c*p.z);
    }
    void main(){
        uv=grid; float y=1.-2.*grid.y; float a=grid.x*6.2831853;float r=sqrt(max(0.,1.-y*y));
        vec3 p=vec3(r*cos(a),y,r*sin(a)); vec4 state=texture(field,grid);
        float relief=min(state.b,2.)*.07+min(state.g,2.)*.015;
        point=turn(p*(1.+relief));normal=turn(p);
        float depth=zoom-point.z;
        float scale=min(2.4,aspect*2.7);
        gl_Position=vec4(point.x*scale/aspect,point.y*scale,depth*.9-.15,depth);
    }`;
    const fragment = `#version 300 es
    precision highp float;
    uniform sampler2D field; in vec3 normal; in vec3 point; in vec2 uv; out vec4 color;
    void main(){
        vec4 state=texture(field,uv);float crust=clamp(state.g/.18,0.,1.);
        vec3 n=normalize(cross(dFdx(point),dFdy(point)));if(dot(n,normal)<0.)n=-n;
        float light=max(0.,dot(n,normalize(vec3(-.6,.7,1.))));
        // Optical texture only: roughness follows the simulated frozen inventory.
        float grain=fract(sin(dot(floor(uv*vec2(1600.,800.)),vec2(12.9898,78.233)))*43758.5453);
        vec3 basalt=mix(vec3(.13,.135,.145),vec3(.19,.17,.15),grain*.4)*(.38+light*.9);
        float heat=clamp((state.r-.2)/1.25,0.,1.);
        vec3 lava=mix(vec3(.35,.018,.002),vec3(1.,.24,.012),heat);
        lava+=vec3(.18,.045,.002)*clamp(state.a*8.,0.,1.);
        // The liquid/solid boundary comes from the evolving cell inventory.
        float solid=smoothstep(.25,.85,crust);
        vec3 surface=mix(lava,basalt,solid);
        float edge=(1.-smoothstep(.7,1.,crust))*smoothstep(.05,.4,crust);
        surface+=vec3(.32,.04,.003)*edge;
        float rim=pow(1.-max(0.,normalize(normal).z),4.);
        surface+=vec3(.055,.021,.012)*rim;
        color=vec4(pow(surface,vec3(.85)),1.);
    }`;
    const compile = (type, source) => {
        const shader = gl.createShader(type); gl.shaderSource(shader, source); gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
        return shader;
    };
    const program = gl.createProgram(), shaders = [compile(gl.VERTEX_SHADER, vertex), compile(gl.FRAGMENT_SHADER, fragment)];
    shaders.forEach(s => gl.attachShader(program, s)); gl.linkProgram(program);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
    shaders.forEach(s => gl.deleteShader(s));
    const world = new FormationDemoWorld(seed), data = new Float32Array(world.lava.length * 4);
    const texture = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    // Float linear filtering is not guaranteed on mobile: use a filterable half-float texture instead.
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA16F, world.width, world.height, 0, gl.RGBA, gl.FLOAT, data);
    const vertices = [], w = 256, h = 128;
    for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) {
        for (const [x,y] of [[i,j],[i,j+1],[i+1,j],[i+1,j],[i,j+1],[i+1,j+1]]) vertices.push(x/w,y/h);
    }
    const vao=gl.createVertexArray(), buffer=gl.createBuffer();gl.bindVertexArray(vao);gl.bindBuffer(gl.ARRAY_BUFFER,buffer);
    gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(vertices),gl.STATIC_DRAW);gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,2,gl.FLOAT,false,0,0);
    const uniforms=Object.fromEntries(['field','rotation','aspect','zoom'].map(n=>[n,gl.getUniformLocation(program,n)]));
    const abort=new AbortController();let drag,lastX,lastY;const camera={x:-.4,y:.12,zoom:3.4};
    canvas.style.touchAction='none';
    canvas.addEventListener('pointerdown',e=>{drag=true;lastX=e.clientX;lastY=e.clientY;canvas.setPointerCapture(e.pointerId);},{signal:abort.signal});
    canvas.addEventListener('pointermove',e=>{if(drag){camera.x+=(e.clientX-lastX)*.006;camera.y=Math.max(-1.2,Math.min(1.2,camera.y+(e.clientY-lastY)*.006));lastX=e.clientX;lastY=e.clientY;}},{signal:abort.signal});
    canvas.addEventListener('pointerup',()=>drag=false,{signal:abort.signal});
    canvas.addEventListener('pointercancel',()=>drag=false,{signal:abort.signal});
    const touches = new Map(); let pinchDistance = 0;
    const distance = () => { const [a,b] = [...touches.values()]; return Math.hypot(a.x-b.x,a.y-b.y); };
    canvas.addEventListener('pointerdown', e => {
        touches.set(e.pointerId,{x:e.clientX,y:e.clientY});
        if(touches.size===2) { pinchDistance=distance(); drag=false; }
    },{signal:abort.signal});
    canvas.addEventListener('pointermove', e => {
        if(!touches.has(e.pointerId)) return;
        touches.set(e.pointerId,{x:e.clientX,y:e.clientY});
        if(touches.size===2) {
            const next=distance();
            if(next>0) camera.zoom=Math.max(1.5,Math.min(5,camera.zoom*pinchDistance/next));
            pinchDistance=next;
        }
    },{signal:abort.signal});
    for(const event of ['pointerup','pointercancel']) canvas.addEventListener(event,e=>touches.delete(e.pointerId),{signal:abort.signal});
    canvas.addEventListener('wheel' ,e=>{e.preventDefault();camera.zoom=Math.max(1.5,Math.min(5,camera.zoom+e.deltaY*.002));},{passive:false,signal:abort.signal});
    instance={world,canvas,gl,program,texture,buffer,vao,abort,camera,dirty:true,frame:0};
    const draw=()=>{
        const s=instance;if(!s)return;
        const dpr=Math.min(window.devicePixelRatio||1,2),rect=canvas.getBoundingClientRect();
        const width=Math.max(1,Math.round(rect.width*dpr)),height=Math.max(1,Math.round(rect.height*dpr));
        if(canvas.width!==width||canvas.height!==height){canvas.width=width;canvas.height=height;}
        gl.viewport(0,0,width,height);gl.clearColor(.008,.011,.018,1);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
        gl.enable(gl.DEPTH_TEST);gl.useProgram(program);gl.bindVertexArray(vao);gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,texture);
        if(s.dirty){for(let i=0;i<world.lava.length;i++){data[i*4]=world.heat[i];data[i*4+1]=world.crust[i];data[i*4+2]=world.uplift[i];data[i*4+3]=world.flux[i];}
            gl.texSubImage2D(gl.TEXTURE_2D,0,0,0,world.width,world.height,gl.RGBA,gl.FLOAT,data);s.dirty=false;}
        gl.uniform1i(uniforms.field,0);gl.uniform2f(uniforms.rotation,camera.x,camera.y);gl.uniform1f(uniforms.aspect,width/height);gl.uniform1f(uniforms.zoom,camera.zoom);
        gl.drawArrays(gl.TRIANGLES,0,vertices.length/2);s.frame=requestAnimationFrame(draw);
    };draw();return world.stats();
}
export function advance(seconds){if(!instance)throw new Error('Initialize the demo first.');const result=instance.world.advance(seconds);instance.dirty=true;return result;}
export function restart(seed){const id=instance.canvas.id;return initialize(id,seed);}
export function dispose(){if(!instance)return;const s=instance;instance=null;cancelAnimationFrame(s.frame);s.abort.abort();
    s.gl.deleteBuffer(s.buffer);s.gl.deleteVertexArray(s.vao);s.gl.deleteTexture(s.texture);s.gl.deleteProgram(s.program);}
