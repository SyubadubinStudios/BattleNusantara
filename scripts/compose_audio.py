"""Composes and renders every sound of Battle of Nusantara: the music (a title theme and an exploration piece whose battle
layer plays in sync on top of it), two stingers, three ambience loops and the sound effects. Everything is synthesized
here with numpy and scipy (no samples, no recordings), then encoded to Ogg Vorbis and AAC with ffmpeg into
src/BattleNusantara.UI/wwwroot/audio/. The output is deterministic for a given numpy and scipy.

Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios. Original compositions.

Usage: python scripts/compose_audio.py [--only name,name] [--keep-wav]
Needs numpy, scipy and ffmpeg (with libvorbis) on PATH.

The music is in D Phrygian, the mode closest to the pelog colour of Javanese gamelan heard through an orchestra: the
gamelan (saron, bonang, gender, kenong, kempul, gong ageng and kendang) carries the pulse, strings and a bamboo suling
carry the theme, and the battle layer adds taiko drums, low string ostinati, brass and choir.
"""
import argparse
import shutil
import subprocess
import tempfile
from pathlib import Path

import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src/BattleNusantara.UI/wwwroot/audio"
rng = np.random.default_rng(2125)


# --------------------------------------------------------------------------------------------- building blocks
def seconds(n):
    return np.arange(int(n * SR)) / SR


def midi(note):
    return 440.0 * 2 ** ((note - 69) / 12)


def _sos(kind, frequency, order=2):
    nyquist = SR * .49
    if isinstance(frequency, (list, tuple)):
        frequency = [min(max(f, 10), nyquist) for f in frequency]
    else:
        frequency = min(max(frequency, 10), nyquist)
    return signal.butter(order, frequency, kind, fs=SR, output="sos")


def lowpass(x, f, order=2):
    return signal.sosfilt(_sos("lowpass", f, order), x)


def highpass(x, f, order=2):
    return signal.sosfilt(_sos("highpass", f, order), x)


def bandpass(x, low, high, order=2):
    return signal.sosfilt(_sos("bandpass", [low, high], order), x)


def noise(n):
    return rng.standard_normal(int(n))


def pink(n):
    # Voss-like approximation: white noise through a gentle -3 dB/octave filter.
    b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
    a = [1, -2.494956002, 2.017265875, -0.522189400]
    return signal.lfilter(b, a, noise(n)) * 8


def saw(phase):
    return 2 * (phase % 1.0) - 1


def fade(n, attack, release, total=None):
    total = total or n
    t = np.arange(n) / SR
    env = np.minimum(1, t / max(attack, 1e-4))
    if release > 0:
        env *= np.clip((total / SR - t) / release, 0, 1)
    return env


def place(mix, at, sound, gain=1.0, pan=0.0):
    """Adds a mono sound to a stereo mix at a time in seconds, with constant-power panning."""
    start = int(at * SR)
    if start >= len(mix):
        return
    end = min(len(mix), start + len(sound))
    left, right = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
    mix[start:end, 0] += sound[:end - start] * gain * left
    mix[start:end, 1] += sound[:end - start] * gain * right


def impulse(length, decay, bright=1.0, seed=7):
    """A stereo hall: decaying noise, darker as it fades, with a few early reflections."""
    local = np.random.default_rng(seed)
    n = int(length * SR)
    t = np.arange(n) / SR
    ir = np.zeros((n, 2))
    for channel in range(2):
        source = local.standard_normal(n)
        low = lowpass(source, 1800 * bright) * np.exp(-t * 6.9 / decay)
        high = highpass(source, 1800 * bright) * np.exp(-t * 6.9 / (decay * .45))
        ir[:, channel] = low + high * .6
        for k in range(12):
            delay = local.integers(int(.007 * SR), int(.09 * SR))
            ir[delay, channel] += local.uniform(.3, .9) * (1 - k / 14)
    ir[: int(.004 * SR)] *= np.linspace(0, 1, int(.004 * SR))[:, None]
    return ir / np.sqrt((ir ** 2).sum(axis=0))


def reverb(mix, ir, wet, loop=False):
    """Convolves a stereo mix. For a loop, the tail that runs past the end is folded back onto the start."""
    out = np.zeros_like(mix)
    for channel in range(2):
        full = signal.fftconvolve(mix[:, channel], ir[:, channel])
        out[:, channel] = full[: len(mix)]
        if loop:
            tail = full[len(mix):]
            out[: len(tail), channel] += tail[: len(mix)]
    return mix * (1 - wet * .35) + out * wet


def fold(mix, length):
    """Keeps a loop of `length` samples, adding whatever rang past its end back onto its start."""
    out = mix[:length].copy()
    over = mix[length:]
    out[: len(over)] += over[: length]
    return out


# --------------------------------------------------------------------------------------------- instruments
def metallophone(frequency, length=2.6, bright=1.0, ombak=4.2, decay=1.0):
    """Saron, bonang or gender: inharmonic bar partials, a twin tuned a few hertz apart (the ombak, the 'wave' of a
    gamelan), and the knock of the mallet."""
    t = seconds(length)
    y = np.zeros_like(t)
    for ratio, amp, rate in [(1, 1, 1.4), (2.76, .5 * bright, 3.2), (5.4, .24 * bright, 6.5), (8.93, .1 * bright, 11)]:
        y += amp * np.sin(2 * np.pi * frequency * ratio * t + rng.uniform(0, 6.3)) * np.exp(-t * rate / decay)
    y += .65 * np.sin(2 * np.pi * (frequency + ombak) * t) * np.exp(-t * 1.5 / decay)
    knock = bandpass(noise(len(t)), 1800, 6500) * np.exp(-t * 180) * .5 * bright
    return (y + knock) * fade(len(t), .002, .25) / 2.2


def gong(frequency=55.0, length=8.0):
    """Gong ageng: a deep hum that swells and beats slowly, over a soft mallet thump."""
    t = seconds(length)
    bend = 1 + .012 * np.exp(-t * 3)
    y = np.zeros_like(t)
    for ratio, amp, rate in [(1, 1, .32), (2.0, .3, .55), (2.42, .22, .7), (3.03, .18, .9), (4.2, .1, 1.4), (5.38, .06, 2)]:
        y += amp * np.sin(2 * np.pi * frequency * ratio * np.cumsum(bend) / SR + rng.uniform(0, 6.3)) * np.exp(-t * rate)
    y += .7 * np.sin(2 * np.pi * (frequency + .75) * t) * np.exp(-t * .3)
    y *= 1 + .22 * np.sin(2 * np.pi * .85 * t)
    y *= np.minimum(1, t / .03)
    thump = lowpass(noise(len(t)), 160) * np.exp(-t * 18) * .8
    return (y + thump) / 2.2


def kendang(kind):
    """Javanese hand drum: dung (open bass), tung (mid) or tak (slap)."""
    t = seconds(.6)
    if kind == "tak":
        y = bandpass(noise(len(t)), 900, 3800) * np.exp(-t * 55) + .3 * np.sin(2 * np.pi * 420 * t) * np.exp(-t * 40)
    else:
        f0 = 92 if kind == "dung" else 175
        f = f0 * (1 + .35 * np.exp(-t * 30))
        y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * (6 if kind == "dung" else 11))
        y += lowpass(noise(len(t)), 1200) * np.exp(-t * 60) * .4
    return y * .8


def taiko(frequency=56.0, size=1.0):
    """A big festival drum: a pitched body that drops, and the skin's slap."""
    t = seconds(1.6 * size)
    f = frequency * (1 + .9 * np.exp(-t * 22))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 4.2 / size)
    skin = lowpass(noise(len(t)), 900) * np.exp(-t * 28) * .7
    sub = np.sin(2 * np.pi * frequency * .5 * t) * np.exp(-t * 3 / size) * .4
    return (body + skin + sub) * .9


def strings(frequency, length, attack=.5, release=1.0, bright=2600, voices=5, vibrato=.0045):
    """A string section: detuned sawtooth voices with their own vibrato, through a gentle low-pass."""
    total = length + release
    t = seconds(total)
    y = np.zeros_like(t)
    for _ in range(voices):
        detune = 2 ** (rng.uniform(-8, 8) / 1200)
        wobble = 1 + vibrato * np.sin(2 * np.pi * rng.uniform(4.8, 5.8) * t + rng.uniform(0, 6.3)) * np.minimum(1, t / .7)
        y += saw(np.cumsum(frequency * detune * wobble) / SR + rng.uniform())
    y = lowpass(y / voices, bright, 2)
    env = np.minimum(1, t / attack) * np.where(t < length, 1.0, np.exp(-(t - length) / max(release / 3, .01)))
    return y * env


def spiccato(frequency, length=.2):
    """Short, bitten low-string notes for the battle ostinato."""
    return strings(frequency, length, attack=.006, release=.16, bright=1900, voices=3, vibrato=0)


def brass(frequency, length, release=.35, power=1.0):
    """Horns and trombones: a scooped attack whose tone opens and settles, saw and square mixed."""
    total = length + release
    t = seconds(total)
    scoop = 1 - .025 * np.exp(-t * 28)
    wobble = 1 + .004 * np.sin(2 * np.pi * 5.3 * t) * np.clip((t - .35) / .4, 0, 1)
    phase = np.cumsum(frequency * scoop * wobble) / SR
    tone = .7 * saw(phase) + .3 * np.sign(np.sin(2 * np.pi * phase))
    tone += .5 * saw(np.cumsum(frequency * 1.003 * scoop * wobble) / SR)
    dark, bright = lowpass(tone, 650), lowpass(tone, 2800 * power)
    open_ = .45 + .55 * np.exp(-t * 5) * np.minimum(1, t / .05)
    y = dark + (bright - dark) * open_
    env = np.minimum(1, t / .06) * np.where(t < length, 1.0, np.exp(-(t - length) / (release / 3)))
    return y * env / 1.6


def choir(frequency, length, vowel="a", release=1.2):
    """A wordless choir: detuned voices shaped by vowel formants."""
    formants = {"a": [(720, 1.0, 120), (1150, .55, 130), (2650, .25, 180)], "o": [(450, 1.0, 90), (800, .5, 100), (2830, .15, 160)]}[vowel]
    total = length + release
    t = seconds(total)
    source = np.zeros_like(t)
    for _ in range(4):
        wobble = 1 + .005 * np.sin(2 * np.pi * rng.uniform(4.6, 5.6) * t + rng.uniform(0, 6.3))
        source += saw(np.cumsum(frequency * 2 ** (rng.uniform(-12, 12) / 1200) * wobble) / SR + rng.uniform())
    y = np.zeros_like(t)
    for center, amp, width in formants:
        y += bandpass(source, center - width, center + width) * amp
    env = np.minimum(1, t / .7) * np.where(t < length, 1.0, np.exp(-(t - length) / (release / 3)))
    return y * env * 1.6


def suling(notes, bpm, length):
    """Bamboo flute: a breathy sine with a little second harmonic, gliding between notes, vibrato that blooms.
    `notes` are (midi, start beat, beats)."""
    beat = 60 / bpm
    out = np.zeros(int(length * SR))
    previous = None
    for note, start, beats in notes:
        dur = beats * beat
        t = seconds(dur + .25)
        f0 = midi(note)
        glide = f0 if previous is None else f0 + (midi(previous) - f0) * np.exp(-t * 28)
        wobble = 1 + .006 * np.sin(2 * np.pi * 5.1 * t) * np.clip((t - .18) / .3, 0, 1)
        phase = np.cumsum(glide * wobble) / SR
        tone = np.sin(2 * np.pi * phase) + .22 * np.sin(4 * np.pi * phase) + .07 * np.sin(6 * np.pi * phase)
        breath = bandpass(noise(len(t)), f0 * 1.5, min(f0 * 5, 9000)) * .16
        chiff = bandpass(noise(len(t)), 2500, 7000) * np.exp(-t * 40) * .25
        env = np.minimum(1, t / .07) * np.clip((dur + .2 - t) / .25, 0, 1)
        sound = (tone + breath) * env + chiff
        start_index = int(start * beat * SR)
        end = min(len(out), start_index + len(sound))
        out[start_index:end] += sound[: end - start_index]
        previous = note
    return out * .55


def pluck(frequency, length=1.6):
    """A plucked zither (kecapi): bright harmonics that die faster than the fundamental."""
    t = seconds(length)
    y = np.zeros_like(t)
    for k in range(1, 9):
        y += np.sin(2 * np.pi * frequency * k * (1 + .0007 * k * k) * t) * np.exp(-t * (1.6 + k * .9)) / k
    return y * fade(len(t), .002, .1) * .6


def bass(frequency, length):
    t = seconds(length + .4)
    y = np.sin(2 * np.pi * frequency * t) + .35 * np.sin(4 * np.pi * frequency * t) + .12 * saw(frequency * t)
    return lowpass(y, 500) * np.minimum(1, t / .04) * np.where(t < length, 1.0, np.exp(-(t - length) / .12)) * .7


def cymbal_swell(length):
    t = seconds(length)
    return highpass(noise(len(t)), 4500) * (t / length) ** 3 * .35


def crash(length=3.0):
    t = seconds(length)
    y = highpass(noise(len(t)), 2800) * np.exp(-t * 1.8)
    for f in [3150, 4720, 5980, 7420]:
        y += .08 * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.3)) * np.exp(-t * 2.5)
    return y * .35


# --------------------------------------------------------------------------------------------- harmony
CHORDS = {"Dm": (50, 53, 57), "Bb": (46, 50, 53), "C": (48, 52, 55), "Eb": (51, 55, 58), "Gm": (43, 46, 50), "A": (45, 49, 52), "F": (41, 45, 48)}
# The exploration piece: 24 bars, three phrases of eight. The last bar's A major leads back into the first bar's D minor.
PROGRESSION = ["Dm", "Bb", "C", "Dm", "Dm", "Bb", "Eb", "C",
               "Gm", "Dm", "Eb", "Bb", "Gm", "Dm", "Eb", "A",
               "Dm", "Eb", "Dm", "C", "Bb", "Gm", "Eb", "A"]
# The theme, sung by the suling (midi, beat, length in beats), starting at bar 5.
THEME = [(69, 0, 2), (70, 2, 1), (69, 3, 1), (65, 4, 2), (67, 6, 1), (69, 7, 1), (74, 8, 3), (72, 11, 1), (69, 12, 4),
         (65, 16, 2), (67, 18, 1), (69, 19, 1), (70, 20, 2), (69, 22, 1), (67, 23, 1), (65, 24, 1), (63, 25, 1), (62, 26, 2), (62, 28, 4)]


def chord_tones(name, octave=0):
    return [n + 12 * octave for n in CHORDS[name]]


def exploration(bpm=92):
    """Both layers of the match music over the same 24 bars, so the client can play them in sync."""
    beat = 60 / bpm
    bar = beat * 4
    length = bar * len(PROGRESSION)
    pad = 9.0
    calm = np.zeros((int((length + pad) * SR), 2))
    war = np.zeros_like(calm)
    patterns = [[0, 1, 2, 1], [2, 1, 0, 1], [0, 2, 1, 2], [1, 2, 0, 2], [2, 0, 1, 0]]
    for index, name in enumerate(PROGRESSION):
        at = index * bar
        tones = chord_tones(name)
        # Gamelan pulse: saron on the beat, bonang anticipating on the off-beats, kenong and kempul marking the bar.
        pattern = patterns[(index * 3 + index // 8) % len(patterns)]
        for k, degree in enumerate(pattern):
            note = tones[degree] + 12
            place(calm, at + k * beat, metallophone(midi(note), 2.2), .34, -.25)
            nxt = tones[pattern[(k + 1) % 4]] + 24
            place(calm, at + (k + .5) * beat, metallophone(midi(nxt), 1.2, bright=.8, decay=.6), .16, .35)
        place(calm, at + 2 * beat, metallophone(midi(tones[0] + 12), 3.0, bright=.5, ombak=2.5, decay=1.8), .22, .1)
        if index % 2 == 1:
            place(calm, at + 2 * beat, gong(110 * 2 ** ((tones[0] - 45) / 12) / 2, 3.5), .32, -.1)
        if index % 8 == 0:
            place(calm, at, gong(55, 8), .75, 0)
        # Strings, bass and a light kendang.
        voicing = [tones[0] - 12, tones[1], tones[2], tones[0] + 12]
        for i, note in enumerate(voicing):
            place(calm, at, strings(midi(note), bar, attack=.6, release=1.2, bright=2200), .085, (-1) ** i * .4)
        place(calm, at, bass(midi(tones[0] - 12), bar * .95), .3, 0)
        for k, (hit, gain) in enumerate([("dung", .35), (None, 0), ("tak", .18), ("tung", .2), (None, 0), ("tak", .14), ("tung", .16), ("tak", .2)]):
            if hit:
                place(calm, at + k * beat / 2, kendang(hit), gain, .2)
        # Battle layer: taiko, low strings, brass, choir and swells.
        for k, gain in enumerate([1.0, .35, .55, .3, .85, .45, .6, .4]):
            place(war, at + k * beat / 2, taiko(56 if k % 4 == 0 else 74, 1.0 if k % 4 == 0 else .6), gain * .55, (-.3, .3)[k % 2])
        if index % 2 == 0:
            place(war, at, taiko(42, 1.4), .7, 0)
        if index % 8 == 7:
            for k in range(8):
                place(war, at + 2 * beat + k * beat / 4, taiko(80, .45), .25 + k * .05, (-.4, .4)[k % 2])
            place(war, at + bar - 2.0, cymbal_swell(2.0), .9, 0)
        if index % 8 == 0:
            place(war, at, crash(), .8, 0)
        root = tones[0] - 12
        for k in range(8):
            note = root + (12 if k in (2, 6) else 0)
            place(war, at + k * beat / 2, spiccato(midi(note)), .2 if k % 4 == 0 else .13, -.2)
            place(war, at + k * beat / 2, spiccato(midi(note - 12)), .12, .2)
        if name in ("Eb", "A") or index % 4 == 0:
            for i, note in enumerate([tones[0], tones[2], tones[0] + 12]):
                place(war, at, brass(midi(note), beat * (2 if index % 4 == 0 else 1.5)), .15, (-.35, 0, .35)[i])
        if index >= 8:
            for i, note in enumerate([tones[0] + 12, tones[1] + 12, tones[2] + 12]):
                place(war, at, choir(midi(note), bar, "a" if index < 16 else "o"), .06, (-.5, 0, .5)[i])
    # The theme: the suling sings it in the calm layer; horns double it an octave down in the battle layer.
    for phrase in (4, 16):
        start = phrase * bar
        flute = suling([(n + 12, b, d) for n, b, d in THEME], bpm, length + pad - start)
        place(calm, start, flute, .3, .15)
        if phrase == 16:
            for note, b, d in THEME:
                place(war, start + b * beat, brass(midi(note), d * beat * .95, power=1.2), .13, -.1)
    return calm, war, length


def title_theme(bpm=72):
    """The title screen: Borobudur Quantum at dawn. Gong, gender shimmer, strings and choir; the suling sings the theme,
    and the horns take it up before the music settles again into the loop."""
    beat = 60 / bpm
    bar = beat * 4
    progression = ["Dm", "Bb", "Gm", "A", "Dm", "Bb", "Eb", "A", "Dm", "Bb", "Gm", "C", "Bb", "Eb", "Gm", "A"]
    length = bar * len(progression)
    mix = np.zeros((int((length + 9) * SR), 2))
    for index, name in enumerate(progression):
        at = index * bar
        tones = chord_tones(name)
        for i, note in enumerate([tones[0] - 12, tones[1], tones[2], tones[0] + 12, tones[1] + 12]):
            place(mix, at, strings(midi(note), bar, attack=1.2, release=2.0, bright=1800), .07, (-1) ** i * .5)
        place(mix, at, choir(midi(tones[0] + 12), bar, "o"), .05, -.3)
        place(mix, at, choir(midi(tones[2] + 12), bar, "o"), .045, .3)
        place(mix, at, bass(midi(tones[0] - 12), bar * .9), .22, 0)
        arpeggio = [tones[0] + 12, tones[1] + 12, tones[2] + 12, tones[0] + 24, tones[2] + 12, tones[1] + 12, tones[0] + 24, tones[2] + 24]
        for k, note in enumerate(arpeggio):
            place(mix, at + k * beat / 2, metallophone(midi(note), 3.0, bright=.55, ombak=3.2, decay=1.5), .1, (-.5, .5)[k % 2])
        if index % 4 == 0:
            place(mix, at, gong(55, 8), .7, 0)
        if index >= 8:
            place(mix, at, taiko(48, 1.2), .3, 0)
            place(mix, at + 2 * beat, taiko(48, 1.0), .2, 0)
        if 12 <= index <= 15:
            for i, note in enumerate([tones[0], tones[2]]):
                place(mix, at, brass(midi(note), bar * .95, release=.8), .09, (-.3, .3)[i])
    flute = suling([(n + 12, b, d) for n, b, d in THEME], bpm, length + 9)
    place(mix, 0, flute, .34, .1)
    for note, b, d in THEME[:9]:
        place(mix, 8 * bar + b * beat, brass(midi(note), d * beat * .95, power=1.1), .12, -.1)
    return mix, length


def stinger(victory):
    mix = np.zeros((int(7.5 * SR), 2))
    if victory:
        # A fanfare that lands on D major: the minor journey ends in light.
        for k, note in enumerate([62, 66, 69]):
            place(mix, k * .22, brass(midi(note), .2 if k < 2 else .1), .3, 0)
        for i, note in enumerate([50, 57, 62, 66, 69, 74]):
            place(mix, .66, brass(midi(note), 2.4, release=1.4, power=1.2), .13, (-.4, .4)[i % 2])
            place(mix, .66, choir(midi(note + 12), 2.6, "a", release=2), .05, (-.4, .4)[i % 2])
        place(mix, .66, gong(55, 6.5), .8, 0)
        place(mix, .66, crash(4.5), .7, 0)
        place(mix, .66, taiko(50, 1.4), .8, 0)
        for k, note in enumerate([74, 78, 81, 86]):
            place(mix, .7 + k * .12, metallophone(midi(note), 3, bright=.7), .2, (-.4, .4)[k % 2])
    else:
        # The gate has fallen: low strings sink to D minor under a distant gong.
        for k, note in enumerate([57, 55, 53, 51, 50]):
            place(mix, k * .55, strings(midi(note - 12), .6 if k < 4 else 3.0, attack=.08, release=1.6, bright=1300), .3, -.15)
            place(mix, k * .55, strings(midi(note), .6 if k < 4 else 3.0, attack=.1, release=1.6, bright=1600), .16, .15)
        place(mix, 2.2, choir(midi(62), 3.2, "o", release=2), .08, -.3)
        place(mix, 2.2, choir(midi(65), 3.2, "o", release=2), .07, .3)
        place(mix, 2.2, gong(49, 5.3), .7, 0)
        place(mix, 0, taiko(40, 1.6), .6, 0)
    return mix


# --------------------------------------------------------------------------------------------- ambience
def loop_crossfade(mix, overlap):
    """Makes a seamless loop by crossfading the extra tail over the start."""
    n = int(overlap * SR)
    body, tail = mix[:-n].copy(), mix[-n:]
    ramp = np.linspace(0, 1, n)[:, None]
    body[:n] = body[:n] * ramp + tail * (1 - ramp)
    return body


def ambience_day(length=24.0):
    total = length + 1.5
    n = int(total * SR)
    t = np.arange(n) / SR
    mix = np.zeros((n, 2))
    wind = lowpass(pink(n), 700) * (.6 + .4 * np.sin(2 * np.pi * t / 11 + 1.3)) * .25
    leaves = highpass(pink(n), 2500) * (.3 + .7 * np.clip(np.sin(2 * np.pi * t / 7.3), 0, 1) ** 2) * .05
    mix[:, 0] += wind + leaves
    mix[:, 1] += np.roll(wind, 5000) + np.roll(leaves, 3000)
    # Birdsong: trills and calls from different places.
    for _ in range(22):
        at = rng.uniform(0, total - 1.5)
        base = rng.uniform(2300, 4600)
        repeats = int(rng.integers(1, 6))
        gap = rng.uniform(.06, .14)
        pan = rng.uniform(-.8, .8)
        for k in range(repeats):
            d = rng.uniform(.05, .13)
            tt = seconds(d)
            sweep = base * (1 + rng.uniform(-.25, .35) * tt / d) * (1 + .04 * np.sin(2 * np.pi * rng.uniform(40, 90) * tt))
            chirp = np.sin(2 * np.pi * np.cumsum(sweep) / SR) * np.sin(np.pi * tt / d) ** 2
            place(mix, at + k * gap, chirp, rng.uniform(.03, .07), pan)
    return loop_crossfade(mix, 1.5)


def ambience_night(length=24.0):
    total = length + 1.5
    n = int(total * SR)
    t = np.arange(n) / SR
    mix = np.zeros((n, 2))
    wind = lowpass(pink(n), 400) * (.6 + .4 * np.sin(2 * np.pi * t / 13)) * .16
    mix += np.stack([wind, np.roll(wind, 7000)], axis=1)
    # Crickets: pulsed high tones in chirps of a few pulses.
    for voice in range(4):
        carrier = rng.uniform(3900, 5200)
        pulse = rng.uniform(28, 46)
        period = rng.uniform(.5, .9)
        pan = rng.uniform(-.9, .9)
        gate = (np.sin(2 * np.pi * pulse * t) > .2) * ((t % period) < period * rng.uniform(.25, .4))
        tone = np.sin(2 * np.pi * carrier * t) * lowpass(gate.astype(float), 300) * .05
        mix[:, 0] += tone * np.cos((pan + 1) * np.pi / 4)
        mix[:, 1] += tone * np.sin((pan + 1) * np.pi / 4)
    # Frogs: low croaks in short runs.
    for _ in range(16):
        at = rng.uniform(0, total - 1)
        f = rng.uniform(140, 260)
        for k in range(int(rng.integers(1, 4))):
            tt = seconds(.14)
            croak = saw(f * tt * (1 + .1 * tt / .14)) * (np.sin(2 * np.pi * 28 * tt) > 0) * np.sin(np.pi * tt / .14)
            place(mix, at + k * .22, bandpass(croak, 250, 1400), .1, rng.uniform(-.7, .7))
    return loop_crossfade(mix, 1.5)


def ambience_rain(length=20.0):
    total = length + 1.5
    n = int(total * SR)
    mix = np.zeros((n, 2))
    for channel in range(2):
        hiss = bandpass(noise(n), 900, 7500) * .12
        body = lowpass(pink(n), 260) * .2
        mix[:, channel] += hiss + body
    for _ in range(900):
        at = rng.uniform(0, total - .05)
        tt = seconds(.02)
        drop = bandpass(noise(len(tt)), 2500, 8000) * np.exp(-tt * 300)
        place(mix, at, drop, rng.uniform(.05, .2), rng.uniform(-1, 1))
    return loop_crossfade(mix, 1.5)


# --------------------------------------------------------------------------------------------- effects
def effect(length):
    return np.zeros(int(length * SR))


def sfx_hit():
    t = seconds(.35)
    thump = np.sin(2 * np.pi * np.cumsum(140 * (1 - .5 * np.minimum(1, t / .08))) / SR) * np.exp(-t * 22)
    smack = bandpass(noise(len(t)), 900, 4200) * np.exp(-t * 70)
    ring = sum(np.sin(2 * np.pi * f * t) for f in (2350, 3710, 5120)) * np.exp(-t * 26) * .12
    return thump * .9 + smack * .8 + ring


def sfx_swing():
    t = seconds(.3)
    y = np.zeros_like(t)
    for k in range(6):
        lo = 500 + k * 350
        y += bandpass(noise(len(t)), lo, lo * 1.6) * np.exp(-((t - .05 - k * .025) / .05) ** 2)
    return y * .9


def sfx_magic():
    out = effect(1.6)
    for k, note in enumerate([74, 77, 81, 86, 89]):
        place_mono(out, k * .07, metallophone(midi(note), 1.4, bright=.9, ombak=6), .5)
    t = seconds(1.6)
    out += bandpass(noise(len(t)), 3000, 9000) * np.sin(np.pi * np.minimum(1, t / 1.2)) * .12
    return out


def sfx_lightning():
    t = seconds(2.2)
    crack = highpass(noise(len(t)), 2000) * np.exp(-t * 60) * 1.4
    crackle = np.zeros_like(t)
    for _ in range(60):
        i = int(rng.uniform(0, .5) * SR)
        crackle[i:i + 40] += rng.uniform(-1, 1) * np.exp(-np.arange(min(40, len(t) - i)) / 8)
    crackle = highpass(crackle, 1500) * .9
    rumble = lowpass(noise(len(t)), 120, 4) * np.exp(-t * 1.8) * 6
    return crack + crackle + rumble


def sfx_shockwave():
    t = seconds(1.6)
    boom = np.sin(2 * np.pi * np.cumsum(70 * (1 - .45 * np.minimum(1, t / .6))) / SR) * np.exp(-t * 3.5)
    blast = lowpass(noise(len(t)), 500) * np.exp(-t * 6) * 1.4
    debris = bandpass(noise(len(t)), 1200, 5000) * (np.sin(2 * np.pi * 13 * t) > .4) * np.exp(-t * 4) * .3
    return boom * 1.2 + blast + debris


def sfx_wave():
    t = seconds(1.8)
    swell = np.sin(np.pi * np.minimum(1, t / 1.5)) ** 2
    surge = bandpass(noise(len(t)), 250, 1600) * swell * 1.3
    spray = highpass(noise(len(t)), 3500) * np.clip((t - .6) / .4, 0, 1) * np.exp(-np.clip(t - 1.0, 0, None) * 4) * .4
    return surge + spray


def sfx_death():
    t = seconds(.9)
    thud = np.sin(2 * np.pi * 75 * t) * np.exp(-t * 12) + lowpass(noise(len(t)), 300) * np.exp(-t * 20) * .7
    exhale = bandpass(noise(len(t)), 300, 1200) * np.exp(-((t - .3) / .2) ** 2) * .5
    return thud + exhale


def growl_voice(f0, length, vowel, rough=30):
    t = seconds(length)
    jitter = 1 + .06 * np.sin(2 * np.pi * 3 * t) + .03 * lowpass(noise(len(t)), 20) * 3
    source = saw(np.cumsum(f0 * jitter) / SR) * (1 + .5 * np.sin(2 * np.pi * rough * t))
    formants = {"o": [(400, 1), (800, .5)], "a": [(650, 1), (1100, .6)], "u": [(300, 1), (700, .4)]}[vowel]
    y = sum(bandpass(source, f * .8, f * 1.25) * a for f, a in formants)
    return y * np.sin(np.pi * np.minimum(1, t / length)) ** .6 * 1.6


def sfx_growl():
    return growl_voice(78, .8, "o") + lowpass(noise(int(.8 * SR)), 400) * np.sin(np.pi * seconds(.8) / .8) * .3


def sfx_boar():
    out = effect(.9)
    for k in range(3):
        place_mono(out, k * .12, growl_voice(110, .1, "u", rough=45), .8)
    t = seconds(.35)
    squeal = np.sin(2 * np.pi * np.cumsum(1100 + 700 * np.sin(np.pi * t / .35)) / SR) * np.sin(np.pi * t / .35) * .35
    place_mono(out, .4, bandpass(squeal + .3 * saw(np.cumsum(1100 + 700 * np.sin(np.pi * t / .35)) / SR) * np.sin(np.pi * t / .35), 700, 4000), 1)
    return out


def sfx_monkey():
    out = effect(1.0)
    for k in range(5):
        d = rng.uniform(.08, .14)
        t = seconds(d)
        f = rng.uniform(600, 1100) * (1 + .4 * np.sin(np.pi * t / d))
        call = saw(np.cumsum(f) / SR) * np.sin(np.pi * t / d)
        place_mono(out, k * .15, bandpass(call, 600, 3200), .6)
    return out


def sfx_chicken():
    out = effect(.8)
    for k, (f, d) in enumerate([(520, .06), (560, .06), (700, .16)]):
        t = seconds(d)
        call = saw(np.cumsum(f * (1 + .3 * t / d)) / SR) + bandpass(noise(len(t)), 800, 3000) * .3
        place_mono(out, k * .12, bandpass(call, 500, 2800) * np.sin(np.pi * t / d), .7)
    return out


def sfx_buffalo():
    t = seconds(1.6)
    f = 105 * (1 - .12 * t / 1.6)
    source = saw(np.cumsum(f) / SR) + .5 * saw(np.cumsum(f * 2.01) / SR)
    vowel = np.clip(t / 1.2, 0, 1)
    y = bandpass(source, 250, 600) * (1 - vowel * .5) + bandpass(source, 400, 900) * vowel * .6
    return y * np.sin(np.pi * t / 1.6) ** .7 * 1.4


def sfx_coin():
    out = effect(.5)
    for k, f in enumerate([2650, 3520]):
        t = seconds(.4)
        ping = sum(np.sin(2 * np.pi * f * r * t) * a for r, a in [(1, 1), (2.4, .3), (3.9, .15)]) * np.exp(-t * 11)
        place_mono(out, k * .07, ping, .5)
    return out


def sfx_loot():
    out = effect(1.4)
    for k, note in enumerate([74, 78, 81, 86, 90]):
        place_mono(out, k * .09, metallophone(midi(note), 1.2, bright=.7, ombak=5), .45)
    return out


def sfx_camp():
    out = effect(2.6)
    place_mono(out, 0, gong(98, 2.6), .9)
    for k, note in enumerate([69, 74, 78, 81]):
        place_mono(out, .15 + k * .1, metallophone(midi(note), 1.8, bright=.6), .4)
    place_mono(out, .1, choir(midi(62), 1.2, "a", release=1)[: int(2.4 * SR)], .25)
    return out


def sfx_levelup():
    out = effect(1.8)
    for k, note in enumerate([62, 66, 69, 74, 78]):
        place_mono(out, k * .1, metallophone(midi(note + 12), 1.4, bright=.8), .4)
        place_mono(out, k * .1, brass(midi(note), .12, release=.2), .18)
    place_mono(out, .5, brass(midi(74), .7, release=.6), .2)
    return out


def sfx_quest():
    out = effect(2.2)
    for k, (note, d) in enumerate([(62, .18), (69, .18), (74, .9)]):
        place_mono(out, k * .22, brass(midi(note), d, release=.5), .35)
    place_mono(out, .44, metallophone(midi(86), 1.6, bright=.6), .4)
    place_mono(out, .44, gong(110, 1.7), .4)
    return out


def sfx_build():
    out = effect(.45)
    for k in range(2):
        t = seconds(.18)
        knock = bandpass(noise(len(t)), 300, 2400) * np.exp(-t * 60) + np.sin(2 * np.pi * 410 * t) * np.exp(-t * 35) * .6
        place_mono(out, k * .2, knock, .9 - k * .25)
    return out


def sfx_mine():
    t = seconds(.45)
    clink = sum(np.sin(2 * np.pi * f * t) for f in (3150, 4480, 6020)) * np.exp(-t * 30) * .3
    gravel = bandpass(noise(len(t)), 800, 4000) * (np.sin(2 * np.pi * 37 * t) > .5) * np.exp(-t * 9) * .35
    return clink + gravel + lowpass(noise(len(t)), 400) * np.exp(-t * 40) * .5


def sfx_click():
    t = seconds(.06)
    return (bandpass(noise(len(t)), 1800, 5000) * np.exp(-t * 300) + np.sin(2 * np.pi * 1600 * t) * np.exp(-t * 120) * .3) * .8


def sfx_horn():
    t = seconds(2.8)
    f = midi(38) * (1 + .03 * np.minimum(1, t / .3) - .03)
    phase = np.cumsum(f) / SR
    tone = saw(phase) + .6 * saw(phase * 1.004) + .3 * np.sign(np.sin(2 * np.pi * phase))
    growl = 1 + .25 * np.sin(2 * np.pi * 27 * t) * np.exp(-t * 1.5)
    y = lowpass(tone * growl, 900) * np.sin(np.pi * np.minimum(1, t / 2.8)) ** .5
    return y * .8


def sfx_gong():
    return gong(55, 6.5)


def sfx_thunder():
    t = seconds(4.5)
    crack = highpass(noise(len(t)), 1500) * np.exp(-t * 25) * .8
    roll = lowpass(noise(len(t)), 160, 4) * (np.exp(-t * .9) * (1 + .6 * np.sin(2 * np.pi * 1.3 * t))) * 7
    return crack + roll


def place_mono(out, at, sound, gain):
    start = int(at * SR)
    end = min(len(out), start + len(sound))
    out[start:end] += sound[: end - start] * gain


EFFECTS = {
    "hit": (sfx_hit, .25), "swing": (sfx_swing, 0), "magic": (sfx_magic, .35), "lightning": (sfx_lightning, .2), "shockwave": (sfx_shockwave, .2),
    "wave": (sfx_wave, .2), "death": (sfx_death, .15), "growl": (sfx_growl, .15), "boar": (sfx_boar, .1), "monkey": (sfx_monkey, .15),
    "chicken": (sfx_chicken, .1), "buffalo": (sfx_buffalo, .15), "coin": (sfx_coin, .2), "loot": (sfx_loot, .35), "camp": (sfx_camp, .35),
    "levelup": (sfx_levelup, .35), "quest": (sfx_quest, .35), "build": (sfx_build, .1), "mine": (sfx_mine, .15), "click": (sfx_click, 0),
    "horn": (sfx_horn, .35), "gong": (sfx_gong, .3), "thunder": (sfx_thunder, .25),
}


# --------------------------------------------------------------------------------------------- output
def encode(name, data, quality, folder, keep):
    """Writes 16-bit WAV, then Ogg Vorbis through ffmpeg."""
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise SystemExit("ffmpeg (with libvorbis) is required on PATH.")
    data = np.clip(data, -1, 1)
    wav = folder / f"{name}.wav"
    wavfile.write(wav, SR, (data * 32767).astype(np.int16))
    target = OUTPUT / f"{name}.ogg"
    subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-i", str(wav), "-c:a", "libvorbis", "-q:a", str(quality), str(target)], check=True)
    # AAC beside it for WebKit on macOS, which does not reliably decode Ogg Vorbis.
    subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-i", str(wav), "-c:a", "libvo_aacenc", "-b:a", f"{64 + quality * 16}k", str(target.with_suffix(".m4a"))], check=True)
    if not keep:
        wav.unlink()
    seconds_long = len(data) / SR
    print(f"{name:18s} {seconds_long:6.1f} s  {target.stat().st_size / 1024:7.0f} KB  peak {np.abs(data).max():.2f}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--only", default="")
    parser.add_argument("--keep-wav", action="store_true")
    args = parser.parse_args()
    only = set(filter(None, args.only.split(",")))
    want = lambda name: not only or name in only
    OUTPUT.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix="nusantara-audio-"))
    hall = impulse(3.2, 2.6, seed=11)
    room = impulse(1.4, .9, bright=1.2, seed=12)
    if want("music-explore") or want("music-battle"):
        calm, war, length = exploration()
        n = int(length * SR)
        calm = fold(reverb(calm, hall, .32), n)
        war = fold(reverb(war, hall, .26), n)
        level = 10 ** (-17 / 20) / np.sqrt((calm ** 2).mean())
        encode("music-explore", np.tanh(calm * level * 1.1) * .95, 5, work, args.keep_wav)
        encode("music-battle", np.tanh(war * level * 1.25) * .95, 5, work, args.keep_wav)
    if want("music-menu"):
        mix, length = title_theme()
        mix = fold(reverb(mix, hall, .4), int(length * SR))
        level = 10 ** (-18 / 20) / np.sqrt((mix ** 2).mean())
        encode("music-menu", np.tanh(mix * level * 1.1) * .95, 5, work, args.keep_wav)
    for name, victory in (("victory", True), ("defeat", False)):
        if want(name):
            mix = reverb(stinger(victory), hall, .38)
            encode(name, np.tanh(mix / np.abs(mix).max() * 1.4) * .8, 5, work, args.keep_wav)
    for name, builder in (("ambience-day", ambience_day), ("ambience-night", ambience_night), ("ambience-rain", ambience_rain)):
        if want(name):
            mix = builder()
            encode(name, mix / np.abs(mix).max() * .7, 3, work, args.keep_wav)
    for name, (builder, wet) in EFFECTS.items():
        if not want(name):
            continue
        dry = builder()
        tail = int(.6 * SR) if wet else 0
        stereo = np.repeat(np.concatenate([dry, np.zeros(tail)])[:, None], 2, axis=1)
        if wet:
            stereo = reverb(stereo, room if name not in ("gong", "thunder", "horn", "camp") else hall, wet)
        mono = stereo.mean(axis=1)
        # Trim the silent end, keep a short fade.
        loud = np.nonzero(np.abs(mono) > np.abs(mono).max() * .002)[0]
        mono = mono[: (loud[-1] + 1) if len(loud) else len(mono)]
        mono *= np.clip((len(mono) - np.arange(len(mono))) / (.02 * SR), 0, 1)
        encode(name, mono / np.abs(mono).max() * .8, 4, work, args.keep_wav)
    if not args.keep_wav:
        shutil.rmtree(work, ignore_errors=True)
    else:
        print(f"WAV files kept in {work}")


if __name__ == "__main__":
    main()
