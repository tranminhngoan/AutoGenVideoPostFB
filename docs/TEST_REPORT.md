# AI NEWS STUDIO - BÁO CÁO KIỂM THỬ (TEST REPORT)

**Thời gian kiểm thử:** 15/11/2026  
**Môi trường:** Local Windows (PostgreSQL 16, Node v24.14.1, .NET 8 runtime)  
**Tình trạng:** **13/13 PASS (100%)**

---

## 1. Kết quả kiểm thử Backend .NET 8 (`dotnet test`)

Lệnh thực thi:
```bash
dotnet test backend/NewsStudio.sln
```

### Chi tiết các ca kiểm thử (13/13 Passed):

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

**Tổng kết:** `Failed: 0, Passed: 13, Skipped: 0, Total: 13. Duration: 10s`

---

## 2. Kết quả kiểm tra PostgreSQL Migration

- **Database:** `newsstudio_db` (PostgreSQL 16)
- **Số bảng đã tạo:** 14 bảng (13 thực thể nghiệp vụ + 1 bảng lịch sử migration):
  - `Articles`, `ArticleImages`, `VideoProjects`, `VideoScenes`, `VideoScripts`, `VideoAssets`, `ProcessingJobs`, `VideoReviews`, `FacebookAccounts`, `FacebookPosts`, `VideoMetrics`, `SystemSettings`, `AuditLogs`, `__EFMigrationsHistory`.
- **Hangfire Schema:** `hangfire` schema được tạo tự động với các bảng `job`, `state`, `counter`, `hash`, `set`.

---

## 3. Kết quả biên dịch Frontend (`npm run build`)

Lệnh thực thi:
```bash
cd frontend && npm run build
```
- **Kết quả:** `✓ built in 4.42s`
- **TypeScript:** Không có lỗi (`tsc` hoàn tất sạch sẽ).
- Các chunk production được tạo:
  - `dist/index.html` (1.13 kB)
  - `dist/assets/index-ytrZbSw7.css` (29.78 kB)
  - `dist/assets/index-B2S8H7UZ.js` (220.64 kB)

---

## 4. Đánh giá chất lượng & Độ sẵn sàng
- Toàn bộ tiêu chí nghiệm thu của Phase 2 đã hoàn thành xuất sắc.
- API tiếp nhận yêu cầu, Hangfire Worker nhận job bất đồng bộ, cập nhật trạng thái vào PostgreSQL, và Frontend theo dõi được tiến trình.
- Sẵn sàng tiến sang Phase 3 (Article Extractor và Image Extractor).
