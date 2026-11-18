// Visual effects: GPU particles (sparks, dust, smoke, embers, wisps, fireflies), light beams over loot, expanding rings,
// and the post-processing chain (bloom and a colour grade). Purely presentation; nothing here reads or changes the match.
import * as THREE from './vendor/three.module.js';
import { EffectComposer } from './vendor/EffectComposer.js';
import { RenderPass } from './vendor/RenderPass.js';
import { UnrealBloomPass } from './vendor/UnrealBloomPass.js';
import { OutputPass } from './vendor/OutputPass.js';
import { ShaderPass } from './vendor/ShaderPass.js';
import { FXAAShader } from './vendor/FXAAShader.js';

/** True when WebGL runs on a software rasterizer (SwiftShader, llvmpipe): post-processing is skipped there. */
export function softwareRenderer(renderer) {
  try {
    const gl = renderer.getContext(), info = gl.getExtension('WEBGL_debug_renderer_info');
    const name = String(info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER));
    return /swiftshader|llvmpipe|software|basic render/i.test(name);
  } catch { return true; }
}

// A gentle grade after tone mapping: warm highlights, cool shadows and a little more saturation.
const GradeShader = {
  uniforms: { tDiffuse: { value: null }, uAmount: { value: 1 } },
  vertexShader: 'varying vec2 vUv;\nvoid main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
  fragmentShader: `uniform sampler2D tDiffuse; uniform float uAmount; varying vec2 vUv;
void main() {
  vec4 color = texture2D(tDiffuse, vUv);
  float l = dot(color.rgb, vec3(.299, .587, .114));
  vec3 graded = mix(vec3(l), color.rgb, 1.12);
  graded += vec3(.035, .015, -.02) * smoothstep(.5, 1.0, l) + vec3(-.01, .0, .018) * (1.0 - smoothstep(.0, .3, l));
  gl_FragColor = vec4(mix(color.rgb, graded, uAmount), color.a);
}`
};

/**
 * Bloom, a colour grade and FXAA, drawn through a half-float target so glowing things can exceed white. Only used on
 * high quality with a real GPU. A multisampled target cost about 15 ms a frame on an Intel UHD 620, so edges are
 * smoothed by FXAA after the grade instead.
 */
export class PostProcessing {
  constructor(renderer, scene, camera) {
    const size = renderer.getDrawingBufferSize(new THREE.Vector2());
    this.target = new THREE.WebGLRenderTarget(size.x, size.y, { type: THREE.HalfFloatType });
    this.composer = new EffectComposer(renderer, this.target);
    this.composer.addPass(new RenderPass(scene, camera));
    this.bloom = new UnrealBloomPass(new THREE.Vector2(size.x, size.y), .42, .5, .86);
    this.composer.addPass(this.bloom);
    this.composer.addPass(new OutputPass());
    this.grade = new ShaderPass(GradeShader);
    this.composer.addPass(this.grade);
    this.fxaa = new ShaderPass(FXAAShader);
    this.composer.addPass(this.fxaa);
    this.fxaa.material.uniforms.resolution.value.set(1 / size.x, 1 / size.y);
  }
  setSize(width, height, pixelRatio) {
    this.composer.setPixelRatio(pixelRatio); this.composer.setSize(width, height);
    this.fxaa.material.uniforms.resolution.value.set(1 / Math.max(1, width * pixelRatio), 1 / Math.max(1, height * pixelRatio));
  }
  render(dt) { this.composer.render(dt); }
  dispose() { this.composer.dispose(); this.bloom.dispose(); this.grade.material.dispose(); this.fxaa.material.dispose(); this.target.dispose(); }
}

const PARTICLE_VERTEX = `
uniform float uTime;
uniform float uScale;
attribute vec3 velocity;
attribute vec4 params; // birth time, life, size, gravity
attribute vec4 tint;   // colour, size at the end of life relative to the start
varying vec4 vColor;
#include <fog_pars_vertex>
void main() {
  float t = uTime - params.x, age = t / params.y;
  if (age < 0.0 || age >= 1.0) { gl_Position = vec4(0.0, 0.0, 2.0, 1.0); gl_PointSize = 0.0; vColor = vec4(0.0); return; }
  vec3 p = position + velocity * t;
  p.y -= .5 * params.w * t * t;
  vec4 mvPosition = modelViewMatrix * vec4(p, 1.0);
  gl_Position = projectionMatrix * mvPosition;
  gl_PointSize = params.z * mix(1.0, tint.a, age) * uScale / max(1.0, -mvPosition.z);
  vColor = vec4(tint.rgb, smoothstep(0.0, .1, age) * (1.0 - smoothstep(.5, 1.0, age)));
  #include <fog_vertex>
}`;
const PARTICLE_FRAGMENT = `
varying vec4 vColor;
#include <fog_pars_fragment>
void main() {
  vec2 c = gl_PointCoord - .5;
  float d = dot(c, c) * 4.0;
  if (d > 1.0) discard;
  float a = 1.0 - d;
  gl_FragColor = vec4(vColor.rgb, vColor.a * a * a);
  #ifdef ADDITIVE
    // Added light fades into the fog instead of taking its colour.
    #ifdef USE_FOG
      #ifdef FOG_EXP2
        gl_FragColor.a *= exp(-fogDensity * fogDensity * vFogDepth * vFogDepth);
      #else
        gl_FragColor.a *= 1.0 - smoothstep(fogNear, fogFar, vFogDepth);
      #endif
    #endif
  #else
    #include <fog_fragment>
  #endif
  #include <tonemapping_fragment>
  #include <colorspace_fragment>
}`;

// A ring buffer of particles drawn as one point cloud; the GPU moves them, the CPU only writes new ones.
class ParticlePool {
  constructor(capacity, additive) {
    this.capacity = capacity; this.next = 0; this.dirtyFrom = Infinity; this.dirtyTo = -1;
    const geometry = new THREE.BufferGeometry();
    this.position = new THREE.BufferAttribute(new Float32Array(capacity * 3), 3).setUsage(THREE.DynamicDrawUsage);
    this.velocity = new THREE.BufferAttribute(new Float32Array(capacity * 3), 3).setUsage(THREE.DynamicDrawUsage);
    this.params = new THREE.BufferAttribute(new Float32Array(capacity * 4).fill(-100), 4).setUsage(THREE.DynamicDrawUsage);
    this.tint = new THREE.BufferAttribute(new Float32Array(capacity * 4), 4).setUsage(THREE.DynamicDrawUsage);
    for (let i = 0; i < capacity; i++) this.params.array[i * 4 + 1] = 1;
    geometry.setAttribute('position', this.position); geometry.setAttribute('velocity', this.velocity);
    geometry.setAttribute('params', this.params); geometry.setAttribute('tint', this.tint);
    this.material = new THREE.ShaderMaterial({
      uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, { uTime: { value: 0 }, uScale: { value: 800 } }]),
      vertexShader: PARTICLE_VERTEX, fragmentShader: PARTICLE_FRAGMENT, transparent: true, depthWrite: false, fog: true,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending, defines: additive ? { ADDITIVE: '' } : {}
    });
    this.points = new THREE.Points(geometry, this.material);
    this.points.frustumCulled = false; this.points.renderOrder = additive ? 3 : 2;
    this.points.name = additive ? 'particles:glow' : 'particles:dust';
  }
  write(time, x, y, z, vx, vy, vz, life, size, gravity, r, g, b, grow) {
    const i = this.next; this.next = (this.next + 1) % this.capacity;
    this.position.array.set([x, y, z], i * 3); this.velocity.array.set([vx, vy, vz], i * 3);
    this.params.array.set([time, life, size, gravity], i * 4); this.tint.array.set([r, g, b, grow], i * 4);
    this.dirtyFrom = Math.min(this.dirtyFrom, i); this.dirtyTo = Math.max(this.dirtyTo, i);
  }
  upload() {
    if (this.dirtyTo < 0) return;
    const from = this.dirtyFrom, count = this.dirtyTo - from + 1;
    for (const [attribute, width] of [[this.position, 3], [this.velocity, 3], [this.params, 4], [this.tint, 4]]) {
      attribute.clearUpdateRanges(); attribute.addUpdateRange(from * width, count * width); attribute.needsUpdate = true;
    }
    this.dirtyFrom = Infinity; this.dirtyTo = -1;
  }
  dispose() { this.points.geometry.dispose(); this.material.dispose(); }
}

const BEAM_VERTEX = 'varying vec2 vUv;\nvoid main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }';
const BEAM_FRAGMENT = `uniform vec3 uColor; uniform float uTime; varying vec2 vUv;
void main() {
  float fade = pow(1.0 - vUv.y, 1.6) * (.65 + .35 * sin(uTime * 3.0 + vUv.y * 9.0 + vUv.x * 6.2831));
  gl_FragColor = vec4(uColor * 1.6, fade * .55);
  #include <colorspace_fragment>
}`;

/** Particles, beams and rings. `quality` and reduced motion scale the amount; nothing here is needed to play. */
export class Effects {
  constructor(scene) {
    this.scene = scene; this.time = 0; this.amount = 1; this.still = false; this.emitted = 0;
    this.glow = new ParticlePool(3072, true); this.dust = new ParticlePool(1536, false);
    scene.add(this.glow.points, this.dust.points);
    this.rings = []; this.beamGeometry = new THREE.CylinderGeometry(.3, .85, 9, 16, 1, true).translate(0, 4.5, 0);
    this.beamUniforms = { uTime: { value: 0 } }; this.beamMaterials = new Map(); this.emitters = [];
    this.color = new THREE.Color();
  }
  get active() { return this.rings.length; }
  configure(quality, reducedMotion, viewportHeight, fov) {
    this.amount = quality === 'low' ? .4 : 1; this.still = reducedMotion;
    const scale = viewportHeight / (2 * Math.tan(THREE.MathUtils.degToRad(fov) / 2));
    this.glow.material.uniforms.uScale.value = scale; this.dust.material.uniforms.uScale.value = scale;
  }
  // One emission. `o` is a recipe: count, colour(s), speed, spread, upward push, life, size, gravity and growth.
  emit(x, y, z, o) {
    const count = Math.max(1, Math.round((o.count || 8) * this.amount * (this.still ? .4 : 1)));
    const pool = o.additive === false ? this.dust : this.glow, color = this.color;
    this.emitted += count;
    for (let k = 0; k < count; k++) {
      color.set(o.colors ? o.colors[k % o.colors.length] : o.color || '#ffffff');
      if (o.bright) color.multiplyScalar(o.bright);
      const angle = Math.random() * Math.PI * 2, tilt = (o.flat ? 0 : Math.random() - .3) * (o.spread ?? 1);
      const speed = (o.speed ?? 3) * (.5 + Math.random() * .7);
      const radius = (o.radius || 0) * Math.sqrt(Math.random());
      pool.write(this.time + (o.stagger || 0) * Math.random(),
        x + Math.cos(angle) * radius, y + (o.height || 0) * Math.random(), z + Math.sin(angle) * radius,
        Math.cos(angle) * speed * (o.flat ? 1 : Math.cos(tilt)), (o.up ?? 2) * (.6 + Math.random() * .8) + speed * Math.sin(tilt) * (o.flat ? 0 : 1), Math.sin(angle) * speed * (o.flat ? 1 : Math.cos(tilt)),
        (o.life || 1) * (.7 + Math.random() * .6), (o.size || .5) * (.7 + Math.random() * .6), o.gravity ?? 6, color.r, color.g, color.b, o.grow ?? .3);
    }
  }
  // Recipes for the moments of play.
  hit(x, z, strong = false) {
    this.emit(x, 2.2, z, { count: strong ? 14 : 8, colors: ['#fff2c0', '#ffc860', '#ff9a40'], bright: 2.2, speed: 6, up: 2.5, life: .32, size: .55, gravity: 14, grow: .15 });
    this.emit(x, .6, z, { count: 3, additive: false, color: '#8a7a64', speed: 1.2, up: .6, life: .8, size: 1.4, gravity: 0, grow: 2.2, flat: true });
  }
  death(x, z, spirit = false) {
    this.emit(x, .5, z, { count: 10, additive: false, colors: ['#7a6a58', '#9a8a74'], speed: 2, up: .9, life: 1.3, size: 2.2, gravity: -.2, grow: 2.6, flat: true });
    if (spirit) this.emit(x, 1.5, z, { count: 12, colors: ['#9ae8ff', '#c8a6ff'], bright: 1.8, speed: .6, up: 2.4, life: 1.6, size: .7, gravity: -1.2, grow: .2, radius: 1 });
  }
  magic(x, z, color = '#f2d27a') {
    this.emit(x, .4, z, { count: 26, color, bright: 2.4, speed: 1.4, up: 3.4, life: 1.1, size: .6, gravity: -1.5, grow: .1, radius: 1.6 });
  }
  lightning(points) {
    for (const p of points) this.emit(p.x, p.y, p.z, { count: 3, colors: ['#e6f8ff', '#9ad8ff'], bright: 3, speed: 5, up: 1, life: .25, size: .45, gravity: 4, grow: .1 });
  }
  shockwave(x, z, color = '#c7a6ff') {
    this.emit(x, .4, z, { count: 34, additive: false, colors: ['#8a7a64', '#a89880', '#6a5a48'], speed: 11, up: .6, life: .9, size: 2, gravity: 2, grow: 2.4, flat: true });
    this.emit(x, .6, z, { count: 18, color, bright: 2, speed: 8, up: 1.5, life: .5, size: .6, gravity: 6, grow: .2, flat: true });
    this.ring(x, z, color, 14, .7);
  }
  splash(x, z) {
    this.emit(x, .5, z, { count: 40, colors: ['#d8f4ff', '#7fe0d0', '#ffffff'], bright: 1.4, speed: 9, up: 6, life: .9, size: .5, gravity: 16, grow: .4, radius: 2 });
  }
  lootBurst(x, z, color) {
    this.emit(x, .8, z, { count: 30, colors: [color, '#fff2c0'], bright: 2.6, speed: 3, up: 7, life: 1.4, size: .5, gravity: 9, grow: .2 });
    this.ring(x, z, color, 6, .8);
  }
  pickup(x, z, color) {
    this.emit(x, .5, z, { count: 24, colors: [color, '#fff8e0'], bright: 2.6, speed: .8, up: 5, life: 1, size: .55, gravity: -2, grow: .1, radius: .8 });
  }
  levelUp(x, z) {
    for (let k = 0; k < 3; k++) this.emit(x, .3 + k * 1.5, z, { count: 16, colors: ['#ffe08a', '#fff4c8', '#ffb347'], bright: 2.6, speed: 1.6, up: 3.5, life: 1.3, size: .55, gravity: -1, grow: .2, radius: 1.4, stagger: .3 });
    this.ring(x, z, '#ffd86a', 7, 1.1);
  }
  campCleared(x, z, color) {
    this.emit(x, .5, z, { count: 40, colors: [color, '#ffdd88'], bright: 2.2, speed: 6, up: 4, life: 1.2, size: .6, gravity: 5, grow: .3, flat: false, radius: 3 });
    this.ring(x, z, color, 12, 1.2);
  }
  // Steady sources, emitted a little every frame: volcano smoke, torch flames, fireflies, loot wisps.
  smoke(x, y, z, dt) {
    if (Math.random() > dt * 4) return;
    this.emit(x, y, z, { count: 1, additive: false, colors: ['#5a5552', '#6e6864', '#4a4644'], speed: 1.5, up: 3, life: 12, size: 16, gravity: -.15, grow: 3.5, radius: 3 });
    if (Math.random() < .3) this.emit(x, y, z, { count: 2, colors: ['#ff8a3a', '#ffb05a'], bright: 2.5, speed: 2, up: 5, life: 2.5, size: 1.2, gravity: 1.5, grow: .2, radius: 2 });
  }
  flame(x, y, z, dt) {
    if (Math.random() > dt * 24) return;
    this.emit(x, y, z, { count: 1, colors: ['#ffb347', '#ff7a2a', '#ffe08a'], bright: 2.4, speed: .25, up: 1.8, life: .5, size: .9, gravity: -.5, grow: .2, radius: .12 });
  }
  wisp(x, z, color, dt) {
    if (Math.random() > dt * 6) return;
    this.emit(x, .4, z, { count: 1, color, bright: 2.2, speed: .3, up: 1.4, life: 1.6, size: .45, gravity: -.3, grow: .1, radius: .7 });
  }
  firefly(x, y, z) {
    this.emit(x, y, z, { count: 1, colors: ['#e8ff8a', '#c8ff6a'], bright: 3, speed: .5, up: .2, life: 4, size: .4, gravity: 0, grow: 1, radius: 0 });
  }
  /** A pillar of light over loot, coloured by rarity. Returns a mesh to add to the loot's group. */
  beam(color) {
    let material = this.beamMaterials.get(color);
    if (!material) {
      material = new THREE.ShaderMaterial({ uniforms: { uColor: { value: new THREE.Color(color) }, uTime: this.beamUniforms.uTime }, vertexShader: BEAM_VERTEX, fragmentShader: BEAM_FRAGMENT, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide });
      material.userData.shared = true; this.beamMaterials.set(color, material);
    }
    const mesh = new THREE.Mesh(this.beamGeometry, material); mesh.renderOrder = 4; mesh.name = 'beam';
    this.beamGeometry.userData.shared = true;
    return mesh;
  }
  /** An expanding ring on the ground that fades out. */
  ring(x, z, color, reach, duration) {
    const mesh = new THREE.Mesh(new THREE.RingGeometry(.7, 1, 48), new THREE.MeshBasicMaterial({ color: new THREE.Color(color).multiplyScalar(2), transparent: true, opacity: .9, side: THREE.DoubleSide, depthWrite: false, blending: THREE.AdditiveBlending }));
    mesh.rotation.x = -Math.PI / 2; mesh.position.set(x, .25, z); this.scene.add(mesh);
    this.rings.push({ mesh, age: 0, reach, duration });
  }
  update(dt) {
    this.time += dt; this.beamUniforms.uTime.value = this.time;
    this.glow.material.uniforms.uTime.value = this.time; this.dust.material.uniforms.uTime.value = this.time;
    this.glow.upload(); this.dust.upload();
    for (let i = this.rings.length - 1; i >= 0; i--) {
      const ring = this.rings[i]; ring.age += dt;
      const t = Math.min(1, ring.age / ring.duration), e = 1 - (1 - t) * (1 - t);
      ring.mesh.scale.setScalar(1 + e * ring.reach); ring.mesh.material.opacity = .9 * (1 - t);
      if (t >= 1) { this.scene.remove(ring.mesh); ring.mesh.geometry.dispose(); ring.mesh.material.dispose(); this.rings.splice(i, 1); }
    }
  }
  dispose() {
    this.glow.dispose(); this.dust.dispose(); this.beamGeometry.dispose(); this.beamMaterials.forEach(m => m.dispose());
    for (const ring of this.rings) { ring.mesh.geometry.dispose(); ring.mesh.material.dispose(); }
    this.rings = [];
  }
}
