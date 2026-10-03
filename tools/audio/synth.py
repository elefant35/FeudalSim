#!/usr/bin/env python3
"""Procedural SFX (32 §8, §12): deterministic numpy synthesis → 48 kHz 16-bit WAV, peak-normalized to -1 dBFS.

Usage: python3 tools/audio/synth.py <recipe> <out.wav> [seed]
Recipes: knap_flake, fire_crackle, ui_click. Same recipe + seed → identical bytes.
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


RECIPES = {"knap_flake": knap_flake, "fire_crackle": fire_crackle, "ui_click": ui_click}


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
