"""ITU-R BS.1770-4 / EBU R128 integrated loudness in pure numpy (32 §12). Used when ffmpeg is unavailable.

K-weighting = high-shelf (+4 dB above ~1.5 kHz) then high-pass (~38 Hz) at 48 kHz (BS.1770 Table 1/2),
mean square over 400 ms blocks with 75 % overlap, absolute gate -70 LUFS, relative gate -10 LU.
"""
import numpy as np

# BS.1770-4 coefficients for fs = 48 kHz.
SHELF = ([1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585])
HIGHPASS = ([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621])


def _biquad(x, b, a):
    y = np.empty_like(x)
    x1 = x2 = y1 = y2 = 0.0
    b0, b1, b2 = b
    _, a1, a2 = a
    for i, xi in enumerate(x):
        yi = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, xi, y1, yi
        y[i] = yi
    return y


def integrated_lufs(channels, fs=48_000):
    """channels: list of float arrays in [-1, 1]. Returns LUFS, or -inf for silence/too short."""
    if fs != 48_000:
        raise ValueError("loudness.py implements the 48 kHz filter coefficients only")
    weighted = [_biquad(_biquad(np.asarray(c, dtype=np.float64), *SHELF), *HIGHPASS) for c in channels]
    block, hop = int(0.400 * fs), int(0.100 * fs)
    n = len(weighted[0])
    if n < block:   # short SFX (< 400 ms): BS.1770 is undefined here (ffmpeg ebur128 reports -70);
                    # we report a single-block estimate over the whole sound so short SFX still get a number
        starts, block = [0], n
    else:
        starts = range(0, n - block + 1, hop)
    z = np.array([sum(np.mean(w[s:s + block] ** 2) for w in weighted) for s in starts])
    with np.errstate(divide="ignore"):
        lk = -0.691 + 10 * np.log10(z)
    gated = z[lk > -70]
    if gated.size == 0:
        return float("-inf")
    rel = -0.691 + 10 * np.log10(gated.mean()) - 10
    with np.errstate(divide="ignore"):
        final = gated[(-0.691 + 10 * np.log10(gated)) > rel]
    return float(-0.691 + 10 * np.log10(final.mean())) if final.size else float("-inf")
