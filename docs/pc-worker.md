# Chạy song song máy tính + Railway

Máy tính bật thì **máy tính làm việc nặng** (ghép video, tạo clip, tự đăng…), Railway chỉ giữ trang web.
Tắt máy thì khoảng 1 phút sau **Railway tự làm tiếp**. Bật máy lên lại thì máy tính tự nhận việc về.
Cả hai dùng chung cơ sở dữ liệu trên Railway và chung một **kho file** (clip, video) nên không có gì phải đồng bộ.

## 1. Tạo kho file miễn phí (một lần, ~10 phút)

**Cloudflare R2** (miễn phí 10 GB, không tính phí tải xuống — Cloudflare có thể yêu cầu thêm thẻ để xác minh):
1. Đăng ký / đăng nhập https://dash.cloudflare.com → **R2 Object Storage** → **Create bucket** → tên `tramtin-studio`.
2. R2 → **Manage API Tokens** → **Create API token** → quyền **Object Read & Write**, chọn bucket `tramtin-studio` → Create.
3. Ghi lại: **Access Key ID**, **Secret Access Key**, và **S3 endpoint** dạng `https://<account-id>.r2.cloudflarestorage.com`.

(Hoặc **Backblaze B2**: tạo bucket private + Application Key; endpoint dạng `https://s3.us-west-004.backblazeb2.com`, Region `us-west-004`.)

## 2. Thêm kho file vào Railway

Railway → service app → **Variables** → thêm 5 biến (tự gõ, đừng gửi khoá cho ai):

```
Storage__S3__ServiceUrl = https://<account-id>.r2.cloudflarestorage.com
Storage__S3__Bucket     = tramtin-studio
Storage__S3__AccessKey  = <Access Key ID>
Storage__S3__SecretKey  = <Secret Access Key>
Storage__S3__Region     = auto
```

Chỉ riêng bước này đã có lợi: **cập nhật code / server khởi động lại không còn làm mất clip**.

## 3. Chuẩn bị máy tính (một lần)

1. Cài **Docker Desktop** (https://www.docker.com/products/docker-desktop/) → mở lên, trong Settings bật **Start Docker Desktop when you sign in**.
2. Trong thư mục dự án, copy `.env.pc.example` thành **`.env.pc`** và điền theo hướng dẫn trong file
   (cách nhanh: Railway → Variables → **Raw Editor** → copy hết → dán; rồi sửa `DATABASE_URL` thành địa chỉ **public**
   của Postgres và thêm 5 dòng `Storage__S3__…`).
3. Windows → **Settings → System → Power** → khi cắm sạc: **Sleep = Never** (máy ngủ là ngừng làm việc).

## 4. Chạy

- Bấm đúp **`scripts\pc\start-pc-worker.bat`**. Lần đầu build mất 5–10 phút. Để cửa sổ mở: nó tự cập nhật khi có bản code mới.
- Muốn tự chạy khi mở máy: nhấn `Win + R` → gõ `shell:startup` → kéo **lối tắt** của `start-pc-worker.bat` vào thư mục đó.
- Muốn dừng ngay (Railway nhận việc liền): bấm đúp **`scripts\pc\stop-pc-worker.bat`**.

## Ghi chú

- Máy tính chỉ nhận việc khi chạy **đúng bản code như Railway**. Vừa có bản mới thì Railway làm trong lúc máy tính đang tự cập nhật.
- Railway không bao giờ bỏ ngang một việc đang làm: nó chỉ nhường khi rảnh.
- Video **tin tức** ghép trên máy tính vẫn đăng bình thường, nhưng xem lại trong trang web (Video → chi tiết) có thể không phát được
  vì file nằm trên máy tính. Video **Studio AI** thì xem được ở mọi nơi (nằm trong kho file chung).
