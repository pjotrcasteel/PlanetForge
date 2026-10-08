// World Machine-inspired 3D geological research renderer. All vertex heights
// come from canonical and evolved physical bedrock, without shader displacement.
const clamp = (value, a, b) => Math.max(a, Math.min(b, value));
const dot = (a, b) => a[0]*b[0] + a[1]*b[1] + a[2]*b[2];
const cross = (a, b) => [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]];
function unit(v) { const d = Math.hypot(...v) || 1; return v.map(n => n/d); }
function perspective(fov, aspect, near, far) {
    const f = 1/Math.tan(fov/2);
    return new Float32Array([f/aspect,0,0,0,0,f,0,0,0,0,(far+near)/(near-far),-1,0,0,2*far*near/(near-far),0]);
}
function lookAt(eye) {
    const forward = unit(eye.map(x => -x)), side = unit(cross(forward,[0,1,0])), up = cross(side,forward);
    return new Float32Array([side[0],up[0],-forward[0],0,side[1],up[1],-forward[1],0,side[2],up[2],-forward[2],0,
        -dot(side,eye),-dot(up,eye),dot(forward,eye),1]);
}
function shader(gl,type,source) {
    const value = gl.createShader(type);
    gl.shaderSource(value,source); gl.compileShader(value);
    if (!gl.getShaderParameter(value,gl.COMPILE_STATUS)) throw Error(gl.getShaderInfoLog(value));
    return value;
}
const vertexShader = [
    '#version 300 es','precision highp float;',
    'layout(location=0) in vec3 aPosition; layout(location=1) in vec3 aNormal; layout(location=2) in vec3 aGeology;',
    'uniform mat4 uProjection; uniform mat4 uView;',
    'out vec3 vNormal; out vec3 vGeology; out vec3 vPosition;',
    'void main(){ vNormal=aNormal; vGeology=aGeology; vPosition=aPosition;',
    'gl_Position=uProjection*uView*vec4(aPosition,1.0); }'
].join('\n');
const fragmentShader = [
    '#version 300 es','precision highp float;',
    'in vec3 vNormal; in vec3 vGeology; in vec3 vPosition; out vec4 outColor;',
    'uniform float uLowest; uniform float uHighest;',
    'void main(){',
    'vec3 n=normalize(vNormal); vec3 sun=normalize(vec3(0.83,0.38,0.42));',
    'float altitude=smoothstep(uLowest,uHighest,vGeology.x);',
    'float exposed=smoothstep(0.012,0.17,1.0-n.y);',
    'vec3 mineral=mix(vec3(0.51,0.32,0.21),vec3(0.66,0.50,0.37),altitude*0.72);',
    'mineral=mix(mineral,vec3(0.34,0.30,0.28),exposed*0.72);',
    'float wear=smoothstep(0.0,75.0,max(vGeology.y,0.0));',
    'float deposit=smoothstep(0.0,55.0,max(-vGeology.y,0.0));',
    'mineral=mix(mineral,vec3(0.41,0.31,0.26),wear*0.17);',
    'mineral=mix(mineral,vec3(0.70,0.53,0.36),deposit*0.12);',
    'float light=clamp(0.24+0.93*max(dot(n,sun),0.0),0.22,1.16);',
    'float fog=smoothstep(65.0,155.0,length(vPosition.xz))*0.15;',
    'outColor=vec4(mix(mineral*light,vec3(0.32,0.32,0.31),fog),1.0); }'
].join('\n');
function program(gl) {
    const p=gl.createProgram(), vs=shader(gl,gl.VERTEX_SHADER,vertexShader), fs=shader(gl,gl.FRAGMENT_SHADER,fragmentShader);
    gl.attachShader(p,vs);gl.attachShader(p,fs);gl.linkProgram(p);
    gl.deleteShader(vs);gl.deleteShader(fs);
    if(!gl.getProgramParameter(p,gl.LINK_STATUS)) throw Error(gl.getProgramInfoLog(p));
    return p;
}
function buildMesh(region,mode) {
    const size=region.width, heights=mode==='before'?region.originalElevationMeters:region.evolvedElevationMeters;
    const minOriginal=Math.min(...region.originalElevationMeters),maxOriginal=Math.max(...region.originalElevationMeters);
    const origin=(minOriginal+maxOriginal)*0.5, spacing=region.cellSpacingMeters, positionStep=spacing/1000;
    const vertices=new Float32Array(size*size*9);
    let min=Infinity,max=-Infinity;
    for(let y=0;y<size;y++)for(let x=0;x<size;x++){
        const i=y*size+x,h=heights[i],west=heights[y*size+Math.max(0,x-1)],east=heights[y*size+Math.min(size-1,x+1)];
        const south=heights[Math.max(0,y-1)*size+x],north=heights[Math.min(size-1,y+1)*size+x];
        const dhdx=(east-west)/((Math.min(size-1,x+1)-Math.max(0,x-1))*spacing);
        const dhdn=(north-south)/((Math.min(size-1,y+1)-Math.max(0,y-1))*spacing);
        const normal=unit([-dhdx,1,dhdn]),base=i*9;
        vertices[base]=(x-(size-1)*0.5)*positionStep;
        vertices[base+1]=(h-origin)/1000;
        vertices[base+2]=-(y-(size-1)*0.5)*positionStep;
        vertices.set(normal,base+3);
        vertices[base+6]=h;
        vertices[base+7]=mode==='before'?0:region.cumulativeCutMeters[i];
        vertices[base+8]=region.accumulatedRunoffCells[i];
        min=Math.min(min,h);max=Math.max(max,h);
    }
    return {vertices,min,max};
}
function buildIndices(size){
    const result=new Uint16Array((size-1)*(size-1)*6);
    let i=0;
    for(let y=0;y<size-1;y++)for(let x=0;x<size-1;x++){
        const a=y*size+x,b=a+1,c=a+size,d=c+1;
        result.set([a,b,c,b,d,c],i);i+=6;
    }
    return result;
}
let scene=null;
function render() {
    if(!scene)return;
    const {gl,canvas,program:p,region}=scene;
    const rect=canvas.getBoundingClientRect(),scale=Math.min(window.devicePixelRatio||1,1.35);
    const width=Math.min(1024,Math.max(1,Math.round(rect.width*scale)));
    const height=Math.min(640,Math.max(1,Math.round(rect.height*scale)));
    if(canvas.width!==width||canvas.height!==height){canvas.width=width;canvas.height=height;}
    const span=(region.width-1)*region.cellSpacingMeters/1000,d=span*scene.zoom;
    const eye=[Math.sin(scene.yaw)*Math.cos(scene.pitch)*d,Math.sin(scene.pitch)*d,Math.cos(scene.yaw)*Math.cos(scene.pitch)*d];
    gl.viewport(0,0,width,height);gl.clearColor(0.07,0.09,0.10,1.0);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
    gl.useProgram(p);
    gl.uniformMatrix4fv(gl.getUniformLocation(p,'uProjection'),false,perspective(Math.PI/3.3,width/height,0.1,d*5));
    gl.uniformMatrix4fv(gl.getUniformLocation(p,'uView'),false,lookAt(eye));
    gl.uniform1f(gl.getUniformLocation(p,'uLowest'),scene.min);
    gl.uniform1f(gl.getUniformLocation(p,'uHighest'),scene.max);
    gl.bindVertexArray(scene.vao);gl.drawElements(gl.TRIANGLES,scene.indices,gl.UNSIGNED_SHORT,0);gl.bindVertexArray(null);
    const error=gl.getError();
    window.__planetForgeHeroRegionStats={seed:region.seed,mode:scene.mode,gridWidth:region.width,
        triangles:scene.indices/3,minElevationMeters:scene.min,maxElevationMeters:scene.max,
        erosionIterations:region.erosionIterations,glError:error};
    window.__planetForgeHeroRegionReady=error===gl.NO_ERROR;
}
function useMode(mode){
    if(!scene) return;
    const {vertices,min,max}=buildMesh(scene.region,mode);
    scene.mode=mode;scene.min=min;scene.max=max;
    scene.gl.bindBuffer(scene.gl.ARRAY_BUFFER,scene.vbo);
    scene.gl.bufferData(scene.gl.ARRAY_BUFFER,vertices,scene.gl.STATIC_DRAW);
    render();
}
function controls(canvas){
    let last=null;
    canvas.style.touchAction='none';
    canvas.addEventListener('pointerdown',event=>{last=[event.clientX,event.clientY];canvas.setPointerCapture(event.pointerId);});
    canvas.addEventListener('pointermove',event=>{
        if(!last||!scene)return;
        scene.yaw+=(event.clientX-last[0])*0.008;
        scene.pitch=clamp(scene.pitch-(event.clientY-last[1])*0.007,0.13,1.20);
        last=[event.clientX,event.clientY];render();
    });
    canvas.addEventListener('pointerup',()=>last=null);
    canvas.addEventListener('pointercancel',()=>last=null);
    canvas.addEventListener('wheel',event=>{
        if(!scene)return;
        event.preventDefault();scene.zoom=clamp(scene.zoom*(event.deltaY>0?1.13:0.89),0.48,2.35);render();
    },{passive:false});
}
export function drawHeroRegion(region,mode='after'){
    const canvas=document.getElementById('lab-hero-render');
    if(!canvas)throw Error('Missing hero region viewport');
    if(region.width*region.width>65535)throw Error('Hero region exceeds 16-bit WebGL index budget');
    const gl=scene?.gl??canvas.getContext('webgl2',{antialias:true,depth:true,preserveDrawingBuffer:true});
    if(!gl)throw Error('Hero region requires WebGL2');
    window.__planetForgeHeroRegionReady=false;
    if(!scene){
        const p=program(gl),vao=gl.createVertexArray(),vbo=gl.createBuffer(),ibo=gl.createBuffer();
        gl.bindVertexArray(vao);gl.bindBuffer(gl.ARRAY_BUFFER,vbo);
        for(let i=0;i<3;i++){gl.enableVertexAttribArray(i);gl.vertexAttribPointer(i,3,gl.FLOAT,false,36,i*12);}
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,ibo);gl.enable(gl.DEPTH_TEST);
        scene={canvas,gl,program:p,vao,vbo,ibo,yaw:0.65,pitch:0.46,zoom:1.14,indices:0};
        controls(canvas);
    }
    const indices=buildIndices(region.width);
    gl.bindVertexArray(scene.vao);gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,scene.ibo);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER,indices,gl.STATIC_DRAW);
    scene.region=region;scene.indices=indices.length;useMode(mode);
}
export function setHeroRegionMode(mode){
    if(mode==='before'||mode==='after')useMode(mode);
}

export function resetHeroRegion() {
    window.__planetForgeHeroRegionReady = false;
    window.__planetForgeHeroRegionStats = null;
    if (!scene) return;
    scene.gl.viewport(0, 0, scene.canvas.width, scene.canvas.height);
    scene.gl.clearColor(0.07, 0.09, 0.10, 1);
    scene.gl.clear(scene.gl.COLOR_BUFFER_BIT | scene.gl.DEPTH_BUFFER_BIT);
    scene.region = null;
}
