# AI NEWS STUDIO - NHẬT KÝ TRIỂN KHAI (IMPLEMENTATION LOG)

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
