# AI NEWS STUDIO - BÁO CÁO KIỂM THỬ (TEST REPORT)

**Thời gian kiểm thử:** 15/11/2026  
**Môi trường:** Local Windows (PostgreSQL 16, Node v24.14.1, .NET 8 runtime)  
**Tình trạng:** **23/23 PASS (100%)**

---

## 1. Kết quả kiểm thử Backend .NET 8 (`dotnet test`)

Lệnh thực thi:
```bash
dotnet test backend/NewsStudio.sln
```

### Chi tiết các ca kiểm thử (23/23 Passed):

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

**Tổng kết kiểm thử:** `Failed: 0, Passed: 23, Skipped: 0, Total: 23. Duration: 11s`

---

## 2. Kết quả kiểm tra PostgreSQL Migration & Storage

- **Database:** `newsstudio_db` (PostgreSQL 16)
- **Số bảng đã tạo:** 14 bảng (13 thực thể nghiệp vụ + 1 bảng lịch sử migration):
  - `Articles`, `ArticleImages`, `VideoProjects`, `VideoScenes`, `VideoScripts`, `VideoAssets`, `ProcessingJobs`, `VideoReviews`, `FacebookAccounts`, `FacebookPosts`, `VideoMetrics`, `SystemSettings`, `AuditLogs`, `__EFMigrationsHistory`.
- **Dữ liệu hình ảnh:** Chỉ lưu trữ metadata (`OriginalImageUrl`, `Caption`, `AltText`, `Order`, `Width`, `Height`, `ImageHash`, `RightsStatus`, `Category`, `Status`), không lưu binary ảnh.

---

## 3. Kết quả biên dịch Frontend (`npm run build`)

Lệnh thực thi:
```bash
cd frontend && npm run build
```
- **Kết quả:** `✓ built in 4.77s`
- **TypeScript:** Không có lỗi (`tsc` hoàn tất sạch sẽ).
- Các bundle production:
  - `dist/index.html` (1.13 kB)
  - `dist/assets/index-CoxUP7cF.css` (32.63 kB)
  - `dist/assets/index-n42ust0h.js` (237.01 kB)
