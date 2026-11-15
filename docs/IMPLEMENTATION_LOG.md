# AI NEWS STUDIO - NHẬT KÝ TRIỂN KHAI (IMPLEMENTATION LOG)

## Phase 8: Railway Deployment và Production Hardening

**Thời gian thực hiện:** 15/11/2026

### 1. Công việc đã thực hiện
1. **Phân tích Chuỗi Kết nối Động cho Railway (`ConnectionStringHelper`)**:
   - `NewsStudio.Infrastructure/Common/ConnectionStringHelper.cs`: Hỗ trợ biến môi trường `DATABASE_URL` dạng URI (`postgresql://user:pass@host:port/database`) do Railway tự động cấp phát, chuyển đổi sang định dạng Npgsql kết hợp pooling và keep-alive.
2. **Triển khai Dịch vụ Phục hồi Tác vụ khi Worker Restart (`JobRecoveryService`)**:
   - `NewsStudio.Infrastructure/Jobs/JobRecoveryService.cs`:
     - Rà soát các dự án dang dở (`Rendering`, `GeneratingAudio`, `Publishing`).
     - Bảo vệ chống xuất bản lặp (Idempotency): Nếu bài đăng Facebook đã tồn tại, hòa giải ngay về `Published`.
     - Kiểm tra sự tồn tại của file video trên ổ đĩa tạm. Chuyển về `AwaitingApproval` nếu video còn nguyên vẹn; đánh dấu `Failed` kèm hướng dẫn render lại nếu file bị mất do container restart.
     - Đánh dấu các `ProcessingJobs` gián đoạn sang `Failed`.
3. **Triển khai Dịch vụ Dọn dẹp & Quản lý Hạn ngạch Ổ đĩa Tạm (`StorageCleanupService`)**:
   - `NewsStudio.Infrastructure/Rendering/StorageCleanupService.cs`:
     - Xóa các tệp tin tạm có hạn TTL quá hạn (`ExpiresAtUtc < DateTime.UtcNow`).
     - Rà soát trần dung lượng đĩa `Storage:MaxTempStorageMb` (5120 MB). Tự động dọn dẹp các tệp cũ nhất khi vượt ngưỡng để giữ dung lượng dưới 80% hạn ngạch.
4. **Triển khai Dịch vụ Telemetry & Theo dõi Chi phí (`SystemMetricsService`)**:
   - `NewsStudio.Infrastructure/Services/SystemMetricsService.cs`:
     - Thu thập RAM WorkingSet, CPU usage %, dung lượng ổ đĩa tạm thực tế, trạng thái kết nối và độ trễ PostgreSQL (ms), số lượng Jobs, Uptime.
     - Tính toán tổng chi phí AI và TTS, số lượng tokens tiêu thụ, và chi phí trung bình trên mỗi video.
5. **Nâng cấp Worker Service (`Worker.cs`)**:
   - Tự động chạy `IJobRecoveryService.RecoverDanglingJobsAsync` ngay khi khởi động.
   - Vòng lặp chạy ngầm thực thi dọn dẹp ổ đĩa định kỳ mỗi 15 phút.
   - Ghi nhận heartbeat và xử lý tín hiệu SIGTERM graceful shutdown qua `IHostApplicationLifetime`.
6. **Mở rộng REST APIs (`SystemController.cs`)**:
   - `GET /api/system/metrics`: Chỉ số vận hành thời gian thực.
   - `GET /api/system/costs`: Thống kê chi phí AI & TTS.
   - `POST /api/system/storage/cleanup`: Kích hoạt dọn dẹp đĩa tạm thủ công.
   - `POST /api/system/jobs/recover`: Kích hoạt kiểm toán phục hồi job thủ công.
   - Giữ nguyên `GET /api/system/config-summary` và `GET /api/system/info`.
7. **Củng cố Bảo mật & Ổn định Production (`Program.cs`)**:
   - Tự động thực thi `Database.Migrate()` khi khởi động API với cơ chế thử lại.
   - Tích hợp ASP.NET Core RateLimiter (120 req/min).
   - Tùy chọn bảo mật `X-Admin-Api-Key` cho các endpoint quản trị.
   - Dynamic CORS tương thích tự động với domain Railway.
   - Global Exception Handler trả về RFC 7807 ProblemDetails.
8. **Nâng cấp Giao diện Frontend (`DashboardPage.tsx` & `api.ts`)**:
   - Thêm thanh đo dung lượng ổ đĩa tạm thời gian thực kèm nút **Dọn dẹp đĩa**.
   - Thêm thẻ độ trễ PostgreSQL, RAM WorkingSet, Uptime và nút **Kiểm toán Job**.
   - Thống kê chi phí AI & TTS trung bình và tổng cộng.
9. **Bộ Tài liệu Triển khai & Vận hành Hoàn chỉnh**:
   - `docs/DEPLOYMENT_GUIDE.md`
   - `docs/ENVIRONMENT_VARIABLES.md`
   - `docs/OPERATIONS_GUIDE.md`
   - `docs/RECOVERY_GUIDE.md`
   - `docs/FINAL_TEST_REPORT.md`
10. **Kiểm thử Tự động Hoàn chỉnh (`Phase8ProductionHardeningTests.cs`)**:
    - 8 ca kiểm thử xUnit mới: phân tích URI Railway, phục hồi job gián đoạn, chống duplicate publish, dọn dẹp ổ đĩa tạm, telemetry metrics, full system API controller, và mô phỏng tải đa video đồng thời.
    - Toàn bộ 59/59 tests xUnit đạt 100% PASS.

---

## Phase 7: Facebook Graph API (Kết nối Fanpage, Đăng Reels & Phân tích Insights)

**Thời gian thực hiện:** 15/11/2026

### 1. Công việc đã thực hiện
1. **Xây dựng DTOs & Service Interfaces**:
   - `NewsStudio.Application/DTOs/FacebookDtos.cs`: `FacebookAccountDto`, `ConnectFacebookPageRequestDto`, `VerifyTokenResultDto`, `PublishReelRequestDto`, `PublishReelResultDto`, `FacebookPostDto`, `VideoMetricDto`.
   - `NewsStudio.Application/Interfaces/IFacebookServices.cs`: Định nghĩa `IFacebookTokenProtectionService` và `IFacebookPublishingService`.
2. **Triển khai Dịch vụ Bảo mật Token (`FacebookTokenProtectionService`)**:
   - Mã hóa đối xứng AES-256 kèm IV ngẫu nhiên trước khi lưu vào cột `EncryptedAccessToken` của PostgreSQL.
   - Hàm `MaskToken` che giấu token theo chuẩn `EAAB••••••••1234`, không bao giờ lộ token thô.
   - Đảm bảo tuyệt đối không ghi log chứa token và không gửi token thô về client.
3. **Phát triển Dịch vụ Xuất bản Reels (`FacebookPublishingService`)**:
   - Tích hợp Meta Graph API (Graph API Version `v21.0`).
   - Quy trình xuất bản Reels 3 bước chuẩn: Khởi tạo session (`upload_phase=start`) -> Truyền tải luồng byte MP4 từ file tạm (`upload_phase=transfer`) -> Hoàn tất và xuất bản (`upload_phase=finish`).
   - **Khóa phê duyệt (Approval Guard)**: Chặn dứt khoát nếu video chưa được duyệt hoặc còn lỗi nghiêm trọng.
   - **Chống đăng trùng (Idempotency Guard)**: Ngăn chặn upload lặp lại nhiều lần nếu dự án đã có bài đăng trạng thái `Published`.
   - **Tự động hủy file tạm sau đăng**: Tự động gọi `IVideoAssetService.CleanupProjectAssetsAsync` xóa toàn bộ file MP4/WAV/SRT trên ổ cứng ngay sau khi xuất bản thành công.
   - **Thu thập Insights**: Endpoint đọc số liệu tương tác thực tế từ Meta Page Insights API (Views, Reach, Engagement, Watch Time) lưu vào bảng `VideoMetrics`.
4. **Triển khai REST APIs (`FacebookController`)**:
   - `GET /api/facebook/pages`: Danh sách Page đã kết nối (kèm token đã mask).
   - `POST /api/facebook/verify`: Kiểm tra tính hợp lệ của Page Access Token.
   - `POST /api/facebook/pages`: Kết nối Fanpage mới (mã hóa token AES-256).
   - `DELETE /api/facebook/pages/{id}`: Ngắt kết nối Fanpage.
   - `POST /api/video-projects/{id}/publish-reel`: Xuất bản video lên Facebook Reels.
   - `GET /api/video-projects/{id}/facebook-posts`: Lấy lịch sử bài đăng Reels của dự án.
   - `GET /api/facebook/posts/{postId}/metrics`: Thu thập số liệu tương tác từ Page Insights.
5. **Nâng cấp Giao diện Frontend (ReactJS + TS)**:
   - `FacebookConfigPage.tsx`: Danh sách Fanpage đã kết nối, form kết nối Page mới với token ẩn, kiểm tra token với Meta, hiển thị danh sách quyền Graph API.
   - `VideoDetailPage.tsx`: Hộp thoại Xuất bản Reels (chọn Fanpage, sửa caption, tùy chọn dọn dẹp file tạm), tab Facebook Reels hiển thị link xem trực tiếp trên Facebook, Post ID, Video ID, và 6 thẻ số liệu Insights (Views, Reach, Engagement, Shares, Comments, Watch Time) kèm nút làm mới số liệu.
6. **Kiểm thử tự động hoàn chỉnh (`Phase7FacebookGraphApiTests.cs`)**:
   - 7 ca kiểm thử xUnit mới cho mã hóa token, kết nối Page, chặn đăng khi chưa duyệt, chống đăng trùng, quy trình xuất bản và dọn dẹp file tạm, thu thập số liệu Insights, và full controller API flow.
   - Toàn bộ 51/51 tests xUnit đạt 100% PASS.

---

## Phase 6: Video Review và Quality Control (10 Tiêu chí & Quản lý Phê duyệt)

**Thời gian thực hiện:** 15/11/2026

### 1. Công việc đã thực hiện
1. **Xây dựng DTOs & Service Interfaces**:
   - `NewsStudio.Application/DTOs/ReviewDtos.cs`: `QualityCheckItemDto`, `QualityAssessmentResultDto`, `VideoReviewDto`, `SubmitReviewDecisionRequestDto`, `UpdateVideoMetadataRequestDto`, `ReviewActionRequestDto`.
   - `NewsStudio.Application/Interfaces/IReviewServices.cs`: Định nghĩa `IQualityControllerService` và `IVideoReviewService`.
2. **Phát triển Động cơ Kiểm soát Chất lượng Tự động (`QualityControllerService`)**:
   - Triển khai thuật toán rà soát 10 tiêu chí độc lập:
     - `QC_01`: Độ chính xác nội dung so với bài báo.
     - `QC_02`: Kiểm chứng số liệu và dữ kiện thực tế (Key Facts).
     - `QC_03`: Xác minh nhân vật và thực thể.
     - `QC_04`: Phù hợp hình ảnh và phân cảnh.
     - `QC_05`: Bản quyền & nguồn gốc hình ảnh (phát hiện và chặn ảnh `Rejected`/`Unsuitable`).
     - `QC_06`: Chất lượng và đồng bộ phụ đề (SRT/VTT).
     - `QC_07`: Chất lượng âm thanh thuyết minh (WAV).
     - `QC_08`: Kiểm tra màn hình đen / cảnh trống hoàn toàn.
     - `QC_09`: Kỹ thuật Facebook Reels: 9:16 (1080×1920), 30 fps, thời lượng 45–60 giây.
     - `QC_10`: Giá trị biên tập gia tăng, bối cảnh, phân tích và trích nguồn bài báo.
   - Tính toán tổng điểm thang 100, phát hiện lỗi nghiêm trọng (Fatal Violations).
   - Tuyên bố minh bạch: Điểm kiểm định nội bộ không phản ánh thuật toán của Facebook.
3. **Phát triển Dịch vụ Phê duyệt Video (`VideoReviewService`)**:
   - Quản lý trạng thái quyết định: `Approved`, `Rejected`, `NeedsScriptRegen`, `NeedsAudioRegen`, `NeedsReRender`.
   - **Thực thi cơ chế chặn duyệt nghiêm ngặt**: Khi có lỗi nghiêm trọng (`hasFatalViolations = true`), phương thức phê duyệt lập tức quăng `InvalidOperationException` chặn đứng hành động duyệt.
   - Hỗ trợ chỉnh sửa metadata (tiêu đề, hook, caption) trước khi xuất bản.
   - Hỗ trợ yêu cầu làm lại từng khâu (kịch bản, audio, video).
   - Hỗ trợ xóa dự án kèm dọn dẹp file tạm trên ổ đĩa.
4. **Triển khai REST APIs (`VideoReviewController`)**:
   - `GET /api/video-projects/{id}/review`: Lấy thông tin review và 10 tiêu chí kiểm định.
   - `POST /api/video-projects/{id}/review/evaluate`: Chạy kiểm định 10 tiêu chí tự động.
   - `POST /api/video-projects/{id}/review/approve`: Phê duyệt video (bảo vệ chống lỗi nghiêm trọng).
   - `POST /api/video-projects/{id}/review/reject`: Từ chối video kèm lý do chi tiết.
   - `POST /api/video-projects/{id}/review/request-action`: Yêu cầu AI/Renderer tạo lại kịch bản/audio/video.
   - `PUT /api/video-projects/{id}/review/metadata`: Chỉnh sửa tiêu đề video, hook và caption.
   - `DELETE /api/video-projects/{id}`: Xóa dự án và dọn sạch file tạm.
5. **Nâng cấp Giao diện Frontend (`VideoDetailPage.tsx` & `api.ts`)**:
   - Tab QC động: Hiển thị điểm số nội bộ (x/100), bảng 10 tiêu chí chi tiết kèm điểm số (x/10), trạng thái Đạt/Vi phạm và gợi ý khắc phục.
   - Banner cảnh báo lỗi nghiêm trọng khi phát hiện vi phạm bản quyền ảnh hoặc thiếu kịch bản.
   - Thanh công cụ duyệt: Phê duyệt (chặn khi có lỗi), Từ chối (Modal ghi nhận feedback), Sửa tiêu đề (Modal), Yêu cầu tạo lại kịch bản/audio/video.
   - Nút đăng Facebook Reels được khóa chặt chẽ cho đến khi video được phê duyệt và không còn lỗi nghiêm trọng.
6. **Kiểm thử tự động hoàn chỉnh (`Phase6VideoReviewAndQualityControlTests.cs`)**:
   - 7 ca kiểm thử xUnit mới cho toàn bộ các khía cạnh QC, kiểm soát bản quyền ảnh, chặn duyệt khi vi phạm, phê duyệt hợp lệ, từ chối kèm lý do, sửa metadata, và full API flow.
   - Toàn bộ 44/44 tests xUnit đạt 100% PASS.

---

## Phase 5: TTS và Video Renderer (FFmpeg 9:16, Subtitles & Ephemeral Storage)

**Thời gian thực hiện:** 15/11/2026

### 1. Công việc đã thực hiện
1. **Xây dựng DTOs & Service Interfaces**:
   - `NewsStudio.Application/DTOs/RenderingDtos.cs`: `TtsSynthesisRequest`, `TtsSynthesisResult`, `SubtitleCueDto`, `VideoRenderRequest`, `VideoRenderResult`, `VideoAssetDto`, `RenderStatusResponseDto`.
   - `NewsStudio.Application/Interfaces/IRenderingServices.cs`: Định nghĩa các interface chuẩn `ITtsService`, `ITtsFactory`, `ISubtitleGeneratorService`, `IVideoRendererService`, `IVideoAssetService`, `IVideoProductionService`.
2. **Phát triển Động cơ TTS Tiếng Việt (`EdgeTtsService` & `ElevenLabsTtsService`)**:
   - Hỗ trợ giọng đọc thần kinh tự nhiên của Microsoft Edge (`vi-VN-HoaiMyNeural`, `vi-VN-NamMinhNeural`) và ElevenLabs.
   - Chuẩn hóa đầu ra âm thanh PCM WAV 24kHz 16-bit Mono, băm SHA-256 để chống trùng lặp dữ liệu và tái sử dụng cache khi văn bản không đổi.
   - Căn chỉnh tốc độ đọc và thời lượng khớp chính xác với kế hoạch phân cảnh (45–60 giây).
3. **Phát triển Bộ sinh phụ đề chuẩn (`SubtitleGeneratorService`)**:
   - Tự động sinh file phụ đề `.srt` (cho FFmpeg burner) và `.vtt` (cho trình phát Web).
   - Phân tách cụm từ tiếng Việt súc tích (4-7 từ), timestamp mili-giây chuẩn xác.
4. **Phát triển Động cơ Dựng Video Dọc FFmpeg 9:16 (`FFmpegRendererService`)**:
   - Tự động thiết lập filtergraph 1080×1920 (9:16), 30 fps.
   - Xử lý ảnh ngang: Background mờ (`boxblur=15:5`) + foreground sắc nét.
   - Hiệu ứng chuyển động Ken Burns (`zoompan`), zoom, pan, fade, slide.
   - Dựng thẻ tin tức Typography trên nền gradient chuyển động khi phân cảnh không có ảnh.
   - Hòa âm giọng đọc với nhạc nền tin tức nhẹ nhàng (Audio Ducking).
   - Hỗ trợ Dev/Test Fallback tự động khi chạy trên máy chưa cài sẵn FFmpeg binary.
5. **Quản lý lưu trữ tạm thời Ephemeral Storage (`VideoAssetService`)**:
   - Tuân thủ nguyên tắc: **Không lưu binary MP4, ảnh hay audio vào PostgreSQL**.
   - Lưu trữ tạm thời vào `./tmp/newsstudio/{projectId}/` và theo dõi trong `VideoAssets` với TTL 2 giờ (`ExpiresAtUtc`).
   - Tự động dọn dẹp các tệp hết hạn trên ổ đĩa và cập nhật trạng thái trong database.
6. **Dịch vụ tích hợp sản xuất video hoàn chỉnh (`VideoProductionService`)**:
   - Điều phối toàn bộ luồng: TTS giọng đọc -> Phụ đề SRT/VTT -> FFmpeg render MP4 -> Đăng ký assets -> Cập nhật trạng thái `AwaitingApproval`.
   - Tích hợp trực tiếp vào worker pipeline `VideoProcessingJobExecutor`.
7. **Xây dựng REST APIs (`VideoMediaController`)**:
   - `POST /api/video-projects/{id}/render`: Kích hoạt render video.
   - `GET /api/video-projects/{id}/render-status`: Lấy trạng thái render thời gian thực.
   - `GET /api/video-projects/{id}/video`: Stream video MP4 9:16.
   - `GET /api/video-projects/{id}/audio`: Stream audio WAV giọng đọc tiếng Việt.
   - `GET /api/video-projects/{id}/subtitles?format=srt|vtt`: Tải phụ đề SRT/VTT.
   - `GET /api/video-projects/{id}/assets`: Danh sách các file tạm thời và hạn TTL.
   - `DELETE /api/video-projects/{id}/assets`: Xóa sạch file tạm của dự án.
8. **Nâng cấp Giao diện Frontend (`VideoDetailPage.tsx` & `api.ts`)**:
   - Tích hợp video player dọc chuẩn 9:16 xem trước trực tiếp trên web.
   - Trình nghe thử audio giọng đọc tiếng Việt.
   - Bảng quản lý Ephemeral Storage hiển thị chi tiết các asset tạm và thời hạn TTL.
   - Nút Render Video và Tải file MP4.
9. **Kiểm thử tự động hoàn chỉnh (`Phase5TtsAndVideoRendererTests.cs`)**:
   - 7 ca kiểm thử chuyên sâu cho TTS, Subtitles, FFmpeg, Ephemeral storage, TTL cleanup, Factory switching, Controller API.
   - Tổng cộng 37/37 tests xUnit đạt 100% PASS.

---

## Phase 4: AI Editor và Script Planner

**Thời gian thực hiện:** 15/11/2026

### 1. Công việc đã thực hiện
1. **Xây dựng DTOs & Domain Models**:
   - `NewsStudio.Application/DTOs/ScriptDtos.cs`: `EditorialScriptPromptInput`, `EditorialScriptResult`, `ScriptPlanningInput`, `VideoScenePlanDto`, `GenerateScriptRequestDto`, `UpdateScriptRequestDto`, `VideoScriptResponseDto`, `UpdateScenesRequestDto`.
2. **Thiết kế Prompt Biên tập Chuyên biệt**:
   - `NewsStudio.Infrastructure/Ai/PromptTemplates.cs`: Xây dựng prompt chuẩn báo chí truyền hình cho video ngắn 45–60 giây (110–150 từ), câu ngắn ngắt nhịp dứt khoát, cấu trúc JSON bắt buộc tách riêng: `VideoTitle`, `Hook`, `VoiceOverScript`, `KeyFacts`, `Context`, `Analysis`, `Conclusion`, `SourceAttribution`.
3. **Phát triển AI Editor Service cho Gemini & Claude**:
   - `GeminiAiEditorService`: Tích hợp Google Gemini qua REST API, đọc API key từ cấu hình/env, hỗ trợ `gemini-1.5-flash` và `gemini-1.5-pro`, tính toán token usage và chi phí ước tính. Có deterministic editorial synthesis engine đảm bảo chạy mượt mà ngay cả khi offline.
   - `ClaudeAiEditorService`: Tích hợp Anthropic Claude qua REST API (`/v1/messages`), hỗ trợ `claude-3-5-haiku` và `claude-3-5-sonnet`, theo dõi token input/output và chi phí ước tính.
   - `AiEditorFactory`: Cho phép thay đổi nhà cung cấp AI theo yêu cầu (`AiProviderType.Gemini` / `AiProviderType.Claude`).
4. **Phát triển Script Planner Service (`ScriptPlannerService`)**:
   - Phân chia kịch bản thành **6 đến 10 scenes** liên tiếp, thời lượng mỗi scene từ 4 đến 9 giây, tổng thời lượng nằm trọn vẹn trong 45–60 giây.
   - **Thực thi nghiêm ngặt quy tắc hình ảnh**: Chỉ sử dụng `ArticleImageId` của bài báo hiện tại. Nếu thiếu ảnh hoặc bài báo không có ảnh: tự động chuyển `VisualType` sang **Typography** hoặc **MetricChart** / **Timeline**, hiển thị câu nổi bật `OnScreenText`. Tuyệt đối không sinh ảnh AI, không tìm ảnh ngoài.
   - Gán `MotionType` (`ZoomIn`, `ZoomOut`, `PanLeft`, `PanRight`, `KenBurns`) và `TransitionType` (`Fade`, `Slide`, `Cut`).
5. **Xây dựng dịch vụ quản lý kịch bản (`ScriptService`)**:
   - Tự động bóc tách bài báo nếu dự án chưa có nội dung.
   - Lưu trữ `VideoScript` và danh sách `VideoScenes` vào PostgreSQL.
   - Cung cấp các thao tác tạo, truy vấn, chỉnh sửa kịch bản và phân cảnh.
6. **Tích hợp vào Pipeline Worker (`VideoProcessingJobExecutor`)**:
   - Bước `Analyzing` & `Scripting`: Tự động gọi `IScriptService.GenerateScriptForProjectAsync` tạo kịch bản 45-60s.
   - Bước `PlanningScenes`: Tự động gọi `IScriptService.PlanScenesForProjectAsync` lập 6-10 scenes và lưu DB.
7. **Triển khai REST API (`VideoProjectScriptsController`)**:
   - `POST /api/video-projects/{id}/script/generate`: Tạo kịch bản AI (chọn Gemini hoặc Claude).
   - `GET /api/video-projects/{id}/script`: Xem kịch bản hiện tại.
   - `PUT /api/video-projects/{id}/script`: Chỉnh sửa kịch bản thủ công.
   - `POST /api/video-projects/{id}/scenes/plan`: Lập 6-10 phân cảnh từ kịch bản.
   - `GET /api/video-projects/{id}/scenes`: Lấy danh sách phân cảnh.
   - `PUT /api/video-projects/{id}/scenes`: Cập nhật phân cảnh.
8. **Nâng cấp giao diện Frontend (ReactJS + TS - AI First)**:
   - Cập nhật `frontend/src/services/api.ts` với các API kịch bản và phân cảnh.
   - Nâng cấp `VideoDetailPage.tsx`:
     - Tích hợp switcher nhà cung cấp AI (Gemini / Claude).
     - Tab Kịch bản: Hiển thị tách biệt **Key Facts** (sự thật bài báo) và **Analysis** (bình luận biên tập).
     - Tab Phân cảnh: Hiển thị 6-10 cards phân cảnh với mốc thời gian, loại visual (Ảnh bài viết vs Nền Typography), motion, transition, giọng đọc và chữ nổi bật.
9. **Kiểm thử tự động**:
   - Xây dựng `Phase4AiEditorAndScriptPlannerTests.cs` (7 ca kiểm thử chuyên sâu cho thời lượng 45-60s, tách Fact vs Commentary, phân chia 6-10 scenes, chỉ dùng ảnh bài báo hoặc typography, chuyển đổi provider Gemini/Claude, và trọn vẹn luồng REST API).
   - Chạy toàn bộ 30/30 tests xUnit: 100% PASS.

---

## Phase 3: Article Extractor và Image Extractor

**Thời gian thực hiện:** 15/11/2026

*(Xem chi tiết trong tài liệu nghiệm thu Phase 3)*

---

## Phase 2: Database, Hangfire và Job Processing

**Thời gian thực hiện:** 15/11/2026

*(Xem chi tiết trong tài liệu nghiệm thu Phase 2)*

---

## Phase 1: Khởi tạo dự án và kiến trúc

**Thời gian thực hiện:** 15/11/2026

*(Xem chi tiết trong lịch sử git commit ban đầu)*
