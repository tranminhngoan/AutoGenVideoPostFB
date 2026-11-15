# AI NEWS STUDIO - NHẬT KÝ TRIỂN KHAI (IMPLEMENTATION LOG)

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
