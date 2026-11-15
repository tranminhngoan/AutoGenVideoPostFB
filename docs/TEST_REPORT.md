# AI NEWS STUDIO - BÁO CÁO KIỂM THỬ (TEST REPORT)

**Thời gian kiểm thử:** 15/11/2026  
**Môi trường:** Local Windows (PostgreSQL 16, Node v24.14.1, .NET 8 runtime)  
**Tình trạng:** **60/60 PASS (100%)**

---

## 1. Kết quả kiểm thử Backend .NET 8 (`dotnet test`)

Lệnh thực thi:
```bash
dotnet test backend/NewsStudio.sln
```

### Chi tiết các ca kiểm thử (60/60 Passed):

| Mã kiểm thử | Nhóm | Tên Test Case | Mô tả & Kỳ vọng | Kết quả |
|---|---|---|---|:---:|
| **TC-01** | Phase 1 | `RootEndpoint_ReturnsHealthyStatusAndDocsUrl` | Kiểm tra GET `/` trả về status 200 và link docs `/swagger` | **PASSED** |
| **TC-02** | Phase 1 | `StandardHealthEndpoints_ReturnSuccess` | Kiểm tra các probe `/health`, `/health/ready`, `/health/live` | **PASSED** |
| **TC-03** | Phase 1 | `ApiHealthController_ReturnsDetailedJson` | Kiểm tra GET `/api/health` trả về JSON trạng thái từng thành phần | **PASSED** |
| **TC-04** | Phase 1 | `SwaggerEndpoint_ReturnsValidJsonSpec` | Kiểm tra GET `/swagger/v1/swagger.json` sinh tài liệu OpenAPI hợp lệ | **PASSED** |
| **TC-05** | Phase 1 | `SystemConfigSummary_DoesNotExposeSecrets` | Kiểm tra GET `/api/system/config-summary` không làm rò rỉ secret | **PASSED** |
| **TC-06** | Phase 1 | `DomainLayer_ShouldNotDependOnInfrastructureOrApi` | Kiểm định Clean Architecture: Domain không phụ thuộc tầng ngoài | **PASSED** |
| **TC-07** | Phase 1 | `ApplicationLayer_ShouldNotDependOnInfrastructureOrApi` | Kiểm định Clean Architecture: Application độc lập với tầng ngoài | **PASSED** |
| **TC-08** | Phase 1 | `VideoProjectStatus_ShouldHaveAllMasterPlanLifecycleStates` | Kiểm định đủ 14 trạng thái vòng đời theo Master Plan | **PASSED** |
| **TC-09** | Phase 2 | `PostVideoProject_CreatesEntityInDatabase_Returns201Created` | Tạo VideoProject, kiểm tra lưu thành công vào PostgreSQL | **PASSED** |
| **TC-10** | Phase 2 | `GetVideoProjects_ReturnsListFromDatabase` | Truy vấn danh sách VideoProjects từ database | **PASSED** |
| **TC-11** | Phase 2 | `GetVideoProjectById_ReturnsCorrectProject_Or404IfNotFound` | Truy vấn chi tiết theo ID hoặc trả về 404 nếu không tồn tại | **PASSED** |
| **TC-12** | Phase 2 | `ProcessVideoProject_EnqueuesJob_AndUpdatesStatus` | Enqueue job Hangfire, kiểm tra cập nhật trạng thái & hủy job | **PASSED** |
| **TC-13** | Phase 2 | `JobExecutor_SimulatesFullLifecycleTransitions` | Chạy pipeline đầy đủ: kiểm tra chuyển đổi trạng thái và không lưu binary media | **PASSED** |
| **TC-14** | Phase 3 | `VnExpressAdapter_Extracts_Title_Body_And_Figures` | Bóc tách bài báo VnExpress: trích xuất đúng tiêu đề, mô tả, nội dung, tác giả, và các khối ảnh `figure.tplCaption` kèm caption | **PASSED** |
| **TC-15** | Phase 3 | `TuoiTreAdapter_Extracts_Correct_Content_And_Images` | Bóc tách bài báo Tuổi Trẻ Online: cấu trúc `VCSortableInLine`/`PhotoCMS_Caption`, lấy đúng ảnh và nội dung | **PASSED** |
| **TC-16** | Phase 3 | `GenericAdapter_Extracts_OpenGraph_And_Standard_Figures` | Bóc tách trang tin chung qua OpenGraph và thẻ `figure` tiêu chuẩn | **PASSED** |
| **TC-17** | Phase 3 | `ArticleExtractor_Filters_Ads_Icons_And_Extracts_Only_Valid_Images` | Lọc bỏ triệt để banner quảng cáo (`/ads/`), pixel 1x1, chỉ giữ lại ảnh nội dung chính quy | **PASSED** |
| **TC-18** | Phase 3 | `ArticleExtractor_Handles_Article_Without_Images_Gracefully` | Xử lý bài báo không chứa ảnh an toàn, không sinh lỗi, danh sách ảnh trả về rỗng hợp lệ | **PASSED** |
| **TC-19** | Phase 3 | `ImageClassifier_Categorizes_Correctly` | Phân loại chính xác 6 danh mục: Panorama, Character (không nhầm từ), Event, Document, Chart, Unsuitable | **PASSED** |
| **TC-20** | Phase 3 | `ArticleService_Persists_Article_And_Images_With_RightsStatus_Unknown` | Kiểm tra quy định bản quyền: ảnh lưu vào PostgreSQL bắt buộc có `RightsStatus = Unknown`, không tự chuyển `Authorized` | **PASSED** |
| **TC-21** | Phase 3 | `ArticlesController_GetArticles_Returns_200OK` | Kiểm tra endpoint GET `/api/articles` trả về 200 OK và danh sách bài báo | **PASSED** |
| **TC-22** | Phase 3 | `ArticlesController_ExtractArticle_Validates_Url_Format` | Kiểm tra validation endpoint POST `/api/articles/extract` chặn URL không phải HTTP/HTTPS | **PASSED** |
| **TC-23** | Phase 3 | `ArticlesController_UpdateArticleImage_Updates_RightsStatus_And_SelectedForVideo` | Kiểm tra PUT `/api/articles/{id}/images/{imageId}` cập nhật caption, RightsStatus sau kiểm duyệt và trạng thái chọn cho video | **PASSED** |
| **TC-24** | Phase 4 | `AIEditor_Generates_Script_With_Target_Duration_55_To_65_Seconds` | Kịch bản ArticleImageOnly đạt chuẩn thời lượng đọc 55–65 giây (target 60s), 120-150 từ, Hook, VoiceOver, SourceAttribution | **PASSED** |
| **TC-25** | Phase 4 | `AIEditor_Separates_KeyFacts_And_Commentary` | Tách bạch tuyệt đối giữa Sự thật khách quan (Key Facts) và Bình luận biên tập (Analysis) | **PASSED** |
| **TC-26** | Phase 4 | `ScriptPlanner_Creates_8_To_10_Scenes_Matching_Duration` | Lập chính xác 8–10 phân cảnh liên tiếp, tổng thời lượng chuẩn 55–65 giây, thời gian các cảnh nối tiếp chính xác | **PASSED** |
| **TC-27** | Phase 4 | `ScriptPlanner_Strictly_Uses_Article_Images_And_Reuses_With_Varying_Crop_And_Motion` | Chỉ dùng ảnh bài báo hiện tại; tái sử dụng ảnh với motion (`PanRight`, `ZoomOut`, `PanLeft`, `ZoomIn`) và crop (`CloseUp`, `Top`, `Bottom`) khi cần | **PASSED** |
| **TC-28** | Phase 4 | `ScriptPlanner_When_Article_Has_Zero_Images_All_Scenes_Use_Typography` | Xử lý an toàn khi bài báo không có ảnh: 100% scenes chuyển sang Typography và MetricChart nền tối | **PASSED** |
| **TC-29** | Phase 4 | `AiEditorFactory_Switches_Between_Gemini_And_Claude` | Factory chuyển đổi chính xác nhà cung cấp AI theo tham số cấu hình: Gemini và Claude | **PASSED** |
| **TC-30** | Phase 4 | `VideoProjectScriptsController_Full_Workflow` | Luồng REST API trọn vẹn: tạo kịch bản AI (55-65s), truy vấn, sửa kịch bản, và đồng bộ 8-10 phân cảnh | **PASSED** |
| **TC-31** | Phase 5 | `EdgeTts_Synthesizes_Vietnamese_Speech_Within_Target_Duration` | Sinh giọng đọc tiếng Việt chuẩn, thời lượng 55-65s, định dạng WAV PCM 24kHz, tạo mã băm SHA-256 | **PASSED** |
| **TC-32** | Phase 5 | `SubtitleGenerator_Produces_Synchronized_Srt_And_Vtt_Cues` | Sinh phụ đề chuẩn đồng bộ `.srt` và WebVTT `.vtt` từ kịch bản phân cảnh | **PASSED** |
| **TC-33** | Phase 5 | `FFmpegRenderer_Builds_Vertical_9x16_Filtergraph_And_Renders` | Xây dựng filtergraph 1080×1920 (9:16), 30 fps, làm mờ ảnh ngang, hiệu ứng Ken Burns, xuất video MP4 55-65s | **PASSED** |
| **TC-34** | Phase 5 | `VideoAssetService_Registers_And_Enforces_Ephemeral_Storage` | Lưu trữ tạm thời `./tmp/newsstudio/{id}/`, kiểm tra metadata TTL 2h, kiểm định không lưu binary vào PostgreSQL | **PASSED** |
| **TC-35** | Phase 5 | `VideoAssetService_Cleans_Up_Expired_Assets` | Tự động dọn dẹp các asset hết hạn: xóa file vật lý trên đĩa và cập nhật trạng thái trong database | **PASSED** |
| **TC-36** | Phase 5 | `TtsFactory_Switches_Between_EdgeTts_And_ElevenLabs` | Factory chuyển đổi nhà cung cấp TTS linh hoạt giữa EdgeTTS và ElevenLabs | **PASSED** |
| **TC-37** | Phase 5 | `VideoMediaController_Endpoints_Full_Workflow` | Luồng REST API media trọn vẹn: kích hoạt render, lấy trạng thái, stream MP4/WAV, tải SRT, liệt kê assets | **PASSED** |
| **TC-38** | Phase 6 | `QualityController_Evaluates_All_10_Criteria_Successfully` | Đánh giá trọn vẹn 10 tiêu chí QC: Content, Factuality, Character, Visual, Rights, Subtitles, Audio, NoBlank, Specs, Editorial | **PASSED** |
| **TC-39** | Phase 6 | `QualityController_Detects_Fatal_Violations_When_Image_Is_Rejected` | Phát hiện lỗi nghiêm trọng khi ảnh gán vào cảnh bị từ chối quyền (Rejected), tự động gắn cờ Fatal và chặn xuất bản | **PASSED** |
| **TC-40** | Phase 6 | `VideoReviewService_Blocks_Approval_When_Fatal_Violations_Exist` | Chặn phê duyệt dứt khoát khi tồn tại lỗi nghiêm trọng (quăng ngoại lệ InvalidOperationException) | **PASSED** |
| **TC-41** | Phase 6 | `VideoReviewService_Submits_Approval_Successfully_When_Standards_Met` | Phê duyệt video thành công khi đạt chuẩn, cập nhật quyết định `Approved`, người duyệt và thời gian duyệt | **PASSED** |
| **TC-42** | Phase 6 | `VideoReviewService_Submits_Rejection_With_Feedback_Notes` | Từ chối duyệt video kèm ghi chú phản hồi chi tiết từ ban biên tập, chặn xuất bản | **PASSED** |
| **TC-43** | Phase 6 | `VideoReviewService_Updates_Metadata_Title_Hook_And_Caption` | Chỉnh sửa tiêu đề video, hook và caption hiển thị phân cảnh trực tiếp trước khi xuất bản | **PASSED** |
| **TC-44** | Phase 6 | `VideoReviewController_Full_Workflow_Http` | Luồng REST API Review trọn vẹn: lấy review, chạy evaluate, phê duyệt qua API, sửa metadata và xóa dự án | **PASSED** |
| **TC-45** | Phase 7 | `TokenProtection_Encrypts_Decrypts_And_Masks_Token_Safely` | Mã hóa AES-256 bảo vệ Access Token, giải mã chính xác và che giấu token an toàn (`EAAB••••••••0XYZ`) | **PASSED** |
| **TC-46** | Phase 7 | `FacebookPublishingService_Connects_And_Retrieves_Pages_Safely` | Kết nối Facebook Page, lưu trữ token đã mã hóa trong PostgreSQL, truy vấn danh sách an toàn | **PASSED** |
| **TC-47** | Phase 7 | `FacebookPublishingService_Blocks_Publish_If_Video_Not_Approved` | Chặn dứt khoát tác vụ xuất bản Reels nếu video chưa có quyết định `Approved` từ ban biên tập | **PASSED** |
| **TC-48** | Phase 7 | `FacebookPublishingService_Prevents_Duplicate_Publishing_Idempotency` | Cơ chế Idempotency: ngăn chặn đăng trùng bài nhiều lần cho cùng một dự án video | **PASSED** |
| **TC-49** | Phase 7 | `FacebookPublishingService_Publishes_Reel_And_Performs_Ephemeral_Cleanup` | Quy trình đăng Facebook Reels 3 pha và tự động dọn dẹp triệt để file MP4/audio/subtitles trên ổ đĩa tạm | **PASSED** |
| **TC-50** | Phase 7 | `FacebookPublishingService_Fetches_Metrics_Without_Fabrication` | Thu thập chỉ số hiệu quả Meta Insights (Views, Reach, Engagement, Watch Time) chuẩn xác không bịa số | **PASSED** |
| **TC-51** | Phase 7 | `FacebookController_Endpoints_Full_Workflow` | Luồng REST API trọn vẹn: xác minh token, kết nối page, xuất bản Reels, truy vấn posts và chỉ số metrics | **PASSED** |
| **TC-52** | Phase 8 | `ConnectionStringHelper_Parses_Railway_Postgres_Uri_Format_Accurately` | Tự động phân tích URI Railway `DATABASE_URL` thành chuỗi kết nối Npgsql hợp lệ | **PASSED** |
| **TC-53** | Phase 8 | `JobRecoveryService_Reconciles_Dangling_Rendering_Jobs_When_Files_Missing` | Phục hồi tác vụ bị gián đoạn do container sập, xử lý an toàn khi mất file tạm | **PASSED** |
| **TC-54** | Phase 8 | `JobRecoveryService_Protects_Already_Published_Jobs_From_Duplicate_Publish` | Đảm bảo an toàn tuyệt đối: không bao giờ xuất bản 2 lần khi worker restart | **PASSED** |
| **TC-55** | Phase 8 | `JobRecoveryService_Advances_To_Review_When_Intact_Video_Found` | Tự động chuyển về `AwaitingApproval` nếu video render còn nguyên trên đĩa | **PASSED** |
| **TC-56** | Phase 8 | `StorageCleanupService_Purges_Expired_Assets_And_Enforces_Quota` | Dọn dẹp tệp tin hết hạn TTL và thực thi trần dung lượng ổ đĩa tạm (Quota) | **PASSED** |
| **TC-57** | Phase 8 | `SystemMetricsService_Computes_Cpu_Memory_Storage_And_Cost_Aggregations` | Thu thập và tính toán chính xác chỉ số CPU, RAM, Disk, DB latency, và chi phí AI | **PASSED** |
| **TC-58** | Phase 8 | `SystemController_Endpoints_Full_Workflow` | Luồng REST API: telemetry `/metrics`, chi phí `/costs`, kích hoạt cleanup & recovery | **PASSED** |
| **TC-59** | Phase 8 | `ProductionHardening_Simulates_Multi_Video_And_Concurrent_Jobs_Safely` | Mô phỏng tải đa video liên tiếp và đồng thời, không xảy ra xung đột hay deadlock | **PASSED** |
| **TC-60** | Mode A | `QualityController_ArticleImageOnly_Detects_External_Images_As_Fatal_Violation` | Phát hiện ảnh ngoài bài báo (ArticleImageOnly violation), gắn cờ Fatal và chặn xuất bản | **PASSED** |

**Tổng kết kiểm thử:** `Failed: 0, Passed: 60, Skipped: 0, Total: 60. Duration: 35s`

---

## 2. Kết quả kiểm tra PostgreSQL Migration & Storage

- **Database:** `newsstudio_db` (PostgreSQL 16)
- **Bảng `VideoScripts`**: Đã lưu trữ kịch bản biên tập (Title, Hook, VoiceOverScript, KeyFacts, Context, Analysis, Conclusion, SourceAttribution, TokenUsage, EstimatedCostUsd).
- **Bảng `VideoScenes`**: Đã lưu trữ 8–10 phân cảnh (SceneNumber, StartTimeSeconds, EndTimeSeconds, VoiceText, OnScreenText, SelectedArticleImageId, MotionType, TransitionType, ScenePurpose).
- **Ràng buộc:** Chỉ liên kết `ArticleImageId` của bài báo hiện tại. Tuyệt đối không lưu binary media vào PostgreSQL.

---

## 3. Kết quả biên dịch Frontend (`npm run build`)

Lệnh thực thi:
```bash
cd frontend && npm run build
```
- **Kết quả:** `✓ built in 5.12s`
- **TypeScript:** Không có lỗi (`tsc` hoàn tất sạch sẽ).
- Bundle production:
  - `dist/index.html` (1.13 kB)
  - `dist/assets/index-Br1e_tiQ.css` (35.85 kB)
  - `dist/assets/index-B0b-UmP9.js` (295.14 kB)

---

## 2. Kết quả kiểm tra PostgreSQL Migration & Storage

- **Database:** `newsstudio_db` (PostgreSQL 16)
- **Bảng `VideoScripts`**: Đã lưu trữ kịch bản biên tập (Title, Hook, VoiceOverScript, KeyFacts, Context, Analysis, Conclusion, SourceAttribution, TokenUsage, EstimatedCostUsd).
- **Bảng `VideoScenes`**: Đã lưu trữ 6–10 phân cảnh (SceneNumber, StartTimeSeconds, EndTimeSeconds, VoiceText, OnScreenText, SelectedArticleImageId, MotionType, TransitionType, ScenePurpose).
- **Ràng buộc:** Chỉ liên kết `ArticleImageId` của bài báo hiện tại. Tuyệt đối không lưu binary media vào PostgreSQL.

---

## 3. Kết quả biên dịch Frontend (`npm run build`)

Lệnh thực thi:
```bash
cd frontend && npm run build
```
- **Kết quả:** `✓ built in 4.89s`
- **TypeScript:** Không có lỗi (`tsc` hoàn tất sạch sẽ).
- Bundle production:
  - `dist/index.html` (1.13 kB)
  - `dist/assets/index-8XrZ7pwz.css` (33.45 kB)
  - `dist/assets/index-DuMiR1ox.js` (244.99 kB)
