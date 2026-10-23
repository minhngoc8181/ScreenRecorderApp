<div align="center">

# 🎬 Screen Recorder

**Ghi lại bài học online — hình ảnh, giọng giảng viên và giọng của bạn — để xem lại bất cứ lúc nào.**

[![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)](#-tải-về-và-chạy)
[![Tải về](https://img.shields.io/github/v/release/minhngoc8181/ScreenRecorderApp?label=t%E1%BA%A3i%20v%E1%BB%81&color=6A52DD)](https://github.com/minhngoc8181/ScreenRecorderApp/releases/latest)
[![Miễn phí](https://img.shields.io/badge/mi%E1%BB%85n%20ph%C3%AD-kh%C3%B4ng%20qu%E1%BA%A3ng%20c%C3%A1o-2F7FD0)](#)

![Giao diện Screen Recorder](docs/screenshot.png)

</div>

## 🎓 Mục đích

Ghi lại các buổi học online để **xem lại khi cần**: nghe lại chỗ chưa hiểu, chép lại phần bị lỡ, ôn bài trước kỳ thi. Thầy cô cũng có thể dùng để lưu bài giảng cho học viên vắng mặt.

> [!NOTE]
> Hãy xin phép giảng viên trước khi ghi và chỉ dùng bản ghi cho việc học.

## ✨ Điểm nổi bật

- 🔊 **Ghi đủ cả hai phía**: tiếng phát ra loa/tai nghe (giảng viên, video) **và** giọng nói từ micro.
- 🎙️ **Giọng rõ, ồn ít**: tự lọc tiếng quạt, tiếng xì nền và tăng giọng của bạn cho cân với tiếng loa.
- 🖥️ **Chọn màn hình bằng hình ảnh**: bấm vào màn hình muốn ghi, kể cả khi dùng nhiều màn hình.
- 🧪 **Thử trước khi ghi**: phát chuông kiểm tra loa, thu thử 5 giây để nghe lại giọng mình.
- ⚡ **Tối ưu máy yếu, nhẹ CPU**: mặc định 15 fps (đủ mượt cho slide lẫn hình thầy cô ở góc màn hình, tốn ít CPU), tự động chọn chụp qua GPU.
- 💾 **File nhỏ, chữ vẫn nét**: không bao giờ tự giảm độ phân giải; file nhỏ hơn khoảng một nửa so với bản 1.0, có thêm tính năng nén video chạy nền.
- 🧳 **Không cần cài đặt**: giải nén là chạy, không cần quyền Admin, không quảng cáo, không thu thập dữ liệu.

## 📥 Tải về và chạy

1. Tải **`ScreenRecorder.zip`** tại trang [**Releases**](https://github.com/minhngoc8181/ScreenRecorderApp/releases/latest).
2. Giải nén ra một thư mục bất kỳ.
3. Nhấp đúp **`ScreenRecorder.exe`**.

> [!TIP]
> Nếu Windows hiện **"Windows protected your PC"**, bấm **More info → Run anyway** (do chương trình chưa được ký số).

Chạy được ngay trên Windows 10/11. Windows 7/8.1 cần cài thêm [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48).

## 🚀 Sử dụng

1. **Bấm vào màn hình** muốn ghi.
2. Ở tab **Âm thanh**: bấm **Phát thử** (kiểm tra tiếng loa) và **Thu thử 5 giây** (kiểm tra micro).
3. Bấm **Bắt đầu ghi**. Học xong bấm **Dừng ghi**, rồi **Mở video**.

Video được lưu trong thư mục `videos` cạnh chương trình.

**Phím tắt:** `Ctrl+Alt+R` bắt đầu/dừng ghi · `Ctrl+Alt+M` tắt/bật micro

## 🎚️ Điều chỉnh chất lượng

Từ bản **1.1**, chương trình mặc định chọn chế độ **nhẹ máy**, hợp với lớp học online (slide, bảng viết, chia sẻ màn hình, hình thầy cô ở một góc). Nếu cần video đẹp hơn, hãy chỉnh các mục dưới đây.

| Tuỳ chọn | Ở đâu | Mặc định | Khi nào nên đổi |
|---|---|---|---|
| **Khung hình / giây** | tab **Video** | 15 fps | Giảm còn **10 fps** nếu máy yếu và bài chỉ có slide (hình thầy cô sẽ hơi giật khi cử động). Tăng lên **24–30 fps** khi bài có video, hoạt hình hoặc thao tác nhanh. 30 fps tốn CPU khoảng gấp đôi 15 fps. |
| **Chất lượng** | tab **Video** | Trung bình | Chọn **Cao** khi chữ nhỏ, code, công thức bị nhoè. File lớn hơn khoảng 15%, CPU gần như không đổi. |
| **Độ phân giải xuất** | tab **Video** | Gốc | Nên giữ **Gốc** để chữ rõ nét. Chương trình **không bao giờ tự giảm độ phân giải**, kể cả khi nén. |
| **Nén video** | tab **Video** | Không nén | **Nén vừa**: nhỏ thêm, chữ vẫn nét. **Nén mạnh**: nhỏ nhất (tối đa 15 fps, tiếng mono). Cả hai giữ nguyên độ phân giải, chạy nền sau khi dừng ghi. |
| **Phương thức chụp** | dưới ảnh màn hình | Tự động | Chọn **GDI** nếu video bị đen hoặc đứng hình. Cách này tốn CPU hơn. |
| **Bộ mã hoá video** | tab **Video** | Tự động | Chỉ chọn **CPU (x264)** khi bộ mã hoá GPU bị lỗi, vì cách này tốn CPU nhiều. |
| **Bitrate âm thanh** | tab **Nâng cao** | 128 kbps | Đủ rõ cho giọng nói. Tăng lên **160–192 kbps** nếu bài có nhiều nhạc. |

**Gợi ý nhanh:**

| Bạn cần | Khung hình | Chất lượng | Độ phân giải | Nén |
|---|---|---|---|---|
| Lớp học online thông thường *(mặc định)* | 15 fps | Trung bình | Gốc | Không |
| Chữ nhỏ, code, công thức | 15 fps | Cao | Gốc | Không hoặc Vừa |
| Bài có video, thao tác nhanh | 24–30 fps | Trung bình hoặc Cao | Gốc | Vừa |
| Máy yếu, ổ cứng sắp đầy | 10 fps | Trung bình | Gốc | Mạnh |

**Dung lượng tham khảo** (MB/giờ, đã gồm tiếng, đo trên buổi học 1080p thật; luôn giữ 1080p, chữ nét như bản gốc):

| Nội dung bài giảng | Bản 1.0 | 1.1 mặc định | Nén vừa | Nén mạnh |
|---|---|---|---|---|
| Chỉ có slide / bảng viết | ~160 | ~80 | ~60 | ~40 |
| Slide + thầy cô ở góc màn hình | ~230 | ~130 | ~80 | ~60 |

> [!TIP]
> Bài giảng phần lớn là hình đứng yên. Bản 1.1 tận dụng điều này: chỉ lưu **phần hình thay đổi**, mỗi 10 giây mới lưu lại một ảnh đầy đủ. Nhờ vậy file nhỏ hơn khoảng một nửa mà **chữ và hình thầy cô vẫn nét như cũ**, không cần giảm độ phân giải. Số khung hình/giây chủ yếu ảnh hưởng **CPU** và **độ mượt của hình người** (15 fps mượt gần như 30 fps), còn chữ thì không đổi.

Muốn biết máy đang dùng cách chụp nào, xem dòng chữ dưới **Phương thức chụp** hoặc tab **Nhật ký**:
- *"Đang dùng Desktop Duplication (GPU)"*: nhẹ CPU nhất.
- *"→ dùng GDI"*: máy không hỗ trợ chụp qua GPU, chương trình tự chuyển sang GDI.

## 💡 Mẹo nhỏ

| Gặp tình huống | Cách làm |
|---|---|
| Giọng mình nhỏ hơn tiếng loa | Kéo **Tăng giọng sau lọc** lên +10…+14 dB, hoặc giảm **Âm lượng** âm thanh hệ thống xuống 60–80%. |
| Video không có tiếng giảng viên | Chọn đúng loa/tai nghe đang dùng, bấm **Phát thử** thấy thanh âm thanh nhảy là được. |
| Tiếng và hình lệch nhau | Chỉnh **Lệch tiếng so với hình** ở tab **Nâng cao**. |
| Máy yếu, video bị giật | Giảm xuống **10 fps** và giữ phương thức chụp **Tự động**. |
| Muốn video nhẹ hơn để nộp/gửi bài | Bật **Nén vừa** hoặc **Nén mạnh** ở tab **Video** (chạy nền sau khi học xong, không làm chậm máy khi đang ghi). |
| Muốn thử nhanh | Bấm **Ghi thử 10 giây**, video sẽ tự mở để bạn kiểm tra. |

Vẫn chưa được? Hãy gửi mô tả lỗi kèm nội dung tab **Nhật ký** tại mục [Issues](https://github.com/minhngoc8181/ScreenRecorderApp/issues).

## 🤖 Chương trình được làm ra như thế nào?

Screen Recorder được tạo theo phong cách **Vibe Coding**: người làm không ngồi gõ từng dòng mã, mà trò chuyện với trợ lý AI — mô tả mong muốn bằng ngôn ngữ đời thường như *"tiếng tôi nói nhỏ hơn tiếng loa"* hay *"chữ trên tab khó đọc quá"* — rồi chạy thử, góp ý và cùng AI chỉnh sửa cho đến khi vừa ý.

Đó cũng là tinh thần của xu hướng **phần mềm cá nhân tự làm** *(personal software)*: thay vì chờ một ứng dụng "tạm dùng được" cho số đông, mỗi người có thể tự tạo ra công cụ đúng với nhu cầu của mình — nhỏ gọn, riêng tư, không rườm rà — rồi chia sẻ lại cho cộng đồng. Nếu công cụ này giúp ích cho bạn, biết đâu công cụ tiếp theo sẽ do chính bạn làm ra.

<details>
<summary><b>🛠️ Dành cho nhà phát triển</b></summary>

<br>

**Công nghệ:** C# · WPF · .NET Framework 4.8 · [NAudio](https://github.com/naudio/NAudio) (WASAPI loopback + micro) · [FFmpeg](https://ffmpeg.org) (chụp màn hình, xử lý âm thanh, mã hoá H.264 bằng Intel QSV / NVENC / AMF / x264).

**Build** (cần [.NET SDK 6+](https://dotnet.microsoft.com/download)):

```powershell
git clone https://github.com/minhngoc8181/ScreenRecorderApp.git
cd ScreenRecorderApp
# Đặt ffmpeg.exe vào tools\  (tải tại https://www.gyan.dev/ffmpeg/builds/)
dotnet build -c Release                                      # -> bin\Release\net48\
powershell -ExecutionPolicy Bypass -File .\make-dist.ps1     # -> dist\ScreenRecorder.zip
```

**Chẩn đoán** (chạy tại thư mục chương trình, kết quả ghi ra file `.log` cạnh exe):

```bat
ScreenRecorder.exe --selftest 8      :: ghi thử 8 giây không cần giao diện
ScreenRecorder.exe --playtest        :: kiểm tra phát âm thanh ra loa
ScreenRecorder.exe --snapshot ui.png :: chụp giao diện ra ảnh
```

</details>

Xem [lịch sử phiên bản](CHANGELOG.md).

## 📄 Ghi công

- [FFmpeg](https://ffmpeg.org) — bản build kèm theo của [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), giấy phép GPLv3.
- [NAudio](https://github.com/naudio/NAudio) — MIT License.

---

<sub>🌐 <b>English:</b> a free, portable Windows screen recorder made for capturing online lessons — screen, system audio and microphone — so you can review them anytime. Download <code>ScreenRecorder.zip</code> from <a href="https://github.com/minhngoc8181/ScreenRecorderApp/releases/latest">Releases</a>, unzip and run. Built through vibe coding with an AI assistant.</sub>
