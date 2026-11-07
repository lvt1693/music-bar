# Gửi file log khi MusicBar bị lỗi

MusicBar ghi lại những gì nó làm vào một file nhật ký (log). Khi gặp lỗi, gửi file này kèm báo lỗi là mình
biết ngay chuyện gì xảy ra, khỏi phải đoán.

> In English: see [the end of this page](#in-english).

## 1. Lấy file log

**Trong app** (dễ nhất):

1. Bấm icon MusicBar ở khay hệ thống (góc phải taskbar) để mở **Cài đặt**.
2. Vào trang **Giới thiệu**, mục **Dữ liệu**.
3. Bấm **Lấy log để gửi**. Thư mục mở ra, file `musicbar-share.log` được chọn sẵn: một file gom đủ các log,
   đã che hết tên bài hát và tên tài khoản Windows. Gửi đúng file này là đủ.
   (Bản 1.8.0 nút này tên là **Xem log** và mở file bằng Notepad: bấm **Mở thư mục** bên cạnh để thấy file.)

**Không mở được app, hoặc đang dùng 1.8.0?** Bấm `Win + R`, gõ `%LOCALAPPDATA%\MusicBar` rồi Enter, rồi gửi:
- `musicbar.log`
- `musicbar.old.log` nếu có (log đầy thì phần cũ chuyển sang đây)
- `taskbar-helper.log` nếu lỗi liên quan tới taskbar trong suốt

Gửi càng sớm sau khi lỗi xảy ra càng tốt: log chỉ giữ phần gần nhất.

## 2. Khi bị crash (app tự tắt, hoặc taskbar biến mất rồi hiện lại)

**Từ bản 1.8.1:** cứ mở lại MusicBar là được. App tự nhận ra lần trước bị tắt ngang và chép báo cáo crash
của Windows vào `musicbar.log`. Taskbar sập (Explorer khởi động lại) cũng được ghi lại. Gửi log như mục 1.

**Bản 1.8.0 trở về trước:** lấy thêm báo cáo của Windows:

1. Bấm `Win + R`, gõ `perfmon /rel` rồi Enter (Reliability Monitor).
2. Bấm vào cột ngày bị crash, tìm dòng có dấu X đỏ tên **MusicBar** hoặc **Windows Explorer**.
3. Bấm đúp vào dòng đó (hoặc **View technical details**), bôi đen hết phần chữ, copy rồi dán vào báo lỗi.

## 3. Gửi ở đâu

- **GitHub**: mở [issue mới](https://github.com/lvt1693/music-bar/issues/new) hoặc bình luận vào issue đang có,
  kéo thả file log vào ô bình luận. GitHub không nhận file thì nén lại thành `.zip`.
- Hoặc gửi qua Facebook cho mình.

Kèm vài dòng: lỗi gì, lúc nào, đang làm gì, dùng trình phát nhạc nào (Spotify, YouTube trên trình duyệt...).

## Trong log có gì

Tên bài hát và trình phát đang mở, vị trí taskbar, thông số màn hình, các lỗi của app, và đường dẫn thư mục
(có tên tài khoản Windows của bạn). Không có mật khẩu, tài khoản hay file cá nhân. File `musicbar-share.log`
(từ bản 1.8.1) đã che tên bài hát và tên tài khoản; với log gốc thì cứ mở bằng Notepad xóa bớt trước khi gửi.

---

## In English

1. Click the MusicBar icon in the notification area to open **Settings**, go to **About**, section **Data**,
   and click **Log to send**: it picks `musicbar-share.log`, every log in one file with song titles and your
   Windows user name masked. On 1.8.0 (**View log**, then **Open folder**), or without the app, press
   `Win + R` and open `%LOCALAPPDATA%\MusicBar`.
2. Send `musicbar-share.log`; on 1.8.0, `musicbar.log` plus `musicbar.old.log` and `taskbar-helper.log` if there.
3. After a crash: from 1.8.1 just start MusicBar again, it copies Windows' crash report into the log. On older
   versions, run `perfmon /rel`, open the red **MusicBar** or **Windows Explorer** entry for that day and copy
   its technical details.
4. Drag the files into a [GitHub issue](https://github.com/lvt1693/music-bar/issues/new) (zip them if GitHub
   refuses the file type). The log has song titles, screen and taskbar details and folder paths with your
   Windows user name; no passwords or personal files.
