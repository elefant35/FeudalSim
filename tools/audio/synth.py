#!/usr/bin/env python3
"""Procedural SFX (32 §8, §12): deterministic numpy synthesis → 48 kHz 16-bit WAV, peak-normalized to -1 dBFS.

Usage: python3 tools/audio/synth.py <recipe> <out.wav> [seed]
Recipes: knap_flake, fire_crackle, ui_click, ui_{open,close,confirm,unsay,glyph,error}, bark_<kind>_<low|high>
(kinds: affirm negate consider question scoff laugh sigh grunt surprise warm). Same recipe + seed → identical bytes.
"""
import sys, wave
import numpy as np

FS = 48_000


def _env(n, attack_s, decay_s):
    t = np.arange(n) / FS
    a = np.clip(t / max(attack_s, 1e-4), 0, 1)
    return a * np.exp(-t / decay_s)


def _highpass(x, alpha=0.97):
    y = np.empty_like(x)
    prev_x = prev_y = 0.0
    for i, xi in enumerate(x):
        prev_y = alpha * (prev_y + xi - prev_x)
        prev_x = xi
        y[i] = prev_y
    return y


def knap_flake(rng):
    """A sharp stone-on-stone snap: click transient + bright, fast-decaying noise + a short 'tick' ring."""
    n = int(0.35 * FS)
    noise = _highpass(rng.standard_normal(n), 0.92) * _env(n, 0.0005, 0.035)
    t = np.arange(n) / FS
    ring = np.sin(2 * np.pi * rng.uniform(2800, 3400) * t) * _env(n, 0.0002, 0.012) * 0.6
    click = np.zeros(n); click[:24] = np.hanning(24) * 1.5
    return noise + ring + click


def fire_crackle(rng):
    """Two seconds of low roar with random pops (loopable bed for a campfire)."""
    n = 2 * FS
    roar = np.cumsum(rng.standard_normal(n)); roar -= np.convolve(roar, np.ones(400) / 400, mode="same")
    roar = roar / (np.abs(roar).max() + 1e-9) * 0.25
    out = roar.copy()
    for _ in range(rng.integers(12, 20)):
        s = rng.integers(0, n - 2_400)
        pop = _highpass(rng.standard_normal(2_400), 0.9) * _env(2_400, 0.0003, 0.008) * rng.uniform(0.4, 1.0)
        out[s:s + 2_400] += pop
    fade = np.ones(n); f = int(0.05 * FS); fade[:f] = np.linspace(0, 1, f); fade[-f:] = np.linspace(1, 0, f)
    return out * fade


def ui_click(rng):
    """A soft parchment/quill tick for UI."""
    n = int(0.06 * FS)
    t = np.arange(n) / FS
    return np.sin(2 * np.pi * 1_800 * t) * _env(n, 0.0005, 0.008) + _highpass(rng.standard_normal(n), 0.9) * _env(n, 0.0002, 0.004) * 0.3


# --- M1-21: dialogue UI sounds and vocal "bark" placeholders (32 §16; real voice sessions from M2) -------------------

def _tone(f, dur, attack=0.004, decay=0.12, rng=None):
    n = int(dur * FS)
    t = np.arange(n) / FS
    return np.sin(2 * np.pi * f * t) * _env(n, attack, decay)


def _resonator(x, freq, bw):
    """Two-pole resonator (a formant): y[n] = x[n] + 2r cos(w) y[n-1] - r^2 y[n-2]."""
    r = np.exp(-np.pi * bw / FS)
    c1, c2 = 2 * r * np.cos(2 * np.pi * freq / FS), -r * r
    y = np.zeros_like(x)
    y1 = y2 = 0.0
    for i, xi in enumerate(x):
        y0 = xi + c1 * y1 + c2 * y2
        y[i] = y0
        y2, y1 = y1, y0
    return y * (1 - r)


def _voice(rng, dur, f0, formants, f0_end=None, breath=0.05, onset_h=0.0):
    """A sung-vowel placeholder: a jittered glottal pulse train through formant resonators, plus breath noise."""
    n = int(dur * FS)
    f = np.linspace(f0, f0_end or f0, n) * (1 + 0.01 * np.convolve(rng.standard_normal(n), np.ones(800) / 800, mode="same"))
    phase = np.cumsum(f / FS)
    pulses = (phase % 1.0) ** 3 - 0.25   # a skewed sawtooth: bright like a glottal source
    src = pulses + breath * rng.standard_normal(n)
    if onset_h > 0:   # an aspirated onset ("h")
        h = int(onset_h * FS)
        src[:h] = rng.standard_normal(h) * 0.4 * np.linspace(1, 0.3, h)
    out = sum(_resonator(src, fq, bw) * g for fq, bw, g in formants)
    env = _env(n, 0.02, dur * 0.7)
    tail = int(0.04 * FS)
    env[-tail:] *= np.linspace(1, 0, tail)
    return out * env


NASAL = [(260, 60, 1.0), (2200, 200, 0.25), (3200, 300, 0.1)]       # "m"
VOWEL_A = [(730, 90, 1.0), (1090, 110, 0.6), (2440, 160, 0.2)]      # "ah"
VOWEL_UH = [(640, 90, 1.0), (1190, 110, 0.5), (2390, 160, 0.2)]     # "uh"
VOWEL_O = [(570, 80, 1.0), (840, 90, 0.6), (2410, 160, 0.15)]       # "oh"


def _gap(s):
    return np.zeros(int(s * FS))


def _f0(rng, voice):
    return (115 if voice == "low" else 215) * rng.uniform(0.95, 1.05)


def bark(kind, voice="low"):
    def make(rng):
        f0 = _f0(rng, voice)
        if kind == "affirm":    # "mm-hm"
            return np.concatenate([_voice(rng, 0.16, f0, NASAL), _gap(0.04), _voice(rng, 0.22, f0 * 1.2, NASAL, f0 * 1.25, onset_h=0.03)])
        if kind == "negate":    # "mm-mm"
            return np.concatenate([_voice(rng, 0.18, f0 * 1.15, NASAL, f0 * 1.1), _gap(0.05), _voice(rng, 0.2, f0 * 1.05, NASAL, f0 * 0.9)])
        if kind == "consider":  # "hmm…"
            return _voice(rng, 0.65, f0 * 1.05, NASAL, f0 * 0.92, onset_h=0.05)
        if kind == "question":  # "huh?"
            return _voice(rng, 0.32, f0, VOWEL_UH, f0 * 1.5, onset_h=0.06)
        if kind == "scoff":     # "hmph"
            return np.concatenate([_voice(rng, 0.12, f0 * 1.1, NASAL, f0 * 0.9, breath=0.2), _highpass(rng.standard_normal(int(0.08 * FS)), 0.9) * _env(int(0.08 * FS), 0.002, 0.03) * 0.2])
        if kind == "laugh":     # "ha-ha-ha"
            return np.concatenate([np.concatenate([_voice(rng, 0.11, f0 * (1.35 - 0.08 * i), VOWEL_A, onset_h=0.03), _gap(0.06)]) for i in range(4)])
        if kind == "sigh":      # a long out-breath with a little voice
            n = int(0.9 * FS)
            breath = _resonator(rng.standard_normal(n), 900, 700) * _env(n, 0.15, 0.5)
            return breath * 0.8 + _voice(rng, 0.9, f0 * 0.95, VOWEL_UH, f0 * 0.8, breath=0.3) * 0.25
        if kind == "grunt":     # effort "uh!"
            return _voice(rng, 0.2, f0 * 0.9, VOWEL_UH, f0 * 0.8, breath=0.25)
        if kind == "surprise":  # "oh!"
            return _voice(rng, 0.25, f0 * 1.3, VOWEL_O, f0 * 1.05)
        if kind == "warm":      # a pleased "mm!"
            return _voice(rng, 0.3, f0 * 1.05, NASAL, f0 * 1.3)
        raise KeyError(kind)
    make.__doc__ = f"bark {kind} ({voice} voice)"
    return make


def ui_open(rng):
    n = int(0.22 * FS)
    sweep = _resonator(rng.standard_normal(n), 1200, 900) * np.linspace(0.2, 1, n) * _env(n, 0.12, 0.08)
    return sweep * 0.6 + _tone(440, 0.22, 0.03, 0.12) * 0.4


def ui_close(rng):
    n = int(0.2 * FS)
    return _resonator(rng.standard_normal(n), 700, 700) * _env(n, 0.005, 0.07) * 0.6 + _tone(330, 0.2, 0.005, 0.08) * 0.4


def ui_confirm(rng):
    return np.concatenate([_tone(660, 0.09, decay=0.06), _tone(990, 0.22, decay=0.12)])


def ui_unsay(rng):
    n = int(0.18 * FS)
    t = np.arange(n) / FS
    f = np.linspace(620, 280, n)
    return np.sin(2 * np.pi * np.cumsum(f) / FS) * _env(n, 0.003, 0.08) + _highpass(rng.standard_normal(n), 0.9) * _env(n, 0.001, 0.02) * 0.15


def ui_glyph(rng):
    return _tone(1320, 0.5, 0.002, 0.25) + _tone(1980, 0.5, 0.002, 0.12) * 0.35


def ui_error(rng):
    return _tone(140, 0.25, 0.003, 0.09) + _highpass(rng.standard_normal(int(0.25 * FS)), 0.95) * _env(int(0.25 * FS), 0.001, 0.02) * 0.2


BARKS = ["affirm", "negate", "consider", "question", "scoff", "laugh", "sigh", "grunt", "surprise", "warm"]
RECIPES = {"knap_flake": knap_flake, "fire_crackle": fire_crackle, "ui_click": ui_click,
           "ui_open": ui_open, "ui_close": ui_close, "ui_confirm": ui_confirm, "ui_unsay": ui_unsay, "ui_glyph": ui_glyph, "ui_error": ui_error,
           **{f"bark_{k}_low": bark(k, "low") for k in BARKS}, **{f"bark_{k}_high": bark(k, "high") for k in BARKS}}


def write_wav(path, samples, peak_dbfs=-1.0):
    peak = np.abs(samples).max()
    scaled = samples / peak * (10 ** (peak_dbfs / 20)) if peak > 0 else samples
    pcm = np.clip(np.round(scaled * 32767), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(FS)
        w.writeframes(pcm.tobytes())


def main():
    recipe, out = sys.argv[1], sys.argv[2]
    seed = int(sys.argv[3]) if len(sys.argv) > 3 else 1
    rng = np.random.default_rng(seed)
    write_wav(out, RECIPES[recipe](rng))
    print(f"RESULT {{\"ok\": true, \"recipe\": \"{recipe}\", \"seed\": {seed}, \"out\": \"{out}\"}}")


if __name__ == "__main__":
    main()
