"""A 16:9 "how to download and use Zoomies" video: the real GitHub pages, then real app clips, with numbered steps.

  python tools/reel/make_tutorial.py <readme.png> <release.png> [clips folder] [output file]

The two PNGs are 1440-wide screenshots of the repository page and the v0.1.0 release page, taken signed out, e.g.
  msedge --headless=new --user-data-dir=<empty folder> --window-size=1440,2400 --screenshot=readme.png <repo url>
"""
import os
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont

from make_reel import ACCENT, BG, BOLD, FFMPEG, GREY, LOGO, REGULAR, ROOT, WHITE, encode_args, lines_png, run

README, RELEASE = sys.argv[1], sys.argv[2]
CLIPS = sys.argv[3] if len(sys.argv) > 3 else os.path.join(ROOT, "demo", "reel-src")
OUT = sys.argv[4] if len(sys.argv) > 4 else os.path.join(ROOT, "demo", "Zoomies-how-to-install.mp4")
W, H = 1920, 1080
SEMIBOLD = r"C:\Windows\Fonts\seguisb.ttf"


def caption_bar(path, step, text):
    """A dark bar along the bottom: the step number in an accent circle, then the instruction."""
    bar = Image.new("RGBA", (W, 130), BG + (235,))
    draw = ImageDraw.Draw(bar)
    draw.ellipse((60, 25, 140, 105), fill=ACCENT)
    num = ImageFont.truetype(BOLD, 48)
    box = num.getbbox(step)
    draw.text((100 - (box[2] - box[0]) / 2 - box[0], 65 - (box[3] - box[1]) / 2 - box[1]), step, font=num, fill=BG)
    font = ImageFont.truetype(SEMIBOLD, 40)
    box = font.getbbox(text)
    draw.text((175 - box[0], 65 - (box[3] - box[1]) / 2 - box[1]), text, font=font, fill=WHITE)
    bar.save(path)


def page_shot(path, src, top, ring):
    """A 16:9 window of a 1440-wide page screenshot starting at `top`, with an accent ring around `ring`, at 4K."""
    img = Image.open(src).convert("RGB").crop((0, top, 1440, top + 810))
    draw = ImageDraw.Draw(img)
    x1, y1, x2, y2 = ring
    draw.rounded_rectangle((x1 - 10, y1 - top - 10, x2 + 10, y2 - top + 10), radius=14, outline=ACCENT, width=5)
    img.resize((3840, 2160), Image.LANCZOS).save(path)


def card(path, title, lines, step=None):
    """A full-screen card: optional step number, a big title, then smaller lines (text, colour)."""
    img = Image.new("RGB", (W, H), BG)
    y = 430
    if step:
        draw = ImageDraw.Draw(img)
        draw.ellipse((W / 2 - 55, y - 120, W / 2 + 55, y - 10), fill=ACCENT)
        num = ImageFont.truetype(BOLD, 64)
        box = num.getbbox(step)
        draw.text((W / 2 - (box[2] - box[0]) / 2 - box[0], y - 65 - (box[3] - box[1]) / 2 - box[1]), step, font=num, fill=BG)
    else:
        logo = Image.open(LOGO).convert("RGBA").resize((200, 200), Image.LANCZOS)
        img.paste(logo, ((W - 200) // 2, y - 230), logo)
    tmp = path + ".text.png"
    lines_png(tmp, W, [(title, BOLD, 80, WHITE)] + [(t, f, s, c) for (t, f, s, c) in lines], gap=34)
    text = Image.open(tmp)
    img.paste(text, (0, y + 20), text)
    img.save(path)


def still(out, image, seconds, bar=None, zoom=None):
    """A video from one image: optionally a slow zoom towards (x, y) as fractions, and a caption bar."""
    frames = int(seconds * 30)
    inputs = ["-i", image]
    if zoom:
        (zx, zy, zmax) = zoom
        rate = (zmax - 1) / frames
        graph = (f"[0:v]zoompan=z='min(1+on*{rate:.5f},{zmax})':d={frames}:s={W}x{H}:fps=30:"
                 f"x='max(0,min(iw-iw/zoom,iw*{zx}-iw/zoom/2))':y='max(0,min(ih-ih/zoom,ih*{zy}-ih/zoom/2))'[v]")
    else:
        inputs = ["-loop", "1", "-t", str(seconds)] + inputs
        graph = f"[0:v]scale={W}:{H},fps=30[v]"
    last = "v"
    if bar:
        inputs += ["-loop", "1", "-t", str(seconds), "-i", bar]
        graph += f";[v][1:v]overlay=0:{H - 130}[b]"
        last = "b"
    audio = 2 if bar else 1  # the silent track comes after the image (and the bar)
    graph += f";[{last}]fade=t=in:st=0:d=0.25,format=yuv420p[out]"
    run([FFMPEG, "-hide_banner", *inputs, "-f", "lavfi", "-t", str(seconds), "-i", "anullsrc=r=44100:cl=stereo",
         "-filter_complex", graph, "-map", "[out]", "-map", f"{audio}:a", "-t", str(seconds), *encode_args(out)])


def clip(out, name, start, seconds, bar):
    """A real app clip at full size with a caption bar."""
    seek = ["-sseof", str(start)] if start < 0 else ["-ss", str(start)]
    run([FFMPEG, "-hide_banner", *seek, "-t", str(seconds), "-i", os.path.join(CLIPS, name + ".mp4"),
         "-loop", "1", "-t", str(seconds), "-i", bar,
         "-f", "lavfi", "-t", str(seconds), "-i", "anullsrc=r=44100:cl=stereo",
         "-filter_complex", f"[0:v]scale={W}:{H},setsar=1[v];[v][1:v]overlay=0:{H - 130},format=yuv420p[out]",
         "-map", "[out]", "-map", "2:a", "-t", str(seconds), *encode_args(out)])


def main():
    tmp = tempfile.mkdtemp(prefix="tutorial-")
    p = lambda name: os.path.join(tmp, name)
    parts = []

    card(p("title.png"), "How to install Zoomies",
         [("free  ·  open source  ·  Windows 10 & 11", BOLD, 40, ACCENT),
          ("2 minutes from download to your first jump", REGULAR, 36, GREY)])
    still(p("0.mp4"), p("title.png"), 3.0)
    parts.append(p("0.mp4"))

    # 1. The repository page: zoom in on the Download button.
    page_shot(p("readme.png"), README, 1000, (442, 1331, 686, 1359))
    caption_bar(p("bar1.png"), "1", "Open github.com/lakshmang007/zoomies and click Download")
    still(p("1.mp4"), p("readme.png"), 6.0, p("bar1.png"), zoom=(564 / 1440, 345 / 810, 1.8))
    parts.append(p("1.mp4"))

    # 1b. The same file from the Releases page.
    page_shot(p("release.png"), RELEASE, 170, (130, 710, 1310, 750))
    caption_bar(p("bar1b.png"), "1", "Or open Releases and download Zoomies-win-Setup.exe (69 MB)")
    still(p("1b.mp4"), p("release.png"), 5.0, p("bar1b.png"), zoom=(720 / 1440, 560 / 810, 1.3))
    parts.append(p("1b.mp4"))

    card(p("install.png"), "Run Zoomies-win-Setup.exe",
         [("No admin rights needed. It installs in seconds and opens Zoomies.", REGULAR, 40, WHITE),
          ("If Windows says \u201cWindows protected your PC\u201d:  More info  \u2192  Run anyway", BOLD, 40, ACCENT),
          ("The installer isn\u2019t code-signed yet. GitHub builds it from the public source.", REGULAR, 32, GREY)],
         step="2")
    still(p("2.mp4"), p("install.png"), 6.5)
    parts.append(p("2.mp4"))

    steps = [
        ("A-find-intern", 0.6, 5.0, "3", "Press Ctrl+Alt+Space anywhere, type a few letters, press Enter"),
        ("C-organize-all", -6.0, 6.0, "4", "Organize a messy folder: check the preview, then Organize now"),
        ("D-recycle-unused", -6.0, 6.0, "5", "Clean up files you haven\u2019t opened in months (Recycle Bin, not delete)"),
        ("H-undo", -3.5, 3.5, "6", "Changed your mind? History \u2192 Undo puts everything back"),
    ]
    for i, (name, start, seconds, step, text) in enumerate(steps):
        caption_bar(p(f"bar{step}.png"), step, text)
        clip(p(f"c{i}.mp4"), name, start, seconds, p(f"bar{step}.png"))
        parts.append(p(f"c{i}.mp4"))

    card(p("end.png"), "That\u2019s it.",
         [("Zoomies starts with Windows and updates itself.", REGULAR, 40, WHITE),
          ("Uninstall anytime: Settings \u2192 Apps \u2192 Installed apps", REGULAR, 36, GREY),
          ("Star it on GitHub:  github.com/lakshmang007/zoomies", BOLD, 40, ACCENT)])
    still(p("9.mp4"), p("end.png"), 4.5)
    parts.append(p("9.mp4"))

    listing = p("list.txt")
    with open(listing, "w", encoding="utf-8") as f:
        f.writelines(f"file '{x.replace(os.sep, '/')}'\n" for x in parts)
    run([FFMPEG, "-hide_banner", "-f", "concat", "-safe", "0", "-i", listing, "-c", "copy",
         "-movflags", "+faststart", "-y", OUT])
    print("wrote", OUT)


if __name__ == "__main__":
    main()
