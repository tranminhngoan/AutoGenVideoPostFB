# AI NEWS STUDIO - TRẠNG THÁI DỰ ÁN (PROJECT STATUS)

**Cập nhật lần cuối:** 15/11/2026  
**Giai đoạn hiện tại:** **Chế độ A: ArticleImageOnly / 60-Second News Video (HOÀN TẤT & ĐẠT CHUẨN KIỂM DUYỆT)**

---

## 1. Cập nhật Yêu cầu mới: Chế độ A – ArticleImageOnly (60-Giây News Video)

- [x] **Nguyên tắc cốt lõi ArticleImageOnly**:
  - Tắt hoàn toàn Veo API, AI Video Generation API, và AI Image Generation.
  - Tuyệt đối không tìm kiếm hoặc tải ảnh từ Internet bên ngoài URL bài báo.
  - Giữ nguyên vẹn nội dung hình ảnh thực tế (không biến đổi khuôn mặt, trang phục, nhân vật, chi tiết).
  - Chỉ dùng hiệu ứng chuyển động FFmpeg: `zoompan` (ZoomIn, ZoomOut, PanLeft, PanRight), `crop`, `scale` và `xfade`.
  - Nếu thiếu ảnh, tự động tái sử dụng ảnh bài báo với các góc crop (`CloseUp`, `Top`, `Bottom`) và hướng chuyển động khác nhau (`IsImageReused = true`). Nếu hoàn toàn không có ảnh, cảnh báo thiếu ảnh rõ ràng trên UI và QC, yêu cầu người dùng quyết định.
- [x] **Thông số kỹ thuật Video chuẩn Facebook Reels**:
  - Thời lượng: **55–65 giây (Target: 60 giây)**.
  - Tỷ lệ khung hình: **9:16 dọc (1080×1920)**.
  - Frame rate: 30 FPS, Codec video H.264, Codec âm thanh AAC/PCM.
- [x] **AI Editorial & Scene Planner (8–10 Phân cảnh)**:
  - Tóm tắt kịch bản tin tức 120–150 từ tiếng Việt (~2.3 từ/giây).
  - Hook nổi bật trong 3–4 giây đầu.
  - Bảo toàn tuyệt đối danh từ riêng, số liệu, thời gian, địa điểm; phân tách rõ Sự thật vs Bình luận.
  - Lập chính xác **8–10 phân cảnh** có đầy đủ: `SceneIndex`, `StartTime`, `Duration`, `VoiceOver`, `Subtitle`, `Headline`, `ArticleImageId`, `MotionType`, `TransitionType`, `CropType`, `IsImageReused`.
- [x] **Quality Control & Giao diện biên tập**:
  - Bổ sung kiểm soát bản quyền `QC_05_IMAGE_RIGHTS`: Chặn đứng xuất bản nếu phát hiện ảnh không thuộc bài báo gốc.
  - Bổ sung `QC_09_SPECS_COMPLIANCE`: Kiểm soát nghiêm ngặt thời lượng 55–65s và số phân cảnh 8–10 cảnh.
  - Giao diện Admin hiển thị badge ArticleImageOnly, banner cảnh báo bài báo thiếu ảnh, thẻ từng phân cảnh hiển thị Headline, VoiceOver, Subtitle, Crop Type, Motion effect và nhãn "Ảnh dùng lại".
- [x] **Kiểm thử tự động**:
  - 60/60 tests xUnit đạt 100% PASS, bao gồm test phát hiện ảnh ngoài bài báo là fatal violation và test lập 8–10 cảnh với kỹ thuật tái sử dụng ảnh an toàn.

---

## 2. Tổng quan tiến độ các Phase

| Phase | Nội dung | Trạng thái | Ghi chú |
|---|---|:---:|---|
| **Phase 1** | Khởi tạo dự án và kiến trúc | **HOÀN THÀNH** | .NET 8 Clean Architecture, Serilog, Swagger, HealthCheck, React Frontend, Dockerfile API & Worker |
| **Phase 2** | Database, Hangfire và Job Processing | **HOÀN THÀNH** | PostgreSQL (13 bảng), EF Core Migrations, Hangfire Server trong Worker, Pipeline Executor, Job API |
| **Phase 3** | Article Extractor và Image Extractor | **HOÀN THÀNH** | Bóc tách nội dung báo, trích xuất nghiêm ngặt ảnh nội bộ bài báo, tính hash SHA-256 deduplication, phân loại 6 nhóm, kiểm soát RightsStatus chặt chẽ |
| **Phase 4** | AI Editor và Script Planner | **HOÀN THÀNH** | Tích hợp linh hoạt Gemini & Claude, kịch bản 45-60s, tách bạch Fact vs Commentary, phân cảnh 6-10 scenes chỉ dùng ảnh bài báo hoặc typography |
| **Phase 5** | TTS và Video Renderer | **HOÀN THÀNH** | Edge-TTS / ElevenLabs tiếng Việt, phụ đề đồng bộ SRT/VTT, dựng video dọc 9:16 (1080×1920) chuẩn Reels bằng FFmpeg, quản lý lưu trữ tạm thời Ephemeral Storage |
| **Phase 6** | Video Review và Quality Control | **HOÀN THÀNH** | Rà soát tự động 10 tiêu chí QC nghiêm ngặt, cơ chế khóa xuất bản khi có lỗi nghiêm trọng, giao diện phê duyệt/từ chối/yêu cầu làm lại chuyên nghiệp |
| **Phase 7** | Facebook Graph API | **HOÀN THÀNH** | Kết nối Page, mã hóa token AES-256, quy trình upload Reels 3 bước (start, transfer, finish), chống đăng trùng lặp, Page Insights metrics, tự động hủy file tạm sau đăng |
| **Phase 8** | **Railway Deployment và Production Hardening** | **HOÀN THÀNH** | Cấu hình Railway multi-service, Dockerfile tối ưu (FFmpeg, fonts, curl), cơ chế tự động phục hồi Job gián đoạn, Rate Limiting, Admin Security, Quota đĩa tạm, Telemetry API |

---

## 2. Tiêu chí nghiệm thu Phase 8 (Production Hardening & Railway)

- [x] **Cấu hình Nền tảng Railway & Dockerfile Độc lập**:
  - `docker/Dockerfile.api`: Tối ưu hóa multi-stage build .NET 8 ASP.NET runtime, cài đặt `curl` cho healthcheck, cấp quyền thư mục tạm `/tmp/newsstudio`.
  - `docker/Dockerfile.worker`: Multi-stage build .NET 8 runtime, cài đặt đầy đủ `ffmpeg`, `fontconfig`, bộ phông chữ tiếng Việt (`fonts-dejavu-core`, `fonts-liberation`), thư mục tạm có quyền ghi, xử lý tín hiệu dừng ứng dụng an toàn qua `IHostApplicationLifetime` (SIGTERM graceful shutdown).
  - Tự động chuyển đổi `DATABASE_URL` dạng URI của Railway (`postgresql://...`) sang chuỗi kết nối Npgsql hợp lệ qua `ConnectionStringHelper`.
- [x] **Cơ chế Phục hồi Tác vụ khi Worker Khởi động lại (Job Recovery Service)**:
  - Triển khai `IJobRecoveryService` tự động chạy khi Worker khởi động:
    - Đối chiếu các dự án dang dở trong PostgreSQL (`Rendering`, `GeneratingAudio`, `Publishing`).
    - Kiểm tra tính nguyên vẹn của tệp video trên ổ đĩa tạm.
    - Chuyển an toàn về `AwaitingApproval` nếu video còn nguyên vẹn, hoặc đánh dấu `Failed` kèm thông báo rõ ràng nếu file tạm bị xóa.
    - **Bảo vệ tuyệt đối chống xuất bản lặp**: Nếu bài đăng đã tồn tại trên Facebook, dự án được chuyển về `Published`, cam kết không bao giờ đăng 2 lần.
- [x] **Quản trị Ổ đĩa Tạm & Thực thi Quota (Storage Cleanup Service)**:
  - Triển khai `IStorageCleanupService` chạy ngầm định kỳ 15 phút và có thể kích hoạt thủ công.
  - Tự động xóa các file tạm có `ExpiresAtUtc < DateTime.UtcNow`.
  - Kiểm tra tổng dung lượng ổ đĩa tạm so với ngưỡng `Storage:MaxTempStorageMb` (5120 MB). Tự động dọn dẹp các tệp cũ nhất khi vượt ngưỡng.
- [x] **Giám sát Telemetry & Theo dõi Chi phí AI/TTS (System Metrics Service)**:
  - Endpoint `GET /api/system/metrics`: Đo lường trực tiếp RAM WorkingSet (MB), CPU usage %, dung lượng ổ đĩa tạm (MB & %), độ trễ kết nối PostgreSQL (ms), số lượng Jobs trong hàng đợi, Uptime hệ thống.
  - Endpoint `GET /api/system/costs`: Thống kê tổng chi phí AI ($ USD), chi phí TTS ($ USD), số lượng tokens, ký tự, và chi phí trung bình trên mỗi video.
  - Nâng cấp màn hình **Dashboard Studio** hiển thị thanh đo dung lượng ổ đĩa tạm, độ trễ database, chỉ số RAM/Uptime, và các nút thao tác nhanh: **Dọn dẹp đĩa** & **Kiểm toán Job**.
- [x] **Bảo mật và Ổn định Production**:
  - Tự động chạy migrations PostgreSQL khi khởi động với retry logic.
  - Bổ sung ASP.NET Core RateLimiter (120 req/min).
  - Hỗ trợ khóa bảo vệ `Security__AdminApiKey` qua header `X-Admin-Api-Key`.
  - Cấu hình Dynamic CORS thích ứng với domain Railway (`FRONTEND_URL`, `RAILWAY_PUBLIC_DOMAIN`).
  - Middleware xử lý lỗi toàn cục theo chuẩn RFC 7807 ProblemDetails.
- [x] **Bộ Tài liệu Triển khai và Vận hành Đầy đủ**:
  - `docs/DEPLOYMENT_GUIDE.md`: Hướng dẫn triển khai chi tiết từng bước lên Railway.
  - `docs/ENVIRONMENT_VARIABLES.md`: Danh mục đầy đủ tất cả biến môi trường.
  - `docs/OPERATIONS_GUIDE.md`: Sổ tay vận hành, giám sát hàng ngày và tối ưu hóa chi phí.
  - `docs/RECOVERY_GUIDE.md`: Sổ tay xử lý sự cố, an toàn dữ liệu và sao lưu PostgreSQL.
  - `docs/FINAL_TEST_REPORT.md`: Báo cáo kiểm thử tổng kết toàn dự án.
- [x] **Kiểm thử Tự động Toàn diện**:
  - 59/59 test cases xUnit đạt 100% PASS (bao gồm 8 ca kiểm thử mới chuyên sâu cho Phase 8: phân tích URI Railway, phục hồi tác vụ, chống đăng trùng lặp, dọn dẹp ổ đĩa tạm, telemetry metrics, full system API controller, và mô phỏng tải đa dự án video đồng thời).

---

## 2. Tiêu chí nghiệm thu Phase 7

- [x] **Quản trị kết nối Fanpage & Bảo mật Token**:
  - Triển khai `IFacebookTokenProtectionService` mã hóa Access Token theo tiêu chuẩn AES-256 kèm IV ngẫu nhiên trước khi lưu vào PostgreSQL.
  - Tuyệt đối không ghi token vào log, không gửi token thô về frontend (chỉ trả về masked token `EAAB••••••••1234`).
  - Hỗ trợ xác thực token trực tiếp với endpoint Meta Graph API `me?fields=id,name`.
- [x] **Quy trình xuất bản Facebook Reels chuẩn Meta Graph API**:
  - Không hardcode phiên bản Graph API cũ (cấu hình linh hoạt qua `Facebook:GraphApiVersion`, mặc định `v21.0`).
  - Thực thi quy trình upload video Reels 3 bước:
    1. Khởi tạo upload session: `POST /{page-id}/video_reels` với `upload_phase=start` để nhận `video_id` và `upload_url`.
    2. Truyền tải nhị phân trực tiếp từ file tạm: `POST {upload_url}` stream byte từ `VideoAsset.LocalTempPath` (không cần lưu trữ lâu dài ở bên thứ ba).
    3. Hoàn tất và xuất bản: `POST /{page-id}/video_reels` với `upload_phase=finish`, `video_state=PUBLISHED`, tự động cấu trúc tiêu đề, hook, nguồn tin và hashtags.
  - Lưu Post ID và Video ID vào bảng `FacebookPosts` trong PostgreSQL.
- [x] **Cơ chế kiểm soát an toàn & chống đăng trùng lặp (Idempotency)**:
  - **Khóa phê duyệt bắt buộc**: Chặn dứt khoát việc xuất bản nếu video chưa được biên tập viên phê duyệt (`Decision != "Approved"`) hoặc còn lỗi nghiêm trọng.
  - **Idempotency**: Rà soát bảng `FacebookPosts`, chặn đăng trùng lặp nếu dự án đã từng xuất bản thành công.
- [x] **Dọn dẹp lưu trữ tạm thời Ephemeral Storage sau xuất bản**:
  - Tuân thủ quy tắc nghiệp vụ: **Không lưu MP4, ảnh hay audio lâu dài**.
  - Tự động gọi `IVideoAssetService.CleanupProjectAssetsAsync` giải phóng toàn bộ file MP4/audio/subtitles trên ổ đĩa ngay sau khi upload thành công lên Meta Graph API.
- [x] **Thu thập số liệu tương tác (Meta Page Insights API)**:
  - Tích hợp endpoint `/{video_id}/video_insights` thu thập số liệu thực tế: Views, Reach, Engagement, Shares, Comments, Watch Time.
  - Lưu lịch sử vào bảng `VideoMetrics`. Tuyệt đối không tự bịa số liệu khi Meta chưa cung cấp.
- [x] **REST APIs hoàn chỉnh cho Facebook Integration**:
  - `GET /api/facebook/pages`: Lấy danh sách Facebook Page đã kết nối.
  - `POST /api/facebook/verify`: Kiểm tra tính hợp lệ của Page Access Token.
  - `POST /api/facebook/pages`: Kết nối Page mới (mã hóa token AES-256).
  - `DELETE /api/facebook/pages/{id}`: Ngắt kết nối Page.
  - `POST /api/video-projects/{id}/publish-reel`: Xuất bản video lên Facebook Reels.
  - `GET /api/video-projects/{id}/facebook-posts`: Xem lịch sử bài đăng Reels của dự án.
  - `GET /api/facebook/posts/{postId}/metrics`: Cập nhật số liệu tương tác từ Page Insights.
- [x] **Giao diện Facebook Reels & Insights UI**:
  - Màn hình Cấu hình Facebook kết nối Fanpage, kiểm tra token, hiển thị quyền Graph API cần thiết.
  - Hộp thoại Xuất bản Reels trong Chi tiết Video: Chọn Fanpage, sửa caption, tùy chọn tự động dọn dẹp file tạm.
  - Tab Facebook Reels: Hiển thị liên kết xem video trực tiếp trên Facebook, mã Video ID/Post ID, và 6 thẻ chỉ số Insights trực quan (Views, Reach, Engagement, Shares, Comments, Watch time).
- [x] **Kiểm thử tự động**:
  - 51/51 test cases xUnit đạt 100% PASS (bao gồm 7 test cases chuyên sâu cho Token Encryption, kết nối Page, chặn đăng khi chưa duyệt, chống đăng trùng lặp, quy trình upload Reels, dọn dẹp file tạm sau đăng, thu thập Insights, và Controller API).

- [x] **Quality Controller tự động 10 tiêu chí**:
  - `QC_01_CONTENT_ACCURACY`: Độ chính xác nội dung so với bài báo gốc (độ dài kịch bản, bao quát sự kiện chính).
  - `QC_02_NO_FABRICATION`: Kiểm chứng dữ kiện, số liệu thực tế (bắt buộc trích xuất minh bạch từ bài báo, không bịa số liệu).
  - `QC_03_CHARACTER_ACCURACY`: Xác minh nhân vật & thực thể (không gán ghép hoặc suy đoán danh tính không có căn cứ).
  - `QC_04_IMAGE_SCENE_MATCH`: Độ phù hợp hình ảnh và phân cảnh (phù hợp với nhịp độ câu chuyện).
  - `QC_05_IMAGE_RIGHTS`: Bản quyền & nguồn gốc hình ảnh (chỉ dùng ảnh bài báo, kiểm tra RightsStatus, phát hiện ảnh Rejected).
  - `QC_06_SUBTITLE_QUALITY`: Chất lượng & đồng bộ phụ đề (đầy đủ file SRT/VTT, timestamp mili-giây không chồng chéo).
  - `QC_07_AUDIO_QUALITY`: Chất lượng giọng đọc & hòa âm (tệp WAV hợp lệ, không mất tiếng, âm lượng chuẩn).
  - `QC_08_NO_BLANK_SCENES`: Kiểm tra cảnh trống / màn hình đen (mọi scene bắt buộc có ảnh bài báo hoặc typography nổi bật).
  - `QC_09_SPECS_COMPLIANCE`: Chuẩn kỹ thuật Facebook Reels: Tỉ lệ 9:16 (1080×1920), 30 fps, thời lượng 45–60 giây.
  - `QC_10_EDITORIAL_VALUE`: Giá trị biên tập (có Hook mở đầu, phân tích bối cảnh, tách bạch Fact vs Analysis, dẫn nguồn báo chí).
- [x] **Cơ chế khóa xuất bản khi có lỗi nghiêm trọng (Fatal Violations)**:
  - Các lỗi nghiêm trọng (Sai thông tin, sai nhân vật, ảnh bị từ chối quyền sử dụng, âm thanh/video hỏng, cảnh trống) sẽ tự động bật cờ `HasFatalViolations = true` và **CHẶN PHÊ DUYỆT (`PublishingBlocked = true`)**.
  - Nếu biên tập viên cố bấm duyệt khi còn lỗi nghiêm trọng, API lập tức từ chối và thông báo nguyên nhân chi tiết.
- [x] **Công bố rõ ràng về điểm kiểm định nội bộ**:
  - Có thông báo minh bạch trên cả UI lẫn API: *"Điểm kiểm định nội bộ là tiêu chuẩn biên tập của AI News Studio, không phản ánh thuật toán của Facebook."*
- [x] **Bộ công cụ điều hướng & phê duyệt chuyên nghiệp**:
  - **Phê duyệt (`Approve`)**: Mở khóa video sẵn sàng cho Phase 7 (đăng lên Meta Graph API).
  - **Từ chối (`Reject`)**: Yêu cầu nhập lý do chi tiết từ biên tập viên để ghi vào lịch sử review.
  - **Yêu cầu làm lại (`RequestAction`)**: `RegenerateScript` (tạo lại kịch bản), `RegenerateAudio` (tạo lại audio/subtitles), `ReRender` (render lại video).
  - **Chỉnh sửa metadata**: Cập nhật tiêu đề video, hook và caption nổi bật trực tiếp trên UI.
  - **Xóa dự án video**: Xóa sạch bản ghi dự án và giải phóng toàn bộ file media tạm trong bộ nhớ đệm.
- [x] **REST APIs hoàn chỉnh cho Review & QC**:
  - `GET /api/video-projects/{id}/review`: Lấy toàn bộ thông tin review và 10 tiêu chí kiểm định.
  - `POST /api/video-projects/{id}/review/evaluate`: Kích hoạt đánh giá 10 tiêu chí tự động.
  - `POST /api/video-projects/{id}/review/approve`: Phê duyệt video (chặn khi có lỗi nghiêm trọng).
  - `POST /api/video-projects/{id}/review/reject`: Từ chối video kèm ghi chú.
  - `POST /api/video-projects/{id}/review/request-action`: Yêu cầu thực hiện lại từng công đoạn.
  - `PUT /api/video-projects/{id}/review/metadata`: Chỉnh sửa tiêu đề và caption.
  - `DELETE /api/video-projects/{id}`: Xóa dự án và dọn sạch file tạm.
- [x] **Giao diện Video Review & Quality Control UI**:
  - Tích hợp tab QC với bảng 10 tiêu chí, điểm số trực quan (x/10), trạng thái Đạt/Vi phạm, điểm tổng kết (thang 100).
  - Banner cảnh báo lỗi nghiêm trọng nhấp nháy nổi bật khi phát hiện vi phạm.
  - Toolbar hành động: Phê duyệt, Từ chối (Modal nhập lý do), Sửa tiêu đề (Modal), Yêu cầu tạo lại kịch bản/audio/video, Xóa dự án.
  - Nút xuất bản Facebook Reels được bảo vệ nghiêm ngặt: chỉ sáng khi đã được biên tập viên duyệt và không có lỗi nghiêm trọng.
- [x] **Kiểm thử tự động**:
  - 44/44 test cases xUnit đạt 100% PASS (bao gồm 7 test cases chuyên sâu cho Quality Controller, phát hiện lỗi bản quyền ảnh, chặn duyệt khi vi phạm, phê duyệt thành công, từ chối kèm lý do, sửa metadata, và full API flow).

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
