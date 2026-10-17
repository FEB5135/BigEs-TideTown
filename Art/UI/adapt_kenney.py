# Kenney UI Pack 2.0'ın (CC0) gri parçalarını oyunun paletine göre yeniden renklendirir.
# Gri sprite'ın her pikselinin parlaklığı, renk rampasındaki bir renge eşlenir:
# en koyu (alt kalınlık) -> kenar -> yüz -> en açık (iç parlama çizgisi).
# Çalıştırma: python Art/UI/adapt_kenney.py <kenney_ui-pack klasörü>
import os
import sys
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "UI", "Menu", "Kenney")


def hex_rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


# Rampalar: (gri parlaklık, renk). Gri butonlarda 102 = alt kalınlık, 152 = kenar, 218 = yüz, 255 = iç çizgi.
RED = [(102, "7E2420"), (152, "A8322E"), (218, "E8524A"), (255, "F58A80")]
WOOD = [(102, "3F2915"), (152, "5E3F24"), (218, "9A6E43"), (255, "C4935F")]
CREAM = [(102, "A88D63"), (152, "C9AF84"), (218, "FFF4DC"), (255, "FFFFFF")]
# Kaydırıcı yolu ve dolgusu kendi gri tonlarına göre.
TRACK = [(150, "B89D73"), (190, "C9AF84"), (250, "E9D9B8")]
FILL = [(150, "5E3F24"), (180, "7A5434"), (215, "9A6E43")]
HANDLE = [(150, "A8322E"), (190, "E8524A"), (250, "F58A80")]


def ramp(stops, lum):
    stops = [(l, hex_rgb(c)) for l, c in stops]
    if lum <= stops[0][0]:
        return stops[0][1]
    for (l0, c0), (l1, c1) in zip(stops, stops[1:]):
        if lum <= l1:
            t = (lum - l0) / (l1 - l0)
            return tuple(round(a + (b - a) * t) for a, b in zip(c0, c1))
    return stops[-1][1]


def recolor(src, dst, stops):
    img = Image.open(src).convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            px[x, y] = ramp(stops, lum) + (a,)
    img.save(os.path.join(OUT, dst))


JOBS = [
    ("Grey/Default/button_rectangle_depth_flat.png", "ButtonRed.png", RED),
    ("Grey/Default/button_rectangle_flat.png", "ButtonRedPressed.png", RED),
    ("Grey/Default/button_rectangle_depth_flat.png", "ButtonWood.png", WOOD),
    ("Grey/Default/button_rectangle_flat.png", "ButtonWoodPressed.png", WOOD),
    ("Grey/Default/button_square_depth_flat.png", "SquareWood.png", WOOD),
    ("Grey/Default/button_square_flat.png", "SquareWoodPressed.png", WOOD),
    ("Grey/Default/button_rectangle_depth_flat.png", "PanelCream.png", CREAM),
    ("Grey/Default/slide_horizontal_grey.png", "SliderTrack.png", TRACK),
    ("Grey/Default/slide_horizontal_color.png", "SliderFill.png", FILL),
    ("Grey/Default/slide_hangle.png", "SliderHandle.png", HANDLE),
    ("Extra/Default/divider.png", "Divider.png", [(152, "C9AF84"), (255, "E9D9B8")]),
]

if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit("Kullanım: python Art/UI/adapt_kenney.py <kenney_ui-pack klasörü>")
    png = os.path.join(sys.argv[1], "PNG")
    os.makedirs(OUT, exist_ok=True)
    for src, dst, stops in JOBS:
        recolor(os.path.join(png, src), dst, stops)
        print(dst)
