"""
AI Film Factory — benchmark kit for Tests A / B / C (Phase 10). Standard library + ffmpeg/ffprobe only.

  1. make     build a worker bundle (jobs.json + voices) for one test, with or without lip sync
  2. (submit) curl -X POST --data-binary @benchA_sync.zip "http://<gpu>:<port>/submit?batch_id=benchA_sync_1"
              then download: curl -o out.zip "http://<gpu>:<port>/download?batch_id=benchA_sync_1"
  3. compose  turn the batch output into the watchable test video (master voices, 25 fps)
  4. report   timing / VRAM / utilisation / SyncNet table + GPU cost, from one or more results.json

Voices are the real Vietnamese master lines (export them from the app or record with the app's Gemini voices),
WAV or MP3. Test A: one line ~8 s (10-15 words). Test B: one line 8-10 s. Test C: two lines, one per character.

Batch ids must not contain "-": the worker reuses outputs of earlier batches that share the text before the
first "-" (episode resume), which would hide real timings.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile

STYLE = "cinematic film still, photorealistic, sharp focus, detailed skin texture, natural soft key light"
NEG_IMG = "blurry, deformed face, bad anatomy, extra fingers, cartoon, 3d render, text, watermark"
NEG_MOTION = ("worst quality, blurry, jittery, distorted face, deformed face, morphing face, melting face, asymmetrical eyes, "
              "screaming, grimace, deformed, extra limbs, morphing body, text, watermark")
TALK = ", the person is speaking to someone, natural jaw and lip movement while talking, natural eye blinks, subtle head movement"
SILENT = ", open mouth, talking, speaking, mouth moving, lip movement"
MOTION_TAIL = ", smooth natural motion, stable face and eyes, highly detailed photorealistic, cinematic camera"

CHAR_HAI = ("Vietnamese man, about 45 years old, sun-tanned weathered skin, short black hair with some grey, thin moustache, "
            "faded light-blue short-sleeve shirt")
CHAR_TEO = ("Vietnamese man, about 28 years old, lean, short messy black hair, cheerful face, white t-shirt, "
            "open-face motorbike helmet pushed back on his head")
MEKONG = "rustic Mekong Delta countryside house, late afternoon golden light, soft smoke drifting from a small charcoal grill"

CHAR_A = "Vietnamese woman, 30 years old, shoulder-length straight black hair, light makeup, cream silk blouse"
CHAR_B = "Vietnamese man, 35 years old, short black hair, clean shaven, navy suit jacket over white shirt"

# Each test: characters (portraits), frames, shots. A shot speaks (voice index) or is a silent reaction (seconds).
TESTS = {
    "A": {
        "chars": {"a": CHAR_A},
        "frames": {"fA": ("a", f"close-up shot of a {CHAR_A}, facing the camera slightly off-axis, in a quiet modern office, evening window light")},
        "shots": [{"frame": "fA", "voice": 0, "motion": "she talks calmly with a slight nod"}],
    },
    "B": {
        "chars": {"b": CHAR_B},
        "frames": {"fB": ("b", f"medium shot from the waist up of a {CHAR_B}, standing by a desk in a modern office, daylight")},
        "shots": [{"frame": "fB", "voice": 0,
                   "motion": "he turns his head from the window toward the camera, raises one open hand slightly while explaining, concerned expression"}],
    },
    "C": {
        "chars": {"a": CHAR_A, "b": CHAR_B},
        "frames": {
            "fA": ("a", f"medium close-up of a {CHAR_A}, sitting at a cafe table, looking slightly left of camera at someone, warm afternoon light"),
            "fB": ("b", f"medium close-up of a {CHAR_B}, sitting at a cafe table, looking slightly right of camera at someone, warm afternoon light"),
        },
        "shots": [
            {"frame": "fA", "voice": 0, "motion": "she speaks firmly, small frown"},
            {"frame": "fB", "silent": 2.5, "motion": "he listens, blinks, lowers his eyes for a moment"},
            {"frame": "fB", "voice": 1, "motion": "he answers apologetically, slight shrug"},
            {"frame": "fA", "silent": 2.0, "motion": "she listens, exhales and looks away briefly"},
        ],
    },
    # The user's script (FilmTestScene on the server: GET /api/film-factory/gpu/test-scene/voice/1..9).
    # Every line split at its sentence break (<= ~5 s per shot); B-roll inserts carry off-screen voice (no lip sync).
    "E": {
        "chars": {"h": CHAR_HAI, "t": CHAR_TEO},
        "frames": {
            "fH1": ("h", f"medium shot of a {CHAR_HAI}, sitting on a low wooden stool on the front porch of a {MEKONG}, "
                         "holding a woven palm-leaf hand fan beside a small clay charcoal grill, looking slightly right of camera toward the road"),
            "fH2": ("h", f"close-up of a {CHAR_HAI}, broad warm smile, on the porch of a {MEKONG}, looking slightly right of camera"),
            "fT1": ("t", f"medium shot of a {CHAR_TEO}, sitting on a small motorbike stopped on a dirt lane in front of a {MEKONG}, "
                         "coconut palms behind, looking slightly left of camera, grinning"),
            "fT2": ("t", f"close-up of a {CHAR_TEO}, sitting on a motorbike on a dirt lane, late afternoon golden light, "
                         "coconut palms softly out of focus, looking slightly left of camera, playful smile"),
            "fFish": (None, "close-up of a whole snakehead fish grilling on a small clay charcoal grill, glowing embers, smoke rising, "
                            "a small bowl of tamarind fish sauce with chopped red chili beside it, late afternoon golden light, no people"),
            "fGlass": (None, "close-up of two hands clinking small glasses of rice wine above a rustic wooden table with grilled fish, "
                             "smoke drifting, late afternoon golden light, faces not visible"),
        },
        "shots": [
            {"frame": "fH1", "voice": 0, "motion": "he raises one hand and calls out loudly toward the road, broad grin, the fan resting in his other hand"},
            {"frame": "fH2", "voice": 1, "motion": "he laughs and beckons with a small nod toward the porch"},
            {"frame": "fT1", "voice": 2, "motion": "he laughs sheepishly while sitting on the motorbike, small shake of the head"},
            {"frame": "fT2", "voice": 3, "motion": "he sniffs the air, raises his eyebrows, curious smile"},
            {"frame": "fH1", "voice": 4, "motion": "he proudly gestures toward the grill with the hand fan, laughing"},
            {"frame": "fFish", "voice": 5, "voiceover": True, "motion": "smoke rises from the grilling fish, embers glow, slow gentle camera push-in"},
            {"frame": "fT1", "voice": 6, "motion": "he quickly switches off the motorbike and leans back, excited expression"},
            {"frame": "fT2", "voice": 7, "motion": "he grins mischievously and winks, leaning in as if sharing a secret"},
            {"frame": "fGlass", "voice": 8, "voiceover": True, "motion": "the two small glasses clink together, rice wine ripples, smoke drifts"},
        ],
    },
    # 30 s scene (FilmTestScene on the server: GET /api/film-factory/gpu/test-scene/voice/1..5).
    # Shot / reverse shot, one visible face per frame, small purposeful motion matched to each line.
    "D": {
        "chars": {"a": CHAR_A, "b": CHAR_B},
        "frames": {
            "fA1": ("a", f"medium close-up of a {CHAR_A}, standing in a cozy apartment living room at night, warm lamp light, "
                         "looking slightly left of camera at someone, hurt and disappointed expression, "
                         "a small birthday cake with lit candles softly out of focus on the table behind her"),
            "fA2": ("a", f"close-up of a {CHAR_A}, cozy apartment living room at night, warm lamp light, "
                         "looking slightly left of camera, eyes glistening, tired expression"),
            "fB1": ("b", f"medium close-up of a {CHAR_B}, just arrived home at night in office clothes, standing near the apartment door, "
                         "warm lamp light, looking slightly right of camera at someone, guilty expression"),
            "fB2": ("b", f"close-up of a {CHAR_B}, apartment entrance at night, warm lamp light, "
                         "looking slightly right of camera, sincere apologetic expression"),
        },
        "shots": [
            {"frame": "fA1", "voice": 0, "motion": "she speaks with disappointment, slight frown, small shake of the head, eyes on him"},
            {"frame": "fB1", "silent": 2.5, "motion": "he lowers his eyes guiltily, swallows, then looks back up at her"},
            {"frame": "fB2", "voice": 1, "motion": "he apologizes softly, regretful expression, slight shrug of the shoulders"},
            {"frame": "fA2", "voice": 2, "motion": "she answers firmly, chin slightly raised, eyes glistening, steady gaze"},
            {"frame": "fB1", "voice": 3, "motion": "he nods earnestly and leans forward slightly, sincere expression"},
            {"frame": "fA2", "silent": 2.5, "motion": "she exhales and looks away for a moment, her expression softening"},
            {"frame": "fA1", "voice": 4, "motion": "she speaks quietly with a faint tired smile, looking back at him"},
        ],
    },
}


def seconds_of(path):
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                         capture_output=True, text=True).stdout.strip()
    return float(out)


def make(args):
    test = TESTS[args.test]
    voices = args.audio or []
    need = 1 + max((s.get("voice", -1) for s in test["shots"]), default=-1)
    if len(voices) < need:
        sys.exit(f"Test {args.test} needs {need} --audio file(s)")
    sync = not args.no_lipsync
    w, h = (832, 480) if args.landscape else (480, 832)
    gw, gh = (1344, 768) if args.landscape else (768, 1344)
    tw, th = (1920, 1080) if args.landscape else (1080, 1920)
    work = tempfile.mkdtemp()
    jobs, shots_meta = [], []

    for cid, look in test["chars"].items():
        jobs.append({"id": f"portrait_{cid}.jpg", "type": "image", "out": f"portrait_{cid}.jpg", "width": 832, "height": 1216,
                     "target_width": 832, "target_height": 1216, "seed": 1000 + ord(cid),
                     "prompt": f"close-up portrait photo of a {look}, looking at camera, neutral plain grey studio background, soft cinematic lighting, {STYLE}",
                     "negative": NEG_IMG})
    for fid, (cid, prompt) in test["frames"].items():
        job = {"id": f"frame_{fid}.jpg", "type": "image", "out": f"frame_{fid}.jpg", "width": gw, "height": gh,
               "target_width": tw, "target_height": th, "seed": 2000 + len(fid) + ord(fid[-1]),
               "prompt": f"{prompt}, {STYLE}", "negative": NEG_IMG}
        if cid:
            job.update(face_ref=f"portrait_{cid}.jpg", face_strength=0.45)
        jobs.append(job)

    for i, s in enumerate(test["shots"]):
        sid = f"s{i + 1}"
        speaking = "voice" in s and not s.get("voiceover")   # a face on screen says the line
        has_voice = "voice" in s
        has_face = test["frames"][s["frame"]][0] is not None
        if has_voice:
            src = voices[s["voice"]]
            vname = f"voice_{sid}{os.path.splitext(src)[1]}"
            shutil.copy(src, os.path.join(work, vname))
            dur = seconds_of(src)
        else:
            vname, dur = None, s["silent"]
        if sync:
            prompt = s["motion"] + MOTION_TAIL + (TALK if speaking else "")
            neg = NEG_MOTION + ("" if speaking or not has_face else SILENT)
            # synced clips play at their own speed; others keep 85% and may be slowed, so ask a little longer
            secs = min(max(dur if speaking else dur / 0.85, 2), 81 / 16)
        else:  # the current production workflow: mouths closed, clip trimmed/slowed by the renderer
            prompt, neg = s["motion"] + MOTION_TAIL, NEG_MOTION.replace("screaming,", "open mouth, screaming,")
            secs = min(max(dur / 0.85, 2), 5)
        jobs.append({"id": f"motion_{sid}", "type": "motion", "out": f"motion_{sid}.mp4", "image": f"frame_{s['frame']}.jpg",
                     "prompt": prompt, "negative": neg, "seconds": round(secs, 2), "width": w, "height": h, "seed": 3000 + i})
        if sync and speaking:
            jobs.append({"id": f"lipsync_{sid}.mp4", "type": "lipsync", "out": f"lipsync_{sid}.mp4",
                         "video": f"motion_{sid}.mp4", "audio": vname, "seed": 4000 + i})
        shots_meta.append({"shot": sid, "voice": vname, "seconds": round(dur, 2),
                           "clip": f"lipsync_{sid}.mp4" if (sync and speaking) else f"motion_{sid}.mp4", "synced": sync and speaking})

    spec = {"batch_id": f"bench{args.test}_{'sync' if sync else 'baseline'}", "jobs": jobs,
            "lipsync": {"steps": args.ls_steps, "guidance": args.ls_guidance, "deepcache": not args.no_deepcache, "max_attempts": 1},
            "bench": {"test": args.test, "lipsync": sync, "shots": shots_meta, "landscape": args.landscape}}
    with open(os.path.join(work, "jobs.json"), "w", encoding="utf-8") as f:
        json.dump(spec, f, ensure_ascii=False, indent=1)
    with zipfile.ZipFile(args.out, "w", zipfile.ZIP_DEFLATED) as z:
        for name in os.listdir(work):
            z.write(os.path.join(work, name), name)
    shutil.rmtree(work)
    print(f"{args.out}: {len(jobs)} jobs ({'with' if sync else 'WITHOUT'} lip sync)")


def compose(args):
    """Joins the shots at 25 fps with their master voices (silence for reactions).
    Synced clips are used as-is; baseline clips get the production renderer's treatment (keep 85%, slow <=1.5x, hold)."""
    spec = json.load(open(os.path.join(args.bundle_dir, "jobs.json"), encoding="utf-8"))
    tw, th = (1920, 1080) if spec["bench"]["landscape"] else (1080, 1920)
    tmp = tempfile.mkdtemp()
    parts = []
    for s in spec["bench"]["shots"]:
        clip = os.path.join(args.results_dir, s["clip"])
        if not os.path.exists(clip):
            sys.exit(f"missing {clip} — check results.json for the failed job")
        d = s["seconds"] + (0.25 if s["voice"] else 0)
        n = max(1, round(d * 25))  # whole frames, so joins never drift or drop a frame
        d = n / 25
        if s["synced"]:
            vf = f"setpts=PTS-STARTPTS,scale={tw}:{th}:force_original_aspect_ratio=increase:flags=lanczos,crop={tw}:{th},fps=25,tpad=stop_mode=clone:stop_duration={d}"
        else:
            kept = seconds_of(clip) * 0.85
            slow = min(max(d / kept, 1.0), 1.5)
            vf = (f"trim=duration={kept:.3f},setpts={slow:.3f}*(PTS-STARTPTS),scale={tw}:{th}:force_original_aspect_ratio=increase:flags=lanczos,"
                  f"crop={tw}:{th},fps=25,tpad=stop_mode=clone:stop_duration={d}")
        audio = ["-i", os.path.join(args.bundle_dir, s["voice"])] if s["voice"] else ["-f", "lavfi", "-t", f"{d}", "-i", "anullsrc=r=48000:cl=stereo"]
        part = os.path.join(tmp, f"{s['shot']}.mp4")
        subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", clip, *audio,
                        "-filter_complex", f"[0:v]{vf},trim=end_frame={n},format=yuv420p[v];"
                                           f"[1:a]aresample=48000,aformat=channel_layouts=stereo,apad,atrim=0:{d:.4f}[a]",
                        "-map", "[v]", "-map", "[a]", "-frames:v", str(n), "-r", "25", "-c:v", "libx264", "-crf", "18",
                        "-c:a", "aac", "-b:a", "192k", part], check=True)
        parts.append(part)
    lst = os.path.join(tmp, "list.txt")
    with open(lst, "w") as f:
        f.writelines(f"file '{p}'\n" for p in parts)
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", lst,
                    "-c", "copy", "-movflags", "+faststart", args.out], check=True)
    shutil.rmtree(tmp)
    print(f"{args.out}: {seconds_of(args.out):.2f}s — WATCH IT WITH SOUND before scoring.")


def report(args):
    rows, total_gpu_s = [], 0.0
    print(f"{'batch':<22}{'job':<20}{'type':<8}{'model':<40}{'res':<10}{'fps':>4}{'frm':>5}{'stp':>4}{'sec':>8}"
          f"{'peakGB':>8}{'util%':>6}  sync(conf/off/verdict)")
    for path in args.results:
        r = json.load(open(path, encoding="utf-8"))
        total_gpu_s += r.get("total_seconds", 0)
        for jid, j in r["jobs"].items():
            kind = "lipsync" if "lipsync" in jid else "motion" if jid.startswith("motion") else "image"
            res = f"{j.get('width', '')}x{j.get('height', '')}" if "width" in j else ""
            q = j.get("qc_sync") or {}
            qs = f"{q.get('confidence')}/{q.get('av_offset_frames')}/{q.get('verdict')}" if q else ""
            print(f"{r.get('batch_id', '')[:21]:<22}{jid[:19]:<20}{kind:<8}{str(j.get('model', r['env'].get('motion_model', '')))[-39:]:<40}"
                  f"{res:<10}{j.get('fps', ''):>4}{j.get('frames', ''):>5}{j.get('steps', ''):>4}{j.get('seconds', 0):>8.1f}"
                  f"{j.get('gpu_peak_mem_gb') or '':>8}{j.get('gpu_util_avg') or '':>6}  {qs}"
                  + ("" if j["status"] == "ok" else f"  FAILED: {j.get('error', '')[:80]}"))
        print(f"  stages: {r.get('stages')}  env: {r.get('env')}  total {r.get('total_seconds')} s")
    if args.usd_per_hour:
        cost = total_gpu_s / 3600 * args.usd_per_hour
        line = f"GPU time {total_gpu_s / 60:.1f} min x ${args.usd_per_hour}/h = ${cost:.3f}"
        if args.video_seconds:
            line += f"  -> ${cost / args.video_seconds:.4f} per second of finished video"
        print(line)
    print("Scores above are machine checks only. Watch and listen to every test video before accepting it.")


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")  # Vietnamese error texts on Windows consoles
    except Exception:
        pass
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    m = sub.add_parser("make")
    m.add_argument("--test", choices=list(TESTS), required=True)
    m.add_argument("--audio", action="append", help="master voice line (repeat for Test C)")
    m.add_argument("--out", required=True)
    m.add_argument("--no-lipsync", action="store_true", help="baseline = current production workflow")
    m.add_argument("--landscape", action="store_true")
    m.add_argument("--ls-steps", type=int, default=20)
    m.add_argument("--ls-guidance", type=float, default=1.5)
    m.add_argument("--no-deepcache", action="store_true")
    c = sub.add_parser("compose")
    c.add_argument("--bundle-dir", required=True, help="unzipped bundle (jobs.json + voices)")
    c.add_argument("--results-dir", required=True, help="unzipped worker output")
    c.add_argument("--out", required=True)
    r = sub.add_parser("report")
    r.add_argument("results", nargs="+")
    r.add_argument("--usd-per-hour", type=float)
    r.add_argument("--video-seconds", type=float)
    a = p.parse_args()
    {"make": make, "compose": compose, "report": report}[a.cmd](a)


if __name__ == "__main__":
    main()
