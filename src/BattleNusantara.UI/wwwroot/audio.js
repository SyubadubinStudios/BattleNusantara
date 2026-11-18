// Music, ambience and sound effects, all composed and synthesized for the game by scripts/compose_audio.py and shipped
// with it (no network). The exploration and battle layers are one piece in two parts that play in sync, so fighting
// raises the drums and brass without restarting the music. Purely presentation.

const MUSIC = ['explore', 'battle'];
const AMBIENCE = ['day', 'night', 'rain'];
export const SOUNDS = ['hit', 'swing', 'magic', 'lightning', 'shockwave', 'wave', 'death', 'growl', 'boar', 'monkey', 'chicken', 'buffalo',
  'coin', 'loot', 'camp', 'levelup', 'quest', 'build', 'mine', 'click', 'horn', 'gong', 'thunder', 'victory', 'defeat'];
// The shortest gap between two plays of the same sound, so a big fight does not become a buzz.
const GAP = { hit: .05, swing: .07, death: .12, growl: .5, boar: .8, monkey: 1, chicken: 2, buffalo: 3, coin: .15, mine: .3, build: .5, click: .03 };
const LEVELS = { music: .62, ambience: .5, effects: .9 };
// Ogg Vorbis where the browser plays it (Chromium, WebKitGTK); AAC elsewhere (WebKit on macOS).
const EXTENSION = (() => { try { return new Audio().canPlayType('audio/ogg; codecs="vorbis"') ? 'ogg' : 'm4a'; } catch { return 'ogg'; } })();

export class Soundscape {
  constructor(base) {
    this.base = base; this.context = null; this.buffers = new Map(); this.last = new Map(); this.playing = 0;
    this.mode = 'off'; this.layers = null; this.loaded = false;
  }
  get state() { return this.context ? this.context.state : 'off'; }
  /** Opens audio on the first user gesture (browsers refuse sound before one). */
  start(volume) {
    if (this.context) return;
    const Context = window.AudioContext || window.webkitAudioContext; if (!Context) return;
    const c = this.context = new Context();
    this.volume = volume;
    this.master = c.createGain(); this.master.gain.value = Soundscape.gain(volume);
    const limiter = c.createDynamicsCompressor();
    limiter.threshold.value = -10; limiter.knee.value = 6; limiter.ratio.value = 6; limiter.attack.value = .004; limiter.release.value = .2;
    this.master.connect(limiter); limiter.connect(c.destination);
    this.bus = {};
    for (const [name, level] of Object.entries(LEVELS)) { const gain = c.createGain(); gain.gain.value = level; gain.connect(this.master); this.bus[name] = gain; }
    // The menu theme streams from a media element; the layered match music is decoded so both layers stay sample-locked.
    this.menu = new Audio(new URL(`music-menu.${EXTENSION}`, this.base).href); this.menu.loop = true; this.menu.preload = 'auto';
    this.menuGain = c.createGain(); this.menuGain.gain.value = 0;
    // If the element cannot be routed through the graph, its own volume follows the settings instead.
    try { c.createMediaElementSource(this.menu).connect(this.menuGain); } catch { this.menuDirect = true; this.menu.volume = 0; }
    this.menuGain.connect(this.bus.music);
    this.load();
  }
  static gain(volume) { return Math.pow(Math.max(0, Math.min(100, volume)) / 100, 1.3); }
  setVolume(volume) { this.volume = volume; if (this.context) this.master.gain.setTargetAtTime(Soundscape.gain(volume), this.context.currentTime, .1); }
  async load() {
    const c = this.context, fetchBuffer = async name => {
      try {
        const response = await fetch(new URL(`${name}.${EXTENSION}`, this.base).href);
        if (!response.ok) return;
        this.buffers.set(name, await c.decodeAudioData(await response.arrayBuffer()));
      } catch { /* A missing or undecodable file only silences that sound. */ }
    };
    // Short effects first, so the first clicks and hits are heard; then ambience and the long music layers.
    await Promise.all(SOUNDS.map(fetchBuffer));
    await Promise.all(AMBIENCE.map(name => fetchBuffer(`ambience-${name}`)));
    await Promise.all(MUSIC.map(name => fetchBuffer(`music-${name}`)));
    if (this.context !== c) return;
    this.loaded = true;
    const at = c.currentTime + .1;
    this.layers = {};
    for (const name of [...MUSIC.map(m => `music-${m}`), ...AMBIENCE.map(a => `ambience-${a}`)]) {
      const buffer = this.buffers.get(name); if (!buffer) continue;
      const source = c.createBufferSource(), gain = c.createGain();
      source.buffer = buffer; source.loop = true; gain.gain.value = 0;
      source.connect(gain); gain.connect(name.startsWith('music') ? this.bus.music : this.bus.ambience);
      source.start(at); this.layers[name] = { source, gain };
    }
  }
  /** Follows the scene: the menu theme, or exploration with the battle layer rising with the fighting. */
  update({ playing, combat = 0, rain = 0, daylight = 1, voice = false, ended = false }) {
    const c = this.context; if (!c || c.state !== 'running') return;
    // Only changed targets are scheduled, so the automation timeline does not grow every frame.
    const t = c.currentTime, set = (node, value, lag = .6) => {
      if (!node || Math.abs((node.target ?? -1) - value) < .01) return;
      node.target = value; node.gain.setTargetAtTime(value, t, lag);
    };
    const mode = !playing ? 'menu' : ended ? 'ended' : combat > .25 ? 'battle' : 'explore';
    if (mode !== this.mode) {
      if (mode === 'menu') { this.menu.play().catch(() => {}); }
      else if (this.mode === 'menu') { const menu = this.menu; setTimeout(() => { if (this.mode !== 'menu') menu.pause(); }, 2500); }
      this.mode = mode;
    }
    set(this.menuGain, mode === 'menu' ? 1 : 0, mode === 'menu' ? .8 : .5);
    if (this.menuDirect) this.menu.volume = mode === 'menu' ? Math.min(1, Soundscape.gain(this.volume) * LEVELS.music) : 0;
    const layers = this.layers || {};
    set(layers['music-explore']?.gain, playing && !ended ? 1 - Math.min(.3, combat * .3) : 0, 1.2);
    set(layers['music-battle']?.gain, playing && !ended ? Math.min(1, Math.max(0, (combat - .12) * 1.6)) : 0, combat > .25 ? .8 : 2.5);
    set(this.bus.music, voice ? LEVELS.music * .4 : LEVELS.music, .3);
    const day = Math.max(0, Math.min(1, (daylight - .3) / .4));
    set(layers['ambience-day']?.gain, (playing ? .9 : .5) * day * (1 - rain * .6), 1.5);
    set(layers['ambience-night']?.gain, (playing ? .9 : .5) * (1 - day), 1.5);
    set(layers['ambience-rain']?.gain, playing ? Math.min(1, rain * 1.2) : 0, .8);
  }
  /** A one-shot effect. `pan` runs from -1 (left) to 1 (right); `gain` already includes distance. */
  play(name, { pan = 0, gain = 1, rate = 1 } = {}) {
    const c = this.context, buffer = this.buffers.get(name);
    if (!c || c.state !== 'running' || !buffer || gain < .02) return false;
    const now = c.currentTime;
    if (now - (this.last.get(name) ?? -9) < (GAP[name] ?? .08) || this.playing >= 16) return false;
    this.last.set(name, now); this.playing++;
    const source = c.createBufferSource(), level = c.createGain(), panner = c.createStereoPanner();
    source.buffer = buffer; source.playbackRate.value = rate * (1 + (Math.random() - .5) * .08);
    level.gain.value = Math.min(1.5, gain); panner.pan.value = Math.max(-1, Math.min(1, pan));
    source.connect(level); level.connect(panner); panner.connect(name === 'victory' || name === 'defeat' ? this.bus.music : this.bus.effects);
    source.onended = () => { this.playing--; level.disconnect(); panner.disconnect(); };
    source.start();
    return true;
  }
  dispose() {
    this.menu?.pause();
    if (this.menu) this.menu.src = '';
    this.context?.close(); this.context = null; this.buffers.clear();
  }
}
