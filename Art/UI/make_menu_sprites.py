# Ana menünün ikon, gölge ve perde sprite'larını üretir (beyaz, Unity'de renklendirilir). Butonlar: adapt_kenney.py. Çalıştırma: python Art/UI/make_menu_sprites.py
import math
import os
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "UI", "Menu")
SS = 4  # Kenar yumuşatma için büyük çizip küçültür.


def canvas(size):
    return Image.new("RGBA", (size * SS, size * SS), (255, 255, 255, 0))


def save(img, name, size):
    img = img.resize((size, size), Image.LANCZOS)
    img.save(os.path.join(OUT, name))


def shadow(name, size, radius, blur):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pad = blur * 2
    ImageDraw.Draw(img).rounded_rectangle((pad, pad, size - 1 - pad, size - 1 - pad), radius, fill=(0, 0, 0, 255))
    img = img.filter(ImageFilter.GaussianBlur(blur))
    white = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    white.putalpha(img.getchannel("A"))
    white.save(os.path.join(OUT, name))


def circle(name, size):
    img = canvas(size)
    ImageDraw.Draw(img).ellipse((0, 0, size * SS - 1, size * SS - 1), fill="white")
    save(img, name, size)


def vignette(name, size):
    img = Image.new("RGBA", (size, size))
    px = img.load()
    c = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            d = math.hypot((x - c) / c, (y - c) / c) / math.sqrt(2)
            a = max(0.0, min(1.0, (d - 0.25) / 0.75)) ** 1.6
            px[x, y] = (255, 255, 255, int(a * 255))
    img.save(os.path.join(OUT, name))


def icon_play(size=128):
    img = canvas(size)
    s = size * SS
    ImageDraw.Draw(img).polygon([(s * 0.28, s * 0.18), (s * 0.28, s * 0.82), (s * 0.84, s * 0.5)], fill="white")
    save(img, "IconPlay.png", size)


def icon_gear(size=128):
    img = canvas(size)
    s = size * SS
    d = ImageDraw.Draw(img)
    c = s / 2
    teeth, r_out, r_in = 8, s * 0.46, s * 0.34
    pts = []
    for i in range(teeth * 4):
        a = i / (teeth * 4) * math.tau
        r = r_out if i % 4 in (1, 2) else r_in
        pts.append((c + math.cos(a) * r, c + math.sin(a) * r))
    d.polygon(pts, fill="white")
    d.ellipse((c - s * 0.36, c - s * 0.36, c + s * 0.36, c + s * 0.36), fill="white")
    d.ellipse((c - s * 0.15, c - s * 0.15, c + s * 0.15, c + s * 0.15), fill=(255, 255, 255, 0))
    save(img, "IconGear.png", size)


def icon_trophy(size=128):
    img = canvas(size)
    s = size * SS
    d = ImageDraw.Draw(img)
    w = s * 0.06
    d.pieslice((s * 0.24, -s * 0.18, s * 0.76, s * 0.62), 0, 180, fill="white")  # Kupa
    d.rectangle((s * 0.24, s * 0.1, s * 0.76, s * 0.22), fill="white")
    d.arc((s * 0.06, s * 0.14, s * 0.36, s * 0.46), 90, 270, fill="white", width=int(w))  # Kulplar
    d.arc((s * 0.64, s * 0.14, s * 0.94, s * 0.46), 270, 90, fill="white", width=int(w))
    d.rectangle((s * 0.45, s * 0.58, s * 0.55, s * 0.76), fill="white")  # Sap
    d.rounded_rectangle((s * 0.28, s * 0.76, s * 0.72, s * 0.9), s * 0.03, fill="white")  # Kaide
    save(img, "IconTrophy.png", size)


def icon_exit(size=128):
    img = canvas(size)
    s = size * SS
    d = ImageDraw.Draw(img)
    w = int(s * 0.09)
    d.line([(s * 0.5, s * 0.14), (s * 0.18, s * 0.14), (s * 0.18, s * 0.86), (s * 0.5, s * 0.86)], fill="white", width=w, joint="curve")
    d.line([(s * 0.42, s * 0.5), (s * 0.82, s * 0.5)], fill="white", width=w)
    d.polygon([(s * 0.92, s * 0.5), (s * 0.7, s * 0.3), (s * 0.7, s * 0.7)], fill="white")
    save(img, "IconExit.png", size)


def waves(size=256):
    # Logonun altındaki dalga çizgileri.
    img = canvas(size)
    s = size * SS
    d = ImageDraw.Draw(img)
    for row in range(3):
        pts = []
        for i in range(801):
            t = i / 800
            pts.append((s * (0.06 + 0.88 * t), s * (0.3 + row * 0.2 + 0.05 * math.sin(t * math.tau * 2))))
        r = s * 0.035
        for x, y in pts:  # Yuvarlak uçlu kalın çizgi.
            d.ellipse((x - r, y - r, x + r, y + r), fill="white")
    save(img, "Waves.png", size)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    shadow("Shadow.png", 96, 22, 10)
    circle("Circle.png", 64)
    vignette("Vignette.png", 256)
    icon_play()
    icon_gear()
    icon_trophy()
    icon_exit()
    waves()
