import * as THREE from './vendor/three.module.js';
import { GLTFLoader } from './vendor/GLTFLoader.js';
import { clone as cloneSkinned } from './vendor/SkeletonUtils.js';
import { buildLandscape, dressObstacles, layout, clearSway, SEA_LEVEL, APRON } from './terrain.js';
import { Effects, PostProcessing, softwareRenderer } from './effects.js';
import { Life } from './life.js';
import { Soundscape } from './audio.js';

let runtime;
const defaults = { language: 'id', quality: 'high', reducedMotion: false, volume: 50, cinematics: true, voices: true };
// Which character model a unit uses: the snapshot names heroes, champions, NPCs, bosses, creeps and wildlife; the rest follow their kind.
const kindModels = { guardian: 'guardian', warden: 'warden', spirit: 'spirit', worker: 'worker' };
const modelFor = unit => unit.model || (unit.kind === 'soldier' ? (unit.team === 0 ? 'prajurit' : 'prajurit-kalatidha') : kindModels[unit.kind]) || null;
const propKinds = { tower: 'tower', outpost: 'outpost', relic: 'relic', artifact: 'artifact', flotsam: 'flotsam' };
// Loot on the ground (Catalog.Loot): the chest, a legendary mustika, the major heirlooms, or a minor charm.
const lootProp = model => model === 'peti' ? 'chest' : model === 'mustika' ? 'loot_legend' : ['keris-luk', 'zirah-sisik', 'terompah-kilat'].includes(model) ? 'loot_major' : 'loot_minor';
// Jungle creeps stand taller the stronger they are; animals keep their own proportions.
const creepSizes = { tuyul: 1, 'siluman-kera': 1.25, 'siluman-macan': 1.4, 'siluman-buaya': 1.4, genderuwo: 1.9, banaspati: 2 };
const animalScales = { kancil: 2.1, kerbau: 2.5, 'babi-hutan': 2.5, 'kera-pencuri': 2.5, ayam: 1.8 };
const animalVoices = { 'babi-hutan': 'boar', 'kera-pencuri': 'monkey', kerbau: 'buffalo', ayam: 'chicken' };
// The full characters for the high quality; a decimated set (assets/blender/create_lods.py) for power saving.
const characterFiles = { high: 'characters.glb', low: 'characters-lod.glb' };
const characterSet = options => options.quality === 'low' ? 'low' : 'high';
// Ultimate effects follow the hero's ultimate (Catalog.cs): chain lightning, leap, the southern wave, or a burst.
const ultimateEffects = { bledhek: 'chain', 'rara-angkasa': 'leap', 'nyi-roro-kidul': 'wave' };
const ultimateSounds = { chain: 'lightning', leap: 'shockwave', wave: 'wave', burst: 'magic' };
// Messages from the simulation that deserve a sound (Match.cs, Match.Neutrals.cs, Match.Adventure.cs).
const cues = [[/^Kamp /, 'camp'], [/^Kera pencuri/, 'monkey'], [/^Emas curian/, 'coin'], [/didapat!/, 'loot'], [/^Tugas/, 'quest'], [/^Prasasti ditemukan/, 'quest'],
  [/^Serpihan kapal/, 'coin'], [/Naga Basuki/, 'gong'], [/^Pos siap/, 'build'], [/^Fondasi ditempatkan/, 'build'], [/dibeli/, 'coin']];
// The title screen: the shrine of Borobudur Quantum in a valley between a volcano, mountains and the sea.
const MENU_MAP = {
  id: 'menu', mode: -1, size: 100, theme: 'valley', obstacles: [], resources: [], camps: [],
  regions: [{ biome: 'subur', x: 26, z: 70, radius: 30 }, { biome: 'hutan', x: 8, z: 30, radius: 24 }, { biome: 'subur', x: 82, z: 86, radius: 24 }, { biome: 'sabana', x: 88, z: 22, radius: 20 }, { biome: 'hutan', x: 40, z: 6, radius: 18 }],
  edges: ['vulkan', 'laut', 'laut', 'gunung'], paths: [[63, 48, 100, 100], [63, 48, 22, 74], [63, 48, 63, 0]], clear: [[63, 48, 24]], village: [20, 70]
};

export function loadSettings() {
  try {
    const saved = JSON.parse(localStorage.getItem('nusantara.settings') || '{}');
    return { language: saved.language === 'en' ? 'en' : 'id', quality: saved.quality === 'low' ? 'low' : 'high', reducedMotion: Boolean(saved.reducedMotion || matchMedia('(prefers-reduced-motion: reduce)').matches), volume: Number.isFinite(saved.volume) ? Math.max(0, Math.min(100, saved.volume)) : defaults.volume, cinematics: saved.cinematics !== false, voices: saved.voices !== false };
  } catch { return { ...defaults }; }
}
// A solo Adventure save lives in this browser profile only. Oversized or unreadable data is treated as absent.
export function loadGame() {
  try { const saved = localStorage.getItem('nusantara.save'); return saved && saved.length <= 12000 ? saved : null; } catch { return null; }
}
export function saveGame(json) {
  try { if (typeof json !== 'string' || json.length > 12000) return false; localStorage.setItem('nusantara.save', json); return true; } catch { return false; }
}
export function clearGame() { try { localStorage.removeItem('nusantara.save'); } catch { /* Nothing to clear. */ } }
// The account session for this browser profile: { server, token }. Anything malformed is treated as signed out.
export function loadSession() {
  try {
    const saved = JSON.parse(localStorage.getItem('nusantara.session') || 'null');
    return saved && typeof saved.server === 'string' && typeof saved.token === 'string' && saved.server.length <= 200 && saved.token.length <= 128 ? { server: saved.server, token: saved.token } : null;
  } catch { return null; }
}
export function saveSession(server, token) {
  try { if (server && token) localStorage.setItem('nusantara.session', JSON.stringify({ server, token })); else localStorage.removeItem('nusantara.session'); } catch { /* Storage can be disabled. */ }
}
// Last rendered frame, for the performance budget checks in docs/PERFORMANCE.md.
export function stats() {
  const r = runtime; if (!r?.ready) return null;
  const info = r.renderer.info, decor = r.landscape ? Object.values(r.landscape.counts).reduce((a, b) => a + b, 0) : 0;
  return { calls: r.frameCalls || 0, triangles: r.frameTriangles || 0, geometries: info.memory.geometries, textures: info.memory.textures, units: r.units.size, frameMs: r.frameMs || 0, animated: r.animated || 0, weather: r.sky.weather, rain: r.rain, daylight: r.daylight, combat: r.combat, cinematic: r.intro ? r.intro.kind : 'off', audio: r.audio.state, models: r.modelled || 0, corpses: r.corpses.length, effects: r.effects.length + r.fx.active, voice: r.voiceKey || '', music: r.audio.mode, map: r.landscape?.id || '', size: r.size, decor, life: r.life?.count || 0, post: !!r.post, zoom: r.view.zoom, neutrals: r.neutrals || 0, particles: r.fx.emitted, characters: r.templates === r.templateSets?.low ? 'lod' : 'full' };
}
// Client coordinates of a ground position, for automated tests that click the arena.
export function screenPoint(x, z) {
  const r = runtime; if (!r?.ready) return null;
  const rect = r.renderer.domElement.getBoundingClientRect(), point = new THREE.Vector3(x, 0, z).project(r.camera);
  return [rect.left + (point.x + 1) / 2 * rect.width, rect.top + (1 - point.y) / 2 * rect.height];
}
export function settings(value) {
  try { localStorage.setItem('nusantara.settings', JSON.stringify(value)); } catch { /* Storage can be disabled in private profiles. */ }
  document.documentElement.lang = value.language;
  document.documentElement.dataset.reducedMotion = String(value.reducedMotion);
  const r = runtime; if (!r) return;
  const qualityChanged = r.options?.quality !== value.quality;
  r.options = value;
  r.renderer.setPixelRatio(value.quality === 'low' ? 1 : Math.min(devicePixelRatio, 1.75));
  r.audio.setVolume(value.volume);
  // Raising the volume from zero is itself a gesture, so sound can start right away.
  if (value.volume > 0 && r.gestured) r.audio.start(value.volume);
  if (r.voice) r.voice.volume = Math.min(1, value.volume / 50);
  r.configure?.();
  if (qualityChanged && r.kit) { applyMap(r, true); ensureCharacters(r); }
}
/** The map on screen: a MapDefinition from Maps.cs, or null for the title screen. Rebuilds the landscape when it changes. */
export function setMap(definition) {
  const r = runtime; if (!r) return;
  r.mapDef = definition || null;
  if (r.kit) applyMap(r);
}
export async function mount(element, reference, options) {
  dispose();
  const scene = new THREE.Scene();
  scene.background = new THREE.Color('#19323e');
  scene.fog = new THREE.FogExp2('#19323e', .0035);
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'high-performance' });
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.75));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.3;
  // Several passes draw each frame once bloom is on; the counters are reset by hand so stats() sees the whole frame.
  renderer.info.autoReset = false;
  element.append(renderer.domElement);
  const camera = new THREE.PerspectiveCamera(39, 1, .1, 900);
  const r = runtime = { element, reference, scene, renderer, camera, options, units: new Map(), resourceMarkers: new Map(), state: null, owner: 0, mixers: [], scenery: new THREE.Group(), battle: new THREE.Group(), story: new THREE.Group(), disposed: false, sky: { weather: 'clear', daylight: 1 }, rain: 0, mist: 0, daylight: 1, swell: .5, combat: 0, combatTarget: 0, flash: 0, intro: null, tintMaterials: new Map(), corpses: [], effects: [], templates: new Map(), props: new Map(), lastCooldown: 0, size: 100, view: { zoom: 1, pan: new THREE.Vector3(), keys: new Set(), drag: null }, look: new THREE.Vector3(43, 9, 45), mapDef: null };
  r.audio = new Soundscape(new URL('./audio/', import.meta.url).href);
  r.fx = new Effects(scene);
  r.software = softwareRenderer(renderer);
  settings(options);
  scene.add(r.scenery, r.battle, r.story);
  const hemisphere = new THREE.HemisphereLight('#b9e1e0', '#30454b', 2.6); scene.add(hemisphere);
  const sun = r.sun = new THREE.DirectionalLight('#ffe4b0', 4.3);
  sun.castShadow = true; sun.shadow.mapSize.set(2048, 2048); sun.shadow.bias = -.0008; sun.shadow.normalBias = .04;
  scene.add(sun, sun.target);
  const rim = new THREE.DirectionalLight('#70bcd8', 3); rim.position.set(100, 40, -60); scene.add(rim);
  // The sea around every island: a swell animated in the vertex shader; colour and surf come from the sea floor depth
  // that the landscape writes into a small texture, so shallows glow turquoise and foam gathers on the beaches.
  r.ocean = { uTime: { value: 0 }, uSwell: { value: .5 }, uShore: { value: null }, uShoreOrigin: { value: new THREE.Vector2(-APRON, -APRON) }, uShoreSize: { value: 240 } };
  const waterMaterial = new THREE.MeshStandardMaterial({ color: '#1d5360', metalness: .2, roughness: .22 });
  waterMaterial.onBeforeCompile = shader => {
    Object.assign(shader.uniforms, r.ocean);
    shader.vertexShader = 'uniform float uTime;\nuniform float uSwell;\nvarying vec2 vSea;\n' + shader.vertexShader
      .replace('#include <beginnormal_vertex>', '#include <beginnormal_vertex>\n  float waveA = position.x * .09 + uTime * .9, waveB = position.y * .07 - uTime * .7, waveC = (position.x + position.y) * .21 + uTime * 1.6;\n  objectNormal = normalize(vec3(-(cos(waveA) * .09 + cos(waveC) * .063) * uSwell, -(cos(waveB) * .07 + cos(waveC) * .063) * uSwell, 1.0));')
      .replace('#include <begin_vertex>', '#include <begin_vertex>\n  vSea = (modelMatrix * vec4(transformed, 1.0)).xz;\n  transformed.z += (sin(waveA) + sin(waveB) + sin(waveC) * .3) * uSwell * .6;');
    shader.fragmentShader = 'uniform float uTime;\nuniform sampler2D uShore;\nuniform vec2 uShoreOrigin;\nuniform float uShoreSize;\nvarying vec2 vSea;\nfloat seaFoam = 0.0;\n' + shader.fragmentShader
      .replace('#include <color_fragment>', `#include <color_fragment>
  vec2 shoreUv = (vSea - uShoreOrigin) / uShoreSize;
  float inside = step(0.0, shoreUv.x) * step(shoreUv.x, 1.0) * step(0.0, shoreUv.y) * step(shoreUv.y, 1.0);
  float depth = mix(10.0, texture2D(uShore, shoreUv).r * 12.0 - 2.0, inside);
  diffuseColor.rgb = mix(vec3(.03, .38, .34), vec3(.006, .05, .085), smoothstep(.2, 4.2, depth));
  float surf = sin(uTime * 1.7 - depth * 6.0 + vSea.x * .21 + vSea.y * .17) * .5 + .5;
  seaFoam = clamp((1.0 - smoothstep(.0, 1.2, depth)) * (.45 + .55 * surf) + (1.0 - smoothstep(.0, .35, depth)) * .5, 0.0, 1.0);
  diffuseColor.rgb = mix(diffuseColor.rgb, vec3(.86, .92, .9), seaFoam * .8);`)
      .replace('#include <roughnessmap_fragment>', '#include <roughnessmap_fragment>\n  roughnessFactor = mix(roughnessFactor, .85, seaFoam);');
  };
  const water = r.water = new THREE.Mesh(new THREE.PlaneGeometry(2000, 2000, 100, 100), waterMaterial); water.rotation.x = -Math.PI / 2; water.position.set(50, SEA_LEVEL, 50); water.name = 'sea'; scene.add(water);
  // Rain rings on the sea around the island, recycled; they appear only while it rains.
  const ripples = new THREE.InstancedMesh(new THREE.RingGeometry(.6, .85, 16), new THREE.MeshBasicMaterial({ color: '#cfe6ea', transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide }), 48);
  const rippleAge = new Float32Array(48), rippleSpot = new Float32Array(96), rippleMatrix = new THREE.Object3D();
  for (let i = 0; i < 48; i++) rippleAge[i] = i / 48;
  ripples.frustumCulled = false; scene.add(ripples);
  const loader = new GLTFLoader(), url = name => new URL(`./models/${name}`, import.meta.url).href;
  const files = ['quantum-shrine.glb', characterFiles[characterSet(options)], 'naga.glb', 'props.glb', 'environment.glb', 'animals.glb'];
  const loaded = await Promise.all(files.map(name => loader.loadAsync(url(name))));
  if (r.disposed) { loaded.forEach(g => release(g.scene, true)); return; }
  const [shrine, characters, naga, props, environment, animals] = loaded;
  shrine.scene.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; } });
  const centerpiece = shrine.scene;
  centerpiece.position.set(63, 0, 48); centerpiece.scale.setScalar(4); centerpiece.rotation.y = -.2;
  r.scenery.add(centerpiece);
  const halo = new THREE.Mesh(new THREE.TorusGeometry(12, .14, 8, 100), new THREE.MeshBasicMaterial({ color: new THREE.Color('#d4b478').multiplyScalar(1.6), transparent: true, opacity: .8 }));
  halo.position.set(63, 22, 48); halo.rotation.y = .7; r.scenery.add(halo);
  const innerHalo = new THREE.Mesh(new THREE.TorusGeometry(10.8, .06, 6, 100), new THREE.MeshBasicMaterial({ color: new THREE.Color('#83c9ba').multiplyScalar(1.6), transparent: true, opacity: .5 }));
  innerHalo.position.copy(halo.position); innerHalo.rotation.copy(halo.rotation); r.scenery.add(innerHalo);
  const mixer = new THREE.AnimationMixer(centerpiece);
  shrine.animations.forEach(a => mixer.clipAction(a).play()); r.mixers.push(mixer);
  prepareModels(r, characterSet(options), characters, naga, props, environment, animals);
  r.characterScenes.push(shrine.scene);
  // The two shrines of a match; their places follow the map size (applyMap).
  r.bases = [['#7ce5cb', 0], ['#da80aa', 1]].map(([color]) => {
    const copy = centerpiece.clone(); copy.scale.setScalar(1.2); r.battle.add(copy);
    const beacon = new THREE.Mesh(new THREE.CylinderGeometry(.5, 1, 20, 8, 1, true), new THREE.MeshBasicMaterial({ color: new THREE.Color(color).multiplyScalar(1.4), transparent: true, opacity: .23, side: THREE.DoubleSide, depthWrite: false })); r.battle.add(beacon);
    return { copy, beacon };
  });
  r.battle.visible = false;
  // Golden motes drifting around wherever the camera looks.
  const positions = new Float32Array(160 * 3);
  for (let i = 0; i < positions.length; i += 3) { positions[i] = (i * 7.31) % 115 - 57; positions[i + 1] = 3 + (i * .71) % 35; positions[i + 2] = (i * 3.77) % 110 - 55; }
  const particlesGeometry = new THREE.BufferGeometry(); particlesGeometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  const particles = new THREE.Points(particlesGeometry, new THREE.PointsMaterial({ color: '#e5c58a', size: .23, transparent: true, opacity: .65, depthWrite: false })); scene.add(particles);
  // Rain: line segments recycled inside a box that follows the camera. The draw range follows the rain level.
  const drops = 1400, rainPositions = new Float32Array(drops * 6);
  for (let i = 0; i < drops; i++) { rainPositions[i * 6] = -80 + (i * 37.7) % 160; rainPositions[i * 6 + 1] = (i * 13.3) % 120; rainPositions[i * 6 + 2] = -90 + (i * 91.1) % 180; }
  const rainGeometry = new THREE.BufferGeometry(); rainGeometry.setAttribute('position', new THREE.BufferAttribute(rainPositions, 3));
  const rain = new THREE.LineSegments(rainGeometry, new THREE.LineBasicMaterial({ color: '#b9d6e2', transparent: true, opacity: 0, depthWrite: false }));
  rain.frustumCulled = false; rain.visible = false; scene.add(rain);
  // Cinematic overlay: letterbox bars and a title card. Purely presentational, and pointer-transparent.
  r.cine = document.createElement('div'); r.cine.className = 'cinematic'; r.cine.setAttribute('aria-hidden', 'true');
  r.cine.innerHTML = '<i class="cine-bar top"></i><i class="cine-bar bottom"></i><div class="cine-title"><b></b><span></span></div>';
  element.append(r.cine);
  r.endIntro = () => { if (!r.intro) return; r.intro = null; r.cine.classList.remove('on'); element.dataset.cinematic = 'off'; };
  r.caption = shot => { r.cine.querySelector('b').textContent = shot.title || ''; r.cine.querySelector('span').textContent = shot.line || ''; };
  element.dataset.cinematic = 'off'; element.dataset.weather = 'clear'; element.dataset.combat = 'calm';
  const cursor = new THREE.Mesh(new THREE.RingGeometry(1.5, 1.75, 32), new THREE.MeshBasicMaterial({ color: '#e9cd8c', side: THREE.DoubleSide })); cursor.rotation.x = -Math.PI / 2; cursor.visible = false; scene.add(cursor); r.cursor = cursor;
  r.placement = new THREE.Mesh(new THREE.PlaneGeometry(6, 6), new THREE.MeshBasicMaterial({ color: '#e9cd8c', transparent: true, opacity: .45, side: THREE.DoubleSide }));
  r.placement.rotation.x = -Math.PI / 2; r.placement.visible = false; scene.add(r.placement);
  // Fog of war: a ground overlay redrawn from the snapshot's vision circles. `seen` remembers explored ground for this match.
  r.fogCanvas = document.createElement('canvas'); r.seenCanvas = document.createElement('canvas');
  r.fogCanvas.width = r.fogCanvas.height = r.seenCanvas.width = r.seenCanvas.height = 128;
  r.fogTexture = new THREE.CanvasTexture(r.fogCanvas); r.fogTexture.colorSpace = THREE.SRGBColorSpace;
  r.fog = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ map: r.fogTexture, transparent: true, depthWrite: false }));
  r.fog.rotation.x = -Math.PI / 2; r.fog.visible = false; r.fog.renderOrder = 1; scene.add(r.fog);
  r.projector = new THREE.Vector3();
  // A sound placed in the world: panned by where it is on screen, quieter the farther it is from what the camera watches.
  r.sound = (name, x, z, gain = 1, rate = 1) => {
    let pan = 0, level = gain;
    if (x !== undefined) {
      r.projector.set(x, 1, z).project(camera);
      pan = THREE.MathUtils.clamp(r.projector.x, -1, 1) * .85;
      const off = Math.max(0, Math.abs(r.projector.x) - 1, Math.abs(r.projector.y) - 1);
      const distance = Math.hypot(x - r.look.x, z - r.look.z), reach = r.size * .45 * r.view.zoom + 12;
      level *= 1 / (1 + (distance / reach) ** 2) * (off > 0 ? Math.max(.12, 1 - off) : 1);
    }
    return r.audio.play(name, { pan, gain: level, rate });
  };
  const ray = new THREE.Raycaster(), pointer = new THREE.Vector2(), plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0), point = new THREE.Vector3();
  const groundAt = (clientX, clientY, out) => {
    const rect = renderer.domElement.getBoundingClientRect();
    pointer.set((clientX - rect.left) / rect.width * 2 - 1, -(clientY - rect.top) / rect.height * 2 + 1);
    ray.setFromCamera(pointer, camera);
    return ray.ray.intersectPlane(plane, out);
  };
  r.click = event => {
    if (!r.state || document.querySelector('.modal-backdrop')) return;
    const rect = renderer.domElement.getBoundingClientRect();
    pointer.set((event.clientX - rect.left) / rect.width * 2 - 1, -(event.clientY - rect.top) / rect.height * 2 + 1);
    ray.setFromCamera(pointer, camera);
    if (r.state.mode === 0 && !r.attackMove && !r.buildMode) {
      const resourceHit = ray.intersectObjects([...r.resourceMarkers.values()], true)[0];
      if (resourceHit) { reference.invokeMethodAsync('GatherResource', resourceHit.object.userData.resourceId).catch(() => {}); return; }
    }
    if (event.button === 0 && r.state.mode === 0 && !r.attackMove && !r.buildMode) {
      const hits = ray.intersectObjects([...r.units.values()], true);
      for (const hit of hits) {
        let object = hit.object;
        while (object && object.userData.unitId === undefined) object = object.parent;
        const unit = r.state.units.find(u => u.id === object?.userData.unitId);
        if (unit && unit.owner === r.owner && ['hero', 'soldier', 'worker'].includes(unit.kind)) {
          reference.invokeMethodAsync('SelectUnit', unit.id, event.shiftKey).catch(() => {});
          return;
        }
      }
    }
    const max = r.size - 4;
    if (ray.ray.intersectPlane(plane, point) && point.x >= 4 && point.x <= max && point.z >= 4 && point.z <= max) {
      cursor.position.set(point.x, .2, point.z); cursor.visible = true;
      reference.invokeMethodAsync('MoveTo', point.x, point.z).catch(() => {});
    }
  };
  // The overlay is pointer-transparent except for actual UI, keeping the arena clickable.
  // RTS left-button presses wait for release: a short press is a click, a drag past 8 px is a box selection.
  // The middle button drags the camera.
  r.box = document.createElement('div'); r.box.className = 'selection-box'; r.box.style.display = 'none'; element.append(r.box);
  r.down = event => {
    r.endIntro();
    if (event.button === 1 && r.state) {
      event.preventDefault();
      if (groundAt(event.clientX, event.clientY, point)) r.view.drag = { id: event.pointerId, start: point.clone(), pan: r.view.pan.clone() };
      try { renderer.domElement.setPointerCapture(event.pointerId); } catch { /* Capture is optional. */ }
      return;
    }
    if (event.button !== 0 || event.pointerType === 'touch' || r.state?.mode !== 0 || r.attackMove || r.buildMode || document.querySelector('.modal-backdrop')) { r.click(event); return; }
    r.drag = { id: event.pointerId, x: event.clientX, y: event.clientY, active: false };
    try { renderer.domElement.setPointerCapture(event.pointerId); } catch { /* Capture is optional. */ }
  };
  r.dragMove = event => {
    const pan = r.view.drag;
    if (pan && event.pointerId === pan.id) {
      // Keep the ground point that was grabbed under the pointer.
      if (groundAt(event.clientX, event.clientY, point)) { r.view.pan.add(pan.start.clone().sub(point).setY(0)); clampView(r); }
      return;
    }
    const drag = r.drag; if (!drag || event.pointerId !== drag.id) return;
    if (!drag.active && Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 8) return;
    drag.active = true;
    const rect = element.getBoundingClientRect();
    Object.assign(r.box.style, { display: 'block', left: `${Math.min(drag.x, event.clientX) - rect.left}px`, top: `${Math.min(drag.y, event.clientY) - rect.top}px`, width: `${Math.abs(event.clientX - drag.x)}px`, height: `${Math.abs(event.clientY - drag.y)}px` });
  };
  r.up = event => {
    if (r.view.drag?.id === event.pointerId) { r.view.drag = null; return; }
    const drag = r.drag; if (!drag || event.pointerId !== drag.id) return;
    r.drag = null; r.box.style.display = 'none';
    if (!drag.active) { r.click(event); return; }
    if (!r.state || document.querySelector('.modal-backdrop')) return;
    const rect = renderer.domElement.getBoundingClientRect();
    const minX = Math.min(drag.x, event.clientX), maxX = Math.max(drag.x, event.clientX), minY = Math.min(drag.y, event.clientY), maxY = Math.max(drag.y, event.clientY);
    const ids = [];
    for (const unit of r.state.units) {
      const object = r.units.get(unit.id);
      if (!object || unit.owner !== r.owner || !['hero', 'soldier', 'worker'].includes(unit.kind)) continue;
      // Test the rendered (interpolated) position, which is what the player enclosed.
      point.copy(object.position).setY(1.5).project(camera);
      const x = rect.left + (point.x + 1) / 2 * rect.width, y = rect.top + (1 - point.y) / 2 * rect.height;
      if (x >= minX && x <= maxX && y >= minY && y <= maxY) ids.push(unit.id);
    }
    reference.invokeMethodAsync('SelectUnits', ids, event.shiftKey).catch(() => {});
  };
  r.dragCancel = event => { if (r.drag?.id === event.pointerId) { r.drag = null; r.box.style.display = 'none'; } if (r.view.drag?.id === event.pointerId) r.view.drag = null; };
  renderer.domElement.addEventListener('pointerdown', r.down);
  renderer.domElement.addEventListener('pointermove', r.dragMove);
  renderer.domElement.addEventListener('pointerup', r.up);
  renderer.domElement.addEventListener('pointercancel', r.dragCancel);
  r.hover = event => {
    if (!r.buildMode) return;
    if (groundAt(event.clientX, event.clientY, point)) { r.placement.position.set(point.x, .2, point.z); r.placement.visible = true; }
  };
  renderer.domElement.addEventListener('pointermove', r.hover);
  r.contextMenu = event => event.preventDefault();
  renderer.domElement.addEventListener('contextmenu', r.contextMenu);
  // The mouse wheel zooms the battle camera between a close look and the whole map.
  r.wheel = event => {
    if (!r.state || r.intro || document.querySelector('.modal-backdrop')) return;
    event.preventDefault();
    r.view.zoom = THREE.MathUtils.clamp(r.view.zoom * Math.exp(event.deltaY * .0012), .32, 1.25);
  };
  renderer.domElement.addEventListener('wheel', r.wheel, { passive: false });
  r.mapClick = event => {
    const map = event.target.closest?.('[data-tactical-map]');
    if (!map || !r.state || document.querySelector('.modal-backdrop')) return;
    const resourceId = event.target.getAttribute('data-resource-id');
    if (resourceId && !r.buildMode) { reference.invokeMethodAsync('GatherResource', Number(resourceId)).catch(() => {}); return; }
    const rect = map.getBoundingClientRect(), size = r.size;
    const x = (event.clientX - rect.left) / rect.width * size, z = (event.clientY - rect.top) / rect.height * size;
    if (x >= 4 && x <= size - 4 && z >= 4 && z <= size - 4) reference.invokeMethodAsync('MoveTo', x, z).catch(() => {});
  };
  r.element.parentElement.addEventListener('pointerdown', r.mapClick);
  const arrows = { ArrowUp: 1, ArrowDown: 1, ArrowLeft: 1, ArrowRight: 1 };
  r.key = event => {
    if (event.target.matches('input,select,textarea')) return;
    if (arrows[event.key] && r.state && !document.querySelector('.modal-backdrop')) { event.preventDefault(); r.view.keys.add(event.key); return; }
    if (event.repeat) return;
    r.endIntro();
    // 4-6 recall a control group; Ctrl/Alt/Cmd + 4-6 save the current selection.
    const group = r.state && /^Digit[456]$/.test(event.code) ? Number(event.code.slice(5)) : 0;
    if (group) { event.preventDefault(); reference.invokeMethodAsync('ControlGroup', group, event.ctrlKey || event.altKey || event.metaKey).catch(() => {}); return; }
    // F centres the camera on the player's hero; Home returns to the whole map.
    if (r.state && (event.key === 'f' || event.key === 'F') && !document.querySelector('.modal-backdrop')) { event.preventDefault(); focusHero(r); return; }
    if (r.state && event.key === 'Home') { event.preventDefault(); resetView(r); return; }
    if (event.key === 'Escape' || ['q', 'w', 'a', 'h', 'b', '1', '2', '3'].includes(event.key.toLowerCase())) { event.preventDefault(); reference.invokeMethodAsync('Key', event.key === 'Escape' ? 'Escape' : event.key.toLowerCase()).catch(() => {}); }
    if (event.key === 'Tab') {
      const dialog = document.querySelector('[role=dialog]');
      if (!dialog) return;
      const targets = [...dialog.querySelectorAll('button:not(:disabled),input,select,summary,[tabindex="0"]')].filter(el => el.getClientRects().length);
      const first = targets[0], last = targets.at(-1);
      if (event.shiftKey && (document.activeElement === first || document.activeElement === dialog)) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    }
  };
  r.keyUp = event => r.view.keys.delete(event.key);
  r.blur = () => r.view.keys.clear();
  window.addEventListener('keydown', r.key);
  window.addEventListener('keyup', r.keyUp);
  window.addEventListener('blur', r.blur);
  // Sound starts with the first gesture (browsers refuse it before one); buttons answer with a soft click.
  // At volume zero no audio graph is made at all; it starts with the first gesture once there is something to hear.
  r.startAudio = () => { r.gestured = true; if (r.options.volume > 0) r.audio.start(r.options.volume); };
  window.addEventListener('pointerdown', r.startAudio);
  window.addEventListener('keydown', r.startAudio);
  r.uiClick = event => { if (event.target.closest?.('button,summary,select,input[type=checkbox]')) r.audio.play('click', { gain: .5 }); };
  document.addEventListener('pointerdown', r.uiClick, true);
  // Recorded dialogue (see docs/ASSETS.md). One clip plays at a time; the music dips under it.
  r.voice = new Audio(); r.voice.preload = 'auto';
  r.playVoice = dialogue => {
    if (dialogue === r.voiceKey) return;
    r.voiceKey = dialogue; r.voice.pause();
    const match = /^([a-z-]+):(\d+)$/.exec(dialogue || '');
    if (!match || r.options.voices === false || r.options.volume <= 0) return;
    r.voice.src = new URL(`./voice/${match[1]}-${match[2]}-${r.options.language === 'en' ? 'en' : 'id'}.mp3`, import.meta.url).href;
    r.voice.volume = Math.min(1, r.options.volume / 50);
    r.voice.play().catch(() => { /* Autoplay can be refused before the first interaction. */ });
  };
  r.configure = () => {
    const low = r.options.quality === 'low', height = element.clientHeight || 800;
    r.fx.configure(r.options.quality, r.options.reducedMotion, height * renderer.getPixelRatio(), camera.fov);
    // Bloom and grading need a real GPU; software rendering (and power saving) draws the scene directly.
    if (low) r.postDropped = false;
    if (!low && !r.software && !r.post && !r.postDropped) { r.post = new PostProcessing(renderer, scene, camera); r.post.setSize(element.clientWidth || 1, element.clientHeight || 1, renderer.getPixelRatio()); }
    if ((low || r.software) && r.post) { r.post.dispose(); r.post = null; }
    // Shadows need a GPU: a software rasterizer (no GPU) draws without them, as it does without bloom.
    sun.castShadow = !low && !r.software;
    // The swell needs fewer vertices when power matters.
    const seaSegments = low ? 48 : 100;
    if (r.water.geometry.parameters.widthSegments !== seaSegments) { r.water.geometry.dispose(); r.water.geometry = new THREE.PlaneGeometry(2000, 2000, seaSegments, seaSegments); }
  };
  r.resize = new ResizeObserver(() => {
    const width = element.clientWidth, height = element.clientHeight;
    renderer.setSize(width, height, false); camera.aspect = width / height; camera.updateProjectionMatrix();
    r.post?.setSize(width, height, renderer.getPixelRatio()); r.configure();
  });
  r.resize.observe(element);
  r.configure();
  applyMap(r);
  let previous = performance.now(), clock = 0;
  const menuCamera = new THREE.Vector3(137, 86, 145), menuTarget = new THREE.Vector3(43, 9, 45);
  const dayColor = new THREE.Color('#19323e'), nightColor = new THREE.Color('#081119'), greyColor = new THREE.Color('#3b4b52'), duskColor = new THREE.Color('#ff9a5c');
  const sunDay = new THREE.Color('#ffe4b0'), sunNight = new THREE.Color('#8fa9ff'), skyColor = new THREE.Color(), focus = new THREE.Vector3(), orbit = new THREE.Vector3(), aim = new THREE.Vector3(), place = new THREE.Vector3();
  const levels = { clear: [0, 0, .5], mist: [0, 1, .35], rain: [.6, .35, .9], storm: [1, .45, 1.8] };
  let orbitAngle = 0, lastFlashTick = -1, nextCall = 4;
  renderer.setAnimationLoop(now => {
    r.frameMs = r.frameMs ? r.frameMs * .95 + (now - previous) * .05 : now - previous;
    // Cutscenes run on wall-clock time, so a slow frame rate does not stretch them.
    const dt = Math.min((now - previous) / 1000, .1), wall = Math.min((now - previous) / 1000, 1); previous = now;
    const playing = !!r.state, still = r.options.reducedMotion, low = r.options.quality === 'low', size = r.size;
    if (!still) clock += dt;
    renderer.info.reset();
    // Weather and time of day come from the match tick (see Atmosphere.cs); the menu keeps a clear afternoon.
    const [rainTarget, mistTarget, swellTarget] = levels[playing ? r.sky.weather : 'clear'] || levels.clear;
    const ease = Math.min(1, dt * .7);
    r.rain += (rainTarget - r.rain) * ease; r.mist += (mistTarget - r.mist) * ease; r.swell += (swellTarget - r.swell) * ease;
    r.daylight += ((playing ? r.sky.daylight : 1) - r.daylight) * ease;
    const day = THREE.MathUtils.clamp((r.daylight - .25) / .75, 0, 1), dusk = Math.max(0, 1 - Math.abs(r.daylight - .58) / .22);
    r.flash = Math.max(0, r.flash - dt * 5);
    skyColor.copy(nightColor).lerp(dayColor, day).lerp(greyColor, Math.min(.75, r.mist * .55 + r.rain * .3)).lerp(duskColor, dusk * .12);
    scene.background.copy(skyColor); scene.fog.color.copy(skyColor);
    scene.fog.density = ((playing ? .0024 : .0032) + r.mist * .0011 + r.rain * .0003) * 100 / Math.max(100, size);
    sun.color.copy(sunNight).lerp(sunDay, day).lerp(duskColor, dusk * .45);
    sun.intensity = 4.3 * (.3 + .7 * day) * (1 - r.rain * .35);
    hemisphere.intensity = 2.6 * (.5 + .5 * day) * (1 - r.rain * .2) + r.flash * 3;
    rim.intensity = 3 * (.45 + .55 * day);
    renderer.toneMappingExposure = 1.3 * (.78 + .22 * day);
    r.ocean.uSwell.value = still ? 0 : r.swell;
    if (!still) r.ocean.uTime.value += dt;
    // Wind follows the weather: foliage leans harder in a storm.
    r.landscape?.update(clock, still ? 0 : .45 + r.rain * .5 + (r.sky.weather === 'storm' ? .5 : 0));
    r.dressing?.update(clock, still ? 0 : .45 + r.rain * .5);
    ripples.visible = r.rain > .1 && !still;
    if (ripples.visible) {
      for (let i = 0; i < 48; i++) {
        rippleAge[i] += dt * (.8 + r.rain);
        if (rippleAge[i] > 1 || rippleSpot[i * 2] === 0) { rippleAge[i] %= 1; const angle = Math.random() * Math.PI * 2, radius = size * (.62 + Math.random() * .45); rippleSpot[i * 2] = size / 2 + Math.cos(angle) * radius; rippleSpot[i * 2 + 1] = size / 2 + Math.sin(angle) * radius; }
        rippleMatrix.position.set(rippleSpot[i * 2], SEA_LEVEL + .3, rippleSpot[i * 2 + 1]); rippleMatrix.rotation.set(-Math.PI / 2, 0, 0); rippleMatrix.scale.setScalar(.3 + rippleAge[i] * 2.4);
        rippleMatrix.updateMatrix(); ripples.setMatrixAt(i, rippleMatrix.matrix);
      }
      ripples.instanceMatrix.needsUpdate = true; ripples.material.opacity = .45 * Math.min(1, r.rain);
    }
    // Rain is hidden under reduced motion; the darker sky, the mist and the HUD label still tell the weather.
    rain.visible = r.rain > .03 && !still;
    if (rain.visible) {
      rain.position.set(r.look.x, 0, r.look.z);
      const active = Math.floor(drops * Math.min(1, r.rain) * (low ? .4 : 1)), fall = dt * (46 + r.rain * 26), drift = dt * 9 * r.rain;
      for (let i = 0; i < active; i++) {
        const base = i * 6; let x = rainPositions[base] + drift, y = rainPositions[base + 1] - fall;
        if (y < 0) { y += 120; x -= 40 * r.rain; } if (x > 80) x -= 160;
        rainPositions[base] = x; rainPositions[base + 1] = y;
        rainPositions[base + 3] = x - .9 * r.rain; rainPositions[base + 4] = y + 5; rainPositions[base + 5] = rainPositions[base + 2];
      }
      rainGeometry.setDrawRange(0, active * 2); rainGeometry.attributes.position.needsUpdate = true;
      rain.material.opacity = .6 * Math.min(1, r.rain * 1.4);
    }
    // One soft flash now and then during a storm, never under reduced motion (no strobing).
    if (playing && r.sky.weather === 'storm' && !still && r.state.tick !== lastFlashTick && (Math.imul(r.state.tick, 2654435761) >>> 0) % 83 === 0) { lastFlashTick = r.state.tick; r.flash = 1; r.audio.play('thunder', { gain: .9, pan: Math.random() - .5 }); }
    r.combat += (r.combatTarget - r.combat) * Math.min(1, dt * 2.5);
    // Sound must never stop the picture: a failing audio graph is logged once and left alone.
    try { r.audio.update({ playing, combat: r.combat, rain: playing ? r.rain : 0, daylight: r.daylight, voice: !!r.voice && !r.voice.paused, ended: playing && r.state.status !== 0 }); }
    catch (error) { if (!r.audioFailed) console.warn('Battle of Nusantara: audio update failed', error); r.audioFailed = true; }
    if (r.fire) { r.fire.intensity = still ? 60 : 60 + Math.sin(now * .013) * 14 + Math.sin(now * .031) * 9; r.flame.scale.y = still ? 1 : 1 + Math.sin(now * .017) * .18; }
    if (r.intro) {
      // A cutscene: a list of shots, each a camera path and a look path over its own duration.
      r.intro.time += wall;
      let shot = r.intro.shots[r.intro.index];
      while (shot && r.intro.time > shot.duration) { r.intro.time -= shot.duration; shot = r.intro.shots[++r.intro.index]; if (shot) r.caption(shot); }
      if (!shot) r.endIntro();
      else {
        const t = Math.min(1, r.intro.time / shot.duration), e = t * t * (3 - 2 * t);
        shot.camera(e, orbit); shot.look(e, focus);
        camera.position.copy(orbit); camera.lookAt(focus); r.look.copy(focus);
        if (r.intro.index === r.intro.shots.length - 1 && t > .72 && r.intro.kind === 'intro') r.cine.classList.remove('on');
      }
    } else if (playing) {
      // Arrow keys pan along the ground as seen on screen; the pan stays over the arena.
      if (r.view.keys.size) {
        aim.subVectors(r.look, camera.position).setY(0).normalize();
        const speed = size * .55 * r.view.zoom * dt, up = (r.view.keys.has('ArrowUp') ? 1 : 0) - (r.view.keys.has('ArrowDown') ? 1 : 0), right = (r.view.keys.has('ArrowRight') ? 1 : 0) - (r.view.keys.has('ArrowLeft') ? 1 : 0);
        r.view.pan.x += (aim.x * up - aim.z * right) * speed; r.view.pan.z += (aim.z * up + aim.x * right) * speed;
        clampView(r);
      }
      if (r.state.status !== 0 && !still && r.options.cinematics) {
        // The match is over: drift slowly around the arena behind the result panel.
        orbitAngle += dt * .1;
        place.set(size / 2 + Math.cos(.86 + orbitAngle) * size * 1.5, size * 1.3, size / 2 + Math.sin(.86 + orbitAngle) * size * 1.5);
        aim.set(size / 2, 0, size / 2);
      } else {
        orbitAngle = 0;
        aim.set(size / 2, 0, size / 2).add(r.view.pan);
        playOffset(size, r.view.zoom, place).add(aim);
      }
      camera.position.lerp(place, Math.min(1, dt * 4));
      r.look.lerp(aim, Math.min(1, dt * 4));
      camera.lookAt(r.look);
    } else {
      // The title screen sways gently around the shrine.
      place.copy(menuCamera); if (!still) place.x += Math.sin(clock * .06) * 7, place.y += Math.sin(clock * .045) * 2;
      camera.position.lerp(place, Math.min(1, dt * 3));
      r.look.lerp(menuTarget, Math.min(1, dt * 3));
      camera.lookAt(r.look);
    }
    if (!still) { particles.rotation.y += dt * .012; r.mixers.forEach(m => m.update(dt)); }
    particles.position.set(r.look.x, 0, r.look.z);
    let animated = 0;
    for (const object of r.units.values()) {
      object.position.lerp(object.userData.target, Math.min(1, dt * 14));
      const data = object.userData;
      if ((data.kind === 'relic' || data.kind === 'artifact') && !still) object.rotation.y += dt;
      if (data.loot) {
        if (!still) { data.body.rotation.y += dt * 1.4; data.body.position.y = .35 + Math.sin(clock * 2.2 + data.unitId) * .18; r.fx.wisp(object.position.x, object.position.z, data.lootColor, dt); }
        continue;
      }
      const figure = data.figure;
      if (figure) {
        // Turn the short way towards the heading, then advance the clip.
        let turn = data.heading - figure.rotation.y; turn = Math.atan2(Math.sin(turn), Math.cos(turn));
        figure.rotation.y += turn * Math.min(1, dt * 10);
        if (!still) { data.mixer.update(dt); animated++; }
      }
    }
    r.animated = animated;
    // Fallen units finish their death clip, sink and are released.
    for (let i = r.corpses.length - 1; i >= 0; i--) {
      const corpse = r.corpses[i]; corpse.age += dt;
      if (!still) corpse.object.userData.mixer.update(dt);
      if (corpse.age > 1.8) corpse.object.position.y = -(corpse.age - 1.8) * 1.5;
      if (corpse.age > 3) { r.scene.remove(corpse.object); release(corpse.object); r.corpses.splice(i, 1); }
    }
    // Ultimate effects: each grows and fades over its own duration.
    for (let i = r.effects.length - 1; i >= 0; i--) {
      const effect = r.effects[i]; effect.age += dt;
      const t = Math.min(1, effect.age / effect.duration); effect.step(t);
      if (t >= 1) { r.scene.remove(effect.object); release(effect.object); r.effects.splice(i, 1); }
    }
    // Steady sources of light and smoke: torches, the volcano, and the Keeper's fire.
    const features = r.landscape?.features;
    if (features && !still) {
      for (const flame of features.flames) r.fx.flame(flame.x, flame.y, flame.z, dt);
      for (const smoke of features.smoke) r.fx.smoke(smoke.x, smoke.y, smoke.z, dt);
      if (r.fire && r.story.visible) r.fx.flame(33, 1.6, 23, dt * 2);
    }
    r.life?.update(dt, r.fx, { danger: r.danger || [], daylight: r.daylight, still, zoom: playing ? r.view.zoom : 1 });
    // Now and then an animal on screen calls out: a buffalo lows, a macaque chatters, a hen clucks in the village.
    if (playing && (nextCall -= dt) < 0) {
      nextCall = 5 + Math.random() * 8;
      const callers = r.state.units.filter(u => u.kind === 'wild' && animalVoices[u.model]);
      const caller = callers[Math.floor(Math.random() * callers.length)];
      if (caller) r.sound(animalVoices[caller.model], caller.x, caller.z, .55);
      else if (features?.village && Math.random() < .5) r.sound('chicken', features.village.x, features.village.z, .4);
    }
    r.fx.update(still ? dt * .5 : dt);
    // A GPU that cannot keep bloom above about 38 frames a second for four seconds draws without it for this session.
    if (r.post && !r.intro && playing) {
      r.slow = r.frameMs > 26 ? (r.slow || 0) + wall : 0;
      if (r.slow > 4) { r.post.dispose(); r.post = null; r.postDropped = true; r.slow = 0; console.info('Battle of Nusantara: bloom turned off, frames took over 26 ms.'); }
    }
    particles.visible = !low;
    if (r.post) r.post.render(dt); else renderer.render(scene, camera);
    r.frameCalls = renderer.info.render.calls; r.frameTriangles = renderer.info.render.triangles;
  });
  camera.position.copy(menuCamera); camera.lookAt(menuTarget);
  r.ready = true;
  element.dataset.ready = 'true';
}

// High quality on a software rasterizer becomes 'medium': half the scattered nature and no shadows.
const landscapeQuality = r => r.options.quality === 'low' ? 'low' : r.software ? 'medium' : 'high';
// The battle camera: above and behind the south-east corner, scaled to the map, and nearer when zoomed in.
const playOffset = (size, zoom, out) => out.set(95, 150, 120).multiplyScalar(size / 100 * zoom);
function clampView(r) { const half = r.size / 2; r.view.pan.x = THREE.MathUtils.clamp(r.view.pan.x, -half, half); r.view.pan.z = THREE.MathUtils.clamp(r.view.pan.z, -half, half); r.view.pan.y = 0; }
function resetView(r) { r.view.zoom = 1; r.view.pan.set(0, 0, 0); r.view.keys.clear(); }
function focusHero(r) {
  const hero = r.state?.units.find(u => u.kind === 'hero' && u.owner === r.owner);
  if (!hero) return;
  r.view.zoom = Math.min(r.view.zoom, .5);
  r.view.pan.set(hero.x - r.size / 2, 0, hero.z - r.size / 2); clampView(r);
}

// Builds (or rebuilds) everything that depends on the map: the landscape, the shrines, the fog overlay, the sun's shadow
// box and the ambient life. The title screen uses its own vista.
function applyMap(r, force = false) {
  const map = r.mapDef || MENU_MAP, quality = landscapeQuality(r);
  if (!force && r.landscape?.id === map.id) return;
  r.life?.dispose(release); r.life = null;
  if (r.landscape) { r.scene.remove(r.landscape.group); r.landscape.dispose(); }
  if (r.dressing) { r.scene.remove(r.dressing.group); r.dressing.dispose(); r.dressing = null; r.obstacleKey = null; }
  r.size = map.size;
  r.landscape = buildLandscape(map, r.kit, quality); r.landscape.id = map.id;
  r.scene.add(r.landscape.group);
  r.ocean.uShore.value = r.landscape.shore; r.ocean.uShoreSize.value = r.landscape.extent;
  r.water.position.set(map.size / 2, SEA_LEVEL, map.size / 2);
  const L = layout(map.size);
  r.bases.forEach(({ copy, beacon }, i) => { const at = i === 0 ? L.ally : L.enemy; copy.position.set(at, 0, at); beacon.position.set(at, 10, at); });
  r.fog.scale.set(map.size, map.size, 1); r.fog.position.set(map.size / 2, .15, map.size / 2);
  const center = map.size / 2, reach = Math.max(90, map.size * .8);
  r.sun.position.set(center + 30, 100, center + 50); r.sun.target.position.set(center, 0, center); r.sun.target.updateMatrixWorld();
  Object.assign(r.sun.shadow.camera, { left: -reach, right: reach, top: reach, bottom: -reach, near: 1, far: 260 }); r.sun.shadow.camera.updateProjectionMatrix();
  r.life = new Life(r.scene, model => makeFigure(r, model, r.templates.get(model)?.animal ? animalScales[model] || 2 : 1.9 * 1.05, '#c8a070'), r.landscape, r.options);
  // Adventure keeps its authored props; they are rebuilt with the map.
  for (const child of [...r.story.children]) { r.story.remove(child); release(child); }
  r.fire = r.flame = r.seal = null; r.obstacleKey = null;
  if (map.mode === 2) buildStory(r);
}

export function update(state, owner, selection = [], attackMove = false, buildMode = false, sky = null) {
  const r = runtime; if (!r || r.disposed || !r.ready) return;
  const before = r.state;
  const started = state && !before, ended = state && before && state.status !== 0 && before.status === 0;
  if (sky) r.sky = sky;
  r.element.dataset.weather = state ? r.sky.weather : 'clear';
  r.element.dataset.map = r.landscape?.id || '';
  if (!state) { r.endIntro(); r.combatTarget = 0; r.danger = []; }
  if (started) { resetView(r); r.sound('horn', undefined, undefined, .8); }
  const cinematic = r.options.cinematics && !r.options.reducedMotion;
  if (started && state.tick < 30 && cinematic) startCutscene(r, 'intro', introShots(r, state, owner));
  if (ended && state.mode === 2 && state.status === 1 && cinematic) startCutscene(r, 'ending', endingShots(r, state));
  r.playVoice(state?.mode === 2 ? state.dialogue : '');
  // A stinger when the match is decided: brass and gong for a victory, falling strings for a defeat.
  if (ended) r.audio.play(state.status === 1 ? 'victory' : 'defeat', { gain: 1 });
  r.story.visible = state?.mode === 2;
  r.state = state; r.owner = owner; r.scenery.visible = !state; r.battle.visible = !!state;
  r.attackMove = attackMove;
  r.buildMode = buildMode;
  if (!buildMode) r.placement.visible = false;
  r.renderer.domElement.style.cursor = attackMove || buildMode ? 'crosshair' : 'default';
  const selected = new Set(selection);
  const obstacleKey = state ? JSON.stringify(state.obstacles) : '';
  if (obstacleKey !== r.obstacleKey && r.landscape) {
    if (r.dressing) { r.scene.remove(r.dressing.group); r.dressing.dispose(); r.dressing = null; }
    if (state?.obstacles?.length) { r.dressing = dressObstacles(state.obstacles, r.kit, r.landscape.heightAt, r.landscape.id, landscapeQuality(r)); r.scene.add(r.dressing.group); }
    // The sealed door of the Mystic Cave glows until the road guardians fall.
    if (r.seal) r.seal.visible = !!state?.obstacles?.some(o => o.kind === 'door');
    r.obstacleKey = obstacleKey;
  }
  if (!state) r.cursor.visible = false;
  const vision = state?.vision || [];
  r.fog.visible = vision.length > 0;
  r.element.dataset.fog = vision.length ? 'on' : 'off';
  if (vision.length) {
    const size = r.fogCanvas.width, scale = size / r.size, seen = r.seenCanvas.getContext('2d'), fog = r.fogCanvas.getContext('2d');
    if (r.fogTick === undefined || state.tick < r.fogTick) seen.clearRect(0, 0, size, size);
    r.fogTick = state.tick;
    seen.fillStyle = '#fff';
    for (const source of vision) { seen.beginPath(); seen.arc(source.x * scale, source.z * scale, source.radius * scale, 0, Math.PI * 2); seen.fill(); }
    fog.globalCompositeOperation = 'source-over'; fog.globalAlpha = 1;
    fog.clearRect(0, 0, size, size); fog.fillStyle = 'rgba(8,22,30,.74)'; fog.fillRect(0, 0, size, size);
    // Explored ground stays half-dimmed; ground in current vision is cleared with a soft edge.
    fog.globalCompositeOperation = 'destination-out'; fog.globalAlpha = .5; fog.drawImage(r.seenCanvas, 0, 0); fog.globalAlpha = 1;
    for (const source of vision) {
      const x = source.x * scale, z = source.z * scale, radius = source.radius * scale;
      const edge = fog.createRadialGradient(x, z, radius * .8, x, z, radius);
      edge.addColorStop(0, 'rgba(0,0,0,1)'); edge.addColorStop(1, 'rgba(0,0,0,0)');
      fog.fillStyle = edge; fog.beginPath(); fog.arc(x, z, radius, 0, Math.PI * 2); fog.fill();
    }
    r.fogTexture.needsUpdate = true;
  } else r.fogTick = undefined;
  const resourceIds = new Set();
  for (const resource of state?.resources || []) {
    resourceIds.add(resource.id);
    let marker = r.resourceMarkers.get(resource.id);
    if (!marker) {
      marker = r.props.has('ore') ? r.props.get('ore').clone() : new THREE.Mesh(new THREE.OctahedronGeometry(2.3), r.fixedMaterial);
      marker.traverse(o => { o.userData.resourceId = resource.id; if (o.isMesh) o.castShadow = true; });
      marker.scale.setScalar(1.8); marker.position.set(resource.x, 0, resource.z); r.scene.add(marker); r.resourceMarkers.set(resource.id, marker);
    }
    // An exhausted mine sinks into the ground.
    marker.position.y = resource.remaining > 0 ? 0 : -1.4;
  }
  for (const [id, marker] of r.resourceMarkers) if (!resourceIds.has(id)) { r.scene.remove(marker); release(marker); r.resourceMarkers.delete(id); }
  const live = new Set(); let damage = 0, sparks = 0, neutrals = 0; const danger = [];
  const now = performance.now();
  // The player's ultimate: the cooldown jumped up since the last snapshot.
  const cast = state && state.ultimateCooldown > r.lastCooldown + 5; r.lastCooldown = state?.ultimateCooldown || 0;
  const ownHero = state?.units.find(u => u.kind === 'hero' && u.owner === owner);
  const castFrom = cast ? r.units.get(ownHero?.id)?.position.clone() : null;
  for (const unit of state?.units || []) {
    live.add(unit.id);
    if (unit.kind === 'creep' || unit.kind === 'wild') neutrals++;
    let object = r.units.get(unit.id);
    if (!object) object = createUnit(r, unit, owner, !started && !!before);
    const data = object.userData, target = data.target, dx = unit.x - target.x, dz = unit.z - target.z;
    target.set(unit.x, 0, unit.z);
    if (unit.health < data.health) {
      const lost = data.health - unit.health;
      damage += lost; data.hurtAt = now;
      danger.push([unit.x, unit.z]);
      // Sparks and a hit sound where blows land, a few per snapshot.
      if (sparks++ < 5 && !data.loot) { r.fx.hit(unit.x, unit.z, lost > 30); r.sound('hit', unit.x, unit.z, .7, unit.kind === 'tower' || unit.kind === 'outpost' ? .7 : .9 + Math.random() * .25); }
    }
    data.health = unit.health; data.maxHealth = unit.maxHealth; data.team = unit.team; data.model = data.model || unit.model;
    data.ownHero = unit.kind === 'hero' && unit.owner === owner;
    if (data.figure && cast && data.ownHero) {
      playOnce(object, 'Cast');
      ultimateEffect(r, ultimateEffects[data.model] || 'burst', castFrom || object.position, unit, state);
    }
    if (data.figure && data.oneShot > now) { /* A cast or hit reaction is still playing. */ }
    else if (data.figure) {
      // The snapshot has no animation state: a unit that moved walks, one standing next to an enemy attacks.
      let clip = 'Idle';
      if (dx * dx + dz * dz > .0009) { clip = 'Walk'; data.heading = Math.atan2(dx, dz); }
      else if (unit.kind !== 'npc' && (unit.kind !== 'wild' || data.hurtAt > now - 8000 || unit.model === 'babi-hutan')) {
        let nearest = unit.kind === 'wild' ? 16 : 81, foe = null;
        for (const other of state.units) {
          if (other.team === unit.team || other.kind === 'relic' || other.kind === 'artifact' || other.kind === 'npc' || other.kind === 'loot') continue;
          // Wildlife is only an enemy once it has been hurt; nobody shapes up to a grazing buffalo.
          if (other.kind === 'wild' && !(r.units.get(other.id)?.userData.hurtAt > now - 8000)) continue;
          const distance = (other.x - unit.x) ** 2 + (other.z - unit.z) ** 2;
          if (distance < nearest) { nearest = distance; foe = other; }
        }
        if (foe) { clip = 'Attack'; data.heading = Math.atan2(foe.x - unit.x, foe.z - unit.z); }
      }
      // A unit that was hurt while standing flinches.
      if (clip === 'Idle' && data.hurtAt > now - 120 && unit.kind !== 'boss') playOnce(object, 'Hit');
      else if (clip !== data.clip) {
        const actions = data.actions;
        actions[clip].reset().fadeIn(.18).play(); actions[data.clip].fadeOut(.18);
        // A creep that turns to fight growls; the player's hero swings.
        if (clip === 'Attack' && unit.kind === 'creep') r.sound('growl', unit.x, unit.z, .7, 1.25 / (creepSizes[unit.model] || 1));
        if (clip === 'Attack' && unit.kind === 'wild' && animalVoices[unit.model]) r.sound(animalVoices[unit.model], unit.x, unit.z, .8);
        if (clip === 'Attack' && data.ownHero) r.sound('swing', unit.x, unit.z, .6);
        data.clip = clip;
      }
    }
    if (data.ring) {
      data.ring.material.color.set(selected.has(unit.id) ? '#fff0b8' : unit.color);
      data.ring.scale.setScalar(selected.has(unit.id) ? 1.25 : 1);
    }
    if (data.bar) {
      data.bar.scale.x = Math.max(.01, unit.health / unit.maxHealth);
      // Neutrals show their health only once they have been hurt.
      if (unit.kind === 'creep' || unit.kind === 'wild') data.bar.visible = unit.health < unit.maxHealth;
    }
    if (data.cargo) {
      if (unit.cargo > (data.cargoAmount || 0)) r.sound('mine', unit.x, unit.z, .55);
      data.cargoAmount = unit.cargo; data.cargo.visible = unit.cargo > 0;
    }
    if (unit.kind === 'outpost') {
      // A foundation rises with the work and stays translucent until it is finished.
      const body = data.body, done = unit.construction >= 100;
      if (unit.construction > (data.construction ?? unit.construction)) r.sound('build', unit.x, unit.z, .6);
      data.construction = unit.construction;
      body.scale.y = 1.15 * (.15 + .85 * unit.construction / 100);
      body.traverse(o => { if (o.isMesh) { o.material.opacity = done ? 1 : .55; o.material.depthWrite = done; } });
    }
    if (unit.kind === 'artifact' || unit.kind === 'relic') data.body.position.y = unit.kind === 'artifact' ? 1 + Math.sin(state.tick * .2) * .3 : 0;
  }
  r.neutrals = neutrals;
  for (const [id, object] of r.units) {
    if (live.has(id)) continue;
    r.units.delete(id);
    const data = object.userData, at = object.position;
    if (data.loot) {
      // Taken by the player's hero: a rising sparkle and a chime.
      const hero = ownHero && (ownHero.x - at.x) ** 2 + (ownHero.z - at.z) ** 2 < 25;
      if (hero && state) { r.fx.pickup(at.x, at.z, data.lootColor); r.sound('loot', at.x, at.z, 1); }
      r.scene.remove(object); release(object);
      continue;
    }
    // A character that vanished while low on health, where the team can still see it, fell: let it play its death.
    const seen = !vision.length || data.team === 0 || vision.some(v => (v.x - at.x) ** 2 + (v.z - at.z) ** 2 <= v.radius * v.radius);
    if (state && data.figure && seen && data.health <= data.maxHealth * .5) {
      r.fx.death(at.x, at.z, data.kind === 'creep' || data.kind === 'boss');
      const voice = data.kind === 'creep' ? 'growl' : data.kind === 'wild' ? animalVoices[data.model] || 'death' : 'death';
      r.sound(voice, at.x, at.z, .8, data.kind === 'creep' ? .8 / (creepSizes[data.model] || 1) : 1);
      if (!r.options.reducedMotion) {
        const actions = data.actions;
        Object.values(actions).forEach(a => a.stop()); actions.Death.reset().play();
        if (data.bar) data.bar.visible = false;
        if (data.ring) data.ring.visible = false;
        r.corpses.push({ object, age: 0 });
        continue;
      }
    }
    data.mixer?.stopAllAction(); r.scene.remove(object); release(object);
  }
  // Moments the simulation reports in its message line, and level and quest changes.
  if (state && before) {
    if (state.message && state.message !== before.message) { const cue = cues.find(([pattern]) => pattern.test(state.message)); if (cue) r.sound(cue[1], undefined, undefined, .9); }
    if (state.level > before.level && ownHero) { r.fx.levelUp(ownHero.x, ownHero.z); r.sound('levelup', ownHero.x, ownHero.z, 1); }
    if (state.gold >= before.gold + 10 && !cues.some(([pattern]) => pattern.test(state.message || ''))) r.sound('coin', ownHero?.x, ownHero?.z, .6);
  }
  r.danger = danger;
  // Fighting intensity for the adaptive music: recent damage, decaying each snapshot.
  r.combatTarget = state && state.status === 0 ? Math.min(1, r.combatTarget * .9 + damage / 160) : 0;
  r.element.dataset.combat = r.combatTarget > .18 ? 'battle' : 'calm';
}

// A unit appears: a rigged character or animal, a prop, or loot with its pillar of light.
function createUnit(r, unit, owner, fresh) {
  const object = new THREE.Group(); object.userData.kind = unit.kind; object.userData.unitId = unit.id;
  let size = unit.kind === 'boss' ? 3.2 : unit.kind === 'tower' ? 2.2 : unit.kind === 'hero' || unit.kind === 'champion' ? 1.8 : unit.kind === 'warden' || unit.kind === 'npc' ? 1.4 : 1.1;
  if (unit.kind === 'creep') size = 1.1 * (creepSizes[unit.model] || 1);
  const model = unit.kind === 'loot' ? null : unit.kind === 'boss' && unit.model === 'naga' ? 'naga' : modelFor(unit);
  const template = model && r.templates.get(model);
  if (template) {
    const scale = template.animal ? animalScales[model] || 2 : (model === 'naga' ? 1.2 : 1.9) * size;
    if (template.animal) size = .8 + (animalScales[model] || 2) * .25;
    const body = makeFigure(r, model, scale, unit.color);
    Object.assign(object.userData, { figure: body.figure, mixer: body.mixer, actions: body.actions, clip: 'Idle', model, oneShot: 0, heading: unit.team === 0 || unit.team === 2 ? .8 : -2.3 });
    body.actions.Idle.time = (unit.id * .37) % body.actions.Idle.getClip().duration; body.mixer.update(0);
    body.figure.rotation.y = object.userData.heading;
    object.add(body.figure); r.modelled = (r.modelled || 0) + 1;
  } else if (unit.kind === 'loot') {
    const body = r.props.get(lootProp(unit.model)).clone();
    body.traverse(o => { if (o.isMesh) { o.castShadow = true; if (o.material.name === 'Tint') o.material = tintMaterial(r, unit.color); } });
    body.scale.setScalar(unit.model === 'peti' ? 1.8 : 2.2);
    object.add(body, r.fx.beam(unit.color));
    Object.assign(object.userData, { body, loot: true, lootColor: unit.color });
    size = .9; r.modelled = (r.modelled || 0) + 1;
    if (fresh) r.fx.lootBurst(unit.x, unit.z, unit.color);
  } else if (propKinds[unit.kind] && r.props.has(propKinds[unit.kind])) {
    const body = r.props.get(propKinds[unit.kind]).clone();
    body.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; if (o.material.name === 'Tint') o.material = tintMaterial(r, unit.color); } });
    if (unit.kind === 'outpost') body.traverse(o => { if (o.isMesh) { o.material = o.material.clone(); o.material.transparent = true; } });
    body.scale.setScalar({ tower: 1.1, outpost: 1.15, relic: 1.3, artifact: 1.3, flotsam: 1.4 }[unit.kind]);
    object.add(body); object.userData.body = body; r.modelled = (r.modelled || 0) + 1;
  } else {
    const material = new THREE.MeshStandardMaterial({ color: unit.color, roughness: .45, metalness: .35, emissive: unit.color, emissiveIntensity: .4 });
    const geometry = unit.kind === 'worker' ? new THREE.IcosahedronGeometry(1.3) : new THREE.ConeGeometry(size, size * 3, 6);
    const body = new THREE.Mesh(geometry, material); body.position.y = size * 1.6; body.castShadow = true; object.add(body);
    object.userData.body = body;
  }
  const ring = new THREE.Mesh(new THREE.RingGeometry(size + .4, size + .65, 24), new THREE.MeshBasicMaterial({ color: unit.owner === owner ? '#fff0b8' : unit.color, side: THREE.DoubleSide }));
  ring.rotation.x = -Math.PI / 2; ring.position.y = .08; object.add(ring);
  object.userData.ring = ring;
  if (unit.kind === 'worker') {
    const cargo = new THREE.Mesh(new THREE.BoxGeometry(.8, .8, .8), new THREE.MeshStandardMaterial({ color: '#e9c368', emissive: '#e9c368', emissiveIntensity: .3 })); cargo.position.set(0, 5.2, 0); object.add(cargo); object.userData.cargo = cargo;
  }
  if (!['relic', 'npc', 'artifact', 'flotsam', 'loot'].includes(unit.kind)) {
    const bar = new THREE.Mesh(new THREE.BoxGeometry(3, .25, .35), new THREE.MeshBasicMaterial({ color: unit.team === 0 ? '#83c9ba' : unit.team === 3 ? '#e9c368' : '#dc8ba8' }));
    bar.position.y = unit.kind === 'wild' ? size * 3 : size * 4.5; object.add(bar); object.userData.bar = bar;
    if (unit.kind === 'creep' || unit.kind === 'wild') bar.visible = unit.health < unit.maxHealth;
  }
  object.position.set(unit.x, 0, unit.z); object.userData.target = new THREE.Vector3(unit.x, 0, unit.z); object.userData.health = unit.health;
  r.units.set(unit.id, object); r.scene.add(object);
  return object;
}
// A rigged figure from a template: shared geometry and fixed material, the Tint parts in the unit's colour.
function makeFigure(r, model, scale, color) {
  const template = r.templates.get(model); if (!template) return null;
  const figure = cloneSkinned(template.scene); figure.scale.setScalar(scale);
  figure.traverse(o => { if (o.isSkinnedMesh) { o.castShadow = true; if (o.material.name === 'Tint') o.material = tintMaterial(r, color); } });
  const mixer = new THREE.AnimationMixer(figure), actions = {};
  for (const clip of template.clips) actions[clip.name] = mixer.clipAction(clip);
  actions.Walk.timeScale = 1.5; actions.Attack.timeScale = 1.25;
  for (const name of ['Cast', 'Hit', 'Death']) { actions[name].setLoop(THREE.LoopOnce); actions[name].clampWhenFinished = true; }
  actions.Idle.play(); mixer.update(0);
  const object = new THREE.Group(); object.add(figure);
  return { object, figure, mixer, actions };
}
// One template per character or animal variant: the shared rig with only that variant's mesh. Fixed parts keep their
// vertex colours; the Tint material is replaced per unit colour. Geometry and the fixed material are shared by every
// clone. The environment kit keeps each object's parts for instancing.
function prepareModels(r, set, characters, naga, props, environment, animals) {
  const fixed = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .6, metalness: .1 }); fixed.name = 'Fixed'; fixed.userData.shared = true;
  r.fixedMaterial = fixed;
  const share = root => root.traverse(o => { if (o.isMesh) { o.geometry.userData.shared = true; if (o.material.name !== 'Tint') { o.material.dispose(); o.material = fixed; } } });
  const bound = new THREE.Sphere();
  const variantsOf = (scene, rigName, animal, templates) => {
    const rig = scene.getObjectByName(rigName);
    const variants = rig.children.filter(c => c.isGroup || c.isSkinnedMesh);
    share(scene);
    // Each template clones the rig with one variant attached, not all of them.
    variants.forEach(variant => rig.remove(variant));
    for (const variant of variants) {
      const name = variant.name;
      rig.add(variant);
      const copy = cloneSkinned(scene);
      rig.remove(variant);
      // Culling uses a sphere around the bind pose, padded for the widest clip, instead of skinning every vertex.
      copy.traverse(o => { if (o.isSkinnedMesh) { o.geometry.computeBoundingSphere(); bound.copy(o.geometry.boundingSphere); bound.radius = bound.radius * 1.5 + .3; o.boundingSphere = bound.clone(); } });
      templates.set(name, { scene: copy, clips: scene.userData.clips, animal });
    }
    variants.forEach(variant => rig.add(variant));
  };
  // Animals and the naga are shared by both character sets.
  const common = new Map();
  animals.scene.userData.clips = animals.animations;
  variantsOf(animals.scene, 'AnimalRig', true, common);
  share(naga.scene);
  naga.scene.traverse(o => { if (o.isSkinnedMesh) o.frustumCulled = false; });
  common.set('naga', { scene: naga.scene, clips: naga.animations });
  r.templateSets = { high: new Map(common), low: new Map(common) };
  r.addCharacters = (which, gltf) => {
    gltf.scene.userData.clips = gltf.animations;
    variantsOf(gltf.scene, 'CharacterRig', false, r.templateSets[which]);
    r.characterScenes.push(gltf.scene);
  };
  share(props.scene);
  for (const child of props.scene.children) r.props.set(child.name, child);
  // The environment kit: one rougher vertex-colour material for nature and stone; glowing parts keep their own.
  const nature = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .86, metalness: 0 }); nature.name = 'Fixed'; nature.userData.shared = true;
  r.kit = { env: new Map(), heights: {} };
  const box = new THREE.Box3();
  for (const child of environment.scene.children) {
    const parts = [];
    child.traverse(o => {
      if (!o.isMesh) return;
      o.geometry.userData.shared = true;
      let material = o.material;
      if (/Glow|Lava/.test(material.name)) { material.userData.shared = true; material.toneMapped = true; }
      else { if (material !== nature) material.dispose(); material = nature; }
      parts.push({ geometry: o.geometry, material });
    });
    r.kit.env.set(child.name, parts);
    box.setFromObject(child); r.kit.heights[child.name] = box.max.y;
  }
  r.characterScenes = [naga.scene, props.scene, environment.scene, animals.scene];
  r.addCharacters(set, characters);
  r.templates = r.templateSets[set];
}
// After the quality changes: loads the other character set if needed, then rebuilds the characters on screen from it.
async function ensureCharacters(r) {
  const set = characterSet(r.options), templates = r.templateSets?.[set];
  if (!templates) return;
  if (!templates.has('prajurit')) {
    if (r.loadingSet === set) return;
    r.loadingSet = set;
    try {
      const gltf = await new GLTFLoader().loadAsync(new URL(`./models/${characterFiles[set]}`, import.meta.url).href);
      if (r.disposed) { release(gltf.scene, true); return; }
      r.addCharacters(set, gltf);
    } catch (error) { console.warn('Battle of Nusantara: characters could not load', error); return; }
    finally { r.loadingSet = null; }
  }
  if (r.disposed || characterSet(r.options) !== set || r.templates === templates) return;
  r.templates = templates;
  // Every unit is drawn again from the new set at the next snapshot.
  for (const object of r.units.values()) { object.userData.mixer?.stopAllAction(); r.scene.remove(object); release(object); }
  r.units.clear();
  applyMap(r, true);
}
function tintMaterial(r, color) {
  let material = r.tintMaterials.get(color);
  if (!material) { material = new THREE.MeshStandardMaterial({ color, roughness: .5, metalness: .25, emissive: color, emissiveIntensity: .18 }); material.name = 'Tint'; material.userData.shared = true; r.tintMaterials.set(color, material); }
  return material;
}
function playOnce(object, name) {
  const actions = object.userData.actions, action = actions[name];
  if (!action) return;
  actions[object.userData.clip]?.fadeOut(.1);
  action.reset().fadeIn(.08).play();
  object.userData.oneShot = performance.now() + action.getClip().duration / action.getEffectiveTimeScale() * 1000 * .9;
  object.userData.clip = name;
}
// A visible ultimate, matching the rules in Match.cs: a burst ring, chain lightning to the enemies that were hurt,
// a leap trail with a shockwave, or the southern wave. Particles and a sound go with each.
function ultimateEffect(r, kind, from, hero, state) {
  const group = new THREE.Group(), x = hero.x, z = hero.z;
  const color = { chain: '#bfeaff', leap: '#c7a6ff', wave: '#7fe0d0', burst: '#f2d27a' }[kind];
  const ring = new THREE.Mesh(new THREE.RingGeometry(.8, 1.4, 48), new THREE.MeshBasicMaterial({ color: new THREE.Color(color).multiplyScalar(1.8), transparent: true, opacity: .9, side: THREE.DoubleSide, depthWrite: false }));
  ring.rotation.x = -Math.PI / 2; ring.position.set(x, .3, z); group.add(ring);
  const reach = { chain: 6, leap: 14, wave: 20, burst: 20 }[kind];
  if (kind === 'chain') {
    // Bolts from the hero through the enemies that lost health, nearest first.
    const targets = state.units.filter(u => u.team === 1 || u.team === 3).map(u => ({ u, object: r.units.get(u.id) })).filter(t => t.object && t.object.userData.hurtAt > performance.now() - 200)
      .sort((a, b) => ((a.u.x - x) ** 2 + (a.u.z - z) ** 2) - ((b.u.x - x) ** 2 + (b.u.z - z) ** 2)).slice(0, 6);
    let px = x, pz = z; const points = [];
    for (const target of targets) {
      for (let k = 0; k <= 6; k++) { const t = k / 6; points.push(new THREE.Vector3(px + (target.u.x - px) * t + (k % 6 ? (Math.random() - .5) * 1.6 : 0), 3 + (k % 6 ? Math.random() * 1.5 : 0), pz + (target.u.z - pz) * t + (k % 6 ? (Math.random() - .5) * 1.6 : 0))); }
      px = target.u.x; pz = target.u.z;
    }
    if (points.length > 1) { group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color: new THREE.Color('#e6f8ff').multiplyScalar(2.5), transparent: true }))); r.fx.lightning(points); }
    r.flash = Math.max(r.flash, .6);
  } else if (kind === 'leap') {
    const arc = []; for (let k = 0; k <= 16; k++) { const t = k / 16; arc.push(new THREE.Vector3(from.x + (x - from.x) * t, Math.sin(t * Math.PI) * 9 + 1, from.z + (z - from.z) * t)); }
    group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(arc), new THREE.LineBasicMaterial({ color: new THREE.Color(color).multiplyScalar(2), transparent: true })));
    r.fx.shockwave(x, z, color);
  } else if (kind === 'wave') {
    const wall = new THREE.Mesh(new THREE.CylinderGeometry(1, 1, 2.5, 48, 1, true), new THREE.MeshBasicMaterial({ color, transparent: true, opacity: .5, side: THREE.DoubleSide, depthWrite: false }));
    wall.position.set(x, 1.2, z); group.add(wall);
    for (let k = 0; k < 6; k++) { const a = k * Math.PI / 3; r.fx.splash(x + Math.cos(a) * 6, z + Math.sin(a) * 6); }
  } else r.fx.magic(x, z, color);
  r.scene.add(group);
  r.effects.push({ object: group, age: 0, duration: kind === 'chain' ? .45 : .9, step: t => {
    const radius = 1 + t * reach;
    ring.scale.setScalar(radius); ring.material.opacity = .9 * (1 - t);
    group.children.forEach(child => { if (child !== ring) { if (child.isLine) child.material.opacity = 1 - t; else { child.scale.set(radius, 1 - t * .6, radius); child.material.opacity = .5 * (1 - t); } } });
  } });
  r.sound(ultimateSounds[kind], x, z, 1.1);
}
// Cutscenes are lists of shots; each shot moves the camera along a path and looks along another.
function startCutscene(r, kind, shots) {
  if (!shots.length) return;
  r.intro = { kind, shots, index: 0, time: 0 };
  r.caption(shots[0]);
  r.cine.classList.add('on'); r.element.dataset.cinematic = kind;
}
const glide = (a, b) => (t, out) => out.set(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t);
function introShots(r, state, owner) {
  const hero = state.units.find(u => u.kind === 'hero' && u.owner === owner), english = r.options.language === 'en', size = r.size;
  const hx = hero?.x ?? size / 2, hz = hero?.z ?? size / 2, rise = playOffset(size, 1, new THREE.Vector3()).add(new THREE.Vector3(size / 2, 0, size / 2)).toArray(), center = [size / 2, 0, size / 2];
  // The last shot: a low orbit around the player's hero that rises into the play camera.
  const toPlay = (title, line, duration) => ({ title, line, duration,
    camera: (t, out) => { const angle = 2.2 + t * 1.5, x = hx + Math.cos(angle) * 34, z = hz + Math.sin(angle) * 34, e = t * t; out.set(x + (rise[0] - x) * e, 15 + (rise[1] - 15) * e, z + (rise[2] - z) * e); },
    look: (t, out) => { const e = t * t; out.set(hx + (center[0] - hx) * e, 3 * (1 - e), hz + (center[2] - hz) * e); } });
  const mapName = r.mapDef ? (english ? r.mapDef.nameEn : r.mapDef.name) : '';
  if (state.mode === 0) return [toPlay('RTS Skirmish', (mapName ? `${mapName}. ` : '') + (english ? 'Build outposts, gather gold, bring down the enemy base.' : 'Bangun pos, kumpulkan emas, runtuhkan markas lawan.'), 5)];
  if (state.mode === 1) return [toPlay('MOBA Arena', english ? 'Push the lane, break the towers, destroy the base.' : 'Dorong jalur, runtuhkan menara, hancurkan markas.', 5)];
  // Adventure: an authored tour of the places the story will lead to.
  return [
    { title: english ? 'The western reef' : 'Karang barat', line: english ? 'When the ages collided, the sea broke a ship.' : 'Saat zaman-zaman bertabrakan, laut mematahkan sebuah kapal.', duration: 3.5,
      camera: glide([-34, 12, 96], [-22, 8, 76]), look: glide([-14, 0, 66], [-6, 0, 62]) },
    { title: english ? 'The Keeper\'s camp' : 'Perkemahan Juru Kunci', line: english ? 'An old keeper waits beside the shrine.' : 'Seorang juru kunci menunggu di dekat candi.', duration: 3,
      camera: glide([46, 9, 4], [40, 6, 8]), look: glide([30, 2, 18], [28, 2, 20]) },
    { title: english ? 'The Mystic Cave' : 'Gua Mistis', line: english ? 'Beyond the sealed door, Sang Kalatidha gathers his power.' : 'Di balik pintu tersegel, Sang Kalatidha menghimpun kekuatannya.', duration: 3.5,
      camera: glide([58, 20, 58], [68, 13, 66]), look: glide([84, 3, 83], [84, 4, 83]) },
    toPlay('Adventure', english ? 'Seek the Keeper.' : 'Temui Juru Kunci.', 3.5)
  ];
}
function endingShots(r, state) {
  const english = r.options.language === 'en';
  return [{ title: english ? 'Jagat Nusantara endures' : 'Jagat Nusantara bertahan', line: english ? 'Sang Kalatidha has fallen at Borobudur Quantum.' : 'Sang Kalatidha tumbang di Borobudur Quantum.', duration: 7,
    camera: (t, out) => { const angle = .4 + t * 1.6; out.set(84 + Math.cos(angle) * 30, 18 - t * 6, 83 + Math.sin(angle) * 30); }, look: (t, out) => out.set(84, 3, 83) }];
}
// Environmental storytelling for Adventure: props that explain the place without a line of dialogue. None block movement.
function buildStory(r) {
  const wood = new THREE.MeshStandardMaterial({ color: '#5b4330', roughness: 1 });
  const iron = new THREE.MeshStandardMaterial({ color: '#8d979a', roughness: .5, metalness: .7 });
  const add = (geometry, material, x, y, z, rx = 0, ry = 0, rz = 0) => { const mesh = new THREE.Mesh(geometry, material); mesh.position.set(x, y, z); mesh.rotation.set(rx, ry, rz); mesh.castShadow = true; r.story.add(mesh); return mesh; };
  // Kit objects as single meshes: they share the environment geometry and materials.
  const kit = (name, x, z, scale = 1, yaw = 0, y = 0, rx = 0) => {
    const group = new THREE.Group();
    for (const part of r.kit.env.get(name) || []) { const mesh = new THREE.Mesh(part.geometry, part.material); mesh.castShadow = true; mesh.receiveShadow = true; group.add(mesh); }
    group.position.set(x, y, z); group.rotation.set(rx, yaw, 0); group.scale.setScalar(scale); r.story.add(group); return group;
  };
  // The Keeper's camp: he has lived beside the path for a long time.
  kit('hut', 25, 16, 1, .6); kit('banner', 34, 17, .8, .3); kit('goods', 29, 20, .9, 1.1); kit('lantern', 31, 15, 1);
  for (let i = 0; i < 3; i++) add(new THREE.CylinderGeometry(.22, .22, 2.4, 5), wood, 33, .3, 23, Math.PI / 2, i * 2.1);
  r.flame = add(new THREE.ConeGeometry(.7, 1.8, 6), new THREE.MeshBasicMaterial({ color: new THREE.Color('#ffb35c').multiplyScalar(1.5) }), 33, 1.2, 23); r.flame.castShadow = false;
  r.fire = new THREE.PointLight('#ff9a4a', 60, 26, 2); r.fire.position.set(33, 2.6, 23); r.story.add(r.fire);
  // Each inscription stands in the remains of a small shrine: some pillars upright, some fallen.
  for (const [x, z] of [[27, 29], [60, 38], [75, 72]])
    for (let i = 0; i < 5; i++) {
      const angle = i * 1.2566 + x, fallen = (i + x) % 3 === 0, px = x + Math.cos(angle) * 4.8, pz = z + Math.sin(angle) * 4.8;
      kit('ruin_pillar', px, pz, .55 + (i % 3) * .08, angle, fallen ? .6 : 0, fallen ? Math.PI / 2 : 0);
    }
  // An old battlefield on the way to the sealed chamber: weapons left where their owners fell.
  for (let i = 0; i < 12; i++) {
    const x = 66 + (i * 7.3) % 13, z = 56 + (i * 4.9) % 12;
    if (i % 3) add(new THREE.BoxGeometry(.16, 2.6, .05), iron, x, 1, z, .3 + (i % 4) * .12, i, .2);
    else add(new THREE.CylinderGeometry(.9, .9, .12, 8), wood, x, .12, z, .15, i, 0);
  }
  // The wreck on the western reef: something happened here before the player arrived.
  const wreck = new THREE.Group();
  const hull = new THREE.Mesh(new THREE.BoxGeometry(5, 4, 16), wood); hull.rotation.set(.1, 0, .5); wreck.add(hull);
  const mast = new THREE.Mesh(new THREE.CylinderGeometry(.3, .4, 15, 6), wood); mast.position.set(2.5, 6, -1); mast.rotation.z = -.75; wreck.add(mast);
  const yard = new THREE.Mesh(new THREE.BoxGeometry(.4, .4, 8), wood); yard.position.set(5, 9, -1); yard.rotation.z = -.75; wreck.add(yard);
  wreck.position.set(-14, SEA_LEVEL - 1, 66); wreck.rotation.y = .5; r.story.add(wreck);
  // The Mystic Cave gate: a split gate whose gap is closed by a shimmering seal while the door is an obstacle.
  kit('gate_bentar', 84, 30, 1.7);
  const seal = new THREE.Mesh(new THREE.PlaneGeometry(3.6, 7.5), new THREE.ShaderMaterial({
    uniforms: { uTime: r.fx.beamUniforms.uTime }, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    vertexShader: 'varying vec2 vUv;\nvoid main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
    fragmentShader: 'uniform float uTime; varying vec2 vUv;\nvoid main() { float band = .5 + .5 * sin(vUv.y * 18.0 - uTime * 3.0 + sin(vUv.x * 9.0 + uTime) * 1.5); float edge = smoothstep(.0, .25, vUv.x) * smoothstep(1.0, .75, vUv.x) * smoothstep(1.0, .7, vUv.y); gl_FragColor = vec4(vec3(.55, .35, 1.0) * (1.2 + band), (.25 + .35 * band) * edge);\n#include <colorspace_fragment>\n}'
  }));
  seal.position.set(84, 3.8, 30); r.story.add(seal); r.seal = seal;
  seal.visible = !!r.state?.obstacles?.some(o => o.kind === 'door');
}
// Geometry and materials shared between characters are kept unless `all` is set (on dispose).
function release(root, all = false) {
  const geometries = new Set(), materials = new Set();
  // Every rigged clone has its own skeleton, whose bone texture must go too.
  root.traverse(object => { if (object.isSkinnedMesh) object.skeleton?.dispose(); if (object.geometry && (all || !object.geometry.userData.shared)) geometries.add(object.geometry); if (object.material) (Array.isArray(object.material) ? object.material : [object.material]).forEach(m => { if (all || !m.userData.shared) materials.add(m); }); });
  geometries.forEach(g => g.dispose()); materials.forEach(m => { for (const value of Object.values(m)) if (value?.isTexture) value.dispose(); m.dispose(); });
}
export function dispose() {
  const r = runtime; if (!r) return; r.disposed = true;
  r.renderer.setAnimationLoop(null); r.resize?.disconnect();
  window.removeEventListener('keydown', r.key); window.removeEventListener('keyup', r.keyUp); window.removeEventListener('blur', r.blur);
  window.removeEventListener('pointerdown', r.startAudio); window.removeEventListener('keydown', r.startAudio);
  document.removeEventListener('pointerdown', r.uiClick, true);
  r.renderer.domElement.removeEventListener('pointerdown', r.down);
  r.renderer.domElement.removeEventListener('pointermove', r.dragMove);
  r.renderer.domElement.removeEventListener('pointerup', r.up);
  r.renderer.domElement.removeEventListener('pointercancel', r.dragCancel);
  r.renderer.domElement.removeEventListener('wheel', r.wheel);
  r.box?.remove(); r.cine?.remove();
  r.renderer.domElement.removeEventListener('pointermove', r.hover);
  r.renderer.domElement.removeEventListener('contextmenu', r.contextMenu);
  r.element.parentElement?.removeEventListener('pointerdown', r.mapClick);
  r.life?.dispose(release); r.landscape?.dispose(); r.dressing?.dispose(); r.post?.dispose(); r.fx.dispose();
  r.mixers.forEach(m => m.stopAllAction()); release(r.scene, true); (r.characterScenes || []).forEach(scene => release(scene, true)); Object.values(r.templateSets || {}).forEach(set => set.forEach(t => release(t.scene, true))); r.tintMaterials.forEach(m => m.dispose()); r.fixedMaterial?.dispose(); clearSway(); r.voice?.pause(); r.renderer.dispose(); r.renderer.domElement.remove();
  r.audio.dispose(); runtime = null;
}
