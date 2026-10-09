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
function lookAt(eye,target) {
    const forward = unit(eye.map((value,index) => target[index]-value));
    const side = unit(cross(forward,[0,1,0])), up = cross(side,forward);
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
    'uniform float uLowest; uniform float uHighest; uniform float uDrainageOverlay;',
    'void main(){',
    'vec3 n=normalize(vNormal); vec3 sun=normalize(vec3(0.88,0.19,0.41));',
    'float altitude=smoothstep(uLowest,uHighest,vGeology.x);',
    'float slope=length(n.xz); float exposed=smoothstep(0.025,0.26,slope);',
    'vec3 mineral=mix(vec3(0.51,0.32,0.21),vec3(0.66,0.50,0.37),altitude*0.72);',
    'mineral=mix(mineral,vec3(0.34,0.30,0.28),exposed*0.72);',
    'float wear=smoothstep(0.0,75.0,max(vGeology.y,0.0));',
    'float deposit=smoothstep(0.0,55.0,max(-vGeology.y,0.0));',
    'mineral=mix(mineral,vec3(0.41,0.31,0.26),wear*0.17);',
    'mineral=mix(mineral,vec3(0.70,0.53,0.36),deposit*0.12);',
    // Diagnostic coloration comes only from actual routed runoff and cut
    // sampled on the physical 3D mesh. It does not add fake water geometry.
    'float stream=smoothstep(12.0,110.0,vGeology.z)*smoothstep(8.0,90.0,max(vGeology.y,0.0));',
    'mineral=mix(mineral,vec3(0.18,0.63,0.64),stream*uDrainageOverlay*0.78);',
    'float angle=dot(n,sun); float raking=clamp((angle-sun.y)*3.45,-0.43,0.43);',
    'float light=clamp(0.43+0.62*max(angle,0.0)+raking,0.17,1.26);',
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
function elevationRange(heights) {
    let min=Infinity,max=-Infinity;
    // Do not spread 257² elevation samples into Math.min/Math.max: Safari has
    // a lower argument limit than desktop Chromium and throws RangeError.
    for(const height of heights){min=Math.min(min,height);max=Math.max(max,height);}
    return {min,max};
}
function buildMesh(region,mode) {
    const size=region.width, heights=mode==='before'?region.originalElevationMeters:region.evolvedElevationMeters;
    const {min:minOriginal,max:maxOriginal}=elevationRange(region.originalElevationMeters);
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
        // Diagnostic flow color represents physical upstream catchment area,
        // not how many raster pixels contributed to that catchment.
        vertices[base+8]=region.accumulatedRunoffCells[i]*spacing*spacing/1_000_000;
        min=Math.min(min,h);max=Math.max(max,h);
    }
    return {vertices,min,max};
}
function buildIndices(size){
    // A 257² mesh has 66,049 vertices: the last vertex index does not fit in
    // UNSIGNED_SHORT. WebGL2 supports UNSIGNED_INT without an extension.
    const result=size*size>65536
        ?new Uint32Array((size-1)*(size-1)*6)
        :new Uint16Array((size-1)*(size-1)*6);
    let i=0;
    for(let y=0;y<size-1;y++)for(let x=0;x<size-1;x++){
        const a=y*size+x,b=a+1,c=a+size,d=c+1;
        result.set([a,b,c,b,d,c],i);i+=6;
    }
    return result;
}
let scene=null;
function render() {
    if(!scene || !scene.region)return;
    const {gl,canvas,program:p,region}=scene;
    const rect=canvas.getBoundingClientRect(),scale=Math.min(window.devicePixelRatio||1,1.35);
    const width=Math.min(1024,Math.max(1,Math.round(rect.width*scale)));
    const height=Math.min(640,Math.max(1,Math.round(rect.height*scale)));
    if(canvas.width!==width||canvas.height!==height){canvas.width=width;canvas.height=height;}
    const span=(region.width-1)*region.cellSpacingMeters/1000,d=span*scene.zoom;
    const eye=[scene.focus[0]+Math.sin(scene.yaw)*Math.cos(scene.pitch)*d,
        scene.focus[1]+Math.sin(scene.pitch)*d,
        scene.focus[2]+Math.cos(scene.yaw)*Math.cos(scene.pitch)*d];
    gl.viewport(0,0,width,height);gl.clearColor(0.07,0.09,0.10,1.0);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
    gl.useProgram(p);
    gl.uniformMatrix4fv(gl.getUniformLocation(p,'uProjection'),false,perspective(Math.PI/3.3,width/height,0.1,d*5));
    gl.uniformMatrix4fv(gl.getUniformLocation(p,'uView'),false,lookAt(eye,scene.focus));
    gl.uniform1f(gl.getUniformLocation(p,'uLowest'),scene.min);
    gl.uniform1f(gl.getUniformLocation(p,'uHighest'),scene.max);
    gl.uniform1f(gl.getUniformLocation(p,'uDrainageOverlay'),scene.drainageOverlay?1.0:0.0);
    gl.bindVertexArray(scene.vao);gl.drawElements(gl.TRIANGLES,scene.indices,scene.indexType,0);gl.bindVertexArray(null);
    const error=gl.getError();
    window.__planetForgeHeroRegionStats={seed:region.seed,mode:scene.mode,gridWidth:region.width,
        triangles:scene.indices/3,indexBits:scene.indexType===gl.UNSIGNED_INT?32:16,
        cellSpacingMeters:region.cellSpacingMeters,regionSpanKilometers:(region.width-1)*region.cellSpacingMeters/1000,
        minElevationMeters:scene.min,maxElevationMeters:scene.max,actualReliefMeters:scene.max-scene.min,
        maximumCutMeters:scene.maximumCutMeters,framing:scene.fullRegion?'full':'valley',
        drainageOverlay:scene.drainageOverlay,
        hillslopeTransportedVolumeCubicMeters:region.hillslopeTransportedVolumeCubicMeters??0,
        hillslopeInitiallyUnstableEdges:region.hillslopeInitiallyUnstableEdges??0,
        erosionIterations:region.erosionIterations,focusCellIndex:region.focusCellIndex,cameraDistanceKm:d,glError:error};
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
    const pointers = new Map();
    let lastPinchDistance = 0;
    const pinchDistance = () => {
        const values = [...pointers.values()];
        return values.length < 2 ? 0 : Math.hypot(values[0][0]-values[1][0],values[0][1]-values[1][1]);
    };
    canvas.style.touchAction='none';
    canvas.addEventListener('pointerdown',event=>{
        pointers.set(event.pointerId,[event.clientX,event.clientY]);
        canvas.setPointerCapture(event.pointerId);
        lastPinchDistance=pinchDistance();
    });
    canvas.addEventListener('pointermove',event=>{
        if(!scene || !pointers.has(event.pointerId))return;
        const previous=pointers.get(event.pointerId);
        pointers.set(event.pointerId,[event.clientX,event.clientY]);
        if(pointers.size>1){
            const distance=pinchDistance();
            if(lastPinchDistance>0 && distance>0){
                scene.zoom=clamp(scene.zoom*lastPinchDistance/distance,0.13,2.35);
            }
            lastPinchDistance=distance;
        }else{
            scene.yaw+=(event.clientX-previous[0])*0.008;
            scene.pitch=clamp(scene.pitch-(event.clientY-previous[1])*0.007,0.13,1.20);
        }
        render();
    });
    const remove=event=>{pointers.delete(event.pointerId);lastPinchDistance=pinchDistance();};
    canvas.addEventListener('pointerup',remove);
    canvas.addEventListener('pointercancel',remove);
    canvas.addEventListener('lostpointercapture',remove);
    canvas.addEventListener('wheel',event=>{
        if(!scene)return;
        event.preventDefault();scene.zoom=clamp(scene.zoom*(event.deltaY>0?1.13:0.89),0.13,2.35);render();
    },{passive:false});
}
export function zoomHeroRegion(zoomIn){
    if(!scene || !scene.region)return;
    scene.zoom=clamp(scene.zoom*(zoomIn?0.78:1.28),0.13,2.35);
    render();
}

// Both views use exactly the same physical vertex positions and metres of
// elevation; only the inspection camera changes. Default to real channel relief.
export function setHeroDrainageOverlay(enabled){
    if(!scene || !scene.region)return;
    scene.drainageOverlay=Boolean(enabled);
    render();
}

export function setHeroRegionFraming(fullRegion){
    if(!scene || !scene.region)return;
    scene.fullRegion=Boolean(fullRegion);
    const span=(scene.region.width-1)*scene.region.cellSpacingMeters/1000;
    scene.zoom=scene.fullRegion?1.08:span<=8?0.40:span<=32?0.35:0.27;
    scene.pitch=scene.fullRegion?0.62:span<=8?0.62:span<=32?0.56:0.46;
    render();
}

export function drawHeroRegion(region,mode='after'){
    const canvas=document.getElementById('lab-hero-render');
    if(!canvas)throw Error('Missing hero region viewport');
    if(!Number.isInteger(region.width)||region.width<9||region.width>257)throw Error('Unsupported hero region grid width');
    const gl=scene?.gl??canvas.getContext('webgl2',{antialias:true,depth:true,preserveDrawingBuffer:true});
    if(!gl)throw Error('Hero region requires WebGL2');
    window.__planetForgeHeroRegionReady=false;
    if(!scene){
        const p=program(gl),vao=gl.createVertexArray(),vbo=gl.createBuffer(),ibo=gl.createBuffer();
        gl.bindVertexArray(vao);gl.bindBuffer(gl.ARRAY_BUFFER,vbo);
        for(let i=0;i<3;i++){gl.enableVertexAttribArray(i);gl.vertexAttribPointer(i,3,gl.FLOAT,false,36,i*12);}
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,ibo);gl.enable(gl.DEPTH_TEST);
        scene={canvas,gl,program:p,vao,vbo,ibo,yaw:0.65,pitch:0.23,zoom:0.79,indices:0,focus:[0,0,0]};
        controls(canvas);
    }
    const indices=buildIndices(region.width);
    gl.bindVertexArray(scene.vao);gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,scene.ibo);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER,indices,gl.STATIC_DRAW);
    scene.indexType=indices instanceof Uint32Array?gl.UNSIGNED_INT:gl.UNSIGNED_SHORT;
    // The same watershed-selected focus is used to sample the nested geological
    // solve. This keeps the player camera on the actual downstream tributaries.
    // View framing never displaces or exaggerates the underlying physical mesh.
    const middle=(region.width-1)*0.5;
    const focusIndex=Number.isInteger(region.focusCellIndex)
        && region.focusCellIndex>=0 && region.focusCellIndex<region.width*region.width
        ?region.focusCellIndex
        :Math.floor(middle)*region.width+Math.floor(middle);
    const fx=focusIndex%region.width,fy=Math.floor(focusIndex/region.width);
    const {min:baseMin,max:baseMax}=elevationRange(region.originalElevationMeters);
    const base=(baseMin+baseMax)*0.5;
    const km=region.cellSpacingMeters/1000;
    scene.focus=[(fx-middle)*km,(region.evolvedElevationMeters[focusIndex]-base)/1000,-(fy-middle)*km];
    // Frame actual valleys close enough to resolve physical slopes on mobile.
    // Previously nested terrain inherited the overview camera framing.
    const regionSpanKm = (region.width - 1) * region.cellSpacingMeters / 1000;
    scene.region=region;
    scene.indices=indices.length;
    scene.maximumCutMeters=0;
    for(const cut of region.cumulativeCutMeters)scene.maximumCutMeters=Math.max(scene.maximumCutMeters,cut);
    scene.fullRegion=false;
    scene.drainageOverlay=true;
    // Keep actual elevation metres. A close camera, not artificial vertical
    // exaggeration, is needed to see a few-hundred-metre valley in a 128 km tile.
    scene.zoom=regionSpanKm<=8?0.40:regionSpanKm<=32?0.35:0.27;
    scene.pitch=regionSpanKm<=8?0.62:regionSpanKm<=32?0.56:0.46;
    useMode(mode);
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
