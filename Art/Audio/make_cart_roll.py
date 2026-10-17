# Maden vagonunun ray üstünde yuvarlanma sesi (döngülü). Alçak uğultu + tekerlek takırtısı + ray eki tıkırtıları.
# Çalıştırma: python Art/Audio/make_cart_roll.py  ->  Assets/Audio/SFX/CartRoll.wav
import os
import wave
import numpy as np

RATE = 44100
LENGTH = 2.0  # saniye; tıkırtılar bu süreye tam bölünür, döngü dikişsiz
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Audio", "SFX", "CartRoll.wav")
rng = np.random.default_rng(7)
n = int(RATE * LENGTH)
t = np.arange(n) / RATE


def lowpass(x, cutoff):
    # Tek kutuplu alçak geçiren, dairesel (döngü başı ve sonu birbirine bağlı) uygulanır.
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.zeros_like(x)
    prev = 0.0
    for _ in range(2):
        for i in range(len(x)):
            prev = (1 - a) * x[i] + a * prev
            y[i] = prev
    return y


# Uğultu: alçak geçirilmiş gürültü + tekerlek dönüşüyle hafif dalgalanma.
rumble = lowpass(rng.standard_normal(n), 120)
rumble /= np.abs(rumble).max()
rumble *= 0.55 + 0.15 * np.sin(2 * np.pi * 4 / LENGTH * t)

# Tekerlek takırtısı: daha tiz, kısık gürültü.
rattle = lowpass(rng.standard_normal(n), 900) - lowpass(rng.standard_normal(n), 300)
rattle /= np.abs(rattle).max()
rattle *= 0.12

# Ray ekleri: her 0.5 sn'de iki tekerlek takımı (ön, arka) için kısa metalik "tak-tak".
clacks = np.zeros(n)
for start in np.arange(0, LENGTH, 0.5):
    for offset, gain in ((0.0, 1.0), (0.09, 0.8)):
        i0 = int((start + offset) * RATE) % n
        m = int(0.08 * RATE)
        k = np.arange(m) / RATE
        hit = (rng.standard_normal(m) * 0.6 + np.sin(2 * np.pi * 620 * k) * 0.5 + np.sin(2 * np.pi * 1340 * k) * 0.25)
        hit *= np.exp(-k * 55) * gain
        idx = (i0 + np.arange(m)) % n
        clacks[idx] += hit
clacks *= 0.5

mix = rumble * 0.6 + rattle + clacks
mix /= np.abs(mix).max() * 1.1
data = (mix * 32767).astype(np.int16)
with wave.open(OUT, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes(data.tobytes())
print("yazıldı:", OUT)
