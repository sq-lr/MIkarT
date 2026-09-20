#!/usr/bin/env python3
"""Fetch and prepare the game's sound effects.

Every clip is CC0: kart and countdown sounds from Freesound (pulled from
the public preview MP3s on cdn.freesound.org, no account needed), UI sounds
from Kenney's asset packs (zip downloads from kenney.nl), and one music
loop per world mood, also from Freesound. SFX are decoded with macOS
`afconvert`, then trimmed / split / peak-normalised here with nothing but
the standard library; music is kept as the downloaded MP3 (Unity imports it
natively, and a two-minute loop as WAV would be ~10 MB in the repo -- the
MP3 encoder padding can give a tiny hiccup at the loop seam). Output goes
to unity/Assets/Resources/Audio/ (music under Audio/Music/) along with a
CREDITS.txt; KartAudio.cs, UISounds.cs, GenerationUI.cs, ResultsUI.cs and
MusicPlayer.cs load the clips by the names below.

    python3 tools/sfx/fetch_sfx.py

Re-running overwrites the outputs; trim points live in CLIPS so a change
of taste is a one-line edit. Requires macOS for afconvert (or pass
--decoder "ffmpeg -y -i {src} -ac 1 -ar 44100 {dst}").
"""

from __future__ import annotations

import argparse
import array
import math
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import wave
import zipfile
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT_DIR = REPO / "unity" / "Assets" / "Resources" / "Audio"
MUSIC_DIR = OUT_DIR / "Music"
SAMPLE_RATE = 44100
PEAK_DBFS = -1.0


@dataclass
class Clip:
    """One Freesound sound and how it becomes one or more game clips."""

    name: str            # output file name (without .wav); split clips get _0, _1, ...
    freesound_id: int
    title: str
    author: str
    preview_url: str
    # Either a (start, end) window in seconds, or split_on_silence to cut a
    # clip of separated hits into numbered one-shots.
    trim: tuple[float, float] | None = None
    split_on_silence: bool = False
    max_variants: int = 8
    silence_threshold: float = 0.02  # RMS (0..1) below which a window counts as a gap
    loop: bool = False   # crossfade the seam so it loops without a click
    # Extra gain (dB) applied after peak-normalising, with a soft clip so the
    # peaks fold over instead of wrapping: squashes a peaky clip louder.
    boost_db: float = 0.0
    notes: str = ""
    extra: dict = field(default_factory=dict)

    @property
    def page_url(self) -> str:
        return f"https://freesound.org/s/{self.freesound_id}/"


@dataclass
class PackClip:
    """One file out of a Kenney (CC0) asset-pack zip."""

    name: str
    pack: str        # pack title, for the credits
    pack_url: str    # kenney.nl page
    zip_url: str
    member: str      # path inside the zip
    notes: str = ""


CLIPS: list[Clip] = [
    Clip(
        name="kart_engine",
        freesound_id=487508,
        title="Noise: Cartoon Car Engine",
        author="NicknameLarry",
        preview_url="https://cdn.freesound.org/previews/487/487508_10418401-hq.mp3",
        trim=(0.2, 4.2),
        loop=True,
        notes="steady putt-putt, the one engine loop: KartAudio pitches it up to rev. "
              "(A synth 'racing' clip was tried as a second layer and dropped -- it "
              "sweeps in pitch, so it audibly restarts every loop.)",
    ),
    Clip(
        name="kart_screech",
        freesound_id=685910,
        title="Skrrt.wav",
        author="elliottdj",
        preview_url="https://cdn.freesound.org/previews/685/685910_6565462-hq.mp3",
        split_on_silence=True,
        max_variants=6,
        notes="a run of separate mouth-made 'skrrt's -- split into one-shot variants",
    ),
    Clip(
        name="kart_hit",
        freesound_id=866950,
        title="TOONImpt - 4 Strong hits with a little tail",
        author="JustWatson64",
        preview_url="https://cdn.freesound.org/previews/866/866950_14366407-hq.mp3",
        split_on_silence=True,
        max_variants=4,
        silence_threshold=0.004,  # keep the quiet ring-out tails
        notes="four toon clangs about a second apart",
    ),
    Clip(
        name="pickup_powerup",
        freesound_id=138491,
        title="Powerup 3",
        author="JustInvoke",
        preview_url="https://cdn.freesound.org/previews/138/138491_758593-hq.mp3",
        trim=(0.0, 1.6),
        notes="organ jingle; tail past 1.6 s is silence",
    ),
    Clip(
        name="pickup_powerdown",
        freesound_id=159399,
        title="Power Down",
        author="noirenex",
        preview_url="https://cdn.freesound.org/previews/159/159399_1656228-hq.mp3",
        trim=(0.0, 1.86),
    ),
    Clip(
        name="countdown_beep",
        freesound_id=368779,
        title="Short beep countdown",
        author="gurie",
        preview_url="https://cdn.freesound.org/previews/368/368779_6605122-hq.mp3",
        trim=(0.0, 0.5),
        notes="the first of its four identical beeps; GenerationUI pitches it up per tick",
    ),
    Clip(
        name="countdown_go",
        freesound_id=68999,
        title="airhorn-short.wav",
        author="guitarguy1985",
        preview_url="https://cdn.freesound.org/previews/68/68999_533680-hq.mp3",
        trim=(0.3, 2.6),
        notes="quick air horn with its echo, for GO!",
    ),
    Clip(
        name="lap_complete",
        freesound_id=386811,
        title="new_fastest_lap.wav",
        author="RichieMcMullen",
        preview_url="https://cdn.freesound.org/previews/386/386811_7236624-hq.mp3",
        trim=(0.0, 1.66),
        notes="short rhythmic chime for a completed lap; KartAudio pitches it up lap by lap, "
              "ResultsUI reuses it when the runner-up crosses the line",
    ),
    Clip(
        name="race_fanfare",
        freesound_id=677858,
        title="Game Success Fanfare Short",
        author="el_boss",
        preview_url="https://cdn.freesound.org/previews/677/677858_9129912-hq.mp3",
        trim=(0.55, 2.7),
        notes="victory jingle for the WIN banner; the first 0.55 s of the source is silence",
    ),
    Clip(
        name="race_cheer",
        freesound_id=333405,
        title="Cheer 1 short.wav",
        author="jayfrosting",
        preview_url="https://cdn.freesound.org/previews/333/333405_5884138-hq.mp3",
        trim=(0.0, 4.8),
        boost_db=8.0,
        notes="crowd cheer with the finish confetti; boosted and soft-clipped so it sits "
              "on top of the music and the fanfare",
    ),
]


@dataclass
class MusicClip:
    """One Freesound music loop, saved verbatim as music_<mood>.mp3."""

    mood: str
    freesound_id: int
    title: str
    author: str
    preview_url: str
    notes: str = ""

    @property
    def name(self) -> str:
        return f"music_{self.mood}"

    @property
    def page_url(self) -> str:
        return f"https://freesound.org/s/{self.freesound_id}/"


# One loop per WorldRecipe world.mood (schemas/world_recipe.schema.json).
# MusicPlayer.cs falls back to music_cheerful for anything it can't find.
MUSIC_CLIPS: list[MusicClip] = [
    MusicClip(
        mood="cheerful",
        freesound_id=841299,
        title="Upbeat Game Loop - Aurora Ride",
        author="Venus17",
        preview_url="https://cdn.freesound.org/previews/841/841299_18297633-hq.mp3",
        notes="123 bpm, 31 s; bright and bouncy",
    ),
    MusicClip(
        mood="chill",
        freesound_id=866738,
        title="Lo-Fi Piano Loop A - Garden Amin 75 BPM",
        author="holizna",
        preview_url="https://cdn.freesound.org/previews/866/866738_12574855-hq.mp3",
        notes="75 bpm, 26 s; mellow lo-fi piano",
    ),
    MusicClip(
        mood="epic",
        freesound_id=755409,
        title="Orchestral Loop 2",
        author="Ncone",
        preview_url="https://cdn.freesound.org/previews/755/755409_14716160-hq.mp3",
        notes="40 s; heroic orchestral",
    ),
    MusicClip(
        mood="spooky",
        freesound_id=443987,
        title="hide loop",
        author="ADnova",
        preview_url="https://cdn.freesound.org/previews/443/443987_3283808-hq.mp3",
        notes="100 bpm, 1:55; atmospheric with a light beat",
    ),
    MusicClip(
        mood="energetic",
        freesound_id=626274,
        title="Hyperactive chiptune loop",
        author="Rolly-SFX",
        preview_url="https://cdn.freesound.org/previews/626/626274_6303715-hq.mp3",
        notes="30 s; fast chiptune",
    ),
]


# Kenney's UI packs (https://kenney.nl, CC0). The zip URLs carry a content
# hash; if one 404s, copy the fresh link from the pack page's Download button.
PACK_CLIPS: list[PackClip] = [
    PackClip(
        name="ui_hover",
        pack="UI Audio",
        pack_url="https://kenney.nl/assets/ui-audio",
        zip_url="https://kenney.nl/media/pages/assets/ui-audio/490d233f68-1677590494/kenney_ui-audio.zip",
        member="Audio/rollover2.ogg",
        notes="soft 60 ms tick for mouse-over",
    ),
    PackClip(
        name="ui_click",
        pack="Interface Sounds",
        pack_url="https://kenney.nl/assets/interface-sounds",
        zip_url="https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip",
        member="Audio/confirmation_001.ogg",
        notes="short bright chime for a button press",
    ),
]


# ---------------------------------------------------------------------------
# audio helpers (16-bit mono, stdlib only)


def download(url: str, dst: Path) -> None:
    req = urllib.request.Request(url, headers={"User-Agent": "mariokart-sfx-fetch/1.0"})
    with urllib.request.urlopen(req, timeout=60) as resp, dst.open("wb") as f:
        shutil.copyfileobj(resp, f)


def decode(src: Path, dst: Path, decoder: str | None) -> None:
    if decoder:
        cmd = decoder.format(src=src, dst=dst).split()
    else:
        cmd = ["afconvert", "-f", "WAVE", "-d", f"LEI16@{SAMPLE_RATE}", "-c", "1", str(src), str(dst)]
    subprocess.run(cmd, check=True, capture_output=True)


def read_wav(path: Path) -> array.array:
    with wave.open(str(path)) as w:
        assert w.getsampwidth() == 2, path
        frames = w.readframes(w.getnframes())
        samples = array.array("h")
        samples.frombytes(frames)
        if w.getnchannels() > 1:
            samples = samples[:: w.getnchannels()]
        if w.getframerate() != SAMPLE_RATE:
            raise SystemExit(f"{path}: expected {SAMPLE_RATE} Hz, got {w.getframerate()}")
        return samples


def write_wav(path: Path, samples: array.array) -> None:
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(samples.tobytes())


def zero_crossing_near(samples: array.array, index: int, window: int = 2000) -> int:
    """Nearest index to `index` where the signal crosses zero going upward."""
    lo = max(1, index - window)
    hi = min(len(samples) - 1, index + window)
    best, best_dist = index, window + 1
    for i in range(lo, hi):
        if samples[i - 1] < 0 <= samples[i] and abs(i - index) < best_dist:
            best, best_dist = i, abs(i - index)
    return best


def trim(samples: array.array, start_s: float, end_s: float) -> array.array:
    a = zero_crossing_near(samples, int(start_s * SAMPLE_RATE))
    b = zero_crossing_near(samples, min(len(samples), int(end_s * SAMPLE_RATE)))
    return samples[a:b]


def split_on_silence(
    samples: array.array,
    threshold: float = 0.02,
    min_gap_s: float = 0.12,
    pad_s: float = 0.01,
    max_parts: int = 8,
) -> list[array.array]:
    """Cut a clip of separated hits at the quiet gaps between them."""
    win = int(SAMPLE_RATE * 0.01)
    loud = []
    for i in range(0, len(samples), win):
        seg = samples[i : i + win]
        rms = math.sqrt(sum(x * x for x in seg) / max(1, len(seg))) / 32768
        loud.append(rms >= threshold)

    parts: list[tuple[int, int]] = []
    min_gap = int(min_gap_s / 0.01)
    start = None
    quiet_run = 0
    for i, is_loud in enumerate(loud):
        if is_loud:
            if start is None:
                start = i
            quiet_run = 0
        elif start is not None:
            quiet_run += 1
            if quiet_run >= min_gap:
                parts.append((start, i - quiet_run + 1))
                start = None
                quiet_run = 0
    if start is not None:
        parts.append((start, len(loud)))

    pad = int(pad_s * SAMPLE_RATE)
    out = []
    for s, e in parts[:max_parts]:
        a = max(0, s * win - pad)
        b = min(len(samples), e * win + pad)
        out.append(samples[a:b])
    return out


def normalise(samples: array.array, peak_dbfs: float = PEAK_DBFS) -> array.array:
    peak = max(1, max(abs(x) for x in samples))
    target = 32767 * (10 ** (peak_dbfs / 20))
    gain = target / peak
    return array.array("h", (int(max(-32768, min(32767, x * gain))) for x in samples))


def boost(samples: array.array, db: float) -> array.array:
    """Gain with a tanh soft clip, so a peaky clip gets denser and louder."""
    if db <= 0:
        return samples
    gain = 10 ** (db / 20)
    return array.array("h", (int(32767 * math.tanh(x * gain / 32767)) for x in samples))


def fade(samples: array.array, ms: float = 5.0) -> array.array:
    n = min(len(samples) // 2, int(SAMPLE_RATE * ms / 1000))
    out = array.array("h", samples)
    for i in range(n):
        g = i / n
        out[i] = int(out[i] * g)
        out[-1 - i] = int(out[-1 - i] * g)
    return out


def loopify(samples: array.array, ms: float = 30.0) -> array.array:
    """Blend the tail into the head and drop it, so end -> start is seamless."""
    n = min(len(samples) // 4, int(SAMPLE_RATE * ms / 1000))
    out = array.array("h", samples[: len(samples) - n])
    for i in range(n):
        g = i / n
        out[i] = int(samples[len(samples) - n + i] * (1 - g) + samples[i] * g)
    return out


# ---------------------------------------------------------------------------


def process(clip: Clip, work: Path, decoder: str | None) -> list[tuple[str, float]]:
    mp3 = work / f"{clip.freesound_id}.mp3"
    wav = work / f"{clip.freesound_id}.wav"
    print(f"  {clip.name}: fetching #{clip.freesound_id} ({clip.title!r} by {clip.author})")
    download(clip.preview_url, mp3)
    decode(mp3, wav, decoder)
    samples = read_wav(wav)

    if clip.split_on_silence:
        parts = split_on_silence(samples, threshold=clip.silence_threshold, max_parts=clip.max_variants)
        if not parts:
            raise SystemExit(f"{clip.name}: split found no hits")
        outputs = [(f"{clip.name}_{i}", fade(normalise(p))) for i, p in enumerate(parts)]
    else:
        assert clip.trim is not None, clip.name
        part = trim(samples, *clip.trim)
        part = boost(normalise(part), clip.boost_db)
        part = loopify(part) if clip.loop else fade(part)
        outputs = [(clip.name, part)]

    written = []
    for name, data in outputs:
        write_wav(OUT_DIR / f"{name}.wav", data)
        written.append((name, len(data) / SAMPLE_RATE))
    return written


def process_pack(clip: PackClip, work: Path, decoder: str | None, zips: dict[str, Path]) -> list[tuple[str, float]]:
    if clip.zip_url not in zips:
        print(f"  fetching {clip.pack} pack from kenney.nl")
        zips[clip.zip_url] = work / f"pack{len(zips)}.zip"
        download(clip.zip_url, zips[clip.zip_url])
    src = work / f"{clip.name}{Path(clip.member).suffix}"
    with zipfile.ZipFile(zips[clip.zip_url]) as z:
        src.write_bytes(z.read(clip.member))
    wav = work / f"{clip.name}.wav"
    decode(src, wav, decoder)
    data = fade(normalise(read_wav(wav)))
    write_wav(OUT_DIR / f"{clip.name}.wav", data)
    return [(clip.name, len(data) / SAMPLE_RATE)]


def process_music(clip: MusicClip) -> list[tuple[str, float]]:
    print(f"  {clip.name}: fetching #{clip.freesound_id} ({clip.title!r} by {clip.author})")
    MUSIC_DIR.mkdir(parents=True, exist_ok=True)
    download(clip.preview_url, MUSIC_DIR / f"{clip.name}.mp3")
    return [(clip.name, 0.0)]  # length isn't measured: the MP3 is kept as-is


def write_credits(results: dict[str, list[tuple[str, float]]]) -> None:
    lines = [
        "Sound effects used by Assets/Scripts/Players/KartAudio.cs,",
        "Assets/Scripts/UI/UISounds.cs, UI/GenerationUI.cs and UI/ResultsUI.cs,",
        "and the music",
        "loops used by Assets/Scripts/Audio/MusicPlayer.cs, fetched (and, for",
        "the effects, trimmed) by tools/sfx/fetch_sfx.py.",
        "",
        "All clips are CC0 1.0 (public domain dedication,",
        "https://creativecommons.org/publicdomain/zero/1.0/). Attribution is",
        "not required but the authors deserve it.",
        "",
        "Kart sounds, from Freesound:",
        "",
    ]
    for clip in CLIPS:
        files = ", ".join(f"{n}.wav ({d:.2f}s)" for n, d in results[clip.name])
        lines.append(f"- {files}")
        lines.append(f"    \"{clip.title}\" by {clip.author} -- {clip.page_url}")
        if clip.notes:
            lines.append(f"    {clip.notes}")
    lines += ["", "UI sounds, from Kenney's asset packs (https://kenney.nl):", ""]
    for clip in PACK_CLIPS:
        files = ", ".join(f"{n}.wav ({d:.2f}s)" for n, d in results[clip.name])
        lines.append(f"- {files}")
        lines.append(f"    {clip.member} from \"{clip.pack}\" -- {clip.pack_url}")
        if clip.notes:
            lines.append(f"    {clip.notes}")
    lines += ["", "Music (one loop per world mood, kept as the Freesound preview MP3):", ""]
    for clip in MUSIC_CLIPS:
        lines.append(f"- Music/{clip.name}.mp3")
        lines.append(f"    \"{clip.title}\" by {clip.author} -- {clip.page_url}")
        if clip.notes:
            lines.append(f"    {clip.notes}")
    (OUT_DIR / "CREDITS.txt").write_text("\n".join(lines) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--decoder", help="command template with {src} and {dst}; default uses macOS afconvert")
    parser.add_argument("--only", nargs="*", help="clip names to (re)build")
    args = parser.parse_args()

    if not args.decoder and shutil.which("afconvert") is None:
        print("afconvert not found; pass --decoder (e.g. an ffmpeg command)", file=sys.stderr)
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    results: dict[str, list[tuple[str, float]]] = {}
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        for clip in CLIPS:
            if args.only and clip.name not in args.only:
                continue
            results[clip.name] = process(clip, work, args.decoder)
            for name, dur in results[clip.name]:
                print(f"    -> {name}.wav  {dur:.2f}s")
        zips: dict[str, Path] = {}
        for clip in PACK_CLIPS:
            if args.only and clip.name not in args.only:
                continue
            results[clip.name] = process_pack(clip, work, args.decoder, zips)
            for name, dur in results[clip.name]:
                print(f"    -> {name}.wav  {dur:.2f}s")
        for clip in MUSIC_CLIPS:
            if args.only and clip.name not in args.only:
                continue
            results[clip.name] = process_music(clip)
            print(f"    -> Music/{clip.name}.mp3")

    if not args.only:
        write_credits(results)
        print(f"wrote {OUT_DIR / 'CREDITS.txt'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
