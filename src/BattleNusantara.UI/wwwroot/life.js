// Ambient life drawn only by this client: villagers going about their day (and running home when fighting comes near),
// chickens, birds over the island, butterflies by day and fireflies at night. None of it exists in the match: it never
// blocks, fights or is seen by the simulation, and other players see their own.
import * as THREE from './vendor/three.module.js';

const VILLAGERS = ['petani', 'ibu', 'anak', 'petani', 'nelayan', 'ibu'];
const BIRD_VERTEX_WING = 'attribute float wing;\nuniform float uTime;\nuniform float uRate;\n';

// A bird or a butterfly: a body and two wings whose tips flap in the vertex shader.
function winged(span, length, body) {
  const geometry = new THREE.BufferGeometry();
  const p = [0, 0, length * .6, -span, 0, -length * .1, 0, 0, -length * .4, 0, 0, length * .6, 0, 0, -length * .4, span, 0, -length * .1];
  if (body) p.push(0, .02, length * .7, -.03, -.02, -length * .5, .03, -.02, -length * .5);
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(p, 3));
  const wing = [0, 1, 0, 0, 0, 1]; if (body) wing.push(0, 0, 0);
  geometry.setAttribute('wing', new THREE.Float32BufferAttribute(wing, 1));
  geometry.computeVertexNormals();
  return geometry;
}
function flapping(material, rate, lift) {
  material.onBeforeCompile = shader => {
    shader.uniforms.uTime = material.userData.time; shader.uniforms.uRate = { value: rate };
    shader.vertexShader = BIRD_VERTEX_WING + shader.vertexShader.replace('#include <begin_vertex>', `#include <begin_vertex>
  #ifdef USE_INSTANCING
    float phase = instanceMatrix[3].x * 1.7 + instanceMatrix[3].z * 2.3;
  #else
    float phase = 0.0;
  #endif
  transformed.y += wing * sin(uTime * uRate + phase) * ${lift.toFixed(2)};`);
  };
  return material;
}

export class Life {
  /**
   * `spawn(model)` returns a character or animal { object, actions, mixer } from the shared templates, or null.
   * `landscape` gives heightAt and the features of the map (village, flowers, fireflies).
   */
  constructor(scene, spawn, landscape, options) {
    this.scene = scene; this.landscape = landscape; this.group = new THREE.Group(); this.group.name = 'life';
    scene.add(this.group);
    const low = options.quality === 'low', features = landscape.features;
    this.people = []; this.flocks = []; this.time = 0;
    const village = features.village;
    const seeded = (() => { let s = (features.size * 7919) | 0; return () => { s = (s * 1103515245 + 12345) & 0x7fffffff; return s / 0x7fffffff; }; })();
    this.random = seeded;
    if (village) {
      const huts = village.huts?.length ? village.huts : [[village.x, village.z]];
      const spots = [...huts.map(([x, z]) => [x + 2.4, z + 2.4]), ...(village.fields || []), [village.x + 2, village.z + 1]];
      const count = low ? 3 : VILLAGERS.length;
      for (let i = 0; i < count; i++) this.add(spawn, VILLAGERS[i], spots, huts, i, village);
      for (let i = 0; i < (low ? 2 : 5); i++) this.add(spawn, 'ayam', spots.slice(0, huts.length), huts, i + 10, village, true);
    }
    // Birds: a few flocks circling high over the island, gulls where the sea is near.
    const birdMaterial = flapping(new THREE.MeshBasicMaterial({ color: '#2c3034', side: THREE.DoubleSide, fog: true }), 11, .32);
    birdMaterial.userData.time = { value: 0 };
    const flocks = low ? 1 : 3, perFlock = low ? 6 : 9;
    this.birds = new THREE.InstancedMesh(winged(.55, .42, true), birdMaterial, flocks * perFlock);
    this.birds.frustumCulled = false; this.birds.name = 'birds';
    for (let f = 0; f < flocks; f++) {
      const size = features.size;
      // Flocks wheel over the island's rim, high enough to stay out of a close camera's way.
      const angle = f * 2.1 + seeded(), reach = size * (.62 + seeded() * .2);
      this.flocks.push({ x: size / 2 + Math.cos(angle) * reach, z: size / 2 + Math.sin(angle) * reach, radius: 18 + seeded() * 22, height: 34 + seeded() * 10, speed: (.08 + seeded() * .05) * (seeded() < .5 ? -1 : 1), phase: seeded() * 6.28, members: Array.from({ length: perFlock }, () => [seeded() * 6 - 3, seeded() * 3, seeded() * 6 - 3]) });
    }
    this.group.add(this.birds);
    // Butterflies over flowers by day.
    const flowers = features.flowers || [];
    this.butterflies = null;
    if (!low && flowers.length) {
      const material = flapping(new THREE.MeshBasicMaterial({ side: THREE.DoubleSide, fog: true }), 22, .35);
      material.userData.time = this.birds.material.userData.time;
      const count = Math.min(28, flowers.length * 8);
      this.butterflies = new THREE.InstancedMesh(winged(.32, .3, false), material, count);
      this.butterflies.frustumCulled = false; this.butterflies.name = 'butterflies';
      const palette = ['#ffd84a', '#ff8ac8', '#ffffff', '#8ad8ff', '#ff9a3a'];
      this.flutter = Array.from({ length: count }, (_, i) => {
        const spot = flowers[i % flowers.length], angle = seeded() * 6.28, distance = spot.radius * Math.sqrt(seeded());
        this.butterflies.setColorAt(i, new THREE.Color(palette[i % palette.length]));
        return { x: spot.x + Math.cos(angle) * distance, z: spot.z + Math.sin(angle) * distance, phase: seeded() * 6.28, radius: 1 + seeded() * 2 };
      });
      this.butterflies.instanceColor.needsUpdate = true;
      this.group.add(this.butterflies);
    }
    this.dummy = new THREE.Object3D();
  }
  add(spawn, model, spots, huts, index, village, small = false) {
    const body = spawn(model);
    if (!body) return;
    const spot = spots[index % spots.length], { heightAt } = this.landscape;
    const x = spot[0] + (this.random() - .5) * 4, z = spot[1] + (this.random() - .5) * 4;
    body.object.position.set(x, heightAt(x, z), z);
    body.object.name = `life:${model}`;
    this.group.add(body.object);
    this.people.push({ ...body, model, spots, huts, village, small, state: 'idle', timer: 1 + this.random() * 4, target: null, clip: 'Idle', heading: this.random() * 6.28, hidden: 0 });
  }
  play(person, clip, rate = 1) {
    if (person.clip === clip) { person.actions[clip].timeScale = rate; return; }
    const next = person.actions[clip], previous = person.actions[person.clip];
    if (!next) return;
    next.reset().setEffectiveTimeScale(rate).fadeIn(.2).play(); previous?.fadeOut(.2);
    person.clip = clip;
  }
  /** `danger` lists recent fighting as [x, z]; `daylight` runs 0 (night) to 1 (noon). */
  update(dt, effects, { danger = [], daylight = 1, still = false, zoom = 1 }) {
    this.time += dt;
    const { heightAt } = this.landscape;
    for (const person of this.people) {
      const object = person.object, near = danger.some(([x, z]) => (x - object.position.x) ** 2 + (z - object.position.z) ** 2 < 24 * 24);
      if (near && !person.small && person.state !== 'flee' && person.state !== 'hide') {
        // Fighting nearby: run to the closest hut and stay indoors for a while.
        const hut = person.huts.reduce((best, h) => (h[0] - object.position.x) ** 2 + (h[1] - object.position.z) ** 2 < (best[0] - object.position.x) ** 2 + (best[1] - object.position.z) ** 2 ? h : best, person.huts[0]);
        person.state = 'flee'; person.target = [hut[0] + 1.5, hut[1] + 1.5];
      }
      if (person.state === 'hide') {
        person.timer -= dt;
        if (person.timer <= 0 && !near) { person.state = 'idle'; person.timer = 2; object.visible = true; }
        continue;
      }
      if (person.state === 'walk' || person.state === 'flee') {
        const [tx, tz] = person.target, dx = tx - object.position.x, dz = tz - object.position.z, distance = Math.hypot(dx, dz);
        const speed = person.state === 'flee' ? 3.4 : person.small ? .9 : 1.25;
        if (distance < .4 || still) {
          if (person.state === 'flee') { person.state = 'hide'; person.timer = 18; object.visible = false; continue; }
          person.state = person.model === 'petani' && this.random() < .6 ? 'work' : 'idle'; person.timer = 3 + this.random() * 6;
        } else {
          const step = Math.min(distance, speed * dt);
          object.position.x += dx / distance * step; object.position.z += dz / distance * step;
          object.position.y = heightAt(object.position.x, object.position.z);
          person.heading = Math.atan2(dx, dz);
          this.play(person, 'Walk', person.state === 'flee' ? 2.2 : person.small ? 1.6 : 1);
        }
      } else {
        person.timer -= dt;
        // Farmers hoe the fields; chickens peck; everyone else rests, then walks somewhere new.
        this.play(person, person.state === 'work' || (person.small && Math.sin(this.time * 1.3 + person.heading * 5) > .6) ? 'Attack' : 'Idle', person.state === 'work' ? .45 : person.small ? 1.4 : 1);
        if (person.timer <= 0 && !still) {
          const spot = person.spots[Math.floor(this.random() * person.spots.length)];
          const reach = person.small ? 3 : 2;
          person.target = [spot[0] + (this.random() - .5) * reach * 2, spot[1] + (this.random() - .5) * reach * 2];
          person.state = 'walk';
        }
      }
      let turn = person.heading - object.rotation.y; turn = Math.atan2(Math.sin(turn), Math.cos(turn));
      object.rotation.y += turn * Math.min(1, dt * 8);
      if (!still) person.mixer.update(dt);
    }
    // Birds by day; the night belongs to the fireflies.
    const time = this.birds.material.userData.time; time.value = this.time;
    this.birds.visible = !still && daylight > .3 && zoom > .6;
    if (this.birds.visible) {
      let i = 0;
      for (const flock of this.flocks) {
        const angle = flock.phase + this.time * flock.speed;
        const cx = flock.x + Math.cos(angle) * flock.radius, cz = flock.z + Math.sin(angle) * flock.radius, heading = angle + (flock.speed > 0 ? Math.PI / 2 : -Math.PI / 2);
        for (const [ox, oy, oz] of flock.members) {
          this.dummy.position.set(cx + ox + Math.sin(this.time * .7 + ox) * .6, flock.height + oy + Math.sin(this.time * 1.1 + oz) * .5, cz + oz);
          this.dummy.rotation.set(0, Math.atan2(Math.cos(heading), Math.sin(heading)), 0); this.dummy.scale.setScalar(1);
          this.dummy.updateMatrix(); this.birds.setMatrixAt(i++, this.dummy.matrix);
        }
      }
      this.birds.instanceMatrix.needsUpdate = true;
    }
    if (this.butterflies) {
      this.butterflies.visible = !still && daylight > .45;
      if (this.butterflies.visible) {
        this.flutter.forEach((b, i) => {
          const t = this.time * .6 + b.phase;
          const x = b.x + Math.cos(t) * b.radius + Math.sin(t * 2.7) * .4, z = b.z + Math.sin(t * 1.3) * b.radius;
          this.dummy.position.set(x, heightAt(x, z) + 1 + Math.abs(Math.sin(t * 3.1)) * .8, z);
          this.dummy.rotation.set(0, t * 1.7, 0); this.dummy.scale.setScalar(1.6);
          this.dummy.updateMatrix(); this.butterflies.setMatrixAt(i, this.dummy.matrix);
        });
        this.butterflies.instanceMatrix.needsUpdate = true;
      }
    }
    // Fireflies drift over forests, swamps and lakes after dusk.
    const dark = 1 - THREE.MathUtils.smoothstep(daylight, .3, .55);
    if (dark > 0 && !still && effects) {
      for (const spot of this.landscape.features.fireflies || []) {
        if (Math.random() > dt * 5 * dark) continue;
        const angle = Math.random() * 6.28, distance = spot.radius * Math.sqrt(Math.random());
        const x = spot.x + Math.cos(angle) * distance, z = spot.z + Math.sin(angle) * distance;
        effects.firefly(x, heightAt(x, z) + .8 + Math.random() * 2.2, z);
      }
    }
  }
  get count() { return this.people.length; }
  dispose(release) {
    for (const person of this.people) { person.mixer.stopAllAction(); release(person.object); }
    this.birds.geometry.dispose(); this.birds.material.dispose(); this.birds.dispose();
    if (this.butterflies) { this.butterflies.geometry.dispose(); this.butterflies.material.dispose(); this.butterflies.dispose(); }
    this.scene.remove(this.group);
  }
}
