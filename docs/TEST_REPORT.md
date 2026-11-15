# AI NEWS STUDIO - BÁO CÁO KIỂM THỬ (TEST REPORT)

**Thời gian kiểm thử:** 15/11/2026  
**Môi trường:** Local Windows (PostgreSQL 16, Node v24.14.1, .NET 8 runtime)  
**Tình trạng:** **37/37 PASS (100%)**

---

## 1. Kết quả kiểm thử Backend .NET 8 (`dotnet test`)

Lệnh thực thi:
```bash
dotnet test backend/NewsStudio.sln
```

### Chi tiết các ca kiểm thử (37/37 Passed):

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
| **TC-24** | Phase 4 | `AIEditor_Generates_Script_With_Target_Duration_45_To_60_Seconds` | Kịch bản biên tập đạt chuẩn thời lượng đọc 45–60 giây, dung lượng từ chuẩn, có đầy đủ Hook, Title, VoiceOver, SourceAttribution | **PASSED** |
| **TC-25** | Phase 4 | `AIEditor_Separates_KeyFacts_And_Commentary` | Tách bạch tuyệt đối giữa Sự thật khách quan (Key Facts) và Bình luận biên tập (Analysis) | **PASSED** |
| **TC-26** | Phase 4 | `ScriptPlanner_Creates_6_To_10_Scenes_Matching_Duration` | Lập 6–10 phân cảnh liên tiếp, tổng thời lượng chuẩn 45–60 giây, thời gian các cảnh nối tiếp chính xác | **PASSED** |
| **TC-27** | Phase 4 | `ScriptPlanner_Strictly_Uses_Article_Images_Or_Falls_Back_To_Typography` | Chỉ gán ảnh thuộc bài báo hiện tại; các cảnh thiếu ảnh tự động chuyển sang Typography / MetricChart (không sinh ảnh AI / ảnh ngoài) | **PASSED** |
| **TC-28** | Phase 4 | `ScriptPlanner_When_Article_Has_Zero_Images_All_Scenes_Use_Typography` | Xử lý an toàn khi bài báo không có ảnh: 100% scenes chuyển sang Typography và MetricChart nền tối | **PASSED** |
| **TC-29** | Phase 4 | `AiEditorFactory_Switches_Between_Gemini_And_Claude` | Factory chuyển đổi chính xác nhà cung cấp AI theo tham số cấu hình: Gemini và Claude | **PASSED** |
| **TC-30** | Phase 4 | `VideoProjectScriptsController_Full_Workflow` | Luồng REST API trọn vẹn: tạo kịch bản AI, truy vấn kịch bản, sửa kịch bản, và đồng bộ 6-10 phân cảnh | **PASSED** |
| **TC-31** | Phase 5 | `EdgeTts_Synthesizes_Vietnamese_Speech_Within_Target_Duration` | Sinh giọng đọc tiếng Việt chuẩn, thời lượng 45-60s, định dạng WAV PCM 24kHz, tạo mã băm SHA-256 | **PASSED** |
| **TC-32** | Phase 5 | `SubtitleGenerator_Produces_Synchronized_Srt_And_Vtt_Cues` | Sinh phụ đề chuẩn đồng bộ `.srt` và WebVTT `.vtt` từ kịch bản phân cảnh | **PASSED** |
| **TC-33** | Phase 5 | `FFmpegRenderer_Builds_Vertical_9x16_Filtergraph_And_Renders` | Xây dựng filtergraph 1080×1920 (9:16), 30 fps, làm mờ ảnh ngang, hiệu ứng Ken Burns, xuất video MP4 | **PASSED** |
| **TC-34** | Phase 5 | `VideoAssetService_Registers_And_Enforces_Ephemeral_Storage` | Lưu trữ tạm thời `./tmp/newsstudio/{id}/`, kiểm tra metadata TTL 2h, kiểm định không lưu binary vào PostgreSQL | **PASSED** |
| **TC-35** | Phase 5 | `VideoAssetService_Cleans_Up_Expired_Assets` | Tự động dọn dẹp các asset hết hạn: xóa file vật lý trên đĩa và cập nhật trạng thái trong database | **PASSED** |
| **TC-36** | Phase 5 | `TtsFactory_Switches_Between_EdgeTts_And_ElevenLabs` | Factory chuyển đổi nhà cung cấp TTS linh hoạt giữa EdgeTTS và ElevenLabs | **PASSED** |
| **TC-37** | Phase 5 | `VideoMediaController_Endpoints_Full_Workflow` | Luồng REST API media trọn vẹn: kích hoạt render, lấy trạng thái, stream MP4/WAV, tải SRT, liệt kê assets | **PASSED** |

**Tổng kết kiểm thử:** `Failed: 0, Passed: 37, Skipped: 0, Total: 37. Duration: 10s`

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
