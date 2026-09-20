# 0010 — Mood-matched soundtracks: a VLM-chosen enum, premade CC0 loops

**Decision:** The world gets a soundtrack picked by its *mood*, and the mood
is one more thing the vision model decides:

1. `SceneUnderstanding` (the VLM's structured output) gains `mood`, one of
   `cheerful`, `chill`, `epic`, `spooky`, `energetic`. The prompt describes
   each in terms of the kinds of scenes it fits and tells the model that the
   player's text description wins when it names or implies a mood; otherwise
   it judges from the photo. It is the same single vision call -- no extra
   request.
2. `WorldRecipe.world.mood` carries it to Unity (schema enum, default
   `energetic`, `version` stays 1 -- an old recipe without it simply gets the
   energetic track). `MockVisionService`'s canned profiles each carry a mood so
   the offline path exercises it, and `DefaultWorldRecipe` says `energetic` too.
3. Unity's `MusicPlayer` (on the `GameManager` object) maps the mood to
   `Resources/Audio/Music/music_<mood>` and plays it on a looping 2D
   `AudioSource`: fading in on `WorldReady` so it runs under the countdown,
   full through the race, ducked on `Results`, faded out back at the lobby.
   An unknown mood falls back to `energetic`; a missing clip logs one warning
   and the game plays silent.
4. The tracks are premade CC0 loops from Freesound, one per mood, chosen and
   fetched by `tools/sfx/fetch_sfx.py` alongside the SFX and listed in
   `Resources/Audio/CREDITS.txt`. They are committed as the downloaded MP3
   rather than decoded to WAV.

**Why a closed enum rather than free text or generated music:** a small,
named set is something the VLM picks reliably in one shot, the schema can
validate, and a human can curate a good track for. Generating music per
world (ElevenLabs, Suno, etc.) would add a vendor, latency on the Generating
screen, credits per race, and unpredictable quality, for a feature the
player hears for three minutes. Five moods cover the range of photos people
actually upload; adding one is a prompt bullet, a schema enum value, and a
file.

**Why the VLM and not the theme profile:** the backend's theme/palette
synthesis is still a deterministic mock keyed on scene tags. Mood is a
judgement about *feel* -- a foggy castle at night and a sunny castle garden
share tags but not a soundtrack -- and the description matters ("make it
scary"). That is exactly what the vision call already reads.

**Why MP3 verbatim:** Unity imports MP3 natively and re-encodes on build
(Vorbis by default), so the source format only affects the repo. A
two-minute loop as 44.1 kHz WAV is ~10 MB; as the 128 kbps preview it is
~2.7 MB. The cost is that MP3 encoder padding can put a few tens of
milliseconds of silence at the loop seam; for background music under engine
noise that is acceptable, and a trimmed WAV can replace any one track later
without touching code.

**Consequences:** `mood` is a schema change and needs Person A + B sign-off
per `CLAUDE.md`. Mood is chosen once per world; there is no in-race dynamic
music (final-lap sting, position-based intensity). The lobby, upload and
generating screens stay silent by design -- the music belongs to the world.
