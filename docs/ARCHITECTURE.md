# AI NEWS STUDIO - TÀI LIỆU KIẾN TRÚC HỆ THỐNG (ARCHITECTURE)

## 1. Mô hình kiến trúc tổng thể

Hệ thống được thiết kế theo nguyên lý **Clean Architecture** và hướng dịch vụ phân tách rõ rệt giữa lớp nhận yêu cầu (**NewsStudio.Api**) và lớp xử lý tác vụ nặng bất đồng bộ (**NewsStudio.Worker**).

```
                      +-----------------------------+
                      |      ReactJS + TS Admin     |
                      |   (Vite + Tailwind CSS)     |
                      +--------------+--------------+
                                     |
                               REST API / JSON
                                     v
                      +-----------------------------+
                      |       NewsStudio.Api        |
                      |  - Swagger / OpenAPI        |
                      |  - Serilog Request Logging  |
                      |  - HealthCheck Endpoints    |
                      |  - Hangfire Dashboard       |
                      |  - ArticlesController       |
                      +--------------+--------------+
                                     |
                          Tạo Job & Truy vấn DB
                                     v
      +------------------------------+-------------------------------+
      |                                                              |
      v                                                              v
+-----------------------------+                      +-------------------------------+
|     NewsStudio.Worker       |                      |     PostgreSQL Database       |
|  - Hangfire Server          |                      |  - Schema public: 13 tables   |
|  - Article / Image Extract  |<====================>|  - Schema hangfire: queue/jobs|
|  - AI Scripting (Gemini)    |                      |  - Dữ liệu nghiệp vụ          |
|  - TTS Audio Synthesis      |                      |  - Metadata VideoProjects     |
|  - FFmpeg Video Renderer    |                      |  (Không lưu binary media)     |
|  - Ephemeral Temp Storage   |                      +-------------------------------+
+--------------+--------------+
               |
               v
+-----------------------------+
|    Meta Graph API (Reels)   |
|  - Fanpage Reels Publishing |
+-----------------------------+
```

---

## 2. Chi tiết Database Schema (PostgreSQL 16)

Hệ thống quản lý 13 thực thể nghiệp vụ thông qua EF Core 8:
1. **Articles**: Lưu trữ thông tin bài báo (Title, NormalizedTitle, Url, CanonicalUrl, Publisher, ContentBody, PublishedAtUtc, Status).
2. **ArticleImages**: Quản lý hình ảnh gốc bài báo (OriginalImageUrl, LocalTempPath, Caption, AltText, Order, Width, Height, ImageHash, RightsStatus, Category, Status).
3. **VideoProjects**: Trung tâm dự án video (Title, SourceUrl, Publisher, TargetDurationSeconds, Status theo 14 vòng đời, Resolution 1080x1920, AspectRatio 9:16).
4. **VideoScenes**: Phân cảnh chi tiết (SceneNumber, StartTime, EndTime, VoiceText, OnScreenText, SelectedArticleImageId, MotionType, TransitionType, ScenePurpose).
5. **VideoScripts**: Kịch bản do AI biên soạn (Title, Hook, VoiceOverScript, KeyFacts, Context, Analysis, Conclusion, SourceAttribution, TokenUsage).
6. **VideoAssets**: Theo dõi các file tạm (AssetType, LocalTempPath, FileSizeBytes, MimeType, Sha256Hash, ExpiresAtUtc, IsCleanedUp).
7. **ProcessingJobs**: Theo dõi tiến trình Hangfire (HangfireJobId, Step, Status, RetryCount, ProgressPercentage, IdempotencyKey, WorkerHost).
8. **VideoReviews**: Báo cáo kiểm định chất lượng nội bộ (ReviewedBy, Decision, FeedbackNotes, InternalQualityScore, SeriousViolationsFound).
9. **FacebookAccounts**: Quản lý kết nối Page (PageId, PageName, EncryptedAccessToken, TokenExpiresAtUtc, IsActive).
10. **FacebookPosts**: Bản ghi xuất bản Reels (FacebookPostId, FacebookVideoId, UploadSessionId, UploadStatus, PublishedAtUtc).
11. **VideoMetrics**: Chỉ số đo lường sau xuất bản (Views, Reach, Engagement, Shares, Comments, WatchTimeSeconds, CollectedAtUtc).
12. **SystemSettings**: Bảng cấu hình động theo nhóm (AI, TTS, Storage, Facebook, Hangfire).
13. **AuditLogs**: Nhật ký kiểm toán thao tác hệ thống (EntityName, EntityId, Action, PerformedBy, DetailsJson).

---

## 3. Vòng đời tác vụ (Job Lifecycle)

Tác vụ xử lý video trải qua 14 trạng thái kế tiếp:
1. `Pending`: Dự án vừa được tạo từ URL.
2. `Extracting`: Worker đang tải và bóc tách HTML bài báo.
3. `Extracted`: Nội dung và danh sách ảnh gốc đã sẵn sàng.
4. `Analyzing`: AI đang phân tích dữ liệu, bối cảnh, sự kiện.
5. `Scripting`: AI biên tập kịch bản 45-60 giây.
6. `PlanningScenes`: Chia kịch bản thành 6-10 cảnh, ghép ảnh bài báo.
7. `GeneratingAudio`: Lồng tiếng tiếng Việt qua TTS.
8. `Rendering`: FFmpeg kết xuất video 9:16 (1080×1920).
9. `QualityChecking`: Kiểm định tự động 10 tiêu chí chất lượng.
10. `AwaitingApproval`: Video hoàn tất và **dừng lại chờ người dùng phê duyệt**.
11. `Publishing`: Đang upload video binary lên Facebook Reels.
12. `Published`: Đăng Reels thành công, lưu Post ID/Video ID.
13. `Failed`: Tác vụ gặp lỗi không thể phục hồi.
14. `Cancelled`: Tác vụ bị hủy bởi người dùng hoặc hệ thống.

---

## 4. Xử lý bất đồng bộ với Hangfire

- **Storage**: Lưu trữ job state trong PostgreSQL schema `hangfire`.
- **Worker Process**: `NewsStudio.Worker` khởi chạy `BackgroundJobServer` với số lượng worker xử lý đồng thời (`WorkerCount = 2`).
- **Idempotency & Retry**: Mỗi job được gắn `IdempotencyKey`. Sử dụng `[AutomaticRetry(Attempts = 3)]` có giới hạn và ghi log rõ ràng theo `JobId`.
- **API không chặn**: `NewsStudio.Api` chỉ tiếp nhận yêu cầu, sinh ID, lưu DB và chuyển giao cho Hangfire qua `IBackgroundJobClient.Enqueue`.

---

## 5. Kiến trúc trích xuất bài báo và kiểm soát hình ảnh (Phase 3)

### 5.1. Mô hình Adapter bóc tách bài báo (`IWebsiteAdapter`)
Áp dụng mẫu **Chain of Responsibility**:
- `VnExpressAdapter`: Nhận diện `vnexpress.net`, bóc tách `h1.title-detail`, `p.description`, đoạn văn `p.Normal` trong `article.fck_detail`, khối ảnh `figure.tplCaption` kèm chú thích `figcaption p.Image`.
- `TuoiTreAdapter`: Nhận diện `tuoitre.vn`, bóc tách `h1.article-title`, `h2.article-sapo`, khối ảnh `div.VCSortable` / `PhotoCMS_Caption`.
- `ThanhNienAdapter`: Nhận diện `thanhnien.vn`, bóc tách cấu trúc bài báo Thanh Niên.
- `DanTriAdapter`: Nhận diện `dantri.com.vn`, bóc tách cấu trúc bài báo Dân Trí.
- `GenericArticleAdapter`: Bộ thích ứng cơ sở và dự phòng cho mọi trang tin: ưu tiên JSON-LD `NewsArticle` (`schema.org`), OpenGraph (`og:title`, `og:description`, `og:image`), semantic HTML (`<article>`, `<figure>`, `<figcaption>`).

### 5.2. Nguyên tắc bất biến trích xuất hình ảnh
- **Chỉ trích xuất ảnh gốc trong chính bài báo**: Không lấy ảnh từ Google Images, không dùng ảnh AI, không tạo nhân vật giả mạo.
- **Lọc nhiễu triệt để**: Tự động loại bỏ ảnh banner quảng cáo (`/ads/`, `banner`), ảnh avatar, icon mạng xã hội, pixel theo dõi.
- **Deduplication bằng SHA-256**: Băm toàn bộ URL ảnh để triệt tiêu trùng lặp và đánh chỉ số thứ tự rõ ràng (`ArticleImageOrder`).
- **Xử lý bài báo không có ảnh**: Hệ thống bóc tách an toàn và cho phép kịch bản video sử dụng nền typography hoặc số liệu ở các phase sau.

### 5.3. Kiểm soát quyền sử dụng (`RightsStatus`)
- Các trạng thái: `Unknown`, `PermissionRequired`, `Licensed`, `Authorized`, `Rejected`.
- **Quy tắc cứng**: Quyền hình ảnh khi trích xuất **bắt buộc mặc định là `Unknown`**. Hệ thống tuyệt đối không tự động nâng cấp lên `Authorized`. Việc cấp quyền phải do biên tập viên xác nhận trên giao diện kiểm duyệt.

### 5.4. Phân loại hình ảnh tự động (`ImageClassifierService`)
- Phân tích ngữ cảnh nội dung và caption ảnh thành 6 nhóm:
  1. `Panorama` (Toàn cảnh / Không gian)
  2. `Character` (Nhân vật / Chuyên gia - không tự suy đoán danh tính nếu không có căn cứ)
  3. `Event` (Sự kiện / Hội nghị / Lễ ký kết)
  4. `Document` (Tài liệu / Văn bản / Quyết định)
  5. `Chart` (Biểu đồ / Đồ thị / Số liệu)
  6. `Unsuitable` (Không phù hợp / Quảng cáo)

---

## 6. Kiến trúc AI Editor và Script Planner (Phase 4)

### 6.1. Thiết kế đa nhà cung cấp AI (`IAIEditorService` & `IAiEditorFactory`)
Hệ thống áp dụng mẫu **Factory / Strategy** cho phép thay đổi nhà cung cấp AI linh hoạt giữa Google Gemini và Anthropic Claude:
- `GeminiAiEditorService`: Sử dụng REST API của Gemini (`gemini-1.5-flash`, `gemini-1.5-pro`), tính toán token usage và chi phí ước tính ($0.075 / 1M input, $0.30 / 1M output).
- `ClaudeAiEditorService`: Sử dụng REST API của Claude (`claude-3-5-haiku-20241022`, `claude-3-5-sonnet-20241022`), tính toán token usage và chi phí ước tính.
- `Deterministic Editorial Engine`: Động cơ tổng hợp tin tức ngữ nghĩa cục bộ đảm bảo hệ thống và các luồng kiểm thử chạy ổn định ngay cả khi chưa có API key ngoài hoặc mất kết nối mạng.

### 6.2. Cấu trúc kịch bản tin tức ngắn (45–60 giây)
- **Chuẩn thời lượng**: 110–150 từ tiếng Việt, tốc độ đọc 2.3 từ/giây.
- **Tách bạch Fact vs Commentary**:
  - `KeyFacts`: Dữ kiện khách quan (số liệu, thời gian, sự kiện, địa điểm).
  - `Context`: Bối cảnh cần biết.
  - `Analysis`: Đánh giá tác động, góc nhìn chuyên môn, tạo giá trị gia tăng riêng cho video.
  - `Hook`: 3-5 giây mở đầu cuốn hút, giữ chân người xem lướt Reels.
  - `Conclusion`: Lời kết súc tích hoặc câu hỏi mở.
  - `SourceAttribution`: Dẫn nguồn báo chí chính thức (ví dụ: 'Theo VnExpress').

### 6.3. Script Planner (6–10 Scenes) & Quy tắc gán hình ảnh
- `ScriptPlannerService` tự động phân tách kịch bản thành 6 đến 10 phân cảnh liên tục:
  - `StartTimeSeconds` và `EndTimeSeconds` phân bổ theo độ dài lời thoại, tổng thời lượng nằm trọn vẹn trong 45–60 giây.
  - `OnScreenText`: Dòng chữ nổi bật ngắn gọn (3-7 từ) viết hoa điểm nhấn.
  - **Quy tắc gán hình ảnh**:
    - **Chỉ sử dụng `ArticleImageId` của bài báo hiện tại**, ưu tiên ảnh hợp lệ (`RightsStatus != Rejected`, `Category != Unsuitable`).
    - Nếu bài báo thiếu ảnh hoặc không có ảnh: Tự động gán `VisualType = "Typography"` hoặc `"MetricChart"`, hiển thị typography chuyển động trên nền tối. Tuyệt đối không sinh ảnh AI, không lấy ảnh ngoài.
  - Cấu hình chuyển động: `MotionType` (`ZoomIn`, `ZoomOut`, `PanLeft`, `PanRight`, `KenBurns`) và `TransitionType` (`Fade`, `Slide`, `Cut`).

---

## 7. Động cơ TTS, Phụ đề & FFmpeg Video Renderer (Phase 5)

### 7.1. Kiến trúc TTS (Edge-TTS & ElevenLabs)
- Hệ thống áp dụng mẫu **Factory / Strategy** thông qua `ITtsFactory` và `ITtsService`.
- **EdgeTtsService**: Sử dụng giọng đọc thần kinh tự nhiên của Microsoft Edge (`vi-VN-HoaiMyNeural`, `vi-VN-NamMinhNeural`) hoàn toàn miễn phí, sinh luồng âm thanh PCM 24kHz 16-bit Mono chuẩn WAV, băm SHA-256 để tránh sinh trùng lặp.
- **ElevenLabsTtsService**: Hỗ trợ chuyển sang giọng đọc cao cấp ElevenLabs đa ngôn ngữ khi có API Key.
- Tự động căn chỉnh tốc độ đọc (speech rate: ~2.3 từ/giây) và phân bổ thời lượng khớp hoàn hảo với 6-10 phân cảnh kịch bản (tổng 45-60 giây).

### 7.2. Subtitle Generator Service
- Hỗ trợ xuất đồng thời hai định dạng phụ đề tiêu chuẩn:
  - **SRT (SubRip)**: Tương thích hoàn hảo với FFmpeg burner filter và các trình dựng video.
  - **WebVTT (Web Video Text Tracks)**: Tương thích với thẻ `<track>` HTML5 trên trình duyệt web.
- Tự động phân đoạn lời thoại thành các cụm từ ngắn gọn (4-7 từ), timestamp căn chỉnh chính xác từng mili-giây, hỗ trợ làm nổi bật chữ hiện tại (karaoke highlight).

### 7.3. Động cơ FFmpeg Vertical 9:16 Video Filtergraph
- **Chuẩn đầu ra Facebook Reels**: Độ phân giải **1080 × 1920 (tỉ lệ 9:16)**, 30 fps, codec `h264`, âm thanh `aac` 192 kbps.
- **Xử lý ảnh ngang (Horizontal / Landscape Image)**:
  - Tách luồng thành 2 layer: Layer nền được phóng to và làm mờ (`boxblur=15:5`), layer trên cùng giữ nguyên tỉ lệ gốc đặt chính giữa.
- **Chuyển động điện ảnh (Cinematic Camera Motion)**:
  - Tích hợp hiệu ứng Ken Burns mượt mà bằng `zoompan` filter (`ZoomIn`, `ZoomOut`, `PanLeft`, `PanRight`).
  - Chuyển cảnh mềm mại giữa các scene (`fade`, `xfade`, `slide`).
- **Phân cảnh chữ (Typography Cards)**:
  - Khi phân cảnh không có ảnh bài báo (`VisualType = "Typography"`), dựng card đồ họa thông tin hiện đại với gradient nền tối và tiêu điểm chữ typography nổi bật.
- **Hòa âm tự động (Audio Ducking)**:
  - Hòa trộn giọng đọc thuyết minh chính với nhạc nền tin tức nhẹ nhàng, tự động giảm âm lượng nhạc nền khi có giọng nói.
- **Cơ chế Dev/Test Fallback**:
  - Tự động định vị binary FFmpeg trên hệ thống. Trong môi trường test hoặc local chưa có binary, tự động tạo script FFmpeg hoàn chỉnh và tạo file MP4 containerized test hợp lệ để pipeline tiếp tục hoạt động thông suốt. Trên Railway/Docker, FFmpeg được cài đặt sẵn qua `apt-get install -y ffmpeg`.

### 7.4. Quản lý lưu trữ tạm thời (Ephemeral Storage Lifecycle)
- **Quy tắc tuyệt đối**: **Không lưu binary MP4, ảnh hay audio vào PostgreSQL**.
- Toàn bộ file media phát sinh được lưu tạm trong thư mục đệm hệ thống: `./tmp/newsstudio/{projectId}/`.
- Mỗi file được ghi nhận vào bảng `VideoAssets` với metadata: `AssetType`, `LocalTempPath`, `FileSizeBytes`, `MimeType`, `Sha256Hash`, `ExpiresAtUtc` (mặc định 2 giờ).
- Dịch vụ `IVideoAssetService` chịu trách nhiệm quét dọn và hủy các tệp hết hạn cả trên ổ cứng vật lý lẫn cập nhật cờ `IsCleanedUp = true` trong database. Có API cho phép chủ động dọn dẹp hoặc dọn dẹp tự động sau khi xuất bản lên Facebook.

---

## 8. Động cơ Video Review & Quality Control (Phase 6)

### 8.1. Hệ thống rà soát 10 tiêu chí chất lượng (`IQualityControllerService`)
Hệ thống triển khai dịch vụ kiểm định tự động theo 10 tiêu chí nghiêm ngặt, cho điểm từng tiêu chí (thang điểm 10/tiêu chí, tổng điểm 100):
1. **QC_01_CONTENT_ACCURACY** (Content): Nội dung kịch bản bám sát thông điệp bài báo, dung lượng từ chuẩn (110-150 từ).
2. **QC_02_NO_FABRICATION** (Factuality): Dữ kiện số liệu thực tế được trích xuất minh bạch, không tự bịa đặt.
3. **QC_03_CHARACTER_ACCURACY** (Factuality): Tên nhân vật, danh tính và tổ chức chuẩn xác theo nguồn bài viết.
4. **QC_04_IMAGE_SCENE_MATCH** (Visual): Hình ảnh hoặc typography đồ họa ăn khớp với nhịp độ phân cảnh.
5. **QC_05_IMAGE_RIGHTS** (Legal & Compliance): Bản quyền và nguồn gốc hình ảnh. Phát hiện ảnh bị `Rejected` hoặc `Unknown` cần rà soát.
6. **QC_06_SUBTITLE_QUALITY** (Subtitles): Chất lượng phụ đề SRT/VTT, timestamp mili-giây không chồng chéo.
7. **QC_07_AUDIO_QUALITY** (Audio): Âm thanh thuyết minh PCM WAV hợp lệ, không mất tiếng, âm lượng chuẩn.
8. **QC_08_NO_BLANK_SCENES** (Visual): Không có cảnh trống hoặc màn hình đen (mỗi cảnh đều có ảnh bài báo hoặc typography).
9. **QC_09_SPECS_COMPLIANCE** (Technical): Chuẩn Facebook Reels 9:16 (1080×1920), 30 fps, thời lượng 45–60 giây.
10. **QC_10_EDITORIAL_VALUE** (Editorial): Giá trị biên tập gia tăng: Có Hook mở đầu, tách biệt Fact vs Analysis, có Source Attribution.

### 8.2. Cơ chế khóa xuất bản khi có lỗi nghiêm trọng (Fatal Violations)
- **Danh mục lỗi nghiêm trọng**:
  - Sai thông tin (`FalseInformation`)
  - Sai nhân vật (`WrongCharacter`)
  - Ảnh không được phép sử dụng (`UnauthorizedImage` / `Rejected`)
  - Âm thanh hỏng (`AudioBroken`)
  - Video hỏng (`VideoBroken`)
  - Cảnh trống hoàn toàn (`BlankScene`)
- **Nguyên tắc bảo vệ**: Khi phát hiện lỗi nghiêm trọng, hệ thống tự động gán `HasFatalViolations = true` và **CHẶN PHÊ DUYỆT (`PublishingBlocked = true`)**. Mọi yêu cầu phê duyệt thông qua API đều bị chặn với thông báo lỗi rõ ràng.

### 8.3. Quản lý vòng đời kiểm duyệt (`IVideoReviewService`)
- Bản ghi `VideoReview` lưu giữ lịch sử kiểm định: `InternalQualityScore`, `Decision` (`Pending`, `Approved`, `Rejected`, `NeedsScriptRegen`, `NeedsAudioRegen`, `NeedsReRender`), `FeedbackNotes`, `ViolationDetails`.
- Hỗ trợ các hành động điều hướng nhanh:
  - `Approve`: Phê duyệt video cho xuất bản (chỉ khả dụng khi không có lỗi nghiêm trọng và video đã render).
  - `Reject`: Từ chối video kèm lý do chi tiết từ biên tập viên.
  - `RequestAction`: Yêu cầu AI/Renderer tạo lại kịch bản, audio hoặc render lại video.
  - `UpdateMetadata`: Cập nhật tiêu đề video, hook và caption phân cảnh trực tiếp.
  - `DeleteProject`: Xóa dự án và giải phóng toàn bộ file tạm thời.
- **Tuyên bố minh bạch**: Điểm kiểm định nội bộ là tiêu chuẩn biên tập của AI News Studio, không phản ánh thuật toán xếp hạng của Facebook.

---

## 9. Tích hợp Meta Graph API & Xuất bản Facebook Reels (Phase 7)

### 9.1. Quản lý Token & Mã hóa An toàn (`IFacebookTokenProtectionService`)
- Áp dụng chuẩn mã hóa đối xứng **AES-256** với vector khởi tạo (IV) ngẫu nhiên cho mỗi bản ghi trước khi lưu trữ vào trường `EncryptedAccessToken` của bảng `FacebookAccounts`.
- Khóa bảo mật được bảo vệ tập trung thông qua biến môi trường hoặc cấu hình hệ thống.
- **Nguyên tắc an ninh nghiêm ngặt**:
  - Không ghi token thô vào Serilog hoặc bất kỳ file log nào.
  - Không gửi token thô về giao diện người dùng frontend (chỉ trả về masked token dạng `EAAB••••••••1234`).
  - Hỗ trợ xác thực tính hợp lệ của token trực tiếp với Graph API endpoint `/me?fields=id,name`.

### 9.2. Quy trình xuất bản Reels 3 bước (Page Reels Publishing Protocol)
Hệ thống sử dụng quy trình upload chuẩn của Meta Graph API (phiên bản `v21.0` có thể cấu hình linh hoạt):
1. **Khởi tạo session (Upload Phase: `start`)**:
   - `POST https://graph.facebook.com/{version}/{page_id}/video_reels` kèm `upload_phase=start`.
   - Nhận về `video_id` và `upload_url`.
2. **Truyền tải dữ liệu video nhị phân (Upload Phase: `transfer`)**:
   - `POST {upload_url}` stream trực tiếp mảng byte từ file tạm trên ổ đĩa (`VideoAsset.LocalTempPath`) với header `Authorization: OAuth {token}`, `file_offset: 0` và `Content-Type: application/octet-stream`.
   - Không cần tải video lên dịch vụ lưu trữ bên thứ ba chỉ để đăng tải, tối ưu băng thông và bảo mật.
3. **Hoàn tất và xuất bản (Upload Phase: `finish`)**:
   - `POST https://graph.facebook.com/{version}/{page_id}/video_reels` với `upload_phase=finish`, `video_state=PUBLISHED`, `video_id` và `description` bao gồm Tiêu đề, Hook, Nguồn tin tức và Hashtags tự động.
   - Lưu trữ bản ghi vào bảng `FacebookPosts`: `FacebookPostId`, `FacebookVideoId`, `PublishedAtUtc`, `UploadStatus = "Published"`.
   - Cập nhật trạng thái `VideoProject.Status = VideoProjectStatus.Published`.

### 9.3. Cơ chế bảo vệ, Chống trùng lặp & Dọn dẹp bộ nhớ tạm
- **Khóa phê duyệt (Approval Guard)**: Chặn dứt khoát việc gọi xuất bản nếu video chưa có bản ghi `VideoReview` mang quyết định `Approved` hoặc còn tồn tại lỗi nghiêm trọng (`hasFatalViolations = true`).
- **Chống đăng trùng (Idempotency Guard)**: Tự động kiểm tra bảng `FacebookPosts` theo `VideoProjectId`. Nếu đã có bản ghi ở trạng thái `Published`, hệ thống trả về kết quả đã đăng, ngăn ngừa tình trạng đăng lặp nhiều lần gây spam Fanpage.
- **Dọn dẹp Ephemeral Storage sau xuất bản**: Ngay sau khi upload video thành công lên Meta, hệ thống tự động kích hoạt `IVideoAssetService.CleanupProjectAssetsAsync` xóa bỏ các file MP4, WAV, SRT tạm thời khỏi ổ đĩa, đảm bảo tuân thủ nghiêm ngặt nguyên tắc Master Plan: **Chỉ lưu dữ liệu nghiệp vụ, không lưu trữ lâu dài binary media**.

### 9.4. Thu thập số liệu tương tác (Meta Page Insights API)
- Dịch vụ `FetchMetricsAsync` kết nối endpoint `/{video_id}/video_insights` để đọc dữ liệu tương tác thực tế: `total_video_views` (Views), `post_impressions_unique` (Reach), `post_engaged_users` (Engagement), `total_video_avg_time_watched` (Watch Time).
- Lưu bản ghi vào bảng `VideoMetrics` phục vụ phân tích hiệu quả nội dung.
- Tuyệt đối không tự suy đoán hoặc bịa số liệu khi Meta chưa cung cấp (mặc định 0).
