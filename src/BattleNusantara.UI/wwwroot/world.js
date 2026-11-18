import * as THREE from './vendor/three.module.js';
import { GLTFLoader } from './vendor/GLTFLoader.js';
import { clone as cloneSkinned } from './vendor/SkeletonUtils.js';
import { mergeGeometries } from './vendor/BufferGeometryUtils.js';

let runtime;
const defaults = { language: 'id', quality: 'high', reducedMotion: false, volume: 20, cinematics: true, voices: true };
// Which character model a unit uses: the snapshot names heroes, champions, NPCs and bosses; the rest follow their kind.
const kindModels = { guardian: 'guardian', warden: 'warden', spirit: 'spirit', worker: 'worker' };
const modelFor = unit => unit.model || (unit.kind === 'soldier' ? (unit.team === 0 ? 'prajurit' : 'prajurit-kalatidha') : kindModels[unit.kind]) || null;
const propKinds = { tower: 'tower', outpost: 'outpost', relic: 'relic', artifact: 'artifact', flotsam: 'flotsam' };
// Ultimate effects follow the hero's ultimate (Catalog.cs): chain lightning, leap, the southern wave, or a burst.
const ultimateEffects = { bledhek: 'chain', 'rara-angkasa': 'leap', 'nyi-roro-kidul': 'wave' };
export function loadSettings() {
  try {
    const saved = JSON.parse(localStorage.getItem('nusantara.settings') || '{}');
    return { language: saved.language === 'en' ? 'en' : 'id', quality: saved.quality === 'low' ? 'low' : 'high', reducedMotion: Boolean(saved.reducedMotion || matchMedia('(prefers-reduced-motion: reduce)').matches), volume: Number.isFinite(saved.volume) ? Math.max(0, Math.min(100, saved.volume)) : 20, cinematics: saved.cinematics !== false, voices: saved.voices !== false };
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
  const info = r.renderer.info;
  return { calls: info.render.calls, triangles: info.render.triangles, geometries: info.memory.geometries, textures: info.memory.textures, units: r.units.size, frameMs: r.frameMs || 0, animated: r.animated || 0, weather: r.sky.weather, rain: r.rain, daylight: r.daylight, combat: r.combat, cinematic: r.intro ? r.intro.kind : 'off', audio: r.audio ? r.audio.state : 'off', models: r.modelled || 0, corpses: r.corpses.length, effects: r.effects.length, voice: r.voiceKey || '', music: r.musicStep || 0 };
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
  if (runtime) {
    runtime.options = value;
    runtime.renderer.setPixelRatio(value.quality === 'low' ? 1 : Math.min(devicePixelRatio, 1.75));
    if (runtime.gain) runtime.gain.gain.setTargetAtTime(value.volume / 3500, runtime.audio.currentTime, .2);
  }
}
export async function mount(element, reference, options) {
  dispose();
  const scene = new THREE.Scene();
  scene.background = new THREE.Color('#19323e');
  scene.fog = new THREE.FogExp2('#19323e', .0035);
  const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.75));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.3;
  element.append(renderer.domElement);
  const camera = new THREE.PerspectiveCamera(39, 1, .1, 600);
  const r = runtime = { element, reference, scene, renderer, camera, options, units: new Map(), resourceMarkers: new Map(), state: null, owner: 0, mixers: [], scenery: new THREE.Group(), battle: new THREE.Group(), ruins: new THREE.Group(), story: new THREE.Group(), disposed: false, sky: { weather: 'clear', daylight: 1 }, rain: 0, mist: 0, daylight: 1, swell: .5, combat: 0, combatTarget: 0, flash: 0, intro: null, tintMaterials: new Map(), corpses: [], effects: [], templates: new Map(), props: new Map(), lastCooldown: 0 };
  settings(options);
  scene.add(r.scenery, r.battle, r.ruins, r.story);
  const hemisphere = new THREE.HemisphereLight('#b9e1e0', '#30454b', 2.6); scene.add(hemisphere);
  const sun = new THREE.DirectionalLight('#ffe4b0', 4.3);
  sun.position.set(30, 100, 50); sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  Object.assign(sun.shadow.camera, { left: -90, right: 90, top: 90, bottom: -90, far: 250 });
  sun.shadow.bias = -.001;
  scene.add(sun);
  const rim = new THREE.DirectionalLight('#70bcd8', 3); rim.position.set(100, 40, -60); scene.add(rim);
  const stone = new THREE.MeshStandardMaterial({ color: '#3d5c60', roughness: .95 });
  const ground = new THREE.Mesh(new THREE.BoxGeometry(116, 3, 116), stone); ground.position.set(50, -2, 50); ground.receiveShadow = true; scene.add(ground);
  // Ocean: a swell animated in the vertex shader, so the waves cost no CPU time. `swell` rises with the weather.
  const waterUniforms = { uTime: { value: 0 }, uSwell: { value: .5 } };
  const waterMaterial = new THREE.MeshStandardMaterial({ color: '#1d5360', metalness: .6, roughness: .25 });
  waterMaterial.onBeforeCompile = shader => {
    Object.assign(shader.uniforms, waterUniforms);
    shader.vertexShader = 'uniform float uTime;\nuniform float uSwell;\n' + shader.vertexShader
      .replace('#include <beginnormal_vertex>', '#include <beginnormal_vertex>\n  float waveA = position.x * .09 + uTime * .9, waveB = position.y * .07 - uTime * .7, waveC = (position.x + position.y) * .21 + uTime * 1.6;\n  objectNormal = normalize(vec3(-(cos(waveA) * .09 + cos(waveC) * .063) * uSwell, -(cos(waveB) * .07 + cos(waveC) * .063) * uSwell, 1.0));')
      .replace('#include <begin_vertex>', '#include <begin_vertex>\n  transformed.z += (sin(waveA) + sin(waveB) + sin(waveC) * .3) * uSwell;');
  };
  const water = new THREE.Mesh(new THREE.PlaneGeometry(700, 700, 96, 96), waterMaterial); water.rotation.x = -Math.PI / 2; water.position.set(50, -4, 50); scene.add(water);
  // The rain box reaches from the arena up to the camera, so streaks fill the view.
  // Shoreline foam where the island meets the sea: a square ring that breathes with the swell.
  const foam = new THREE.Mesh(new THREE.RingGeometry(58 * Math.SQRT2, 63 * Math.SQRT2, 4, 1), new THREE.MeshBasicMaterial({ color: '#d9eef0', transparent: true, opacity: .35, depthWrite: false, side: THREE.DoubleSide }));
  foam.rotation.set(-Math.PI / 2, 0, Math.PI / 4); foam.position.set(50, -3.85, 50); scene.add(foam);
  // Rain rings on the sea around the island, recycled; they appear only while it rains.
  const ripples = new THREE.InstancedMesh(new THREE.RingGeometry(.6, .85, 16), new THREE.MeshBasicMaterial({ color: '#cfe6ea', transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide }), 48);
  const rippleAge = new Float32Array(48), rippleSpot = new Float32Array(96), rippleMatrix = new THREE.Object3D();
  for (let i = 0; i < 48; i++) { rippleAge[i] = i / 48; const angle = i * 2.39996, radius = 64 + (i * 13.7) % 26; rippleSpot[i * 2] = 50 + Math.cos(angle) * radius; rippleSpot[i * 2 + 1] = 50 + Math.sin(angle) * radius; }
  ripples.frustumCulled = false; scene.add(ripples);
  // A wreck on the western reef: something happened here before the player arrived.
  const wreckMaterial = new THREE.MeshStandardMaterial({ color: '#4a3a2c', roughness: 1 });
  const wreck = new THREE.Group();
  const hull = new THREE.Mesh(new THREE.BoxGeometry(5, 4, 16), wreckMaterial); hull.rotation.set(.1, 0, .5); wreck.add(hull);
  const mast = new THREE.Mesh(new THREE.CylinderGeometry(.3, .4, 15, 6), wreckMaterial); mast.position.set(2.5, 6, -1); mast.rotation.z = -.75; wreck.add(mast);
  const yard = new THREE.Mesh(new THREE.BoxGeometry(.4, .4, 8), wreckMaterial); yard.position.set(5, 9, -1); yard.rotation.z = -.75; wreck.add(yard);
  wreck.position.set(-14, -3.2, 66); wreck.rotation.y = .5; scene.add(wreck);
  const tileMaterial = new THREE.MeshStandardMaterial({ color: '#7b8170', roughness: .9 });
  const tileGeometry = new THREE.BoxGeometry(3.8, .15, 3.8);
  const paths = new THREE.InstancedMesh(tileGeometry, tileMaterial, 125);
  let tile = 0; const dummy = new THREE.Object3D();
  for (let i = 0; i < 25; i++) {
    for (const [x, z] of [[i * 3 + 13, i * 3 + 13], [18, 13 + i * 3], [13 + i * 3, 18], [87, 13 + i * 3], [13 + i * 3, 87]]) {
      dummy.position.set(x, -.38, z); dummy.rotation.y = i * .05; dummy.updateMatrix(); paths.setMatrixAt(tile++, dummy.matrix);
    }
  }
  paths.receiveShadow = true; scene.add(paths);
  // Deterministic landscape decorations, shared geometry and instanced draw calls.
  const rocks = new THREE.InstancedMesh(new THREE.DodecahedronGeometry(1.8, 0), new THREE.MeshStandardMaterial({ color: '#385054', roughness: 1 }), 80);
  const trees = new THREE.InstancedMesh(new THREE.ConeGeometry(2.6, 10, 5), new THREE.MeshStandardMaterial({ color: '#244d47', roughness: 1 }), 80);
  for (let i = 0; i < 80; i++) {
    const angle = i * 2.39996; const radius = 57 + Math.sin(i * 7.2) * 5;
    const x = 50 + Math.cos(angle) * radius, z = 50 + Math.sin(angle) * radius;
    dummy.position.set(x, .2, z); dummy.scale.setScalar(.6 + (i % 5) / 4); dummy.rotation.set(0, angle, .2); dummy.updateMatrix(); rocks.setMatrixAt(i, dummy.matrix);
    dummy.position.set(x + 2, 3, z - 2); dummy.rotation.set(0, angle, 0); dummy.updateMatrix(); trees.setMatrixAt(i, dummy.matrix);
  }
  rocks.castShadow = true; trees.castShadow = true; scene.add(rocks, trees);
  const shrine = await new GLTFLoader().loadAsync(new URL('./models/quantum-shrine.glb', import.meta.url).href);
  if (r.disposed) { release(shrine.scene); return; }
  shrine.scene.traverse(o => { if (o.isMesh) { o.castShadow = true; o.receiveShadow = true; } });
  const centerpiece = shrine.scene;
  r.ruinTemplate = centerpiece;
  centerpiece.position.set(63, 0, 48); centerpiece.scale.setScalar(4); centerpiece.rotation.y = -.2;
  r.scenery.add(centerpiece);
  const halo = new THREE.Mesh(new THREE.TorusGeometry(12, .14, 8, 100), new THREE.MeshBasicMaterial({ color: '#d4b478', transparent: true, opacity: .8 }));
  halo.position.set(63, 22, 48); halo.rotation.y = .7; r.scenery.add(halo);
  const innerHalo = new THREE.Mesh(new THREE.TorusGeometry(10.8, .06, 6, 100), new THREE.MeshBasicMaterial({ color: '#83c9ba', transparent: true, opacity: .5 }));
  innerHalo.position.copy(halo.position); innerHalo.rotation.copy(halo.rotation); r.scenery.add(innerHalo);
  const mixer = new THREE.AnimationMixer(centerpiece);
  shrine.animations.forEach(a => mixer.clipAction(a).play()); r.mixers.push(mixer);
  const loader = new GLTFLoader(), url = name => new URL(`./models/${name}`, import.meta.url).href;
  const [characters, naga, props] = await Promise.all([loader.loadAsync(url('characters.glb')), loader.loadAsync(url('naga.glb')), loader.loadAsync(url('props.glb'))]);
  if (r.disposed) { [characters, naga, props].forEach(g => release(g.scene, true)); return; }
  prepareModels(r, characters, naga, props);
  for (const [x, z, color] of [[13, 13, '#7ce5cb'], [87, 87, '#da80aa']]) {
    const copy = centerpiece.clone(); copy.position.set(x, 0, z); copy.scale.setScalar(1.2); r.battle.add(copy);
    const beacon = new THREE.Mesh(new THREE.CylinderGeometry(.5, 1, 20, 8, 1, true), new THREE.MeshBasicMaterial({ color, transparent: true, opacity: .23, side: THREE.DoubleSide, depthWrite: false })); beacon.position.set(x, 10, z); r.battle.add(beacon);
  }
  r.battle.visible = false;
  const positions = new Float32Array(160 * 3);
  for (let i = 0; i < positions.length; i += 3) { positions[i] = (i * 7.31) % 115; positions[i + 1] = 3 + (i * .71) % 35; positions[i + 2] = (i * 3.77) % 110; }
  const particlesGeometry = new THREE.BufferGeometry(); particlesGeometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  const particles = new THREE.Points(particlesGeometry, new THREE.PointsMaterial({ color: '#e5c58a', size: .23, transparent: true, opacity: .65 })); scene.add(particles);
  // Rain: line segments recycled inside a box over the arena. The draw range follows the rain level.
  const drops = 1400, rainPositions = new Float32Array(drops * 6);
  for (let i = 0; i < drops; i++) { rainPositions[i * 6] = -10 + (i * 37.7) % 160; rainPositions[i * 6 + 1] = (i * 13.3) % 120; rainPositions[i * 6 + 2] = -10 + (i * 91.1) % 180; }
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
  r.fog = new THREE.Mesh(new THREE.PlaneGeometry(100, 100), new THREE.MeshBasicMaterial({ map: r.fogTexture, transparent: true, depthWrite: false }));
  r.fog.rotation.x = -Math.PI / 2; r.fog.position.set(50, .15, 50); r.fog.visible = false; scene.add(r.fog);
  const ray = new THREE.Raycaster(), pointer = new THREE.Vector2(), plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0), point = new THREE.Vector3();
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
    if (ray.ray.intersectPlane(plane, point) && point.x >= 4 && point.x <= 96 && point.z >= 4 && point.z <= 96) {
      cursor.position.set(point.x, .2, point.z); cursor.visible = true;
      reference.invokeMethodAsync('MoveTo', point.x, point.z).catch(() => {});
    }
  };
  // The overlay is pointer-transparent except for actual UI, keeping the arena clickable.
  // RTS left-button presses wait for release: a short press is a click, a drag past 8 px is a box selection.
  r.box = document.createElement('div'); r.box.className = 'selection-box'; r.box.style.display = 'none'; element.append(r.box);
  r.down = event => {
    r.endIntro();
    if (event.button !== 0 || event.pointerType === 'touch' || r.state?.mode !== 0 || r.attackMove || r.buildMode || document.querySelector('.modal-backdrop')) { r.click(event); return; }
    r.drag = { id: event.pointerId, x: event.clientX, y: event.clientY, active: false };
    try { renderer.domElement.setPointerCapture(event.pointerId); } catch { /* Capture is optional. */ }
  };
  r.dragMove = event => {
    const drag = r.drag; if (!drag || event.pointerId !== drag.id) return;
    if (!drag.active && Math.hypot(event.clientX - drag.x, event.clientY - drag.y) < 8) return;
    drag.active = true;
    const rect = element.getBoundingClientRect();
    Object.assign(r.box.style, { display: 'block', left: `${Math.min(drag.x, event.clientX) - rect.left}px`, top: `${Math.min(drag.y, event.clientY) - rect.top}px`, width: `${Math.abs(event.clientX - drag.x)}px`, height: `${Math.abs(event.clientY - drag.y)}px` });
  };
  r.up = event => {
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
  r.dragCancel = event => { if (r.drag?.id === event.pointerId) { r.drag = null; r.box.style.display = 'none'; } };
  renderer.domElement.addEventListener('pointerdown', r.down);
  renderer.domElement.addEventListener('pointermove', r.dragMove);
  renderer.domElement.addEventListener('pointerup', r.up);
  renderer.domElement.addEventListener('pointercancel', r.dragCancel);
  r.hover = event => {
    if (!r.buildMode) return;
    const rect = renderer.domElement.getBoundingClientRect();
    pointer.set((event.clientX - rect.left) / rect.width * 2 - 1, -(event.clientY - rect.top) / rect.height * 2 + 1);
    ray.setFromCamera(pointer, camera);
    if (ray.ray.intersectPlane(plane, point)) { r.placement.position.set(point.x, .2, point.z); r.placement.visible = true; }
  };
  renderer.domElement.addEventListener('pointermove', r.hover);
  r.contextMenu = event => event.preventDefault();
  renderer.domElement.addEventListener('contextmenu', r.contextMenu);
  r.mapClick = event => {
    const map = event.target.closest?.('[data-tactical-map]');
    if (!map || !r.state || document.querySelector('.modal-backdrop')) return;
    const resourceId = event.target.getAttribute('data-resource-id');
    if (resourceId && !r.buildMode) { reference.invokeMethodAsync('GatherResource', Number(resourceId)).catch(() => {}); return; }
    const rect = map.getBoundingClientRect();
    const x = (event.clientX - rect.left) / rect.width * 100, z = (event.clientY - rect.top) / rect.height * 100;
    if (x >= 4 && x <= 96 && z >= 4 && z <= 96) reference.invokeMethodAsync('MoveTo', x, z).catch(() => {});
  };
  r.element.parentElement.addEventListener('pointerdown', r.mapClick);
  r.key = event => {
    if (event.target.matches('input,select,textarea') || event.repeat) return;
    r.endIntro();
    // 4-6 recall a control group; Ctrl/Alt/Cmd + 4-6 save the current selection.
    const group = r.state && /^Digit[456]$/.test(event.code) ? Number(event.code.slice(5)) : 0;
    if (group) { event.preventDefault(); reference.invokeMethodAsync('ControlGroup', group, event.ctrlKey || event.altKey || event.metaKey).catch(() => {}); return; }
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
  window.addEventListener('keydown', r.key);
  // Adaptive audio, all synthesized (no audio files): a pad, rain noise that follows the weather, a tense layer
  // and drum hits that follow the fighting, thunder, and a short phrase when the match ends.
  r.startAudio = () => {
    if (r.audio) return;
    const Audio = window.AudioContext || window.webkitAudioContext; if (!Audio) return;
    const audio = r.audio = new Audio(); r.gain = audio.createGain(); r.gain.gain.value = r.options.volume / 3500; r.gain.connect(audio.destination);
    [110, 164.81, 220].forEach(frequency => { const oscillator = audio.createOscillator(); oscillator.type = 'sine'; oscillator.frequency.value = frequency; oscillator.connect(r.gain); oscillator.start(); });
    r.noise = audio.createBuffer(1, audio.sampleRate * 2, audio.sampleRate);
    const samples = r.noise.getChannelData(0); for (let i = 0; i < samples.length; i++) samples[i] = Math.random() * 2 - 1;
    const rainSource = audio.createBufferSource(); rainSource.buffer = r.noise; rainSource.loop = true;
    const rainFilter = audio.createBiquadFilter(); rainFilter.type = 'bandpass'; rainFilter.frequency.value = 1700; rainFilter.Q.value = .4;
    r.rainGain = audio.createGain(); r.rainGain.gain.value = 0; rainSource.connect(rainFilter); rainFilter.connect(r.rainGain); r.rainGain.connect(r.gain); rainSource.start();
    const tension = audio.createBiquadFilter(); tension.type = 'lowpass'; tension.frequency.value = 420;
    r.combatGain = audio.createGain(); r.combatGain.gain.value = 0; tension.connect(r.combatGain); r.combatGain.connect(r.gain);
    [73.42, 110.6].forEach(frequency => { const oscillator = audio.createOscillator(); oscillator.type = 'sawtooth'; oscillator.frequency.value = frequency; oscillator.connect(tension); oscillator.start(); });
    r.musicGain = audio.createGain(); r.musicGain.gain.value = .55; r.musicGain.connect(r.gain);
  };
  r.tone = (frequency, endFrequency, start, length, level, type = 'sine') => {
    const audio = r.audio; if (!audio || audio.state !== 'running') return;
    const oscillator = audio.createOscillator(), envelope = audio.createGain(), at = audio.currentTime + start;
    oscillator.type = type; oscillator.frequency.setValueAtTime(frequency, at); oscillator.frequency.exponentialRampToValueAtTime(endFrequency, at + length);
    envelope.gain.setValueAtTime(level, at); envelope.gain.exponentialRampToValueAtTime(.001, at + length);
    oscillator.connect(envelope); envelope.connect(r.gain); oscillator.start(at); oscillator.stop(at + length + .02);
  };
  r.thunder = () => {
    const audio = r.audio; if (!audio || audio.state !== 'running') return;
    const source = audio.createBufferSource(), filter = audio.createBiquadFilter(), envelope = audio.createGain();
    source.buffer = r.noise; filter.type = 'lowpass'; filter.frequency.value = 170;
    envelope.gain.setValueAtTime(5, audio.currentTime + .15); envelope.gain.exponentialRampToValueAtTime(.001, audio.currentTime + 1.6);
    source.connect(filter); filter.connect(envelope); envelope.connect(r.gain); source.start(audio.currentTime + .15); source.stop(audio.currentTime + 1.7);
  };
  // Music: a short gamelan-like piece in a pelog-style scale, composed for the game and synthesized here, not recorded.
  // Calm play keeps the saron melody, kenong and gong; fighting adds the kendang and quickens the pulse.
  const pelog = [293.66, 311.13, 349.23, 440, 466.16, 587.33];
  const balungan = [2, 3, 2, 1, 2, 3, 5, 3, 1, 2, 3, 2, 4, 3, 2, 1];
  const strike = (frequency, at, level, decay, partial = 2.76) => {
    const audio = r.audio, envelope = audio.createGain();
    envelope.gain.setValueAtTime(level, at); envelope.gain.exponentialRampToValueAtTime(.0005, at + decay);
    envelope.connect(r.musicGain);
    for (const [ratio, weight] of [[1, 1], [partial, .35]]) {
      const oscillator = audio.createOscillator(), gain = audio.createGain();
      oscillator.frequency.value = frequency * ratio; gain.gain.value = weight;
      oscillator.connect(gain); gain.connect(envelope); oscillator.start(at); oscillator.stop(at + decay + .05);
    }
  };
  const drum = (at, level) => {
    const audio = r.audio, source = audio.createBufferSource(), filter = audio.createBiquadFilter(), envelope = audio.createGain();
    source.buffer = r.noise; filter.type = 'lowpass'; filter.frequency.value = 420;
    envelope.gain.setValueAtTime(level, at); envelope.gain.exponentialRampToValueAtTime(.001, at + .18);
    source.connect(filter); filter.connect(envelope); envelope.connect(r.musicGain); source.start(at); source.stop(at + .2);
  };
  r.music = now => {
    const audio = r.audio; if (!audio || audio.state !== 'running' || !r.musicGain) return;
    if (!r.musicAt || r.musicAt < audio.currentTime - 1) r.musicAt = audio.currentTime + .1;
    const fighting = !!r.state && r.combat > .3, beat = (fighting ? .3 : .42) * (r.state ? 1 : 1.25);
    while (r.musicAt < audio.currentTime + .25) {
      const step = r.musicStep = ((r.musicStep || 0) + 1) % 64, note = balungan[step % 16], at = r.musicAt;
      strike(pelog[note], at, .5, 1.1);
      if (step % 2 === 1) strike(pelog[(note + 2) % pelog.length] * 2, at, .18, .5, 3.1); // bonang answer
      if (step % 4 === 0) strike(146.83, at, .45, 1.6, 2.1); // kenong
      if (step % 16 === 15) { strike(65.4, at, 1.1, 5, 1.5); strike(66.1, at, .8, 5, 1.5); } // gong, beating slowly
      if (fighting) { drum(at, .9 + r.combat); if (step % 2 === 0) drum(at + beat / 2, .5); }
      r.musicAt += beat;
    }
  };
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
  window.addEventListener('pointerdown', r.startAudio, { once: true });
  r.resize = new ResizeObserver(() => { const width = element.clientWidth, height = element.clientHeight; renderer.setSize(width, height, false); camera.aspect = width / height; camera.updateProjectionMatrix(); });
  r.resize.observe(element);
  let previous = performance.now();
  const playCamera = new THREE.Vector3(145, 150, 170), menuCamera = new THREE.Vector3(137, 86, 145);
  const playTarget = new THREE.Vector3(50, 0, 50), menuTarget = new THREE.Vector3(43, 9, 45);
  const dayColor = new THREE.Color('#19323e'), nightColor = new THREE.Color('#081119'), greyColor = new THREE.Color('#3b4b52'), duskColor = new THREE.Color('#ff9a5c');
  const sunDay = new THREE.Color('#ffe4b0'), sunNight = new THREE.Color('#8fa9ff'), skyColor = new THREE.Color(), focus = new THREE.Vector3(), orbit = new THREE.Vector3();
  const levels = { clear: [0, 0, .5], mist: [0, 1, .35], rain: [.6, .35, .9], storm: [1, .45, 1.8] };
  let nextDrum = 0, orbitAngle = 0, lastFlashTick = -1;
  renderer.setAnimationLoop(now => {
    r.frameMs = r.frameMs ? r.frameMs * .95 + (now - previous) * .05 : now - previous;
    // Cutscenes run on wall-clock time, so a slow frame rate does not stretch them.
    const dt = Math.min((now - previous) / 1000, .1), wall = Math.min((now - previous) / 1000, 1); previous = now;
    const playing = !!r.state, still = r.options.reducedMotion, low = r.options.quality === 'low';
    // Weather and time of day come from the match tick (see Atmosphere.cs); the menu keeps a clear afternoon.
    const [rainTarget, mistTarget, swellTarget] = levels[playing ? r.sky.weather : 'clear'] || levels.clear;
    const ease = Math.min(1, dt * .7);
    r.rain += (rainTarget - r.rain) * ease; r.mist += (mistTarget - r.mist) * ease; r.swell += (swellTarget - r.swell) * ease;
    r.daylight += ((playing ? r.sky.daylight : 1) - r.daylight) * ease;
    const day = THREE.MathUtils.clamp((r.daylight - .25) / .75, 0, 1), dusk = Math.max(0, 1 - Math.abs(r.daylight - .58) / .22);
    r.flash = Math.max(0, r.flash - dt * 5);
    skyColor.copy(nightColor).lerp(dayColor, day).lerp(greyColor, Math.min(.75, r.mist * .55 + r.rain * .3)).lerp(duskColor, dusk * .12);
    scene.background.copy(skyColor); scene.fog.color.copy(skyColor);
    scene.fog.density = (playing ? .0025 : .0035) + r.mist * .0011 + r.rain * .0003;
    sun.color.copy(sunNight).lerp(sunDay, day).lerp(duskColor, dusk * .45);
    sun.intensity = 4.3 * (.3 + .7 * day) * (1 - r.rain * .35);
    hemisphere.intensity = 2.6 * (.5 + .5 * day) * (1 - r.rain * .2) + r.flash * 3;
    rim.intensity = 3 * (.45 + .55 * day);
    renderer.toneMappingExposure = 1.3 * (.78 + .22 * day);
    waterUniforms.uSwell.value = still ? 0 : r.swell;
    foam.material.opacity = .22 + (still ? 0 : .12 * Math.sin(now * .0011)) + r.swell * .1;
    ripples.visible = r.rain > .1 && !still;
    if (ripples.visible) {
      for (let i = 0; i < 48; i++) {
        rippleAge[i] += dt * (.8 + r.rain);
        if (rippleAge[i] > 1) { rippleAge[i] -= 1; const angle = Math.random() * Math.PI * 2, radius = 60 + Math.random() * 40; rippleSpot[i * 2] = 50 + Math.cos(angle) * radius; rippleSpot[i * 2 + 1] = 50 + Math.sin(angle) * radius; }
        rippleMatrix.position.set(rippleSpot[i * 2], -3.7, rippleSpot[i * 2 + 1]); rippleMatrix.rotation.set(-Math.PI / 2, 0, 0); rippleMatrix.scale.setScalar(.3 + rippleAge[i] * 2.4);
        rippleMatrix.updateMatrix(); ripples.setMatrixAt(i, rippleMatrix.matrix);
      }
      ripples.instanceMatrix.needsUpdate = true; ripples.material.opacity = .45 * Math.min(1, r.rain);
    }
    if (!still) waterUniforms.uTime.value += dt;
    // Rain is hidden under reduced motion; the darker sky, the mist and the HUD label still tell the weather.
    rain.visible = r.rain > .03 && !still;
    if (rain.visible) {
      const active = Math.floor(drops * Math.min(1, r.rain) * (low ? .4 : 1)), fall = dt * (46 + r.rain * 26), drift = dt * 9 * r.rain;
      for (let i = 0; i < active; i++) {
        const base = i * 6; let x = rainPositions[base] + drift, y = rainPositions[base + 1] - fall;
        if (y < 0) { y += 120; x -= 40 * r.rain; } if (x > 150) x -= 160;
        rainPositions[base] = x; rainPositions[base + 1] = y;
        rainPositions[base + 3] = x - .9 * r.rain; rainPositions[base + 4] = y + 5; rainPositions[base + 5] = rainPositions[base + 2];
      }
      rainGeometry.setDrawRange(0, active * 2); rainGeometry.attributes.position.needsUpdate = true;
      rain.material.opacity = .6 * Math.min(1, r.rain * 1.4);
    }
    // One soft flash now and then during a storm, never under reduced motion (no strobing).
    if (playing && r.sky.weather === 'storm' && !still && r.state.tick !== lastFlashTick && (Math.imul(r.state.tick, 2654435761) >>> 0) % 83 === 0) { lastFlashTick = r.state.tick; r.flash = 1; r.thunder(); }
    r.combat += (r.combatTarget - r.combat) * Math.min(1, dt * 2.5);
    if (r.audio) {
      const time = r.audio.currentTime;
      r.rainGain.gain.setTargetAtTime(playing ? r.rain * 1.6 : 0, time, .4);
      r.combatGain.gain.setTargetAtTime(playing ? r.combat * .8 : 0, time, .3);
      if (playing && r.combat > .18 && now >= nextDrum) { r.tone(130, 46, 0, .2, 1.2 + r.combat * 2.2); nextDrum = now + (r.combat > .6 ? 300 : 520); }
      if (r.musicGain) r.musicGain.gain.setTargetAtTime(r.voice && !r.voice.paused ? .25 : .55, time, .3);
      r.music(now);
    }
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
        camera.position.copy(orbit); camera.lookAt(focus);
        if (r.intro.index === r.intro.shots.length - 1 && t > .72 && r.intro.kind === 'intro') r.cine.classList.remove('on');
      }
    } else {
      let cameraPosition = playing ? playCamera : menuCamera;
      if (playing && r.state.status !== 0 && !still && r.options.cinematics) {
        // The match is over: drift slowly around the arena behind the result panel.
        orbitAngle += dt * .1;
        cameraPosition = orbit.set(50 + Math.cos(.86 + orbitAngle) * 150, 130, 50 + Math.sin(.86 + orbitAngle) * 150);
      } else orbitAngle = 0;
      camera.position.lerp(cameraPosition, Math.min(1, dt * 3));
      camera.lookAt(playing ? playTarget : menuTarget);
    }
    if (!still) { particles.rotation.y += dt * .012; r.mixers.forEach(m => m.update(dt)); }
    let animated = 0;
    for (const object of r.units.values()) {
      object.position.lerp(object.userData.target, Math.min(1, dt * 14));
      if ((object.userData.kind === 'relic' || object.userData.kind === 'artifact') && !still) object.rotation.y += dt;
      const figure = object.userData.figure;
      if (figure) {
        // Turn the short way towards the heading, then advance the clip.
        let turn = object.userData.heading - figure.rotation.y; turn = Math.atan2(Math.sin(turn), Math.cos(turn));
        figure.rotation.y += turn * Math.min(1, dt * 10);
        if (!still) { object.userData.mixer.update(dt); animated++; }
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
    sun.castShadow = !low; particles.visible = !low;
    renderer.render(scene, camera);
  });
  camera.position.set(137, 86, 145);
  r.ready = true;
  element.dataset.ready = 'true';
}

export function update(state, owner, selection = [], attackMove = false, buildMode = false, sky = null) {
  const r = runtime; if (!r || r.disposed || !r.ready) return;
  const started = state && !r.state, ended = state && r.state && state.status !== 0 && r.state.status === 0;
  if (sky) r.sky = sky;
  r.element.dataset.weather = state ? r.sky.weather : 'clear';
  if (!state) { r.endIntro(); r.combatTarget = 0; }
  const cinematic = r.options.cinematics && !r.options.reducedMotion;
  if (started && state.tick < 30 && cinematic) startCutscene(r, 'intro', introShots(r, state, owner));
  if (ended && state.mode === 2 && state.status === 1 && cinematic) startCutscene(r, 'ending', endingShots(r, state));
  r.playVoice(state?.mode === 2 ? state.dialogue : '');
  // A short phrase when the match is decided: rising for a victory, falling for a defeat.
  if (ended) (state.status === 1 ? [220, 277.18, 329.63, 440] : [220, 207.65, 174.61, 146.83]).forEach((note, i) => r.tone(note, note, i * .17, .5, 2.4, 'triangle'));
  r.story.visible = state?.mode === 2;
  if (state?.mode === 2 && !r.story.children.length) buildStory(r);
  r.state = state; r.owner = owner; r.scenery.visible = !state; r.battle.visible = !!state;
  r.attackMove = attackMove;
  r.buildMode = buildMode;
  if (!buildMode) r.placement.visible = false;
  r.renderer.domElement.style.cursor = attackMove || buildMode ? 'crosshair' : 'default';
  const selected = new Set(selection);
  const obstacleKey = JSON.stringify(state?.obstacles || []);
  if (obstacleKey !== r.obstacleKey) {
    for (const wall of r.ruins.children) if (wall.userData.wall) wall.geometry.dispose();
    r.ruins.clear();
    for (const obstacle of state?.obstacles || []) {
      if (state.mode === 2) {
        // Adventure obstacles are the Mystic Cave walls and its sealed door, not shrine ruins.
        r.wallMaterial ||= new THREE.MeshStandardMaterial({ color: '#56686a', roughness: .95 });
        const wall = new THREE.Mesh(new THREE.BoxGeometry(obstacle.width, 6, obstacle.depth), r.wallMaterial);
        wall.position.set(obstacle.x, 3, obstacle.z); wall.castShadow = true; wall.receiveShadow = true; wall.userData.wall = true;
        r.ruins.add(wall);
        continue;
      }
      const ruin = r.ruinTemplate.clone();
      ruin.position.set(obstacle.x, 0, obstacle.z);
      ruin.rotation.set(0, 0, 0);
      ruin.scale.set(obstacle.width / 10, .55, obstacle.depth / 13);
      r.ruins.add(ruin);
    }
    r.obstacleKey = obstacleKey;
  }
  if (!state) r.cursor.visible = false;
  const vision = state?.vision || [];
  r.fog.visible = vision.length > 0;
  r.element.dataset.fog = vision.length ? 'on' : 'off';
  if (vision.length) {
    const size = r.fogCanvas.width, scale = size / 100, seen = r.seenCanvas.getContext('2d'), fog = r.fogCanvas.getContext('2d');
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
  const live = new Set(); let damage = 0; const hurt = [];
  // The player's ultimate: the cooldown jumped up since the last snapshot.
  const cast = state && state.ultimateCooldown > r.lastCooldown + 5; r.lastCooldown = state?.ultimateCooldown || 0;
  const castFrom = cast ? r.units.get(state.units.find(u => u.kind === 'hero' && u.owner === owner)?.id)?.position.clone() : null;
  for (const unit of state?.units || []) {
    live.add(unit.id);
    let object = r.units.get(unit.id);
    if (!object) {
      object = new THREE.Group(); object.userData.kind = unit.kind; object.userData.unitId = unit.id;
      const size = unit.kind === 'boss' ? 3.2 : unit.kind === 'tower' ? 2.2 : unit.kind === 'hero' || unit.kind === 'champion' ? 1.8 : unit.kind === 'warden' || unit.kind === 'npc' ? 1.4 : 1.1;
      const model = unit.kind === 'boss' && unit.model === 'naga' ? 'naga' : modelFor(unit), template = model && r.templates.get(model);
      if (template) {
        const figure = cloneSkinned(template.scene); figure.scale.setScalar((model === 'naga' ? 1.2 : 1.9) * size);
        figure.traverse(o => { if (o.isSkinnedMesh) { o.castShadow = true; o.frustumCulled = false; if (o.material.name === 'Tint') o.material = tintMaterial(r, unit.color); } });
        const mixer = new THREE.AnimationMixer(figure), actions = {};
        for (const clip of template.clips) actions[clip.name] = mixer.clipAction(clip);
        actions.Walk.timeScale = 1.5; actions.Attack.timeScale = 1.25;
        for (const name of ['Cast', 'Hit', 'Death']) { actions[name].setLoop(THREE.LoopOnce); actions[name].clampWhenFinished = true; }
        actions.Idle.play(); actions.Idle.time = (unit.id * .37) % actions.Idle.getClip().duration;
        mixer.update(0);
        Object.assign(object.userData, { figure, mixer, actions, clip: 'Idle', model, oneShot: 0, heading: unit.team === 0 || unit.team === 2 ? .8 : -2.3 });
        figure.rotation.y = object.userData.heading;
        object.add(figure); r.modelled = (r.modelled || 0) + 1;
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
      const ring = new THREE.Mesh(new THREE.RingGeometry(size + .4, size + .65, 24), new THREE.MeshBasicMaterial({ color: unit.owner === owner ? '#fff0b8' : unit.color, side: THREE.DoubleSide })); ring.rotation.x = -Math.PI / 2; ring.position.y = .08; object.add(ring);
      object.userData.ring = ring;
      if (unit.kind === 'worker') {
        const cargo = new THREE.Mesh(new THREE.BoxGeometry(.8, .8, .8), new THREE.MeshStandardMaterial({ color: '#e9c368', emissive: '#e9c368', emissiveIntensity: .3 })); cargo.position.set(0, 5.2, 0); object.add(cargo); object.userData.cargo = cargo;
      }
      if (!['relic', 'npc', 'artifact', 'flotsam'].includes(unit.kind)) {
        const bar = new THREE.Mesh(new THREE.BoxGeometry(3, .25, .35), new THREE.MeshBasicMaterial({ color: unit.team === 0 ? '#83c9ba' : unit.team === 3 ? '#e9c368' : '#dc8ba8' })); bar.position.y = size * 4.5; object.add(bar); object.userData.bar = bar;
      }
      object.position.set(unit.x, 0, unit.z); object.userData.target = new THREE.Vector3(unit.x, 0, unit.z); object.userData.health = unit.health;
      r.units.set(unit.id, object); r.scene.add(object);
    }
    const target = object.userData.target, dx = unit.x - target.x, dz = unit.z - target.z;
    target.set(unit.x, 0, unit.z);
    if (unit.health < object.userData.health) { damage += object.userData.health - unit.health; hurt.push(object); object.userData.hurtAt = performance.now(); }
    object.userData.health = unit.health; object.userData.maxHealth = unit.maxHealth; object.userData.team = unit.team;
    object.userData.ownHero = unit.kind === 'hero' && unit.owner === owner;
    if (object.userData.figure && cast && object.userData.ownHero) {
      playOnce(object, 'Cast');
      ultimateEffect(r, ultimateEffects[object.userData.model] || 'burst', castFrom || object.position, unit, state);
    }
    if (object.userData.figure && object.userData.oneShot > performance.now()) { /* A cast or hit reaction is still playing. */ }
    else if (object.userData.figure) {
      // The snapshot has no animation state: a unit that moved walks, one standing next to an enemy attacks.
      let clip = 'Idle';
      if (dx * dx + dz * dz > .0009) { clip = 'Walk'; object.userData.heading = Math.atan2(dx, dz); }
      else if (unit.kind !== 'npc') {
        let nearest = 81, foe = null;
        for (const other of state.units) {
          if (other.team === unit.team || other.kind === 'relic' || other.kind === 'artifact' || other.kind === 'npc') continue;
          const distance = (other.x - unit.x) ** 2 + (other.z - unit.z) ** 2;
          if (distance < nearest) { nearest = distance; foe = other; }
        }
        if (foe) { clip = 'Attack'; object.userData.heading = Math.atan2(foe.x - unit.x, foe.z - unit.z); }
      }
      // A unit that was hurt while standing flinches.
      if (clip === 'Idle' && object.userData.hurtAt > performance.now() - 120 && unit.kind !== 'boss') playOnce(object, 'Hit');
      else if (clip !== object.userData.clip) {
        const actions = object.userData.actions;
        actions[clip].reset().fadeIn(.18).play(); actions[object.userData.clip].fadeOut(.18);
        object.userData.clip = clip;
      }
    }
    object.userData.ring.material.color.set(selected.has(unit.id) ? '#fff0b8' : unit.color);
    object.userData.ring.scale.setScalar(selected.has(unit.id) ? 1.25 : 1);
    if (object.userData.bar) object.userData.bar.scale.x = Math.max(.01, unit.health / unit.maxHealth);
    if (object.userData.cargo) object.userData.cargo.visible = unit.cargo > 0;
    if (unit.kind === 'outpost') {
      // A foundation rises with the work and stays translucent until it is finished.
      const body = object.userData.body, done = unit.construction >= 100;
      body.scale.y = 1.15 * (.15 + .85 * unit.construction / 100);
      body.traverse(o => { if (o.isMesh) { o.material.opacity = done ? 1 : .55; o.material.depthWrite = done; } });
    }
    if (unit.kind === 'artifact' || unit.kind === 'relic') object.userData.body.position.y = unit.kind === 'artifact' ? 1 + Math.sin(state.tick * .2) * .3 : 0;
  }
  for (const [id, object] of r.units) {
    if (live.has(id)) continue;
    r.units.delete(id);
    // A character that vanished while low on health, where the team can still see it, fell: let it play its death.
    const seen = !vision.length || object.userData.team === 0 || vision.some(v => (v.x - object.position.x) ** 2 + (v.z - object.position.z) ** 2 <= v.radius * v.radius);
    if (state && object.userData.figure && seen && object.userData.health <= object.userData.maxHealth * .5 && !r.options.reducedMotion) {
      const actions = object.userData.actions;
      Object.values(actions).forEach(a => a.stop()); actions.Death.reset().play();
      if (object.userData.bar) object.userData.bar.visible = false;
      object.userData.ring.visible = false;
      r.corpses.push({ object, age: 0 });
      continue;
    }
    object.userData.mixer?.stopAllAction(); r.scene.remove(object); release(object);
  }
  // Fighting intensity for the adaptive audio: recent damage, decaying each snapshot.
  r.combatTarget = state && state.status === 0 ? Math.min(1, r.combatTarget * .9 + damage / 160) : 0;
  r.element.dataset.combat = r.combatTarget > .18 ? 'battle' : 'calm';
}
// One template per character variant: the shared rig with only that variant's mesh. Fixed parts keep their vertex
// colours; the Tint material is replaced per unit colour. Geometry and the fixed material are shared by every clone.
function prepareModels(r, characters, naga, props) {
  const fixed = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .6, metalness: .1 }); fixed.name = 'Fixed'; fixed.userData.shared = true;
  r.fixedMaterial = fixed;
  const share = root => root.traverse(o => { if (o.isMesh) { o.geometry.userData.shared = true; if (o.material.name !== 'Tint') { o.material.dispose(); o.material = fixed; } } });
  const rig = characters.scene.getObjectByName('CharacterRig');
  const variants = rig.children.filter(c => c.isGroup || c.isSkinnedMesh).map(c => c.name);
  share(characters.scene);
  for (const name of variants) {
    const copy = cloneSkinned(characters.scene);
    const copyRig = copy.getObjectByName('CharacterRig');
    for (const child of [...copyRig.children]) if ((child.isGroup || child.isSkinnedMesh) && child.name !== name) copyRig.remove(child);
    r.templates.set(name, { scene: copy, clips: characters.animations });
  }
  share(naga.scene);
  r.templates.set('naga', { scene: naga.scene, clips: naga.animations });
  share(props.scene);
  for (const child of props.scene.children) r.props.set(child.name, child);
  r.characterScenes = [characters.scene, naga.scene, props.scene];
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
// a leap trail with a shockwave, or the southern wave.
function ultimateEffect(r, kind, from, hero, state) {
  const group = new THREE.Group(), x = hero.x, z = hero.z;
  const color = { chain: '#bfeaff', leap: '#c7a6ff', wave: '#7fe0d0', burst: '#f2d27a' }[kind];
  const ring = new THREE.Mesh(new THREE.RingGeometry(.8, 1.4, 48), new THREE.MeshBasicMaterial({ color, transparent: true, opacity: .9, side: THREE.DoubleSide, depthWrite: false }));
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
    if (points.length > 1) group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color: '#e6f8ff', transparent: true })));
    r.flash = Math.max(r.flash, .6);
  } else if (kind === 'leap') {
    const arc = []; for (let k = 0; k <= 16; k++) { const t = k / 16; arc.push(new THREE.Vector3(from.x + (x - from.x) * t, Math.sin(t * Math.PI) * 9 + 1, from.z + (z - from.z) * t)); }
    group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(arc), new THREE.LineBasicMaterial({ color, transparent: true })));
  } else if (kind === 'wave') {
    const wall = new THREE.Mesh(new THREE.CylinderGeometry(1, 1, 2.5, 48, 1, true), new THREE.MeshBasicMaterial({ color, transparent: true, opacity: .5, side: THREE.DoubleSide, depthWrite: false }));
    wall.position.set(x, 1.2, z); group.add(wall);
  }
  r.scene.add(group);
  r.effects.push({ object: group, age: 0, duration: kind === 'chain' ? .45 : .9, step: t => {
    const radius = 1 + t * reach;
    ring.scale.setScalar(radius); ring.material.opacity = .9 * (1 - t);
    group.children.forEach(child => { if (child !== ring) { if (child.isLine) child.material.opacity = 1 - t; else { child.scale.set(radius, 1 - t * .6, radius); child.material.opacity = .5 * (1 - t); } } });
  } });
  r.tone(kind === 'chain' ? 1200 : 220, kind === 'chain' ? 300 : 90, 0, .5, 2.2, kind === 'chain' ? 'sawtooth' : 'sine');
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
  const hero = state.units.find(u => u.kind === 'hero' && u.owner === owner), english = r.options.language === 'en';
  const hx = hero?.x ?? 50, hz = hero?.z ?? 50, rise = [145, 150, 170], center = [50, 0, 50];
  // The last shot: a low orbit around the player's hero that rises into the play camera.
  const toPlay = (title, line, duration) => ({ title, line, duration,
    camera: (t, out) => { const angle = 2.2 + t * 1.5, x = hx + Math.cos(angle) * 34, z = hz + Math.sin(angle) * 34, e = t * t; out.set(x + (rise[0] - x) * e, 15 + (rise[1] - 15) * e, z + (rise[2] - z) * e); },
    look: (t, out) => { const e = t * t; out.set(hx + (center[0] - hx) * e, 3 * (1 - e), hz + (center[2] - hz) * e); } });
  if (state.mode === 0) return [toPlay('RTS Skirmish', english ? 'Build outposts, gather gold, bring down the enemy base.' : 'Bangun pos, kumpulkan emas, runtuhkan markas lawan.', 5)];
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
  const wood = new THREE.MeshStandardMaterial({ color: '#5b4330', roughness: 1 }), stone = new THREE.MeshStandardMaterial({ color: '#6c7a78', roughness: .95 });
  const canvas = new THREE.MeshStandardMaterial({ color: '#8a7350', roughness: 1, side: THREE.DoubleSide }), iron = new THREE.MeshStandardMaterial({ color: '#8d979a', roughness: .5, metalness: .7 });
  const add = (geometry, material, x, y, z, rx = 0, ry = 0, rz = 0) => { const mesh = new THREE.Mesh(geometry, material); mesh.position.set(x, y, z); mesh.rotation.set(rx, ry, rz); mesh.castShadow = true; r.story.add(mesh); return mesh; };
  // The Keeper's camp: he has lived beside the path for a long time.
  add(new THREE.ConeGeometry(4, 5, 4), canvas, 25, 2.5, 16, 0, .6);
  add(new THREE.CylinderGeometry(.12, .12, 7, 5), wood, 34, 3.5, 17);
  add(new THREE.BoxGeometry(.1, 2.2, 3), new THREE.MeshStandardMaterial({ color: '#83c9ba', roughness: .8, side: THREE.DoubleSide }), 34, 5.6, 18.5);
  for (let i = 0; i < 3; i++) add(new THREE.CylinderGeometry(.22, .22, 2.4, 5), wood, 33, .3, 23, Math.PI / 2, i * 2.1);
  r.flame = add(new THREE.ConeGeometry(.7, 1.8, 6), new THREE.MeshBasicMaterial({ color: '#ffb35c' }), 33, 1.2, 23); r.flame.castShadow = false;
  r.fire = new THREE.PointLight('#ff9a4a', 60, 26, 2); r.fire.position.set(33, 2.6, 23); r.story.add(r.fire);
  // Each inscription stands in the remains of a small shrine: some pillars upright, some fallen.
  for (const [x, z] of [[27, 29], [60, 38], [75, 72]])
    for (let i = 0; i < 5; i++) {
      const angle = i * 1.2566 + x, fallen = (i + x) % 3 === 0, px = x + Math.cos(angle) * 4.6, pz = z + Math.sin(angle) * 4.6;
      add(new THREE.CylinderGeometry(.5, .6, fallen ? 4.5 : 3 + (i % 3) * 1.4, 6), stone, px, fallen ? .5 : 1.5 + (i % 3) * .7, pz, fallen ? Math.PI / 2 : 0, angle, fallen ? .3 : 0);
    }
  // An old battlefield on the way to the sealed chamber: weapons left where their owners fell.
  for (let i = 0; i < 12; i++) {
    const x = 66 + (i * 7.3) % 13, z = 56 + (i * 4.9) % 12;
    if (i % 3) add(new THREE.BoxGeometry(.16, 2.6, .05), iron, x, 1, z, .3 + (i % 4) * .12, i, .2);
    else add(new THREE.CylinderGeometry(.9, .9, .12, 8), wood, x, .12, z, .15, i, 0);
  }
}
// Geometry and materials shared between characters are kept unless `all` is set (on dispose).
function release(root, all = false) {
  const geometries = new Set(), materials = new Set();
  root.traverse(object => { if (object.geometry && (all || !object.geometry.userData.shared)) geometries.add(object.geometry); if (object.material) (Array.isArray(object.material) ? object.material : [object.material]).forEach(m => { if (all || !m.userData.shared) materials.add(m); }); });
  geometries.forEach(g => g.dispose()); materials.forEach(m => { for (const value of Object.values(m)) if (value?.isTexture) value.dispose(); m.dispose(); });
}
export function dispose() {
  const r = runtime; if (!r) return; r.disposed = true;
  r.renderer.setAnimationLoop(null); r.resize?.disconnect();
  window.removeEventListener('keydown', r.key); window.removeEventListener('pointerdown', r.startAudio);
  r.renderer.domElement.removeEventListener('pointerdown', r.down);
  r.renderer.domElement.removeEventListener('pointermove', r.dragMove);
  r.renderer.domElement.removeEventListener('pointerup', r.up);
  r.renderer.domElement.removeEventListener('pointercancel', r.dragCancel);
  r.box?.remove(); r.cine?.remove();
  r.renderer.domElement.removeEventListener('pointermove', r.hover);
  r.renderer.domElement.removeEventListener('contextmenu', r.contextMenu);
  r.element.parentElement?.removeEventListener('pointerdown', r.mapClick);
  r.mixers.forEach(m => m.stopAllAction()); release(r.scene, true); (r.characterScenes || []).forEach(scene => release(scene, true)); r.templates?.forEach(t => release(t.scene, true)); r.tintMaterials.forEach(m => m.dispose()); r.fixedMaterial?.dispose(); r.voice?.pause(); r.renderer.dispose(); r.renderer.domElement.remove();
  r.audio?.close(); runtime = null;
}
