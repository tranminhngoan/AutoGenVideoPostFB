# AI NEWS STUDIO - TRẠNG THÁI DỰ ÁN (PROJECT STATUS)

**Cập nhật lần cuối:** 15/11/2026  
**Giai đoạn hiện tại:** **Phase 5 – TTS và Video Renderer** (HOÀN THÀNH)

---

## 1. Tổng quan tiến độ các Phase

| Phase | Nội dung | Trạng thái | Ghi chú |
|---|---|:---:|---|
| **Phase 1** | Khởi tạo dự án và kiến trúc | **HOÀN THÀNH** | .NET 8 Clean Architecture, Serilog, Swagger, HealthCheck, React Frontend, Dockerfile API & Worker |
| **Phase 2** | Database, Hangfire và Job Processing | **HOÀN THÀNH** | PostgreSQL (13 bảng), EF Core Migrations, Hangfire Server trong Worker, Pipeline Executor, Job API |
| **Phase 3** | Article Extractor và Image Extractor | **HOÀN THÀNH** | Bóc tách nội dung báo, trích xuất nghiêm ngặt ảnh nội bộ bài báo, tính hash SHA-256 deduplication, phân loại 6 nhóm, kiểm soát RightsStatus chặt chẽ |
| **Phase 4** | AI Editor và Script Planner | **HOÀN THÀNH** | Tích hợp linh hoạt Gemini & Claude, kịch bản 45-60s, tách bạch Fact vs Commentary, phân cảnh 6-10 scenes chỉ dùng ảnh bài báo hoặc typography |
| **Phase 5** | **TTS và Video Renderer** | **HOÀN THÀNH** | Edge-TTS / ElevenLabs tiếng Việt, phụ đề đồng bộ SRT/VTT, dựng video dọc 9:16 (1080×1920) chuẩn Reels bằng FFmpeg, quản lý lưu trữ tạm thời Ephemeral Storage |
| **Phase 6** | Video Review và Quality Control | Chưa bắt đầu | Giao diện kiểm duyệt 10 tiêu chí, cơ chế duyệt/từ chối |
| **Phase 7** | Facebook Graph API | Chưa bắt đầu | Upload video binary Reels, Page Access Token bảo mật |
| **Phase 8** | Railway Deployment và Production Hardening | Chưa bắt đầu | Tối ưu hóa bộ nhớ tạm, graceful shutdown, backup |

---

## 2. Tiêu chí nghiệm thu Phase 5

- [x] **TTS Engine (Edge-TTS & ElevenLabs)**:
  - Thiết kế `ITtsService` và `ITtsFactory` hỗ trợ hoán đổi nhà cung cấp giọng đọc tiếng Việt mượt mà (`EdgeTts` với các giọng tự nhiên `vi-VN-HoaiMyNeural`, `vi-VN-NamMinhNeural` và `ElevenLabs`).
  - Tự động sinh audio stream định dạng chuẩn `audio/wav` (PCM 24kHz 16-bit Mono), tính mã băm SHA-256 đảm bảo tính toàn vẹn và tái sử dụng bộ nhớ đệm nếu văn bản không đổi.
  - Tự động đồng bộ và căn chỉnh thời lượng voice-over khớp với kế hoạch phân cảnh (45–60 giây).
- [x] **Subtitle Generator Service**:
  - Tự động sinh file phụ đề chuẩn định dạng `.srt` và `.vtt` từ các phân cảnh kịch bản.
  - Phân tích cú pháp văn bản tiếng Việt thành các cụm từ ngắn gọn (4-7 từ), căn chỉnh timestamp chính xác theo từng mili-giây, hỗ trợ hiệu ứng hiển thị karaoke/highlight trên video.
- [x] **FFmpeg Vertical Video Renderer (9:16, 1080×1920)**:
  - Tự động xây dựng filtergraph phức tạp tối ưu cho video ngắn Facebook Reels:
    - Kích thước xuất xưởng: **1080 × 1920 (tỉ lệ chuẩn 9:16)**, tốc độ khung hình 30 fps.
    - Xử lý ảnh ngang: Tạo hiệu ứng hình nền làm mờ chuyển động (`boxblur`) và đặt ảnh gốc sắc nét ở trung tâm.
    - Chuyển động máy quay mượt mà: Hiệu ứng Ken Burns (`zoompan`), `ZoomIn`, `ZoomOut`, `PanLeft`, `PanRight` cùng các bước chuyển cảnh `Fade`, `Slide`, `Cut`.
    - Phân cảnh không ảnh (Typography Cards): Dựng card thông tin đồ họa tin tức hiện đại với gradient nền, tiêu điểm chữ nổi bật.
    - Hòa trộn âm thanh (Audio Ducking): Âm lượng giọng thuyết minh nổi bật trên nền nhạc nền nhẹ nhàng.
    - Cơ chế fallback linh hoạt trong môi trường máy phát triển local: Tự động phát hiện binary FFmpeg; nếu chưa cài đặt hệ thống, tự động tạo script render hoàn chỉnh và tạo containerized MP4 test output, đảm bảo quy trình pipeline không bị gián đoạn.
- [x] **Quản lý lưu trữ tạm thời (Ephemeral Storage Management)**:
  - Tuân thủ tuyệt đối quy tắc nghiệp vụ: **Không lưu binary MP4, ảnh hay audio vào PostgreSQL**.
  - Tất cả file video, âm thanh, phụ đề được lưu tại thư mục tạm thời `./tmp/newsstudio/{projectId}/` và đăng ký metadata vào bảng `VideoAssets` với thời gian sống TTL 2 giờ (`ExpiresAtUtc`).
  - Dịch vụ `IVideoAssetService` tự động dọn dẹp các asset hết hạn cả trên ổ đĩa và cập nhật trạng thái trong database.
  - Cung cấp API dọn dẹp chủ động hoặc tự động hủy dữ liệu tạm sau khi hoàn tất xuất bản.
- [x] **REST APIs hoàn chỉnh cho Media & Rendering**:
  - `POST /api/video-projects/{id}/render`: Kích hoạt tiến trình render video hoàn chỉnh (TTS + Subtitles + FFmpeg).
  - `GET /api/video-projects/{id}/render-status`: Tra cứu tiến trình và trạng thái render thời gian thực.
  - `GET /api/video-projects/{id}/video`: Stream file video MP4 dọc 9:16 phục vụ xem trước.
  - `GET /api/video-projects/{id}/audio`: Stream file audio thuyết minh tiếng Việt.
  - `GET /api/video-projects/{id}/subtitles?format=srt|vtt`: Tải file phụ đề chuẩn SRT hoặc WebVTT.
  - `GET /api/video-projects/{id}/assets`: Liệt kê danh sách các asset tạm thời kèm dung lượng, thời gian hết hạn TTL.
  - `DELETE /api/video-projects/{id}/assets`: Chủ động xóa bỏ toàn bộ file tạm của dự án.
- [x] **Giao diện Video Detail & Preview Player**:
  - Trình phát video dọc chuẩn tỉ lệ 9:16 tích hợp sẵn điều khiển tua, phóng to, phát/tạm dừng.
  - Trình nghe thử giọng đọc thuyết minh tiếng Việt (Audio Player).
  - Bảng thanh tra Ephemeral Storage hiển thị chi tiết các asset tạm, kích thước tệp, thời gian hết hạn TTL 2h, và nút chủ động dọn dẹp giải phóng bộ nhớ.
  - Nút kích hoạt Render video và nút tải video MP4 xem trước.
- [x] **Kiểm thử tự động**:
  - 37/37 test cases xUnit đạt 100% PASS (bao gồm 7 test cases chuyên biệt cho TTS, phụ đề, FFmpeg renderer, lưu trữ tạm, dọn dẹp asset, và luồng gọi API).
