# Chạy song song máy tính + Railway

Máy tính bật thì **máy tính làm việc nặng** (ghép video, tạo clip, tự đăng…), Railway chỉ giữ trang web.
Tắt máy thì khoảng 1 phút sau **Railway tự làm tiếp**. Bật máy lên lại thì máy tính tự nhận việc về.
Cả hai dùng chung cơ sở dữ liệu trên Railway và chung một **kho file** (clip, video) nên không có gì phải đồng bộ.

## 1. Tạo kho file miễn phí (một lần, ~10 phút)

**Cloudflare R2** (miễn phí 10 GB, không tính phí tải xuống — Cloudflare có thể yêu cầu thêm thẻ để xác minh):
1. Đăng ký / đăng nhập https://dash.cloudflare.com → **R2 Object Storage** → **Create bucket** → tên `tramtin-studio`.
2. R2 → **Manage API Tokens** → **Create API token** → quyền **Object Read & Write**, chọn bucket `tramtin-studio` → Create.
3. Ghi lại: **Access Key ID**, **Secret Access Key**, và **S3 endpoint** dạng `https://<account-id>.r2.cloudflarestorage.com`.

(Hoặc **Backblaze B2**: tạo bucket private + Application Key; endpoint dạng `https://s3.us-west-004.backblazeb2.com`, Region `us-west-004`.)

## 2. Thêm kho file vào Railway

Railway → service app → **Variables** → thêm 5 biến (tự gõ, đừng gửi khoá cho ai):

```
Storage__S3__ServiceUrl = https://<account-id>.r2.cloudflarestorage.com
Storage__S3__Bucket     = tramtin-studio
Storage__S3__AccessKey  = <Access Key ID>
Storage__S3__SecretKey  = <Secret Access Key>
Storage__S3__Region     = auto
```

Chỉ riêng bước này đã có lợi: **cập nhật code / server khởi động lại không còn làm mất clip**.

## 3. Chuẩn bị máy tính (một lần)

1. Cần có **.NET 8**, **Git**, **FFmpeg** (`D:\tools\ffmpeg-master-latest-win64-gpl\bin` hoặc trong PATH) và Python 3 có
   `pillow` + `edge-tts` (cho video tin tức). Chạy thẳng trên Windows — **không cần Docker**.
2. Trong thư mục dự án, copy `.env.pc.example` thành **`.env.pc`** và điền theo hướng dẫn trong file
   (cách nhanh: Railway → app → Variables → **Raw Editor** → copy hết → dán; thêm dòng `DATABASE_PUBLIC_URL=…`
   lấy từ Railway → Postgres → Variables, và 5 dòng `Storage__S3__…`). Dòng `${{…}}`, `PORT`, `RAILWAY_*` được bỏ qua tự động.
3. Windows → **Settings → System → Power** → khi cắm sạc: **Sleep = Never** (máy ngủ là ngừng làm việc).

## 4. Chạy

- Bấm đúp **`scripts\pc\start-pc-worker.bat`**. Lần đầu build mất vài phút. Để cửa sổ mở: nó tự cập nhật (kiểm tra mỗi phút) khi có bản code mới
  và tự chạy lại nếu chương trình dừng. Nhật ký: `%USERPROFILE%\TramTinPcWorker-data\worker.log`.
- Muốn tự chạy khi mở máy: nhấn `Win + R` → gõ `shell:startup` → kéo **lối tắt** của `start-pc-worker.bat` vào thư mục đó.
- Muốn dừng ngay (Railway nhận việc liền): bấm đúp **`scripts\pc\stop-pc-worker.bat`**.

## Ghi chú

- Máy tính chỉ nhận việc khi chạy **đúng bản code như Railway**. Vừa có bản mới thì Railway làm trong lúc máy tính đang tự cập nhật.
- Railway không bao giờ bỏ ngang một việc đang làm: nó chỉ nhường khi rảnh.
- Video **tin tức** ghép trên máy tính vẫn đăng bình thường, nhưng xem lại trong trang web (Video → chi tiết) có thể không phát được
  vì file nằm trên máy tính. Video **Studio AI** thì xem được ở mọi nơi (nằm trong kho file chung).

## AI Video Studio (phân tích video tham chiếu trên máy tính — miễn phí; tạo clip bằng Seedance)

Menu **AI Video Studio** thay cho Reup Video. Bước **phân tích** chạy **chỉ trên máy tính** (hàng đợi `pc_local`): không gọi AI trả phí,
không tốn CPU Railway; máy tắt thì việc nằm chờ. Bước **tạo clip** dùng Seedance (Vercel AI Gateway, cùng khoá / cùng bảng chi phí và trần
ngân sách tháng với Studio Video AI) — máy nào nhận việc cũng được vì Seedance chạy trên cloud.

1. Cài một lần: bấm đúp **`scripts\pc\setup-ai-studio.bat`** (thêm `-With8B` để có model 8B chính xác hơn, `-NoWhisper` để bỏ phần nghe lời nói).
   Thư mục `D:\TramTinAI` (đổi bằng biến `AI_STUDIO_HOME`). Mọi file cố định phiên bản, kiểm tra SHA-256:
   llama.cpp b12703 (MIT, Vulkan + CPU), Qwen3-VL 4B/8B Instruct GGUF Q4_K_M (Apache-2.0, bản chính thức của Qwen),
   whisper.cpp b5907 + ggml-small (MIT), OpenCV (Apache-2.0) / NumPy (BSD).
2. Chạy `start-pc-worker.bat` như bình thường. Khoảng 1 phút sau trang AI Video Studio hiện "Bộ phân tích sẵn sàng" và tốc độ đo được.

Đo trên máy này (2× Xeon E5-2680 v4, 64 GB RAM, GTX 1050 Ti 4 GB, driver 532 / CUDA 12.1):
- Bản CUDA 12.4 của llama.cpp **lỗi** với driver này → dùng **Vulkan** (không cần cập nhật driver).
- Model 4B chạy hết trên card (~22 token/giây): khoảng 1,5–2,5 phút mỗi cảnh (8 khung hình) → video 22 giây 3 cảnh ≈ 8 phút.
- Model 8B chỉ vừa một phần trên card 4 GB (~3,4 token/giây): chính xác hơn về góc máy / thứ tự hành động nhưng chậm ~4–5 lần.
- Cắt cảnh, đo chuyển động máy quay (optical flow), màu (k-means) và chọn khung hình chạy bằng OpenCV, không cần AI: vài giây.
- Kết quả từng cảnh được lưu đệm trong `D:\TramTinAI\work\ai-studio\<dự án>`: huỷ / lỗi giữa chừng thì lần sau chạy tiếp từ cảnh chưa xong.

**Lời bình** (chủ đề Ký ức tuổi thơ mặc định bật): whisper nghe lời nói (chủ đề này ép tiếng Việt), đo giọng người đọc gốc
(cao độ, tốc độ đọc, độ lên xuống — không AI), model 4B dịch từng câu sang tiếng Anh (~10 giây). Mỗi câu thuộc đúng một cảnh,
được đưa vào prompt cảnh đó làm ngữ cảnh câu chuyện. Ở Storyboard, Gemini TTS (cùng khoá / bảng chi phí với Studio) đọc lại
bằng giọng gần giống người đọc gốc; clip được kéo dài cho vừa lời bình; khi ghép, mỗi cảnh có giọng AI của cảnh đó.
Muốn nghe chuẩn hơn ggml-small: đặt `ggml-large-v3-turbo*.bin` hoặc `ggml-medium.bin` vào `D:\TramTinAI\whisper` — bộ phân tích tự dùng model tốt nhất có sẵn.

Video tham chiếu trên R2 tự xoá sau 7 ngày (phân tích, prompt, khung hình chính được giữ), clip Seedance sau 14 ngày (đổi trong Cài đặt).
Bảng `Reup*` cũ vẫn còn trong cơ sở dữ liệu (không xoá dữ liệu); code không dùng nữa.
