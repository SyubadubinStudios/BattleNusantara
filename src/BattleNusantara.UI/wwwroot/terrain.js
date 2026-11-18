// The landscape of a map: terrain with biomes and edges, sea and lakes, scattered nature, a village, camp lairs, and the
// look of every obstacle. Built once per map from its definition (Maps.cs) and the environment kit (environment.glb).
// Purely presentation: the simulation knows only the arena square and its obstacles.
import * as THREE from './vendor/three.module.js';

/** How far the land reaches beyond the arena. Every map is an island: the outer rim sinks into the sea. */
export const APRON = 70;
export const SEA_LEVEL = -2.2;

// Ground colours per biome (sRGB). Two tones each; noise blends between them.
const BIOME_COLORS = {
  subur: ['#5d9a3c', '#7bb24a'], hutan: ['#3a6a2e', '#4d7c34'], tandus: ['#8a7658', '#a08a66'], gurun: ['#d6bd86', '#e4cf9c'],
  sabana: ['#a69c4c', '#c2b25e'], rawa: ['#4a6a42', '#5a7a4c'], pantai: ['#dcca98', '#ead9aa'], vulkanik: ['#4a4644', '#5e5854']
};
const THEME_BASE = { valley: 'subur', coast: 'sabana', forest: 'hutan', savanna: 'sabana', river: 'subur' };
const DIRT = new THREE.Color('#9a8460'), ROCK = new THREE.Color('#7a776e'), ROCK_DARK = new THREE.Color('#5c5a54'), ASH = new THREE.Color('#57524c');
const SAND = new THREE.Color('#dccb98'), WET_SAND = new THREE.Color('#a39c7c'), LAKE_BED = new THREE.Color('#3e5a4e'), SEA_BED = new THREE.Color('#c9b98a');
// Things that sway in the wind, with how far their tips travel.
const SWAY = { tree_broad: .22, tree_cemara: .3, tree_palm: .38, tree_dead: .08, bamboo: .42, banana: .2, bush_a: .05, bush_flower: .05, grass_a: .1, grass_b: .14, reeds: .16, fern: .05, shrub_dry: .05 };
// Tall things that cast shadows inside the arena; ground cover never does.
const CASTERS = /^(tree_|bamboo|banana|hut|ruin_|statue|stupa|gate_|cliff|rock_a|torch|lantern|banner|fence|boat|pier|goods)/;

function mulberry(seed) {
  return () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
}
function hashString(text) { let h = 2166136261; for (const c of text) h = Math.imul(h ^ c.charCodeAt(0), 16777619); return h >>> 0; }
function hash2(x, z) { const h = Math.sin(x * 127.1 + z * 311.7) * 43758.5453; return h - Math.floor(h); }
function noise(x, z) {
  const ix = Math.floor(x), iz = Math.floor(z), fx = x - ix, fz = z - iz;
  const u = fx * fx * (3 - 2 * fx), v = fz * fz * (3 - 2 * fz);
  const a = hash2(ix, iz), b = hash2(ix + 1, iz), c = hash2(ix, iz + 1), d = hash2(ix + 1, iz + 1);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}
const fbm = (x, z) => noise(x, z) * .55 + noise(x * 2.1, z * 2.1) * .3 + noise(x * 4.3, z * 4.3) * .15;
// Sharp crests: folded noise, squared, over four octaves (0 to about 0.9).
function ridged(x, z) {
  let sum = 0, amplitude = .5, frequency = 1;
  for (let o = 0; o < 4; o++) { const n = 1 - Math.abs(noise(x * frequency + o * 17, z * frequency) * 2 - 1); sum += n * n * amplitude; amplitude *= .5; frequency *= 2.03; }
  return sum;
}
const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
function segmentDistance(px, pz, ax, az, bx, bz) {
  const dx = bx - ax, dz = bz - az, t = Math.max(0, Math.min(1, ((px - ax) * dx + (pz - az) * dz) / (dx * dx + dz * dz || 1)));
  return Math.hypot(px - (ax + dx * t), pz - (az + dz * t));
}

/** Map positions shared with Maps.cs (MapLayout). */
export function layout(size) {
  return {
    size, ally: size * .13, enemy: size * .87,
    lane: lane => lane === 0 ? [size * .2, size * .8] : lane === 2 ? [size * .8, size * .2] : [size * .5, size * .5]
  };
}

// Collects placements of kit objects, then draws each kind with one instanced mesh per part.
class Scatter {
  constructor(kit, heightAt, random) { this.kit = kit; this.heightAt = heightAt; this.random = random; this.near = new Map(); this.far = new Map(); }
  place(name, x, z, scale = 1, yaw = this.random() * Math.PI * 2, sink = 0, stretch = 1, far = false) {
    if (!this.kit.env.has(name)) return;
    const matrix = new THREE.Matrix4().compose(new THREE.Vector3(x, this.heightAt(x, z) - sink, z),
      new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), yaw), new THREE.Vector3(scale, scale * stretch, scale));
    const layer = far ? this.far : this.near;
    if (!layer.has(name)) layer.set(name, []);
    layer.get(name).push(matrix);
  }
  build(group, shadows, wind) {
    const counts = {};
    for (const [layer, far] of [[this.near, false], [this.far, true]]) {
      for (const [name, list] of layer) {
        counts[name] = (counts[name] || 0) + list.length;
        for (const part of this.kit.env.get(name)) {
          const glow = /Glow|Lava/.test(part.material.name);
          const material = SWAY[name] && !glow ? swaying(part.material, name, wind) : part.material;
          const mesh = new THREE.InstancedMesh(part.geometry, material, list.length);
          list.forEach((m, i) => mesh.setMatrixAt(i, m));
          mesh.castShadow = shadows && !far && CASTERS.test(name); mesh.receiveShadow = !far;
          mesh.name = `decor:${name}`; mesh.userData.decor = name; mesh.userData.glow = glow;
          mesh.computeBoundingSphere();
          group.add(mesh);
        }
      }
    }
    return counts;
  }
}

const swayCache = new Map();
/** Forgets the wind-bent material copies (their kit is being released). */
export function clearSway() { swayCache.forEach(m => m.dispose()); swayCache.clear(); }
// A copy of the kit material whose vertices bend with the wind, more the higher they are; each instance has its own phase.
function swaying(material, name, wind) {
  const key = `${material.uuid}:${name}`;
  if (swayCache.has(key)) return swayCache.get(key);
  const copy = material.clone();
  copy.userData = { shared: true, sway: true };
  const height = Math.max(.3, wind.heights?.[name] || 1);
  copy.onBeforeCompile = shader => {
    shader.uniforms.uTime = wind.time; shader.uniforms.uWind = wind.strength;
    shader.uniforms.uTip = { value: SWAY[name] / (height * height) };
    shader.vertexShader = 'uniform float uTime;\nuniform float uWind;\nuniform float uTip;\n' + shader.vertexShader.replace('#include <begin_vertex>', `#include <begin_vertex>
  #ifdef USE_INSTANCING
    float phase = instanceMatrix[3].x * .37 + instanceMatrix[3].z * .23;
  #else
    float phase = 0.0;
  #endif
  float rise = max(0.0, transformed.y), bend = rise * rise * uTip * uWind;
  transformed.x += (sin(uTime * 1.7 + phase) + .4 * sin(uTime * 4.3 + phase * 2.0)) * bend;
  transformed.z += cos(uTime * 1.3 + phase * 1.3) * bend * .6;`);
  };
  copy.customProgramCacheKey = () => 'sway';
  swayCache.set(key, copy);
  return copy;
}

/**
 * Builds the landscape. `kit.env` maps an object name to its parts [{ geometry, material }]; `kit.heights` gives each
 * object's height. Returns the group, the height function, notable places for life and effects, and hooks for the frame
 * loop. Water obstacles are carved here because they never change; other obstacles are dressed by dressObstacles.
 */
export function buildLandscape(map, kit, quality) {
  const size = map.size, L = layout(size), random = mulberry(hashString(map.id));
  const group = new THREE.Group(); group.name = `landscape:${map.id}`;
  const base = THEME_BASE[map.theme] || 'subur';
  const regions = map.regions || [], edges = map.edges || ['gunung', 'laut', 'laut', 'hutan'];
  const water = (map.obstacles || []).filter(o => o.kind === 'water');
  // 'medium' is what a software renderer gets for the high setting (see world.js).
  const low = quality === 'low', density = low ? .25 : quality === 'medium' ? .5 : 1;
  const village = map.village || (map.mode === 2 ? null : [L.ally - 18, L.ally - 18]);

  // Paths worn into the ground: lanes in MOBA, the march between bases and to the mines in RTS, trails in Adventure.
  const paths = [];
  if (map.paths) paths.push(...map.paths);
  else if (map.mode === 1) for (let lane = 0; lane < 3; lane++) { const [x, z] = L.lane(lane); paths.push([L.ally, L.ally, x, z], [x, z, L.enemy, L.enemy]); }
  else if (map.mode === 0) { paths.push([L.ally, L.ally, L.enemy, L.enemy]); for (const r of map.resources || []) paths.push([L.ally, L.ally, r.x, r.z]); }
  else paths.push([24, 24, 30, 20], [30, 20, 27, 29], [27, 29, 60, 38], [60, 38, 75, 72], [75, 72, 84, 83], [24, 24, 10, 60], [60, 38, 84, 30], [30, 20, 50, 8]);
  if (village && map.mode >= 0) paths.push([L.ally, L.ally, village[0], village[1]]);

  // Distance outside the arena (rounded at the corners) and the edge that side belongs to.
  const edgeAt = (x, z) => {
    const sides = [-z, x - size, z - size, -x]; // north, east, south, west
    let best = 0, side = -1;
    for (let i = 0; i < 4; i++) if (sides[i] > best) { best = sides[i]; side = i; }
    const dx = Math.max(0, -x, x - size), dz = Math.max(0, -z, z - size);
    return { distance: side < 0 ? 0 : Math.hypot(dx, dz), type: side < 0 ? null : edges[side] };
  };
  // A volcano stands on each 'vulkan' side: a cone with a crater, rising out of the mountains.
  const volcanoes = edges.map((type, side) => type === 'vulkan' ? [[size / 2, -36], [size + 36, size / 2], [size / 2, size + 36], [-36, size / 2]][side] : null).filter(Boolean);
  const volcanoAt = (x, z) => {
    let h = 0;
    for (const [vx, vz] of volcanoes) {
      const r = Math.hypot(x - vx, z - vz) + (noise(x * .15, z * .15) - .5) * 3;
      if (r > 42) continue;
      const cone = 44 * Math.pow(1 - smooth(3, 42, r), 1.25) - 9 * (1 - smooth(2, 7.5, r));
      h = Math.max(h, cone);
    }
    return h;
  };
  // Lakes and rivers: rounded rectangles with a ragged bank, so a chain of squares reads as one winding river.
  const waterDepth = (x, z) => {
    let depth = 0;
    for (const o of water) {
      const dx = Math.abs(x - o.x) - o.width / 2, dz = Math.abs(z - o.z) - o.depth / 2;
      const outside = Math.hypot(Math.max(0, dx), Math.max(0, dz)) + Math.min(0, Math.max(dx, dz)) * .3 + (noise(x * .22, z * .22) - .5) * 1.6;
      depth = Math.max(depth, 1 - smooth(-1.4, 1.3, outside));
    }
    return depth;
  };
  const heightAt = (x, z) => {
    const { distance: d, type } = edgeAt(x, z);
    let h = 0;
    if (type) {
      const n = fbm(x * .05, z * .05), coast = d + (fbm(x * .025 + 3, z * .025) - .5) * 22 * smooth(2, 14, d);
      if (type === 'gunung') h = smooth(2, 34, d) * (14 + 34 * ridged(x * .03, z * .03)) + (n - .5) * 6 * smooth(4, 20, d);
      else if (type === 'vulkan') h = smooth(2, 30, d) * (9 + 16 * ridged(x * .035, z * .035)) + (n - .5) * 4 * smooth(4, 20, d);
      else if (type === 'hutan') h = smooth(2, 36, d) * 7 + (n - .5) * 4 * smooth(4, 20, d);
      else if (type === 'gurun') h = smooth(2, 40, d) * 4 + Math.sin(x * .22 + Math.cos(z * .09) * 2) * 1.4 * smooth(4, 18, d);
      else if (type === 'laut') h = -smooth(1.5, 18, coast) * 7.5;
      if (volcanoes.length) h = Math.max(h, volcanoAt(x, z));
      // The island ends: whatever the side, the land falls into the sea at a ragged outer rim.
      h += (SEA_LEVEL - 5 - h) * smooth(APRON - 18, APRON - 3, coast + (n - .5) * 10);
      // Always under water at the very edge of the land, so the sea never meets a cliff or a seam.
      h += (SEA_LEVEL - 5 - h) * smooth(APRON - 9, APRON - 1, d);
    }
    return h - waterDepth(x, z) * 1.7;
  };

  // ----------------------------------------------------------------------------------- terrain mesh
  const extent = size + APRON * 2, spacing = low ? 3.2 : quality === 'medium' ? 2.6 : 2.1, segments = Math.round(extent / spacing);
  const geometry = new THREE.PlaneGeometry(extent, extent, segments, segments);
  geometry.rotateX(-Math.PI / 2);
  geometry.translate(size / 2, 0, size / 2);
  const position = geometry.attributes.position, colors = new Float32Array(position.count * 3);
  const color = new THREE.Color(), tone = new THREE.Color();
  const palette = Object.fromEntries(Object.entries(BIOME_COLORS).map(([k, [a, b]]) => [k, [new THREE.Color(a), new THREE.Color(b)]]));
  const nearPath = (x, z) => { let near = 99; for (const [ax, az, bx, bz] of paths) near = Math.min(near, segmentDistance(x, z, ax, az, bx, bz)); return near; };
  for (let i = 0; i < position.count; i++) {
    const x = position.getX(i), z = position.getZ(i), h = heightAt(x, z);
    position.setY(i, h);
    const n = fbm(x * .11, z * .11), blotch = noise(x * .035 + 7, z * .035 + 3);
    // Biome blend: the theme's base, then each region painted over with a soft, ragged edge.
    const [b0, b1] = palette[base]; color.copy(b0).lerp(b1, n);
    for (const region of regions) {
      const dist = Math.hypot(x - region.x, z - region.z) + (noise(x * .08, z * .08) - .5) * region.radius * .35;
      const w = 1 - smooth(region.radius * .45, region.radius, dist);
      if (w <= 0) continue;
      const [c0, c1] = palette[region.biome] || palette.subur;
      tone.copy(c0).lerp(c1, n); color.lerp(tone, w);
    }
    const { distance: d, type } = edgeAt(x, z);
    if (type === 'gunung' || type === 'vulkan') {
      const steep = smooth(6, 16, h) * (.75 + .25 * noise(x * .4, z * .4));
      color.lerp(tone.copy(palette.hutan[0]).lerp(palette.hutan[1], n), smooth(1, 5, h) * (1 - steep));
      color.lerp(h > 26 ? ROCK : tone.copy(ROCK_DARK).lerp(ROCK, n), steep);
      if (volcanoes.length) { const v = volcanoAt(x, z); if (v > 12) color.lerp(ASH, smooth(12, 30, v) * (.7 + .3 * n)); }
    }
    else if (type === 'gurun') color.lerp(SAND, smooth(2, 12, d));
    else if (type === 'hutan') color.lerp(palette.hutan[0], smooth(2, 10, d) * .8);
    // Every shore: dry sand above the waterline, wet sand at it, a pale sea bed below.
    if (type) {
      const above = h - SEA_LEVEL;
      color.lerp(SAND, smooth(2.6, .8, above) * smooth(3, 8, d) * (type === 'laut' || type === 'gurun' ? 1 : .7));
      if (above < .5) color.lerp(above < -.3 ? SEA_BED : WET_SAND, smooth(.5, -.6, above));
    }
    // Worn paths: dirt with a ragged edge.
    if (h > SEA_LEVEL + .5) color.lerp(DIRT, (1 - smooth(1.2, 3.2, nearPath(x, z) + (noise(x * .3, z * .3) - .5) * 1.2)) * .85);
    const lake = waterDepth(x, z);
    if (lake > 0) color.lerp(LAKE_BED, lake);
    // Light variation and darker patches, so open ground never looks flat.
    color.multiplyScalar(.9 + n * .18 - blotch * .08);
    colors[i * 3] = color.r; colors[i * 3 + 1] = color.g; colors[i * 3 + 2] = color.b;
  }
  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  geometry.computeVertexNormals();
  const ground = new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .96, metalness: 0 }));
  ground.receiveShadow = true; ground.name = 'terrain';
  group.add(ground);

  // The sea floor seen from above: depth below sea level in a small texture, read by the ocean shader for colour and foam.
  const shoreSize = 128, shoreData = new Uint8Array(shoreSize * shoreSize * 4);
  for (let j = 0; j < shoreSize; j++) for (let i = 0; i < shoreSize; i++) {
    const x = -APRON + (i + .5) / shoreSize * extent, z = -APRON + (j + .5) / shoreSize * extent;
    const rim = i === 0 || j === 0 || i === shoreSize - 1 || j === shoreSize - 1;
    const depth = rim ? 12 : SEA_LEVEL - heightAt(x, z), k = (j * shoreSize + i) * 4;
    shoreData[k] = Math.round(THREE.MathUtils.clamp((depth + 2) / 12, 0, 1) * 255); shoreData[k + 3] = 255;
  }
  const shore = new THREE.DataTexture(shoreData, shoreSize, shoreSize);
  shore.magFilter = THREE.LinearFilter; shore.minFilter = THREE.LinearFilter; shore.wrapS = shore.wrapT = THREE.ClampToEdgeWrapping; shore.needsUpdate = true;

  // ----------------------------------------------------------------------------------- lakes and rivers
  const lakeUniforms = { uTime: { value: 0 } };
  const lakeMaterial = new THREE.MeshStandardMaterial({ color: '#2a6a68', metalness: .4, roughness: .12, transparent: true, opacity: .84 });
  lakeMaterial.onBeforeCompile = shader => {
    Object.assign(shader.uniforms, lakeUniforms);
    shader.vertexShader = 'uniform float uTime;\n' + shader.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\n  transformed.z += sin(position.x * .6 + uTime * 1.3) * .04 + sin(position.y * .5 - uTime) * .03;');
  };
  if (water.length) {
    const surface = new THREE.Mesh(new THREE.PlaneGeometry(size, size, 24, 24), lakeMaterial);
    surface.rotation.x = -Math.PI / 2; surface.position.set(size / 2, -.34, size / 2); surface.name = 'lake';
    group.add(surface);
  }
  // Glowing lava in each crater.
  for (const [vx, vz] of volcanoes) {
    const lava = new THREE.Mesh(new THREE.CircleGeometry(5.5, 24), new THREE.MeshBasicMaterial({ color: new THREE.Color('#ff6a1a').multiplyScalar(2.6), fog: true }));
    lava.rotation.x = -Math.PI / 2; lava.position.set(vx, heightAt(vx, vz) + 1.2, vz); lava.name = 'lava';
    group.add(lava);
  }

  // ----------------------------------------------------------------------------------- scattered objects
  const scatter = new Scatter(kit, heightAt, random), place = scatter.place.bind(scatter);
  const inArena = (x, z, margin = 0) => x >= margin && x <= size - margin && z >= margin && z <= size - margin;
  const obstacles = map.obstacles || [];
  const blocked = (x, z, pad = 2) => obstacles.some(o => Math.abs(x - o.x) <= o.width / 2 + pad && Math.abs(z - o.z) <= o.depth / 2 + pad);
  const clearings = map.clear || [];
  const keepClear = (x, z) => (map.mode >= 0 && (Math.hypot(x - L.ally, z - L.ally) < 16 || Math.hypot(x - L.enemy, z - L.enemy) < 16))
    || (map.camps || []).some(c => Math.hypot(x - c.x, z - c.z) < 5) || (map.resources || []).some(r => Math.hypot(x - r.x, z - r.z) < 4)
    || clearings.some(([cx, cz, r]) => Math.hypot(x - cx, z - cz) < r)
    || (village && Math.hypot(x - village[0], z - village[1]) < 15);
  const biomeAt = (x, z) => {
    let best = base, weight = 0;
    for (const region of regions) { const w = 1 - Math.hypot(x - region.x, z - region.z) / region.radius; if (w > weight) { weight = w; best = region.biome; } }
    return best;
  };
  const pick = list => list[Math.floor(random() * list.length)];

  // Inside the arena: low plants that never hide units, and a few trees in the border band nobody walks.
  const interior = Math.round(size * size * .028 * density);
  for (let k = 0; k < interior; k++) {
    const x = random() * size, z = random() * size;
    if (blocked(x, z, 1.5) || keepClear(x, z) || waterDepth(x, z) > .1) continue;
    const biome = biomeAt(x, z), border = !inArena(x, z, 4);
    if (nearPath(x, z) < 2.6 && !border) continue;
    const tall = border && random() < .5, grass = s => !low && place(s, x, z, 2 + random() * 1.2);
    switch (biome) {
      case 'subur': if (tall) place(pick(['banana', 'tree_palm', 'bamboo']), x, z, .8 + random() * .3); else if (random() < .5) grass(pick(['grass_a', 'grass_b'])); else place(pick(['bush_flower', 'fern', 'bush_flower']), x, z, .8 + random() * .4); break;
      case 'hutan': if (tall) place(pick(['tree_broad', 'tree_cemara', 'bamboo']), x, z, .8 + random() * .4); else if (random() < .4) grass('grass_b'); else place(pick(['fern', 'bush_a']), x, z, .8 + random() * .5); break;
      case 'tandus': if (tall) place('tree_dead', x, z, .8 + random() * .4); else place(pick(['shrub_dry', 'rock_b', 'rock_c', 'shrub_dry']), x, z, .6 + random() * .5); break;
      case 'gurun': place(pick(['shrub_dry', 'rock_c', 'dune', 'dune']), x, z, 1 + random() * 1.5, undefined, .1, .8); break;
      case 'sabana': if (tall) place(pick(['tree_broad', 'tree_dead']), x, z, .7 + random() * .3); else if (random() < .7) grass('grass_b'); else place('shrub_dry', x, z, .9 + random() * .4); break;
      case 'rawa': if (random() < .6) place('reeds', x, z, 1.2 + random() * .6); else grass('grass_b'); break;
      case 'pantai': if (tall) place('tree_palm', x, z, .8 + random() * .3); else place(pick(['rock_c', 'shrub_dry']), x, z, .6 + random() * .4); break;
      case 'vulkanik': place(pick(['rock_b', 'tree_dead', 'rock_c']), x, z, .7 + random() * .5); break;
    }
  }
  // Beyond the arena: each side dressed by its edge, thinning out towards the sea.
  const outside = Math.round((extent * extent - size * size) * .006 * density);
  for (let k = 0; k < outside; k++) {
    const x = -APRON + random() * extent, z = -APRON + random() * extent;
    if (inArena(x, z, -1) || keepClear(x, z)) continue;
    const { distance: d, type } = edgeAt(x, z), h = heightAt(x, z);
    if (h < SEA_LEVEL + .3) { if (h < SEA_LEVEL - 2 && random() < .03) place('boat', x, z, 1.3, undefined, h - SEA_LEVEL + .15, 1, true); continue; }
    const coast = h < SEA_LEVEL + 2.2;
    const inner = biomeAt(THREE.MathUtils.clamp(x, 0, size), THREE.MathUtils.clamp(z, 0, size));
    if (coast) { if (random() < .5) place(pick(['tree_palm', 'tree_palm', 'rock_c', 'grass_b']), x, z, 1 + random() * .4, undefined, 0, 1, true); continue; }
    if (type === 'laut') { place(pick(['tree_palm', 'banana', 'bush_a', 'tree_palm']), x, z, 1 + random() * .4, undefined, 0, 1, true); continue; }
    if (type === 'gurun') {
      const kind = pick(['dune', 'dune', 'dune', 'shrub_dry', 'rock_a', 'tree_dead']);
      place(kind, x, z, kind === 'dune' ? 2.5 + random() * 4 : kind === 'rock_a' ? 1 + random() * 1.5 : .9 + random() * .5, undefined, kind === 'dune' ? .3 : 0, kind === 'dune' ? .7 : 1, true);
      continue;
    }
    if (inner === 'subur' && d < 18 && random() < .14 && type !== 'vulkan') { place('sawah', x, z, 1, Math.round(random() * 2) * Math.PI / 2, .05, 1, true); continue; }
    if (type === 'hutan') { place(pick(['tree_broad', 'tree_cemara', 'tree_broad', 'bamboo', 'bush_a']), x, z, 1 + random() * .6, undefined, 0, 1, true); continue; }
    // Mountains: forest on the lower slopes, scattered boulders above; a volcano's slopes are burnt.
    if (volcanoes.length && volcanoAt(x, z) > 8) { if (random() < .3) place(pick(['tree_dead', 'rock_a']), x, z, 1 + random() * .8, undefined, 0, 1, true); continue; }
    if (h < 9) place(pick(['tree_cemara', 'tree_cemara', 'tree_broad', 'tree_cemara']), x, z, 1 + random() * .7, undefined, 0, 1, true);
    else if (h < 20 && random() < .45) place(pick(['tree_cemara', 'rock_a']), x, z, .9 + random() * .6, undefined, 0, 1, true);
    else if (random() < .15) place(pick(['cliff', 'rock_a']), x, z, 1.2 + random() * 1.5, undefined, 0, 1, true);
  }
  // Mountain ranges and the volcano on the horizon.
  const features = { smoke: [], flames: [], village: village ? { x: village[0], z: village[1], huts: [], fields: [] } : null, camps: [], fireflies: [], flowers: [], lakes: water.map(o => ({ x: o.x, z: o.z, width: o.width, depth: o.depth })), size, sea: edges.map(e => e === 'laut') };
  for (const [vx, vz] of volcanoes) features.smoke.push({ x: vx, y: heightAt(vx, vz) + 3, z: vz });
  // The village behind the allied shrine: huts, rice fields, a fence, a banner and lamps.
  if (village) {
    const [vx, vz] = village;
    const huts = [[vx - 4, vz + 6, .3], [vx + 6, vz - 5, 1.2], [vx - 8, vz - 7, .8], [vx + 9, vz + 7, 2.4]];
    huts.forEach(([x, z, yaw]) => place('hut', x, z, 1, yaw));
    const fields = [[vx - 16, vz + 4], [vx - 16, vz + 12], [vx + 2, vz - 17], [vx + 11, vz - 17]];
    fields.forEach(([x, z]) => place('sawah', x, z, 1, Math.PI / 2 * Math.round(random()), .05));
    for (let k = 0; k < 4; k++) place('fence', vx + 2 + k * 3.2, vz + 2, 1, 0);
    place('banner', vx + 8, vz + 10, 1.1, .8); place('goods', vx + 2, vz - 1, 1, .4); place('lantern', vx + 4, vz + 4, 1, 0); place('torch', vx - 2, vz + 9, 1, 0);
    features.flames.push({ x: vx - 2, y: heightAt(vx - 2, vz + 9) + 3.1, z: vz + 9 });
    features.village.huts = huts.map(([x, z]) => [x, z]);
    features.village.fields = fields;
    // The enemy side: ruins, dark statues and dead trees.
    if (map.mode === 0 || map.mode === 1) {
      const e = L.enemy;
      [[e + 18, e + 8], [e + 8, e + 18], [e + 22, e + 22]].forEach(([x, z]) => place(pick(['ruin_wall', 'statue', 'ruin_pillar']), x, z, 1.2));
      for (let k = 0; k < 6; k++) place('tree_dead', e + 12 + random() * 20, e + 12 + random() * 20, 1 + random() * .5);
      for (const [x, z] of [[e + 10, e + 14], [e + 14, e + 10]]) { place('torch', x, z, 1.2, 0); features.flames.push({ x, y: heightAt(x, z) + 3.7, z }); }
    }
  }
  // Camp lairs: a ring of broken stones and two torches, so camps read from afar; stronger camps get bigger stones.
  for (const camp of map.camps || []) {
    const tier = Number(camp.tier) || 0;
    for (let k = 0; k < 4 + tier; k++) { const a = k * Math.PI * 2 / (4 + tier) + .4; place(k % 2 ? 'ruin_pillar' : 'rock_b', camp.x + Math.cos(a) * 5.4, camp.z + Math.sin(a) * 5.4, .5 + tier * .12); }
    if (tier >= 2) place('statue', camp.x - 3.2, camp.z - 4.4, .9 + tier * .1, 0);
    for (const [x, z] of [[camp.x + 4.2, camp.z - 3.4], [camp.x - 4.2, camp.z + 3.4]]) { place('torch', x, z, 1, 0); features.flames.push({ x, y: heightAt(x, z) + 3.1, z }); }
    features.camps.push({ x: camp.x, z: camp.z, tier });
  }
  // Lakes and rivers: reeds on the banks, lilies on the water.
  for (const o of water) {
    const count = Math.round((o.width + o.depth) * .9 * density);
    for (let k = 0; k < count; k++) {
      const side = k % 4, t = random() - .5;
      const x = o.x + (side < 2 ? t * o.width : (side === 2 ? -1 : 1) * (o.width / 2 + 1.2));
      const z = o.z + (side < 2 ? (side === 0 ? -1 : 1) * (o.depth / 2 + 1.2) : t * o.depth);
      if (!blocked(x, z, -.5)) place(random() < .7 ? 'reeds' : 'rock_c', x, z, 1.1 + random() * .5);
    }
    for (let k = 0; k < Math.round(o.width * o.depth * .02 * density) + 1; k++) place('lily', o.x + (random() - .5) * o.width * .8, o.z + (random() - .5) * o.depth * .8, .9 + random() * .5, undefined, -1.45);
    features.fireflies.push({ x: o.x, z: o.z, radius: Math.max(o.width, o.depth) });
  }
  for (const region of regions) {
    if (region.biome === 'hutan' || region.biome === 'rawa') features.fireflies.push({ x: region.x, z: region.z, radius: region.radius * .7 });
    if (region.biome === 'subur' || region.biome === 'sabana') features.flowers.push({ x: region.x, z: region.z, radius: region.radius * .7 });
  }

  const wind = { time: { value: 0 }, strength: { value: .6 }, heights: kit.heights };
  const counts = scatter.build(group, quality === 'high', wind);
  return {
    group, heightAt, features, size, counts, shore, extent,
    update(time, strength) { lakeUniforms.uTime.value = time; wind.time.value = time; wind.strength.value = strength; },
    dispose() {
      group.traverse(o => { if (o.isInstancedMesh) o.dispose(); else if (o.isMesh) { o.geometry.dispose(); if (o.material !== lakeMaterial) o.material.dispose(); } });
      lakeMaterial.dispose(); shore.dispose();
    }
  };
}

/**
 * Dresses the obstacles of the snapshot: ruins, rocks, groves, and in Adventure the Mystic Cave walls. Rebuilt whenever
 * the obstacle list changes. Water is drawn by the landscape; the cave door is drawn by the renderer as a sealed gate.
 */
export function dressObstacles(obstacles, kit, heightAt, seed, quality) {
  const group = new THREE.Group(); group.name = 'obstacles';
  const random = mulberry(hashString(seed)), scatter = new Scatter(kit, heightAt, random), place = scatter.place.bind(scatter);
  for (const o of obstacles) {
    const fill = (names, spacing, scale) => {
      for (let x = o.x - o.width / 2 + spacing / 2; x <= o.x + o.width / 2; x += spacing)
        for (let z = o.z - o.depth / 2 + spacing / 2; z <= o.z + o.depth / 2; z += spacing)
          place(names[Math.floor(random() * names.length)], x + (random() - .5) * spacing * .4, z + (random() - .5) * spacing * .4, scale * (.85 + random() * .3));
    };
    const wall = scale => {
      const horizontal = o.width >= o.depth, length = Math.max(o.width, o.depth), pieces = Math.max(1, Math.round(length / 6.2));
      for (let k = 0; k < pieces; k++) {
        const t = -length / 2 + (k + .5) * length / pieces;
        place('ruin_wall', o.x + (horizontal ? t : 0), o.z + (horizontal ? 0 : t), scale, horizontal ? 0 : Math.PI / 2, 0, 1.6);
      }
    };
    switch (o.kind) {
      case 'water': case 'door': case 'outpost': break;
      case 'rock': fill(['cliff', 'rock_a', 'cliff'], 4.5, Math.max(.8, Math.min(o.width, o.depth) / 5)); break;
      case 'grove': fill(['tree_broad', 'tree_cemara', 'bamboo', 'tree_broad'], 3.4, 1.05); fill(['bush_a', 'fern'], 4.5, 1); break;
      case 'wall': wall(1.05); for (let k = 0; k < Math.max(o.width, o.depth) / 5; k++) place('rock_a', o.x + (random() - .5) * o.width, o.z + (random() - .5) * o.depth, 1 + random()); break;
      default: {
        // Ruins: a broken wall along the long side, pillars at the corners and a guardian statue.
        wall(1);
        for (const [sx, sz] of [[-1, -1], [1, 1], [1, -1], [-1, 1]]) place('ruin_pillar', o.x + sx * o.width / 2.4, o.z + sz * o.depth / 2.4, .9);
        const horizontal = o.width >= o.depth;
        place(random() < .5 ? 'statue' : 'stupa_small', o.x + (horizontal ? 0 : o.width / 3), o.z + (horizontal ? o.depth / 3 : 0), 1.2, random() * Math.PI);
      }
    }
  }
  const wind = { time: { value: 0 }, strength: { value: .6 }, heights: kit.heights };
  scatter.build(group, quality === 'high', wind);
  return { group, update(time, strength) { wind.time.value = time; wind.strength.value = strength; }, dispose() { group.traverse(o => { if (o.isInstancedMesh) o.dispose(); }); } };
}
