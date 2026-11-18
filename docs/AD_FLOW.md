# Live-action ad: ChatGPT Go → Google Flow (AI Plus) → CapCut

A live-action version of the **"Found it."** ad, built on the structure of Base44's Big Game ad *"It's App to You"*
(https://youtu.be/kLdaIxDM-_Y). We copy the **structure and pacing only**: never Base44's lines, characters, music,
logo or tagline.

| | Base44 ad (55 s) | Our ad (~60 s) |
|---|---|---|
| Hook | Someone shows off something impossible | Riya finds a file in a 300-file Downloads in one second |
| Disbelief | Coworkers don't believe it, then test it | Mei and Leo don't believe it, then test it |
| Montage | Fast cuts: everyone builds something | Fast cuts: everyone fixes their own mess |
| Button | One last absurd joke, end card | "Can it find my motivation?" → *No matches.* → "It finds files, Sam." |

**Who does what**
- **ChatGPT Go** draws every *keyframe*: the exact first and last picture of each AI clip.
- **Google Flow** (Veo 3.1 Fast, *Frames to Video*) animates between two keyframes, with dialogue.
- **Real screen recordings** show the app. They're already made: `demo/inserts/` (Sand theme).
- **CapCut** joins it all, adds music and the end card.

### Why the video looks continuous

The last keyframe of one clip is the first keyframe of the next. Flow starts each clip on exactly the picture where the
previous one ended, so clips 1 → 2 → 3 play as one unbroken scene:

```text
Clip 1: K1 ──► K2
Clip 2:        K2 ──► K3
Clip 3:               K3 ──► K4     ← one continuous scene
"Then everyone wanted it."
Clip 4: K5 ──► K6                   ← one continuous camera move
Clip 5: K7 ──► K8
```

---

## Your budget

| | What you have | What this ad uses |
|---|---|---|
| Google AI Plus | About **200 Flow credits a month**. Veo 3.1 Fast ≈ 20 credits per clip; Quality ≈ 100, so skip it. | 5 clips ≈ 100 credits, leaving ≈ 100 for 5 retries |
| ChatGPT Go | OpenAI doesn't publish a fixed image limit. | 13 images. If you hit the limit, finish the next day. |

Flow shows your real balance next to your profile picture. **Check it before you start**: these numbers come from
third-party guides and change often.

---

## Ground rules

1. **No app UI in AI shots.** Laptop screens face away from the camera. Any moment on screen cuts to a real recording.
2. **No words in AI shots.** No captions, signs or logos. All text goes on in CapCut, so a rename later costs nothing.
3. **Same words every time.** Paste the *Style lock* and *Voices* exactly as written. Change one adjective and you
   change the actor.
4. **One output per prompt.** In Flow's settings, set *Outputs per prompt* to **1**. Each extra output costs credits too.

---

## Step 1 — ChatGPT Go: cast and set (5 images)

Do everything in **one ChatGPT chat**, so it remembers how everyone looks. Download each image and rename it as shown.

**1a. Character sheets.** Send this four times, swapping in each description:

```text
Photoreal character reference sheet for a TV commercial. Two views side by side on a plain light-grey studio
background: front view and three-quarter view, waist up, soft even studio light. No text, no logos, no watermark.
Character: <DESCRIPTION>
```

| Save as | Description |
|---|---|
| `riya.png` | Riya, a 21-year-old Indian woman with shoulder-length wavy black hair and small gold stud earrings, wearing a mustard-yellow cardigan over a plain white t-shirt. Calm, quietly confident, a little deadpan. |
| `sam.png` | Sam, a 22-year-old man with short curly black hair and light stubble, wearing a plain green hoodie. Expressive and easily panicked. |
| `mei.png` | Mei, a 21-year-old East Asian woman with a sleek black bob with bangs and round tortoiseshell glasses, wearing a soft pink crew-neck sweater. Sceptical, dry. |
| `leo.png` | Leo, a 22-year-old Latino man with wavy brown hair tied back, wearing a light sky-blue denim shirt with rolled sleeves. Curious, a bit nerdy. |

If one comes out wrong, reply "same person, but…" instead of starting again.

**1b. The set.**

```text
Photoreal wide establishing shot for a TV commercial, landscape, no people. A bright, modern college study room:
one long light-oak table with four chairs, a large window on the left with plants on the sill, warm ivory walls,
soft pastel accents, natural daylight. No text, no signs, no logos.
```
Save as `set-study.png`.

---

## Step 2 — ChatGPT Go: the 8 keyframes

Every keyframe uses the same framing rule, so Flow's 16:9 crop never cuts off a head. Add this line to the end of
every keyframe prompt:

```text
Landscape photo, photoreal film still from a TV commercial. Keep all people in the middle band of the frame, with
empty space above their heads and below the table edge. Laptop screens face away from the camera. No text anywhere.
```

**K1**: attach `riya.png`, `sam.png` and `set-study.png`.
```text
Using these exact people and this exact room: Riya sits at the oak table typing on an open laptop. Sam leans over
the back of her chair, panicking, holding his phone. Medium two-shot from the front, daylight from the window.
```

**K2**: an edit of K1.
```text
Edit the last image. Keep everything identical (camera, room, light, clothes, positions) and change only this:
Sam stares at the laptop screen, astonished, mouth slightly open. Riya has a small calm smile.
```

**K3**: an edit of K2; attach `mei.png` and `leo.png`.
```text
Same moment and same room, but the camera has pulled back to a wide shot. Riya and Sam are exactly as in the last
image, now on the left. At the far right end of the table, Mei has turned round in her chair, sceptical, lowering
a book. Leo is sliding his chair towards Riya, curious.
```

**K4**: an edit of K3.
```text
Edit the last image. Same wide shot, same room and light. Now Mei and Leo stand behind Riya with Sam, all four
leaning in towards her laptop, impressed, their faces lit by the screen glow.
```

**K5**: attach `leo.png`, `mei.png`, `sam.png` and `set-study.png`.
```text
The next morning in the same study room, brighter light. Leo, Mei and Sam sit in a row along the oak table, each
at their own open laptop. Medium shot framed on Leo at the left end, grinning at the camera; Mei and Sam are
partly visible to the right.
```

**K6**: an edit of K5.
```text
Edit the last image. Same moment and light, but the camera has moved along the table to the right: it's now framed
on Sam at the right end, pointing proudly at his laptop. Mei is partly visible to the left.
```

**K7**: attach `riya.png`, `sam.png` and `set-study.png`.
```text
Evening in the same study room, warm low sunset light through the window. Riya sits at her open laptop. Sam slumps
next to her, chin on his folded arms, looking dead serious. Medium two-shot from the front.
```

**K8**: an edit of K7.
```text
Edit the last image. Same framing and light. Riya has turned to look at Sam, deadpan. Sam's forehead is down on
his folded arms in defeat.
```

ChatGPT makes landscape images a little taller than 16:9, which is why the framing line asks for space above and
below. Flow crops that space off.

---

## Step 3 — Google Flow: five clips

### Set up once
1. Go to **labs.google/flow**, sign in with your AI Plus Google account, and click **New project**.
2. Open the settings (the tune icon by the prompt box):
   - **Model:** Veo 3.1 – Fast
   - **Outputs per prompt:** 1
   - **Aspect ratio:** Landscape 16:9
3. Note your credit balance.

### For each clip
1. In the prompt box, switch the mode to **Frames to Video**.
2. Click the first frame slot and upload the clip's start keyframe. When Flow asks you to crop, keep everyone's head in the frame.
3. Click the last frame slot and upload the end keyframe.
4. Paste the clip prompt, then the *Style lock*, then the *Voices* lines for the people in that clip.
5. Generate. It takes a minute or two.
6. If it's good, download it (720p is fine) and name it `clip1.mp4`, `clip2.mp4` and so on. If not, change one thing in
   the prompt and try again. A retry costs credits too.

### Style lock (paste into every clip)
```text
Style: a bright, funny live-action TV commercial. Cinema camera, 35mm lens, natural light, warm ivory and soft
pastel colours, shallow depth of field. Comedic timing with quick reactions. Laptop screens always face away from
the camera. No subtitles, no captions, no on-screen text, no logos. Clear dialogue, light room tone, no music.
```

### Voices (paste the lines for whoever speaks)
```text
Riya speaks in a calm, low, unhurried voice with a light Indian English accent.
Sam speaks in a fast, bright, slightly breathless voice with a neutral American accent.
Mei speaks in a dry, crisp voice with a light British accent.
Leo speaks in a warm, slow, thoughtful voice with a light Spanish accent.
```

### The clips (8 s each)

**Clip 1 — Hook** (K1 → K2)
```text
Sam leans over Riya's chair, panicking, and says fast: "Riya! Where's the internship letter? They need it in five
minutes!" Riya, completely calm, without looking up: "One sec." She types three quick keys. Sam looks at the
screen and freezes, astonished. Slow push-in.
```

**Clip 2 — Disbelief** (K2 → K3)
```text
Sam, staring at the screen: "Woah. How did you find that?" Riya shrugs: "I just typed 'intern'." The camera
slowly pulls back to a wide shot as, at the far end of the table, Mei turns round in her chair and says flatly:
"No way." Leo starts sliding his chair over.
```

**Clip 3 — Everyone leans in** (K3 → K4)
```text
Mei, sceptical: "Her Downloads has three hundred files." Leo, slowly: "Can it find last semester's notes?" Riya
types. Mei and Leo get up and crowd behind her with Sam, all four leaning in, impressed. Riya says: "It even
tidies them."
```

**Clip 4 — Montage** (K5 → K6)
```text
One smooth camera move sliding right along the table. Leo, grinning at the camera: "My STL files got their own
folder!" The camera passes Mei, who pushes up her glasses: "Four copies of the same résumé." It ends on Sam,
pointing proudly at his laptop: "Saved straight into Invoices!"
```

**Clip 5 — Button** (K7 → K8)
```text
Sam, chin on his arms, dead serious: "Can it find my motivation?" Riya looks at him for a beat, then types,
deadpan. She turns to him: "It finds files, Sam." Sam drops his forehead onto his arms with a groan.
```

If a voice drifts between clips, run that clip's audio through ElevenLabs Voice Changer with one fixed voice per
character. The lip-sync stays.

---

## Step 4 — The real-app clips (done)

Recorded on the private sample PC in the Sand theme, with no captions or key hints. Re-record any time with:
`tools\DemoRecorder\bin\Debug\net10.0-windows\DemoRecorder.exe record --script inserts --theme sand`

| File in `demo/inserts/` | Goes after | What it shows |
|---|---|---|
| `A-find-intern.mp4` | Clip 1, "One sec." | Quick Jump: `intern` → the letter highlighted among 300 files |
| `B-jump-sem5.mp4` | Clip 3, Leo's line | Quick Jump: `sem 5` → the folder opens |
| `C-organize-all.mp4` | Clip 3, "It even tidies them." | Organize this folder → **Organize now** |
| `G-stl-folder.mp4` | Clip 4, Leo | Search `stl`, name the folder `3D Prints`, organize |
| `E-duplicates.mp4` | Clip 4, Mei | Four copies of the résumé found |
| `F-save-dialog.mp4` | Clip 4, Sam | A Save dialog jumps straight to Invoices |
| `D-recycle-unused.mp4` | after Clip 4 | Unused files → Recycle Bin. Caption: *Old files? Recycle Bin.* |
| `H-undo.mp4` | after D | History → Undo. Caption: *Changed your mind? Undo.* |
| `I-no-matches.mp4` | Clip 5, while Riya types | `motivation` → **No matches.** |

---

## Step 5 — CapCut

1. New project, **16:9, 1080p, 30 fps**. Put clips 1–5 in order.
2. Cut in the inserts at the points in the table. Trim each to 2–3 seconds.
3. In clip 4, cut to each insert just after the person starts their line, and let the voice carry on over the
   screen (a J-cut).
4. Between clips 3 and 4, add a 1.5-second card: *Then everyone wanted it.*, white on black.
5. Music: one upbeat track from CapCut's library that is marked for commercial use, kept under the dialogue at
   about −20 dB. Mute it for the beat before "It finds files, Sam."
6. Turn on auto-captions. Most people watch muted.
7. End card, 3 s: `src/Zoomies/Assets/logo.png` on ivory `#FAF9F5`, the app name, *Find it. Organize it. Undo
   anything.* and *Free for Windows*.
8. Export a 16:9 master. For Shorts and Reels, duplicate the project, switch to 9:16 and re-centre each shot.

---

## Before you post

- **Disclose AI.** On YouTube, set *Altered or synthetic content* to **Yes**. Instagram and LinkedIn have similar labels.
- Don't mention or show Base44, its music or its tagline.
- Check Flow's and ChatGPT's current terms for commercial use of what they generate.
- Every claim in the ad must be something the real-app clips actually show.
