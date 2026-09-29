"""Generate the original, license-free pet foley SFX used by VRtest.

This script only rebuilds paw steps and body-settle foley. Dog vocal sounds are
cut from real CC0 recordings by Assets/Editor/PetAudioRecordingProcessor.cs so
running this generator cannot accidentally replace them with synthetic tones.
"""

from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np


SAMPLE_RATE = 44_100
ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets" / "Audio" / "PetAudio" / "Resources" / "PetAudio"


def one_pole_lowpass(signal: np.ndarray, cutoff: float) -> np.ndarray:
    alpha = 1.0 - math.exp(-2.0 * math.pi * cutoff / SAMPLE_RATE)
    result = np.empty_like(signal)
    previous = 0.0
    for index, value in enumerate(signal):
        previous += alpha * (float(value) - previous)
        result[index] = previous
    return result


def highpass(signal: np.ndarray, cutoff: float) -> np.ndarray:
    return signal - one_pole_lowpass(signal, cutoff)


def bandpass(signal: np.ndarray, low: float, high: float) -> np.ndarray:
    return highpass(one_pole_lowpass(signal, high), low)


def time_axis(duration: float) -> np.ndarray:
    return np.arange(max(1, int(duration * SAMPLE_RATE)), dtype=np.float64) / SAMPLE_RATE


def attack_decay(t: np.ndarray, attack: float, decay: float) -> np.ndarray:
    attack_part = 1.0 - np.exp(-t / max(0.0001, attack))
    return attack_part * np.exp(-t / max(0.0001, decay))


def pulse(t: np.ndarray, start: float, attack: float, decay: float) -> np.ndarray:
    local = np.maximum(0.0, t - start)
    envelope = attack_decay(local, attack, decay)
    envelope[t < start] = 0.0
    return envelope


def smooth_fade(signal: np.ndarray, fade_seconds: float = 0.012) -> np.ndarray:
    count = min(len(signal) // 2, int(fade_seconds * SAMPLE_RATE))
    if count <= 1:
        return signal
    ramp = np.linspace(0.0, 1.0, count, endpoint=True)
    signal[:count] *= ramp
    signal[-count:] *= ramp[::-1]
    return signal


def normalize(signal: np.ndarray, peak: float = 0.82) -> np.ndarray:
    signal = np.nan_to_num(signal, copy=False)
    maximum = float(np.max(np.abs(signal))) if len(signal) else 0.0
    if maximum > 1e-8:
        signal = signal * (peak / maximum)
    return np.clip(signal, -1.0, 1.0)


def write_wav(name: str, signal: np.ndarray, peak: float = 0.82) -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    samples = (normalize(smooth_fade(signal), peak) * 32767.0).astype("<i2")
    with wave.open(str(OUTPUT / f"{name}.wav"), "wb") as target:
        target.setnchannels(1)
        target.setsampwidth(2)
        target.setframerate(SAMPLE_RATE)
        target.writeframes(samples.tobytes())


def phase_from_frequency(frequency: np.ndarray) -> np.ndarray:
    return np.cumsum(2.0 * math.pi * frequency / SAMPLE_RATE)


def make_paw_step(seed: int, hard: bool) -> np.ndarray:
    rng = np.random.default_rng(seed)
    duration = rng.uniform(0.115, 0.155)
    t = time_axis(duration)
    base_frequency = rng.uniform(64.0, 92.0) if hard else rng.uniform(52.0, 76.0)
    thump = np.sin(phase_from_frequency(base_frequency * np.exp(-3.2 * t)))
    thump *= attack_decay(t, 0.0025, rng.uniform(0.032, 0.052))

    noise = rng.normal(0.0, 1.0, len(t))
    texture = bandpass(noise, 170.0 if hard else 90.0, 2600.0 if hard else 1250.0)
    texture *= attack_decay(t, 0.0015, rng.uniform(0.022, 0.044))

    if hard:
        click_frequency = rng.uniform(1750.0, 2450.0)
        click = np.sin(2.0 * math.pi * click_frequency * t + rng.uniform(0.0, math.tau))
        click *= attack_decay(t, 0.0006, rng.uniform(0.009, 0.015))
        return 0.64 * thump + 0.32 * texture + 0.16 * click

    pad = one_pole_lowpass(rng.normal(0.0, 1.0, len(t)), 420.0)
    pad *= attack_decay(t, 0.003, rng.uniform(0.04, 0.06))
    return 0.62 * thump + 0.23 * texture + 0.22 * pad


def make_pant(seed: int, exhale: bool) -> np.ndarray:
    rng = np.random.default_rng(seed)
    duration = rng.uniform(0.29, 0.39) if exhale else rng.uniform(0.22, 0.31)
    t = time_axis(duration)
    noise = rng.normal(0.0, 1.0, len(t))
    breath = bandpass(noise, 340.0 if exhale else 520.0, 5200.0)
    breath = 0.78 * breath + 0.22 * one_pole_lowpass(np.abs(breath), 1200.0)

    attack = rng.uniform(0.025, 0.045) if exhale else rng.uniform(0.018, 0.035)
    decay = rng.uniform(0.15, 0.22) if exhale else rng.uniform(0.10, 0.16)
    envelope = attack_decay(t, attack, decay)
    end_shape = np.clip((duration - t) / 0.055, 0.0, 1.0)
    envelope *= end_shape

    formant_frequency = rng.uniform(620.0, 810.0) if exhale else rng.uniform(820.0, 1120.0)
    formant = np.sin(2.0 * math.pi * formant_frequency * t + rng.uniform(0.0, math.tau))
    formant *= envelope * (0.09 if exhale else 0.045)
    return 0.74 * breath * envelope + formant


def make_bark(seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    duration = rng.uniform(0.42, 0.56)
    t = time_axis(duration)

    f0_start = rng.uniform(185.0, 235.0)
    f0_end = rng.uniform(92.0, 128.0)
    sweep = f0_end + (f0_start - f0_end) * np.exp(-t * rng.uniform(8.0, 11.0))
    sweep *= 1.0 + 0.025 * np.sin(2.0 * math.pi * rng.uniform(18.0, 25.0) * t)
    phase = phase_from_frequency(sweep)

    voice = np.zeros_like(t)
    for harmonic in range(1, 7):
        voice += (1.0 / harmonic ** 0.78) * np.sin(
            harmonic * phase + rng.uniform(0.0, math.tau)
        )

    first = pulse(t, 0.0, 0.006, rng.uniform(0.105, 0.14))
    second_start = rng.uniform(0.105, 0.145)
    second = pulse(t, second_start, 0.012, rng.uniform(0.12, 0.17))
    envelope = first + rng.uniform(0.45, 0.7) * second
    envelope *= np.clip((duration - t) / 0.075, 0.0, 1.0)

    noise = bandpass(rng.normal(0.0, 1.0, len(t)), 190.0, 3600.0)
    growl = one_pole_lowpass(rng.normal(0.0, 1.0, len(t)), 330.0)
    chest = np.sin(phase * 0.5) * attack_decay(t, 0.004, 0.16)

    signal = 0.54 * voice * envelope
    signal += 0.35 * noise * envelope
    signal += 0.19 * growl * envelope
    signal += 0.22 * chest
    return np.tanh(signal * 1.35)


def make_happy_whine(seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    duration = rng.uniform(0.68, 0.95)
    t = time_axis(duration)
    start = rng.uniform(430.0, 540.0)
    peak = rng.uniform(690.0, 880.0)
    contour = np.sin(np.clip(t / duration, 0.0, 1.0) * math.pi) ** 0.8
    frequency = start + (peak - start) * contour
    frequency *= 1.0 + 0.018 * np.sin(2.0 * math.pi * rng.uniform(5.2, 6.5) * t)
    phase = phase_from_frequency(frequency)
    tone = np.sin(phase) + 0.26 * np.sin(2.0 * phase + 0.3)
    envelope = attack_decay(t, 0.055, duration * 0.7)
    envelope *= np.clip((duration - t) / 0.16, 0.0, 1.0)
    breath = bandpass(rng.normal(0.0, 1.0, len(t)), 600.0, 4200.0)
    return 0.72 * tone * envelope + 0.12 * breath * envelope


def make_sniff(seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    duration = rng.uniform(0.28, 0.40)
    t = time_axis(duration)
    noise = bandpass(rng.normal(0.0, 1.0, len(t)), 650.0, 6500.0)
    first = pulse(t, 0.0, 0.008, 0.055)
    second = pulse(t, rng.uniform(0.105, 0.145), 0.008, 0.065)
    nasal = np.sin(2.0 * math.pi * rng.uniform(980.0, 1320.0) * t)
    return noise * (0.75 * first + 0.9 * second) + 0.06 * nasal * (first + second)


def make_body_settle(seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    duration = rng.uniform(0.28, 0.42)
    t = time_axis(duration)
    low = one_pole_lowpass(rng.normal(0.0, 1.0, len(t)), 240.0)
    rustle = bandpass(rng.normal(0.0, 1.0, len(t)), 180.0, 1700.0)
    thump_frequency = rng.uniform(44.0, 65.0)
    thump = np.sin(phase_from_frequency(thump_frequency * np.exp(-2.5 * t)))
    first = attack_decay(t, 0.004, 0.09)
    second = pulse(t, rng.uniform(0.09, 0.14), 0.012, 0.11)
    return 0.42 * low * (first + 0.45 * second) + 0.25 * rustle * second + 0.55 * thump * first


def main() -> None:
    for index in range(6):
        write_wav(f"Paw_Soft_{index + 1:02d}", make_paw_step(100 + index, False), 0.70)
        write_wav(f"Paw_Hard_{index + 1:02d}", make_paw_step(200 + index, True), 0.72)

    for index in range(3):
        write_wav(f"BodySettle_{index + 1:02d}", make_body_settle(800 + index), 0.68)

    generated = sorted(OUTPUT.glob("*.wav"))
    print(f"Generated {len(generated)} WAV files in {OUTPUT}")
    for path in generated:
        print(path.relative_to(ROOT))


if __name__ == "__main__":
    main()
