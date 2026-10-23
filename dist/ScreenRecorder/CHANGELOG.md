# Lịch sử phiên bản

## 1.1.0 — Nhẹ máy hơn cho lớp học online

**Mới**
- Mặc định **15 fps**: hình thầy cô ở góc màn hình mượt gần như 30 fps, CPU giảm khoảng một nửa.
- Phương thức chụp **Tự động**: ưu tiên Desktop Duplication (GPU), tự chuyển sang GDI nếu máy không hỗ trợ.
  - Đang ghi mà mất kết nối GPU (khoá máy, đổi độ phân giải…), chương trình tự ghi tiếp bằng GDI vào file mới để không mất bài.
- **File nhỏ hơn khoảng một nửa, chữ vẫn nét như cũ**: ảnh đầy đủ mỗi 10 giây (trước là 2 giây), phần còn lại chỉ lưu chỗ thay đổi. Độ phân giải giữ nguyên.
- Âm thanh mặc định **128 kbps** (trước 160 kbps), đủ rõ cho giọng nói.
- **Nén video** (tab Video): *Nén vừa* và *Nén mạnh* (tối đa 15 fps, tiếng mono), luôn giữ nguyên độ phân giải gốc.
  - Chạy nền ở mức ưu tiên thấp sau khi dừng ghi.
  - File nén thay thế file gốc trong cùng thư mục, chỉ khi nhỏ hơn.
- Nút **Nén file có sẵn…** để nén các video đã ghi trước đây, có thanh tiến độ và nút hủy.
- Hiển thị số phiên bản trên đầu cửa sổ.

**Tối ưu**
- Khi đang ghi: ảnh xem trước cập nhật 3 giây một lần.
- Không vẽ thanh mức âm khi cửa sổ bị thu nhỏ.
- Giảm tốc độ vẽ của hiệu ứng nhấp nháy REC.

**Sửa lỗi**
- Thanh tiến độ nén video không chạy.

**Nâng cấp từ 1.0**
- Cài đặt cũ được tự chuyển sang tối đa 15 fps, phương thức chụp Tự động, âm thanh 128 kbps (nếu đang để mặc định 160).
- Các lựa chọn khác vẫn giữ nguyên: chất lượng, micro, thư mục lưu…

## 1.0.0 — Phiên bản đầu tiên

- Ghi màn hình kèm âm thanh hệ thống (WASAPI loopback) và micro.
- Lọc ồn micro, tăng giọng sau lọc, cân bằng giọng nói, tự giảm âm thanh hệ thống khi nói.
- Chọn màn hình bằng ảnh thu nhỏ.
- Phát thử loa, thu thử micro, ghi thử 10 giây.
- Mã hoá bằng GPU (Intel QSV / NVENC / AMF) hoặc x264.
