# AI FILM FACTORY — KIỂM TOÁN & NÂNG CẤP LÊN DÂY CHUYỀN DIỄN XUẤT ĐIỆN ẢNH

Ngày: 2026-11-22 · Nhánh: `feat/film-cinematic-upgrade` · Điểm rollback: tag `pre-cinematic-upgrade` (= commit `9cfa2ef` trên `main`)

> Số dòng code trong báo cáo này trỏ vào bản ở tag `pre-cinematic-upgrade` (trước khi sửa).

---

## 0. Tóm tắt trạng thái (trung thực)

| Phase | Nội dung | Trạng thái |
|---|---|---|
| 1 | Audit source | **Xong**. Kết quả ở mục 1–2. |
| 1 | Audit môi trường GPU (RTX PRO 6000 Max-Q) | **Chưa chạy được**: phiên làm việc này không truy cập được máy Vast.ai (không có SSH, không có `VAST_API_KEY`; GPU trên máy local là GTX 1050 Ti 4 GB). Đã viết script `gpu_env_audit.sh` để anh chạy trên máy GPU. |
| 2 | Phân tích nguyên nhân + kiến trúc mới | **Xong** (mục 2, 3). |
| 3 | Kiểm tra model / license / tương thích | **Xong một phần**: LatentSync 1.6 đã kiểm tra từ repo gốc (mục 4.2). Chưa benchmark trên GPU. |
| 4 | Lip Sync | **Đã code, đã test phần nối ống (plumbing) trên máy local; CHƯA chạy với model thật**. Tắt mặc định (`Film:LipSync:Enabled=false`). |
| 5–9 | Character, QC đầy đủ, tối ưu GPU, tự tạo lại, DB | **Chưa làm**. Có kế hoạch cụ thể ở mục 6. |
| 10 | 3 benchmark A/B/C | **Chưa chạy, cần máy GPU**. Bộ công cụ `film_bench.py` đã sẵn sàng (mục 5). |

**Chưa có video đầu ra nào được tạo hoặc xem trong phiên này.** Vì vậy chưa có tiêu chí nghiệm thu nào ở mục XVIII được coi là đạt.

---

## 1. Kiến trúc hiện tại (Phase 1)

### 1.1 Thành phần

| Vai trò | Hiện tại | Ở đâu |
|---|---|---|
| Biên kịch / phân cảnh | Gemini (JSON: scenes → frames → shots, gồm speaker, line, emotion, acting, motionPrompt, camera) | `FilmShowrunnerService.cs` |
| Giọng nói | Gemini TTS (mỗi nhân vật 1 voice; không truyền cảm xúc vào TTS) | `FilmRendererService.EnsureVoicesAsync`, `GeminiTtsService.cs` |
| Ảnh tĩnh (khung hình) | RealVisXL V5 (SDXL) + IP-Adapter Plus Face (0.45) + Real-ESRGAN x2 | `film_gpu_worker.py` stage `image` |
| Chuyển động | Wan2.1-I2V-14B-480P (diffusers), 40 bước, CFG 4.0, 480×832, 16 fps; dự phòng LTX-Video 0.9.5 | `film_gpu_worker.py` stage `motion` |
| **Lip Sync** | **Không có.** Không có model, không có bước nào trong pipeline | — |
| QC | ffprobe: có hình, có tiếng, âm lượng, độ dài | `FilmRendererService.InspectAsync` |
| Composer | FFmpeg (C#): mỗi shot = clip + giọng → nối → watermark | `FilmRendererService.cs` |
| Điều phối GPU | Hangfire tick 2 phút, 1 tập = 1 batch zip → worker HTTP | `FilmGpuService.cs`, `VastAiGpuBackend.cs` |

### 1.2 Luồng dữ liệu

```
Gemini script ─► FilmShot (DB) ─► Gemini TTS (voice per shot, đo độ dài)
                     │
                     ▼
            jobs.json + portraits ──zip──► GPU worker
                                            ├─ image: SDXL + IP-Adapter → frame 1080×1920
                                            └─ motion: Wan I2V 480×832, ≤5 s, 16 fps
                     ◄──zip── results.json + frames + clips
                     ▼
   Renderer: clip (giữ 85%, làm chậm ≤1.5×, đứng hình phần còn thiếu) + giọng → shot 30 fps → nối → MP4
```

### 1.3 Log thực tế có trong repo

Chỉ có `ep2_results.zip`, batch `ep2-20261121091609`, chạy trên **RTX 3090 24 GB** (torch 2.8.0+cu129):
- 12/12 khung hình OK, ~9.6 s/khung.
- **26/26 clip chuyển động lỗi CUDA OOM.** Job đầu chết sau 30.5 s, 25 job sau chết ngay trong 0.1 s. Sau OOM đầu tiên bộ nhớ không được giải phóng, nên cả batch hỏng dây chuyền.
- Không có log của lần chạy A100 (90 s video / 4.5 giờ), nên **thời gian từng bước trên A100 chưa đo được**.

---

## 2. Nguyên nhân gốc (Phase 2), dựa trên source và log

### 2.1 Khẩu hình không khớp lời thoại

1. **Không có bước lip sync nào.** Giọng chỉ được ghép đè lên clip Wan (`FilmRendererService.cs:372-379`). Miệng nhân vật chuyển động hoàn toàn độc lập với âm thanh.
2. **Prompt âm bảo model ngậm miệng.** Mọi job motion có `open mouth` trong negative prompt (`FilmGpuService.cs:250`, `film_gpu_worker.py:447`). Kết quả: nhân vật "nói" mà miệng đóng.
3. Prompt chuyển động không hề cho Wan biết shot đó có người đang nói (`FilmGpuService.cs:243-244`).

### 2.2 Chuyển động không khớp thoại, giật

1. Clip bị cắt cứng ở 5 s (`film_gpu_worker.py:439`). Renderer chỉ giữ 85% clip, làm chậm tới 1.5×, rồi **đứng hình** phần còn thiếu (`FilmRendererService.cs:357-363`).
   Ví dụ câu thoại 8 s: clip 5 s → giữ 4.25 s → làm chậm thành 6.4 s → **đứng hình 1.6 s**.
2. Wan xuất 16 fps, bị nhân bản khung lên 30 fps. Nếu làm chậm thêm 1.5× thì chỉ còn khoảng **10.7 khung hình thật mỗi giây**, gây cảm giác giật.
3. Mỗi khung hình tĩnh được 2–4 shot dùng lại (`FilmShowrunnerService.cs:414`). Chuyển động được viết bằng 1 câu tiếng Anh ≤35 từ, không gắn với nhịp câu nói.

### 2.3 Biến dạng / mất nhất quán nhân vật

1. Wan I2V chỉ nhận khung đầu tiên, không có điều kiện danh tính, nên mặt trôi dần trong 5 s.
2. Khung có 2 người chỉ được neo mặt của **người nói** (`FilmGpuService.cs:218-227`). Mặt người còn lại do SDXL vẽ ngẫu nhiên, nên khác nhau giữa các shot.
3. Đầu ra 480×832 được phóng 2.25× lên 1080×1920 kèm unsharp, nên mặt mềm và nhòe.
4. Chế độ `WAN_RESOLUTION=720p` ép 1280×720 lên checkpoint **480P** (`film_gpu_worker.py:431`), tức là sinh ngoài kích thước đã huấn luyện. **Đã sửa.**
5. Danh sách model thử `Wan2.1-I2V-1.3B-480P-Diffusers` (`film_gpu_worker.py:350-354`): Wan-AI không phát hành bản I2V 1.3B (1.3B chỉ có T2V). Log 3090 khớp với điều này: lệnh tải 1.3B thất bại rồi rơi xuống 14B trên card 24 GB, nên OOM.

### 2.4 Thời gian render 4.5 giờ / 90 s

Chưa đo được vì không có log A100. Phân tích theo code (**là ước tính, cần benchmark xác nhận**):
- `FullMotion=true`, `MotionShare=1.0` → **mọi shot** (26–40 shot/tập) đều chạy Wan 14B.
- 40 bước × CFG 4.0 = **80 lượt chạy transformer 14B** cho mỗi clip 77 khung.
- Trên A100 80 GB, code đi nhánh `enable_model_cpu_offload()` (`film_gpu_worker.py:400-403`, vì VRAM < 85), nên model bị chuyển CPU↔GPU.
- 4.5 h / ~30 clip ≈ **9 phút/clip**, phù hợp với cấu hình trên.
- Mỗi lần khởi động còn chạy `pip install -U` không ghim phiên bản, có thể đổi version thư viện giữa các lần chạy.

### 2.5 Quản lý VRAM

- `torch.cuda.empty_cache()` + `gc.collect()` trước **mọi** clip (`film_gpu_worker.py:425-427`). **Đã bỏ**, giờ chỉ gọi sau OOM.
- Sau OOM không có phục hồi thật: pipeline giữ nguyên trạng thái lỗi. Log 3090 cho thấy hỏng dây chuyền. Phục hồi đúng cần nạp lại pipeline; việc này nằm trong Phase 7.
- Không đo peak VRAM / GPU utilization. **Đã thêm** (mục 4.1).

### 2.6 Khác

- QC chỉ kiểm tra file kỹ thuật, không đánh giá khẩu hình, mặt hay chuyển động.
- Gemini TTS không nhận cảm xúc. Trước đây đã thử chèn chỉ dẫn phong cách nhưng model **đọc luôn chỉ dẫn thành tiếng** (`GeminiTtsService.cs:161-166`), nên cần cách khác (Phase 4b).
- Cơ chế "khôi phục từ batch trước" tìm clip theo shot id, nên dùng lại clip cũ dù motion prompt đã đổi. (Với lip sync, tên file đã kèm hash của file giọng nên không bị lỗi này.)

---

## 3. Kiến trúc mới: 9 engine trên nền code hiện có

Không viết lại từ đầu. Mỗi engine gắn vào thành phần đã có:

| # | Engine | Gắn vào | Trạng thái |
|---|---|---|---|
| 1 | Director AI | `FilmShowrunnerService` (JSON scene/frame/shot) | Có. Đã thêm luật dàn cảnh thoại cho lip sync. Schema production đầy đủ (camera, reaction, nhiều nhân vật) → Phase 5. |
| 2 | Character Consistency | Portrait + IP-Adapter (SDXL) | Một phần. Cần ảnh 3 góc + đo nhận diện (Phase 5). |
| 3 | Script & Scene | Như #1 | Có. |
| 4 | Voice & Audio | Gemini TTS, **thu trước khi sinh video** (đã đúng) | Có. Cảm xúc chưa truyền vào → Phase 4b. |
| 5 | Motion & Performance | Wan 2.1 I2V 14B, **giữ nguyên** tới khi benchmark chứng minh phương án khác tốt hơn | Đã tách prompt "người nói" và "người nghe" (`FilmShotPlanner`). |
| 6 | Video Generation | Như #5 | Có. |
| 7 | **Lip Sync** | **Mới**: stage `lipsync` (LatentSync 1.6) trong worker | Đã code, chưa chạy thật. |
| 8 | Quality Control | **Mới**: SyncNet confidence + offset mỗi shot; PASS / WARNING / REJECT | Nhóm A đã code. Nhóm B–E → Phase 6. |
| 9 | Film Composer | `FilmRendererService` | Có. Clip đã khớp khẩu hình được dùng nguyên tốc độ. |

**Nguyên tắc tách vai trò (đã áp dụng):** Wan chỉ lo cơ thể, đầu, mắt và máy quay. LatentSync chỉ lo miệng. Giọng master không bao giờ bị sửa: renderer ghép file gốc, LatentSync chỉ đọc bản 16 kHz để lái miệng.

Ai được lip sync (`FilmShotPlanner.IsOnCameraSpeech`):

| Tình huống | Lip sync? | Prompt chuyển động |
|---|---|---|
| Người nói là khuôn mặt **duy nhất** trong khung, không phải toàn cảnh | Có | Thêm "đang nói, hàm/môi cử động, chớp mắt"; bỏ `open mouth` khỏi negative |
| 2 mặt trong khung | Không (model có thể "nhép" nhầm người nghe) | Người nghe: negative cấm nói, cấm mở miệng |
| Lời dẫn / người nói ngoài khung | Không | Như trên |
| Toàn cảnh (WideShot) | Không (miệng quá nhỏ) | Như trên |

---

## 4. Đã làm trong phiên này

### 4.1 File đã sửa / thêm

| File | Thay đổi |
|---|---|
| `GpuWorker/film_gpu_worker.py` | Stage `lipsync` (LatentSync 1.6 chạy trong venv riêng, chạy tuần tự sau khi đã giải phóng Wan); SyncNet QC → PASS/WARNING/REJECT; kiểm tra độ dài clip đã sync bằng độ dài giọng (±0.2 s); retry có giới hạn (`max_attempts`, mỗi lần retry dùng seed mới); **mỗi job ghi lại** thời gian, peak VRAM (nvidia-smi, thấy cả tiến trình con), GPU util trung bình, peak torch alloc, và thông số đã dùng (model, độ phân giải, fps, số khung, steps, cfg, seed); ghi thời gian nạp model theo stage; Wan được dùng đủ 81 khung (5.06 s); 720p chỉ khi checkpoint là 720P; bỏ `empty_cache` máy móc; `FILM_WORKER_SKIP_PIP=1` để test offline |
| `FilmShotPlanner.cs` (mới) | Quyết định shot nào được lip sync, prompt/negative cho người nói và người nghe, độ dài clip yêu cầu |
| `FilmGpuService.cs` | Gửi giọng master kèm batch; tạo job `lipsync`; gửi thông số `lipsync` + ngưỡng `qc`; ingest clip đã sync (`VisualMethod = LipSyncMotion`); lip sync lỗi → dừng tập và báo rõ; QC chưa PASS → cảnh báo kèm số liệu để xem lại bằng mắt |
| `FilmRendererService.cs` | Clip đã sync: dùng từ khung 0, **không cắt 85%, không làm chậm**; giữ cảnh báo QC trên tập hoàn thành; nếu mất file giọng của shot đã sync thì **không tự thu giọng mới** khi dựng (sẽ lệch miệng), mà báo gửi lại GPU |
| `FilmShowrunnerService.cs` | Khi bật lip sync: `characters` chỉ liệt kê người thấy mặt; thoại dàn theo shot/reverse-shot; chuyển động nhỏ, có chủ đích |
| `appsettings.json` | `Film:LipSync:*` (mặc định **tắt**), `Film:Qc:*` |
| `GpuWorker/gpu_env_audit.sh` (mới) | Audit Phase 1, chỉ đọc |
| `GpuWorker/setup_lipsync.sh` (mới) | Cài LatentSync có chủ đích (opt-in) + smoke test |
| `GpuWorker/film_bench.py` (mới) | Tạo bundle Test A/B/C (bản lip sync và bản workflow hiện tại), dựng video test, in bảng benchmark |
| `tests/.../FilmShotPlannerTests.cs` (mới) | 10 test |

### 4.2 Model

| Model | Hành động | Lý do / ghi chú |
|---|---|---|
| **LatentSync 1.6** (ByteDance, Apache-2.0) | Thêm, **chưa cài**; cài bằng `setup_lipsync.sh` | Huấn luyện ở 512×512 (giảm nhòe so với 1.5), cần ~18 GB VRAM. Upstream ghim `torch==2.5.1+cu121`, bản này **không chạy được trên Blackwell (sm_120)**, nên script cài torch 2.7.1+cu128 trong venv riêng. Tổ hợp này chưa được upstream xác nhận; smoke test sẽ quyết định. |
| SyncNet v2 (`auxiliary/syncnet_v2.model`, repo LatentSync-1.6) | Thêm cho QC | Đo độ khớp khẩu hình (confidence) và độ lệch (offset theo khung @25 fps). Cần mặt được theo dõi liên tục ≥50 khung (2 s); clip ngắn hơn sẽ báo `NOT_MEASURED`. |
| MuseTalk | Chưa thêm | Sẽ chỉ thêm nếu Test A với LatentSync không đạt, để so sánh cùng đầu vào. Không cài hàng loạt. |
| Đã loại | Không có | Workflow cũ giữ nguyên khi `LipSync:Enabled=false` (có unit test chứng minh prompt và độ dài giống hệt bản cũ). |

### 4.3 Test đã chạy

- Backend: build sạch; **170/170 test pass** (gồm 10 test mới).
- Worker, trên máy local (CPU, stub torch, LatentSync giả lập bằng ffmpeg): stage lipsync chạy hết vòng; clip 2.06 s được loop đúng theo giọng 3.3 s; kiểm tra độ dài, parse SyncNet và verdict hoạt động; nhánh "chưa cài LatentSync" báo lỗi rõ ràng.
- `film_bench.py`: tạo bundle Test A/C (lip sync và baseline); compose cho ra video 25 fps, **0 khoảng lệch khung ở điểm nối** (đã sửa một lỗi lệch khung ở điểm nối phát hiện khi test); vị trí giọng đúng (kiểm bằng `silencedetect`).
- **Chưa test:** LatentSync thật, SyncNet thật, Wan/SDXL, bất kỳ thứ gì trên Blackwell.

---

## 5. Việc cần làm trên máy GPU (runbook)

Worker mới **chưa có trên `main`**. `start_vast.sh` tải worker từ Railway/`main`, nên production không bị ảnh hưởng. Để đưa file lên máy GPU, chọn một trong hai:
- (a) `scp` 4 file trong `GpuWorker/` lên `/workspace/`, hoặc
- (b) push nhánh `feat/film-cinematic-upgrade` rồi `curl` từ `raw.githubusercontent.com/.../feat/film-cinematic-upgrade/...`.

```bash
# 1. Audit môi trường (chỉ đọc, ~2 phút)
bash gpu_env_audit.sh                       # -> /workspace/gpu_audit_*.txt / .json
#    Cần xem: arch_supported=true (sm_120), allocatable_vram_gib, bf16_matmul_tflops,
#    tốc độ đọc/ghi đĩa, dung lượng trống.

# 2. Cài LatentSync (một lần, cần >= 20 GB trống) + smoke test trên clip mẫu
bash setup_lipsync.sh                       # xem /workspace/lipsync_smoke/demo1_out.mp4

# 3. Khởi động lại worker bằng film_gpu_worker.py mới

# 4. Benchmark (giọng tiếng Việt thật, xuất từ app)
python3 film_bench.py make --test A --audio lineA.wav --out benchA_sync.zip
python3 film_bench.py make --test A --audio lineA.wav --out benchA_base.zip --no-lipsync
curl -X POST --data-binary @benchA_sync.zip "http://127.0.0.1:8888/submit?batch_id=benchA_sync_1"
curl -X POST --data-binary @benchA_base.zip "http://127.0.0.1:8888/submit?batch_id=benchA_base_1"
#    (B: 1 file giọng 8-10 s; C: --audio lineA.wav --audio lineB.wav)
#    Khi xong: tải /download?batch_id=..., giải nén bundle và kết quả, rồi:
python3 film_bench.py compose --bundle-dir benchA_sync --results-dir outA_sync --out testA_sync.mp4
python3 film_bench.py report outA_*/results.json --usd-per-hour <giá thuê/giờ> --video-seconds 8
```

**Batch id không được chứa dấu `-`.** Worker dùng lại kết quả của batch có cùng tiền tố trước dấu `-` (cơ chế resume tập phim), khi đó thời gian đo sẽ là 0 và sai.

**Dung lượng (đo từ Hugging Face API ngày 2026-11-22): ổ 85 GB KHÔNG đủ.**

| Thứ cần tải | Dung lượng |
|---|---|
| `Wan-AI/Wan2.1-I2V-14B-480P-Diffusers` (trọng số FP32: transformer 65.6 GB, text encoder 22.7 GB) | **90.1 GB** |
| RealVisXL V5 (bản fp16) | 6.9 GB |
| IP-Adapter Plus Face + image encoder | ≤5.9 GB |
| LatentSync 1.6 (unet, whisper, SyncNet, S3FD) + sd-vae | 6.0 GB |
| venv LatentSync, pip cache, batch tạm | ước tính ~15 GB |
| **Tổng** | **~125 GB** |

Vast không cho mở rộng ổ của máy đã tạo ("Disk size is permanent and cannot be changed after instance creation"). Cần tạo máy mới với ổ **200 GB** (còn chỗ để thử thêm một model motion khác).

**Nghiệm thu Test A trước khi bật production:** xem và nghe `testA_sync.mp4` cạnh `testA_base.mp4`. Chỉ khi khẩu hình khớp bằng mắt **và** SyncNet hợp lý mới đặt `Film:LipSync:Enabled=true` trên Railway. Ngưỡng QC hiện tại (PASS ≥6.0 và lệch ≤1 khung; REJECT <3.0 hoặc lệch >3 khung) là **tạm thời**, sẽ hiệu chỉnh theo số đo của Test A.

### Bảng benchmark (điền sau khi chạy, không so sánh các dòng khác thông số mà không ghi rõ)

| Test | Workflow | Model | Res | FPS | Khung | Steps | Gen (s) | LipSync (s) | QC (s) | Peak VRAM | GPU util | SyncNet conf/offset | Mặt nhất quán | Chuyển động | Hình ảnh | Chi phí |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| A | Hiện tại | Wan2.1-I2V-14B-480P | 480×832 | 16 | 81 | 40 | | — | — | | | | | | | |
| A | Wan + LatentSync 1.6 | | | | | | | | | | | | | | | |
| B | … | | | | | | | | | | | | | | | |
| C | … | | | | | | | | | | | | | | | |

---

## 6. Kế hoạch các phase tiếp theo (chỉ làm sau khi có số đo Test A)

- **Phase 4b, cảm xúc giọng:** thử truyền emotion theo cách không bị đọc thành tiếng (ví dụ chỉ dẫn ở system instruction thay vì đặt trước câu thoại). Kiểm tra bằng ASR (so text) trước khi bật.
- **Phase 5, nhân vật:** ảnh tham chiếu 3 góc / nhân vật; neo mặt cho cả hai người trong khung 2 người (hoặc giữ luật "1 mặt / khung" cho thoại); đo nhận diện bằng ArcFace (InsightFace) giữa portrait và từng shot.
- **Phase 5, ứng viên motion có điều khiển (chỉ đánh giá license, VRAM, chất lượng; không cài hàng loạt):**
  - Wan2.2-Animate (chuyển diễn xuất từ video người thật quay bằng điện thoại sang nhân vật);
  - các model "audio → video nói" như Wan2.2-S2V, MultiTalk/InfiniteTalk, sinh cả miệng, đầu và thân theo giọng.
  - Nếu Test A/B với Wan + LatentSync không đạt, đây là hướng so sánh tiếp theo, với cùng đầu vào và cùng thông số.
- **Phase 6, QC đủ 5 nhóm:** B. nhận diện mặt + flicker vùng mặt; C. optical flow / pose, phát hiện tay chân biến dạng; D. ASR so với lời thoại, clipping; E. độ nét, phơi sáng, flicker. Mỗi nhóm có điểm riêng; một nhóm REJECT là REJECT, không lấy trung bình.
- **Phase 7, tốc độ:** đo riêng từng biến trên cùng một shot: số bước (40 → 30/25), CFG, cache bước (TeaCache/MagCache), attention (SDPA/Sage), LoRA chưng cất bước; cùng với đó là phục hồi OOM thật (nạp lại pipeline) và ghim phiên bản thư viện. Mục tiêu là chi phí trên mỗi giây video **đạt QC**, không phải giây video bất kỳ.
- **Phase 8, tạo lại tự động:** lip sync REJECT thì chỉ chạy lại lip sync (seed mới, tối đa N lần); motion lỗi thì tạo lại motion rồi lip sync. Worker đã bỏ qua output đã có, nên chỉ chạy phần bị lỗi.
- **Phase 9, DB:** bảng QC theo shot (điểm từng nhóm, phiên bản model, seed, peak VRAM, số lần thử, lưu mọi phiên bản), thay cho việc ghi cảnh báo vào `ErrorMessage` như hiện tại; trạng thái theo danh sách ở mục XIV.

## 7. Rollback

- `git checkout main`: code cũ nguyên vẹn. Tag `pre-cinematic-upgrade` đánh dấu điểm trước khi sửa.
- Ngay cả trên nhánh mới, `Film:LipSync:Enabled=false` (mặc định) tạo ra job giống hệt bản cũ (có unit test).
- Worker mới chỉ thêm stage, không đổi định dạng `results.json` cũ (chỉ thêm trường), nên backend cũ vẫn đọc được.
