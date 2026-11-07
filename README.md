# MusicBar

[English](README.en.md) · **Tiếng Việt**

Thanh taskbar nằm im ba mươi năm rồi. Cho nó hát đi.

**Tải về:** [bản cài đặt](https://github.com/lvt1693/music-bar/releases/latest/download/MusicBar-1.8.0-Setup.exe)
hoặc [bản portable](https://github.com/lvt1693/music-bar/releases/latest/download/MusicBar-1.8.0-portable.zip)
(Windows 10 2004 trở lên hoặc Windows 11, 64-bit, không cần cài .NET).

**Hướng dẫn dùng:** [wiki tiếng Việt](https://github.com/lvt1693/music-bar/wiki), có ảnh từng tab cài
đặt và phần hỏi đáp.

**Trang web:** [musicbar.truclv.com](https://musicbar.truclv.com), có **kho giao diện** của cộng đồng
(skin, visualizer, dải LED) để tải về hoặc đăng giao diện của bạn.

**Có gì mới:** bản 1.8.1 thêm **log để gửi** đã che tên bài hát, **chế độ an toàn** cho Explorer, nút tắt
tăng tốc phần cứng và sửa lỗi đổi loa làm thanh đứng nhịp. Bản 1.8 có **kho giao diện trực tuyến** ngay trong app, **phần hướng dẫn lần đầu mở** (chọn gì thấy ngay trên taskbar), sửa vụ
thanh tiến trình biến mất khi bật taskbar trong suốt, **nhẹ RAM hẳn** (không còn phình dần khi
chạy lâu), chạy gọn với **taskbar tự ẩn**, đọc được
**VLC**, thêm nút khôi phục cài đặt mặc định và chấm đỏ báo thông báo.
Từng bản có gì xem ở [CHANGELOG](CHANGELOG.md).

Thấy MusicBar hữu ích, muốn mời mình ly cà phê thì quét mã này nhé (VPBank · 178971468 · LE VAN TRUC).

<img src="docs/donate.png" width="220" alt="Mã QR chuyển khoản VPBank, LE VAN TRUC, 178971468">

![Thanh taskbar ngủ gật, tiếng cào đĩa, icon MusicBar rơi xuống và đèn sân khấu bật sáng](docs/wake-up.gif)

Nhạc phát cả ngày mà taskbar chỉ biết hiện mỗi cái đồng hồ. MusicBar biến cả chiều ngang
taskbar thành một thanh tiến trình đổi màu theo ảnh bìa và nhún nhảy theo nhạc thật, đặt ảnh
bìa với nút ⏮ ⏯ ⏭ ngay cạnh khay hệ thống, và cho lời bài hát chạy karaoke trên khoảng trống
của taskbar. Taskbar vẫn bấm, vẫn kéo, vẫn ghim app như cũ. MusicBar chỉ ở dưới chân nó.

Nó bắt mọi app đang phát nhạc qua Windows Media Session: Spotify, YouTube trong trình duyệt,
Media Player, Apple Music… VLC không báo cho Windows, nên bật **Đọc VLC** ở Cài đặt → Chung là
MusicBar đọc thẳng VLC. Không có nhạc thì nó lặn mất, taskbar về nguyên trạng.

## Nó làm gì

- **Thanh tiến trình trải hết taskbar**, nằm dưới các icon. Mười bốn kiểu (bảng bên dưới),
  kiểu nào cũng lấy màu từ ảnh bìa, màu app đang phát, màu nhấn của Windows hoặc màu bạn chọn.
  Chạy theo 4 hướng: trái sang phải, phải sang trái, từ giữa ra hai đầu, hoặc từ hai đầu vào
  giữa. Không thích nó chạy qua các icon thì bật **Chỉ chạy ở chỗ trống**: thanh né Start, icon
  ứng dụng và khay hệ thống, chạy nối từ khoảng trống này sang khoảng trống kia, mép mờ dần
  cạnh icon.
- **Hoặc một visualizer thay cho thanh tiến trình:** 6 kiểu chỉ nhảy theo nhạc, 60 khung / giây,
  và 10 hiệu ứng **dải LED RGB** như dải LED gaming dán dưới màn hình. Mỗi kiểu nhớ bảng màu
  riêng, và tự làm skin được (chi tiết ở dưới). Trong cài đặt chúng nằm gọn trong 4 tab: Thanh
  tiến trình, Visualizer, Dải LED RGB, Skin; rê chuột lên ô nào thì dòng bên dưới tả ô đó.
- **Taskbar nằm đâu cũng chạy:** dưới, trên, hay dựng dọc bên trái / bên phải. Taskbar dọc thì
  thanh tiến trình chạy từ dưới lên, ảnh bìa và nút ⏮ ⏯ ⏭ xếp chồng, lời bài hát hiện mỗi chữ
  một dòng, còn thẻ bài hát và Control Center mở sang bên cạnh.
- **Nhiều màn hình:** chọn thanh tiến trình hiện ở màn chính, tất cả hay một màn, và chọn riêng
  màn cho trình phát với lời bài hát (Cài đặt → Chung → Màn hình).
- **Tối giản nếu thích:** ẩn trình phát mà giữ lời (lời tự dời vào khoảng trống lớn nhất), hoặc
  ẩn cả hai, chỉ để thanh tiến trình chạy (Cài đặt → Trình phát).
- **Một cái mặt sống trên taskbar** (chi tiết ở dưới): cầm mic hát theo nhạc, ngủ gật về đêm,
  cười khi bị chọc, giận khi bị chọc hoài, đội mũ Noel khi tới Noel, canh giờ Pomodoro cho bạn.
- **Tổng kết năm âm nhạc** (chi tiết ở dưới): top bài, nghệ sĩ ruột, giờ vàng, bạn là kiểu người
  nghe nào, gói vào một tấm ảnh để khoe.
- **Lần đầu mở có phần hướng dẫn:** đi qua kiểu thanh, nền taskbar, trình phát, lời, cái mặt và
  vài thói quen; chọn gì taskbar đổi ngay cái đó (chưa có nhạc thì một bài mẫu phát để xem thử).
  Bỏ qua bước nào cũng được, mỗi bước chỉ chỗ đổi lại trong cài đặt. Xem lại ở Cài đặt → Giới thiệu.
- **Tự báo bản mới:** mở app (hay mở máy) là nó xem GitHub có bản mới chưa, có thì hỏi một câu ở
  góc màn hình.
- **Nền trong suốt, sáng hay tối:** mặc định taskbar trong suốt, thanh nằm ngay dưới icon. Muốn có
  nền thì chọn tối, sáng, *Theo Windows* (đổi theo ngay khi Windows đổi chế độ) hay *Theo Windows,
  pha màu bài hát*. Nền sáng thì lời bài hát,
  nút và cái mặt chuyển sang màu tối cho icon đen của Windows vẫn rõ. Taskbar trong suốt (hoặc
  nền rất mờ) thì MusicBar nhìn hình nền ngay sau taskbar: sáng thì chữ tối, tối thì chữ trắng,
  cả với hình nền động của Wallpaper Engine.
- **Nhảy theo nhạc thật.** Các kiểu động nghe chính âm thanh đang ra loa (WASAPI loopback),
  xử lý ngay trong máy, không gửi đi đâu. Tắt đi thì chúng nhún theo nhịp giả lập.
- **Lời bài hát đồng bộ** từ [LRCLIB](https://lrclib.net), tô màu karaoke từng chữ, 8 hiệu ứng
  đổi dòng (cuộn, mờ, quét, phóng, nảy từng chữ, gõ chữ, băng chạy…). Lời tự tìm khoảng
  trống lớn nhất trên taskbar, hoặc trượt ra cạnh ảnh bìa. Lời đã tải lưu thành `.lrc`, lần
  sau khỏi cần mạng. Lệch thì lăn chuột trên lời để chỉnh sớm / trễ 0,25 giây cho từng bài.
  Bấm vào lời là ra bảng lời cả bài (chi tiết ở dưới).
- **Kiểu chữ tự chọn:** cỡ, màu, độ đậm, viền (nhỏ là viền sắc, lớn là quầng mềm), và 10
  phông miễn phí đủ dấu tiếng Việt: Be Vietnam Pro, Lexend, Montserrat, Quicksand, Nunito,
  Comfortaa, Playfair Display, Lora, Dancing Script, Pacifico.
- **Vùng yên tĩnh quanh lời:** ngay dưới dòng lời đang hiện, thanh tiến trình dịu xuống còn
  khoảng 30 %, dài ngắn theo từng dòng, nên chữ luôn đọc rõ.
- **Taskbar Windows 11 trong suốt**, tự làm, không cần cài thêm gì (chi tiết ở dưới).
- **Trung tâm điều khiển riêng:** bấm nút loa trên khay là ra một bảng gom thông báo, cài
  đặt nhanh, âm lượng và thiết bị phát. Danh sách loa, mạng Wi-Fi, thiết bị Bluetooth trượt ra
  bên cạnh, chọn là đổi ngay.
- **Chỉnh taskbar của Windows:** cỡ nhỏ / vừa (đọc đúng cài đặt *Kích thước taskbar* mới của
  Windows 11), Start và icon ở giữa. Chạy được cả với taskbar kiểu Windows 10 của
  [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher).
- **Thẻ thông tin bài hát:** giữ chuột trên ảnh bìa là ra ảnh lớn, ca sĩ, album, năm, thể loại.
- **Tiếng Anh và tiếng Việt:** giao diện mặc định là tiếng Anh, đổi sang tiếng Việt ở ô ngôn
  ngữ góc trên bên trái cửa sổ cài đặt. Đổi là đổi ngay, không cần mở lại app.
- **Bảy kiểu nút ⏮ ⏯ ⏭** (Cài đặt → Trình phát → Kiểu nút điều khiển, hoặc chuột phải lên
  widget): Đĩa sáng, Tối giản, Viên kẹo, Vòng tiến trình (vòng quanh nút phát chạy theo bài),
  Đĩa than (đĩa xoay khi phát, nhãn đĩa là ảnh bìa), Phím cassette (phím bấm lún, có đèn báo)
  và Bong bóng (bấm là nảy như thạch).

## 14 bộ áo

Chọn ở **Cài đặt → Thanh tiến trình** (bản xem trước chạy thật với bài đang phát) hoặc chuột
phải → Kiểu thanh tiến trình.

![Thanh taskbar mặc bộ Cực quang trên sân khấu](docs/outfits.jpg)

| Kiểu | Trông ra sao |
| --- | --- |
| Cực quang (mặc định) | Ba dải sáng uốn lượn theo bảng màu bài hát, biên độ theo nhạc |
| Lưới màu | Những mảng màu lớn từ ảnh bìa trôi chậm, hòa vào nhau, phồng lên theo từng dải tần |
| Sợi tơ | 14 sợi tơ mảnh xoắn quanh nhau, se lại thành một sợi ở đầu phát |
| Ánh kim | Giấy kim tuyến đổi sắc quanh màu bài hát, vệt sáng lướt qua mặt giấy |
| Ánh đèn mờ | Đốm đèn bokeh xa gần, mỗi đốm nhấp nháy theo một dải tần |
| Mực loang | Màu nước loang sau đầu phát, giọt mực vẫn nở tiếp sau khi đầu phát đi qua |
| Dải màu ảnh bìa | Màu từng cột của ảnh bìa trải thành dải, lộ dần theo bài |
| Điện ảnh | Ảnh bìa nhòe sâu, trôi chậm như một cảnh phim |
| Ống neon | Ống neon dọc mép, chập chờn, hắt sáng xuống taskbar |
| Chất lỏng | Mặt chất lỏng gợn sóng ở đầu phát, bọt khí li ti |
| Phổ âm | Cột phổ âm thanh thật, phần đã phát được tô màu |
| Dạng sóng | Sóng âm kiểu SoundCloud, vẽ dần dạng sóng thật khi phát |
| Ô LED | Ma trận ô vuông lấp lánh, cột đầu phát kiêm đồng hồ mức âm |
| Cổ điển | Phủ màu và quầng sáng ở đầu phát |

Kiểu động vẽ 20 - 30 khung / giây khi đang phát. Phổ âm, các visualizer và skin vẽ theo đúng nhịp
màn hình (60 khung / giây) khi cắm sạc, chạy pin thì về khoảng 30 cho đỡ tốn. Tạm dừng thì chúng
trôi chậm tại chỗ, phần nhún theo nhạc lắng xuống thành nhịp thở nhẹ; muốn đứng im hẳn thì tắt
**Vẫn chuyển động khi tạm dừng** ở Cài đặt → Thanh tiến trình.

### Visualizer

Không cần biết bài tới đâu, chỉ muốn taskbar nhảy theo nhạc? Chọn một visualizer ở cùng trang:

| Kiểu | Trông ra sao |
| --- | --- |
| Cột sóng | Cột nhạc khắp taskbar, trầm ở giữa, bổng ra hai đầu, có vạch đỉnh rơi chậm |
| Chân trời | Phổ âm thành dải núi soi bóng trên một đường sáng |
| Dao động | Chính sóng âm, một nét sáng chạy ngang, đứng yên chứ không giật |
| Hào quang | Những quầng sáng mềm phồng lên theo từng phần của bản nhạc, lóe lên theo nhịp trống |
| Đom đóm | Đốm sáng bay lên từ mép màn hình, nhạc càng to càng nhiều, bùng lên theo từng nhịp |
| Đèn LED | Phổ âm bằng ma trận đèn, từ mát đến nóng, có vạch đỉnh |

### Dải LED RGB

Một dải đèn chạy dọc mép màn hình, ánh sáng tỏa mềm lên taskbar như dải LED có tấm khuếch tán,
hiệu ứng như bộ điều khiển LED gaming nhưng nhảy theo nhạc:

| Hiệu ứng | Trông ra sao |
| --- | --- |
| Cầu vồng | Cầu vồng chạy dọc dải đèn, nhạc càng dồn càng nhanh, càng sáng |
| Phổ cầu vồng | Mỗi đoạn đèn là một dải tần: bass đỏ ở giữa, treble tím ra hai đầu |
| Nhịp thở | Cả dải thở theo nhạc, đổi dần qua ba màu của bảng màu |
| Gợn sóng | Mỗi nhịp trống thả một gợn sóng lan ra dọc dải |
| Đèn quét | Một con mắt sáng quét qua lại để vệt mờ dần, nhạc to thì quét nhanh |
| Sao lấp lánh | Đèn lấp lánh ngẫu nhiên, đoạn nhạc to thì dày hơn, tới nhịp thì rộ lên |
| Lửa | Lửa bập bùng dọc dải, tiếng bass làm tóe lửa |
| Sao băng | Mỗi nhịp một vệt sao băng lao ngang, đuôi lấp lánh tắt dần |
| Đèn chạy | Những dải sáng ba màu đuổi nhau, tới nhịp thì vọt lên |
| Đồng hồ VU | Xanh, vàng rồi đỏ từ giữa ra hai bên theo độ to, có vạch đỉnh |

Cầu vồng, phổ cầu vồng, lửa và đồng hồ VU có màu riêng của chúng; các hiệu ứng còn lại theo bảng
màu bạn chọn.

### Bảng màu

Mỗi kiểu (cả visualizer và skin) nhớ màu riêng: **Theo bài hát** (mặc định), một trong 11 bảng
phối sẵn (Hoàng hôn, Đại dương, Cực quang, Neon, Kẹo bông, Anh đào, Rừng xanh, Than hồng, Vàng kim,
Băng tuyết, Bạc), hoặc **Tự chọn** ba màu. Chọn ngay dưới thư viện kiểu.

### Skin tự làm

Skin là một file `.hlsl` nhỏ: một pixel shader vẽ thanh bằng card đồ họa, kiểu Shadertoy. MusicBar
đưa vào thời gian, tiến trình, độ to, nhịp, phổ âm, dạng sóng và ba màu của bảng màu. Skin chỉ vẽ,
không đọc file hay vào mạng. **Thêm skin mẫu** để có 4 skin (Liquid Chrome, Comet, Plasma,
Synthwave); **Nhập skin…** để thêm file của bạn. Không biết viết shader thì dán
[hướng dẫn skin](docs/skins.md) cho ChatGPT / Claude / Gemini kèm mô tả thứ bạn muốn: trong đó có
sẵn câu lệnh để dán.

## Cài

- **Bản cài đặt** (`MusicBar-1.8.0-Setup.exe`): cài cho riêng tài khoản của bạn vào
  `%LOCALAPPDATA%\Programs\MusicBar`, không hỏi quyền admin, có mục trong Start và gỡ được
  trong Cài đặt → Ứng dụng. Cài đè hay gỡ đi thì MusicBar được báo thoát trước, để trả taskbar
  và khay về đúng như Windows sắp xếp.
- **Gỡ là trả lại như cũ:** lần đầu chạy, trước khi đụng vào gì, MusicBar ghi lại Windows đang
  thế nào (chế độ sáng / tối của taskbar, cỡ taskbar, căn giữa hay căn trái, kiểu taskbar của
  ExplorerPatcher) vào `%LOCALAPPDATA%\MusicBar\windows-before.json`. Gỡ cài đặt thì cái nào
  MusicBar đã đổi được trả về như ban đầu, trừ khi sau đó bạn tự đổi lại (khi ấy lựa chọn của bạn
  được giữ). Thoát app bình thường thì chỉ trả những gì MusicBar giữ lúc đang chạy (taskbar trong
  suốt, khay gộp, chế độ sáng / tối nó tự đổi); cỡ và kiểu căn taskbar bạn chọn trong MusicBar vẫn
  để nguyên vì đó là lựa chọn của bạn.
- **Bản portable** (`MusicBar-1.8.0-portable.zip`): giải nén ra đâu cũng được, chạy
  `MusicBar.exe`.

**Cập nhật:** từ bản 1.4, MusicBar tự xem GitHub mỗi lần chạy (và mỗi ngày một lần khi đang
chạy). Có bản mới thì một thẻ hiện ở góc trên khay: *Cập nhật* là tải về, kiểm tra mã SHA-256,
cài êm rồi tự mở lại; *Để lần sau* thì lần sau hỏi tiếp; tick *Bỏ qua phiên bản này* là im luôn
tới bản sau nữa. Bản portable thì nút đó mở trang tải. Tắt được ở **Cài đặt → Giới thiệu**.
Đang dùng bản 1.3 trở về trước thì tải tay bản 1.4 một lần, từ đó về sau nó tự báo.

Lên từ bản 1.1 trở về trước thì khởi động lại Explorer (hoặc đăng xuất rồi vào lại) một lần,
để module làm trong suốt taskbar bản mới thay bản cũ. Chưa làm thì MusicBar vẫn chạy, chỉ là
có nhiều màn hình thì nó tạm phủ mờ thay vì làm trong suốt, và cài đặt sẽ nhắc.

Hai bản là cùng một app, đã gói sẵn .NET 10 nên không cần cài gì thêm, và lúc phát nhạc tốn
khoảng 50 MB RAM (cột Memory của Task Manager, đo trên máy thử), chạy lâu không tăng thêm. Lần đầu chạy, phần hướng dẫn
tự mở; sau đó nhấp biểu tượng ở khay để mở cài đặt. Muốn nó chạy cùng Windows thì bật
**Khởi động cùng Windows** trong cài đặt. Cài đặt, lời bài hát và nhật ký nằm ở `%LOCALAPPDATA%\MusicBar`
cho cả hai bản.

App chưa có chữ ký số, nên lần đầu SmartScreen sẽ cản: bấm **More info → Run anyway**.

Dòng lệnh, nếu bạn thích:

```powershell
MusicBar.exe --demo       # bài hát mẫu có sẵn lời, không cần trình phát nào
MusicBar.exe --settings   # mở cài đặt (cả khi app đang chạy)
MusicBar.exe --welcome    # mở phần hướng dẫn lần đầu
MusicBar.exe --recap      # hiện ngay banner tổng kết năm, không đợi tới 24/12
MusicBar.exe --exit       # thoát, trả taskbar về như cũ
```

## Taskbar trong suốt

Windows không cho vẽ "vào trong" taskbar, nên thanh tiến trình là một cửa sổ riêng giữ ngay
dưới taskbar, và chỉ nhìn xuyên qua được khi taskbar trong suốt. Bật ở **Cài đặt → Taskbar →
Làm taskbar trong suốt**. Đang chạy [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB)
thì MusicBar để nó lo.

Taskbar đặt **tự ẩn** thì MusicBar giữ nền của Windows và phủ mờ thanh lên trên: lúc taskbar trồi
lên, phía sau nó là cửa sổ ứng dụng chứ không phải hình nền, làm trong suốt thì chữ của ứng dụng sẽ
đè vào icon và lời. Thanh, trình phát và cái mặt trượt lên xuống cùng taskbar.

- **Taskbar kiểu Windows 10** (Windows 10, ExplorerPatcher): đặt thuộc tính DWM của cửa sổ
  taskbar, và đặt lại mỗi khi Explorer tự đổi nền (mở Start, đổi theme).
- **Taskbar Windows 11 gốc:** nền là một phần tử XAML bên trong Explorer. MusicBar nạp một
  module nhỏ (`MusicBar.TaskbarTap.dll`, nhúng sẵn trong app) vào Explorer theo cách
  TranslucentTB làm, viết lại độc lập. Module dùng API chẩn đoán XAML công khai để làm mờ lớp
  nền về 0 (không đụng tới màu nền, nên Windows vẫn tự đổi nền khi đổi sáng / tối). Nó chỉ giữ nền
  trong suốt khi MusicBar còn sống: MusicBar thoát hay bị tắt ngang (cả bằng Task Manager) là
  taskbar tự có lại nền. Bản mới tự bảo bản cũ trong Explorer nhường chỗ, nên cập nhật không phải
  khởi động lại Explorer.
- **Icon của Windows đổi màu theo nền:** nền MusicBar sáng mà Windows đang tối thì icon khay, đồng
  hồ, Wi-Fi, loa sẽ trắng trên nền sáng. MusicBar tự chuyển chế độ taskbar của Windows cho khớp, và
  trả lại khi thoát (bị tắt ngang thì module trong Explorer trả giúp). Tắt ở **Cài đặt → Taskbar →
  Icon taskbar của Windows đổi màu theo nền**.

| Chế độ lớp | Khi nào |
| --- | --- |
| Tự động (mặc định) | Taskbar trong suốt được thì nằm dưới icon, không thì phủ mờ lên trên |
| Nằm dưới icon | Taskbar đã trong suốt: trông như chính nền taskbar |
| Phủ mờ lên taskbar | Taskbar không trong suốt: phủ lên trên, tự giới hạn độ đậm để icon vẫn rõ |

## Trung tâm điều khiển

![Bảng điều khiển của MusicBar: thông báo, cài đặt nhanh, âm lượng, danh sách loa trượt ra bên trái](docs/control-center.jpg)

Khi MusicBar chạy, nút mạng / âm lượng (Windows 11) hoặc nút Control Center (ExplorerPatcher)
mở bảng của MusicBar, từ trên xuống:

- **Thông báo** của Windows: bấm để mở app gửi, × để bỏ từng cái, *Xóa tất cả* để dọn.
- **Cài đặt nhanh:** Wi-Fi, Bluetooth, Ánh sáng đêm, Phụ đề trực tiếp bật / tắt thật. *Màn
  hình chiếu* (như Win+P) và *Truyền màn hình* (Miracast) mở danh sách trượt ra bên cạnh.
- **Wi-Fi:** bấm mũi tên bên ô Wi-Fi là ra danh sách mạng. Bấm một mạng để kết nối (nhập mật
  khẩu, có nút hiện mật khẩu, tick *Tự động kết nối*), chuột phải để kết nối / ngắt / quên
  mạng. Từ Windows 11 24H2, Windows chỉ cho app xem danh sách Wi-Fi khi được dùng vị trí; máy
  tắt vị trí thì MusicBar mở thẳng danh sách Wi-Fi của Windows.
- **Bluetooth:** thiết bị đã ghép đôi và thiết bị ở gần. Ghép đôi (mã PIN hiện ngay trong bảng),
  kết nối / ngắt tai nghe và loa, gỡ thiết bị, cũng có menu chuột phải.
- **Độ sáng màn hình** (laptop, hoặc màn rời nhận lệnh DDC/CI qua cáp): kéo hay lăn chuột như
  thanh trong Quick Settings của Windows. Máy không chỉnh được độ sáng thì hàng này tự ẩn.
- **Âm lượng** và **thiết bị phát**: danh sách loa trượt ra, chọn là đổi ngay. Lăn chuột trên hàng
  âm lượng, hoặc ngay trên nút mạng / loa ở taskbar, là tăng giảm 2 % mỗi nấc như Windows.

**Win + A** mở bảng của MusicBar (bấm lại để đóng), tắt được ở **Cài đặt → Taskbar**. Win + N vẫn
là bảng thông báo của Windows. Tắt tính năng hoặc thoát MusicBar là khay về nguyên như Windows sắp
xếp. Có thông báo mới thì nút trên khay hiện số; thích gọn thì bật **Chấm đỏ báo thông báo mới** ở
**Cài đặt → Taskbar** để chỉ hiện một chấm đỏ nhỏ.

## Cái mặt

Anh em xem video giới thiệu thích cái mặt quá, nên giờ nó ở hẳn trên taskbar, cạnh trình phát
(đổi chỗ ở **Cài đặt → Cái mặt**).

- **8 kiểu mặt:** Cổ điển (đúng cái mặt trong video), Cổ điển nữ, Anime nữ, Anime nam, Người
  ngoài hành tinh, Rô-bốt (mắt LED, miệng là cột sóng nhạc), Mèo, Ngáo.
- **Mặt của bạn:** ảnh, GIF hoặc WebP (động cũng được) cho từng tâm trạng: bình thường, đang phát
  nhạc, tạm dừng, ngủ, chán, vui, chóng mặt, lo lắng, tập trung. Chỉ cần ảnh *Bình thường*, tâm
  trạng nào chưa có ảnh thì mượn ảnh gần giống. Ảnh được giữ tối đa 256 px cho nhẹ máy, và vẫn
  nhún, nghiêng, nảy theo nhạc như mặt vẽ. Chọn ở **Cài đặt → Cái mặt → Mặt của bạn**.
- **Sống theo tình hình:** có nhạc thì nhún theo nhịp và hát theo, qua bài mới thì mắt sao, tạm
  dừng thì ngó quanh chờ, nhạc tắt sau một buổi dài thì rưng rưng.
- **Sống theo giờ:** sáng ngáp rồi vươn vai, gần trưa thèm ăn, ăn xong gật gù, đúng giờ chẵn giật
  mình, khuya thì mí mắt sụp, bạn rời máy là nó ngủ (có Zzz), quay lại là nó dậy.
- **Không đứng yên:** chớp mắt, liếc ngang liếc dọc, ngân nga, nháy mắt, lè lưỡi, mắt nhìn theo
  con trỏ khi lại gần.
- **Trêu được:** bấm là cười, bấm liên tục thì bực rồi nổi cáu rồi dỗi quay đi; giữ chuột rê qua
  lại (hoặc rê con trỏ qua lại trên nó) là cười chảy nước mắt rồi thả tim; bấm đúp mắt sao; cuộn
  chuột là chóng mặt; đang ngủ mà chọc là giật mình.
- **Cái mic trả lại rồi:** có nhạc là cái mic trong video giới thiệu bật ra, nó cầm hát theo,
  tạm dừng là cất đi.
- **Mũ, kính, tai nghe:** mũ lưỡi trai (màu theo bài hát), mũ len, vương miện, kính tròn, kính
  râm, kính pixel "deal with it", tai nghe. Tới mùa lễ thì tự đổi mũ: hoa mai dịp Tết, tai thỏ
  Trung thu (tính theo âm lịch), mũ phù thủy Halloween, mũ Noel.
- **Hẹn giờ tập trung (Pomodoro):** chuột phải lên mặt → *Bắt đầu tập trung*. Nó nhíu mày làm
  việc cùng bạn, có đồng hồ nhỏ bên cạnh; hết giờ thì nói qua bong bóng nhắc đứng dậy vươn vai,
  uống nước. Cứ 4 hiệp được nghỉ dài. Đổi số phút ở **Cài đặt → Cái mặt**.
- **Để ý cái máy:** CPU chạy hết công suất thì đổ mồ hôi bốc khói, rớt mạng thì hoảng, có mạng
  lại thì mừng, cắm sạc thì sướng, pin yếu thì uể oải.
- **Đi dạo:** rảnh không có nhạc thì thỉnh thoảng nó đi dọc khoảng taskbar trống, ngó quanh rồi
  về chỗ cũ. Không bao giờ đi đè lên trình phát hay lời bài hát.
- Chuột phải lên nó để đổi kiểu, đội mũ, hẹn giờ hay ẩn đi. Không có nhạc mà MusicBar ẩn thì mặt
  vẫn ở lại ngủ gật (tắt được).

## Lời bài hát

![Lời bài hát chạy karaoke trên taskbar bằng phông Playfair Display](docs/karaoke.jpg)

Tìm theo thứ tự: file `.lrc` trong thư mục lời (`%LOCALAPPDATA%\MusicBar\Lyrics`, tên
`Ca sĩ - Tên bài.lrc`, tự thả file của mình vào cũng được), rồi tới LRCLIB theo tên bài, ca sĩ,
album và thời lượng, rồi tới NetEase nếu bạn bật. Bài không có lời được nhớ 3 ngày để khỏi hỏi lại.
Mạng cần proxy thì điền ở **Cài đặt → Lời bài hát → Proxy**.

- **Đúng nghệ sĩ:** bài trùng tên mà khác ca sĩ thì bị loại, trừ khi dài đúng bằng bài đang phát
  (cùng bản, chỉ là tên ca sĩ viết kiểu khác). Thà báo không có còn hơn hiện lời của bài khác.
- **Karaoke đúng từng chữ:** LRCLIB giờ lưu được thời gian của từng chữ. Bài nào có thì chữ được
  tô đúng lúc ca sĩ hát nó, thay vì ước lượng theo độ dài câu. Hiện chưa nhiều bài có.
- **Gửi lời lên LRCLIB:** có file `.lrc` của mình mà LRCLIB chưa có, hoặc đã chỉnh lời bị lệch,
  thì trong bảng lời bấm *Gửi lên LRCLIB*. Sửa tên bài, ca sĩ nếu cần, bấm *Gửi*. MusicBar tự giải
  bài toán chống spam của LRCLIB (vài giây) rồi gửi; bản đã chỉnh giờ thành bản mới của đúng bài
  đó trên LRCLIB, không đẻ thêm bài trùng. Từ đó ai dùng LRCLIB cũng có.
- **NetEase dự phòng:** bật ở **Cài đặt → Lời bài hát → Tìm thêm trên NetEase**. Khi LRCLIB không
  có lời chạy theo nhạc (hay đang sập), MusicBar hỏi NetEase Cloud Music, mạnh nhạc Hàn, Hoa,
  Nhật. Chỉ nhận đúng ca sĩ, đúng độ dài, không lấy bản live / remix / cover. Kết quả NetEase cũng
  hiện trong ô tìm tay. Đây là nguồn không chính thức nên mặc định tắt.

- **Title YouTube dài cỡ nào cũng dọn được:** kiểu "HIEUTHUHAI - Không Thể Say (prod. by
  Kewtiie) | Album 'Ai Cũng Phải Bắt Đầu Từ Đâu Đó'" được tách theo dấu `-` và `|`, bỏ phần
  Album, OST, prod., Official MV, Lyrics Video, số track, hashtag, nhận ra đoạn nào là tên kênh,
  còn lại "Không Thể Say". LRCLIB vẫn không ra thì MusicBar tự thử tiếp tên ngắn dần, và tên
  bài không kèm ca sĩ (kênh của hãng đĩa thường không phải tên ca sĩ).
- **Bảng lời cả bài:** bấm vào lời cạnh ảnh bìa (hoặc chuột phải → Lời bài hát → Lời cả bài).
  Dòng đang hát sáng lên và luôn nằm giữa; bấm dòng nào là phát từ dòng đó; có nút chỉnh sớm /
  trễ (nhấn giữ là chỉnh liên tục, nhanh dần), và nút *Tìm lyrics khác* khi bị ra sai bài: gõ tên,
  chọn kết quả đúng (có dấu ✓ khi dài bằng bài đang phát), MusicBar nhớ lựa chọn đó cho lần sau.
- **Số lệch đọc như phụ đề phim:** *Lời trễ +0,50 giây* là lời hiện muộn hơn, *Lời sớm −0,50 giây*
  là hiện sớm hơn. Trong file `.lrc` thì thẻ `[offset:]` vẫn giữ đúng chuẩn LRC (dương là sớm) để
  trình phát khác đọc đúng.
- **Karaoke theo nhịp hát:** câu không có thời gian từng chữ thì tốc độ tô màu được ước theo số âm
  tiết (tiếng Việt mỗi chữ một âm, chữ Hán / kana / Hangul mỗi ký tự một âm), không kéo dài tới tận
  câu sau nữa, nên vệt màu đi cùng giọng hát.
- **Phiên âm Hàn, Nhật, Trung:** hiện dưới mỗi dòng hoặc thay luôn lời gốc (**Cài đặt → Lời bài
  hát → Phiên âm**), và có trong bảng lời cả bài. Tiếng Hàn theo cách phiên âm chuẩn (có nối âm),
  tiếng Trung ra pinyin có dấu thanh, tiếng Nhật ra romaji. Kanji tiếng Nhật thì Windows chỉ
  đọc được khi máy có cài tiếng Nhật; không có thì kana vẫn ra romaji, kanji giữ nguyên.

## Tổng kết năm

MusicBar đếm những gì bạn nghe: bài, nghệ sĩ, số phút, ngày, giờ và app, lưu trong một file nhỏ
mỗi năm ở `%LOCALAPPDATA%\MusicBar\Stats`. Không gửi đi đâu, tắt được ở **Cài đặt → Giới thiệu**.
Một bài tính là một lượt sau nửa phút nghe, nghe lặp lại thì tính thêm lượt.

Từ 24/12 tới 7/1, một banner ở góc màn hình mời bạn xem tổng kết năm (một ngày hỏi tối đa một
lần, tick *Không nhắc lại năm nay* là thôi). Ngoài mùa đó thì xem lúc nào cũng được: chuột phải
→ *Tổng kết năm âm nhạc*. Tấm card gồm tổng số giờ, top 5 bài, nghệ sĩ ruột, ngày nghe nhiều
nhất, giờ vàng, chuỗi ngày nghe liên tiếp, app hay dùng, và bạn là kiểu nào: Cú đêm, Chim sớm,
Dân văn phòng chill, Fan cứng nghe lặp, Thợ săn nhạc mới hay Người nghe đa năng. *Lưu ảnh* ra
PNG 1080 × 1350 (đúng khổ ảnh dọc của Facebook), *Copy ảnh* để dán thẳng vào bài đăng.

## Build

Cần .NET 10 SDK, và MSVC (Visual Studio hoặc Build Tools, workload "Desktop development with
C++") cho module taskbar Windows 11.

```powershell
dotnet build src/MusicBar/MusicBar.csproj
dotnet run --project src/MusicBar -- --demo
.\installer\build.ps1 -Version 1.8.0     # ra artifacts\: Setup.exe (Inno Setup 6) và portable.zip
```

## Bên trong

```
src/MusicBar/
  App.xaml(.cs)                 khởi động, một phiên bản duy nhất, khay hệ thống, --uninstall
  Models/AppSettings.cs         mọi tùy chọn (%LOCALAPPDATA%\MusicBar\settings.json)
  Services/MediaService.cs      theo dõi session, nội suy vị trí phát, điều khiển
  Services/LyricsService.cs     cache .lrc + LRCLIB;  LrcParser.cs đọc LRC
  Services/TitleCleaner.cs      dọn title YouTube, các cách gọi tên bài để thử dần
  Services/Lyricsfile.cs        đọc Lyricsfile của LRCLIB (thời gian từng chữ)
  Services/NetEaseLyrics.cs     NetEase Cloud Music làm nguồn lời dự phòng
  Services/WallpaperTone.cs     độ sáng hình nền sau taskbar, để chữ tự đổi màu
  Services/Romanizer.cs         phiên âm Hàn / Nhật / Trung (pinyin từ dữ liệu Unihan)
  Services/ListeningStats.cs    đếm giờ nghe cho tổng kết năm;  YearRecap.cs tính tổng kết
  Services/UpdateService.cs     xem bản mới trên GitHub, tải, kiểm tra SHA-256, cài êm
  Services/SkinLibrary.cs       dịch skin (HLSL) bằng trình dịch shader có sẵn trong Windows
  Services/SystemSnapshot.cs    Windows trước khi có MusicBar, trả lại khi gỡ cài đặt
  Services/FocusTimer.cs        hẹn giờ Pomodoro;  SystemSense.cs CPU, mạng, pin cho cái mặt
  Services/AudioOutputs.cs      thiết bị phát, âm lượng, đổi loa mặc định
  Services/NotificationFeed.cs  đọc / bỏ thông báo của Windows
  Services/WifiNetworks.cs      quét, kết nối, quên mạng Wi-Fi (wlanapi)
  Services/BluetoothDevices.cs  thiết bị Bluetooth, ghép đôi;  BluetoothAudioLink.cs nối tai nghe
  Interop/TaskbarTracker.cs     vị trí taskbar (cả taskbar dọc, cả taskbar tự ẩn), dò khoảng trống
  Interop/TaskbarAutomation.cs  đọc các nút trên taskbar qua UI Automation COM, không rò bộ nhớ
  Services/Community.cs         kho trực tuyến: duyệt, tải (kiểm SHA-256), đăng; banner tài trợ
  Services/LookPresets.cs       preset tải từ kho (kiểu thanh, ba màu, độ đậm, hướng; không có mã)
  web/                          trang musicbar.truclv.com (FastAPI): giới thiệu, tải về, kho, trang duyệt,
                                trình quét file gửi lên (không bao giờ chạy file), bản triển khai lên server
  Interop/XamlTaskbar.cs        nạp module làm trong suốt taskbar Windows 11
  Interop/TrayMerge.cs          ẩn icon âm lượng / chuông, dồn khay, khôi phục
  Views/SeekRenderer.cs         14 kiểu thanh tiến trình, các visualizer, 4 hướng chạy;
                                SeekRenderer.Leds.cs dải LED RGB
  Views/CpuRendering.cs         các cửa sổ trên taskbar vẽ bằng CPU (đỡ tới 200 MB bộ nhớ driver)
  Views/LyricsPresenter.cs      lời bài hát và các hiệu ứng;  WordStackPresenter.cs cho taskbar dọc
  Views/TaskbarOverlay.cs       điều phối: xếp lớp, màu, lời, menu
  Views/TaskbarOverlays.cs      một overlay cho mỗi taskbar được chọn
  Views/LyricsPanelWindow       bảng lời cả bài, tìm lyrics bằng tay
  Views/RecapWindow             card tổng kết năm;  RecapBannerWindow banner cuối năm
  Views/Face/                   cái mặt: FaceBrain (tâm trạng), FaceRig (chuyển động), FacePainters (8 kiểu),
                                FaceProps (mũ, kính, mic), FaceSeasons (mũ theo mùa lễ, âm lịch)
src/MusicBar.TaskbarTap/        module C++ chạy trong Explorer (build bằng MSVC cùng app)
installer/                      Inno Setup + build.ps1
promo/                          video giới thiệu (HyperFrames)
```

## Video giới thiệu

Video 52 giây trong [release](https://github.com/lvt1693/music-bar/releases/latest): một thanh
taskbar ngủ gật, kèn buồn, tiếng cào đĩa, rồi sân khấu bật đèn. Toàn bộ phần MusicBar trong
video là chính code của app vẽ ra (`promo/tools/capture` gọi thẳng SeekRenderer,
LyricsPresenter và bảng điều khiển), phần còn lại là HTML dựng bằng
[HyperFrames](https://hyperframes.heygen.com). Giọng đọc từ queen-tts, nhạc nền và tiếng động
từ ElevenLabs. Muốn dựng lại: vào `promo/`, `npx hyperframes preview`.

## Nói thật

- Mới chạy kỹ trên Windows 11 (bản 26300), cả taskbar gốc lẫn taskbar ExplorerPatcher.
  Windows 10 có code cho nó, icon và phông đều có bản dự phòng sẵn trên Windows 10, nhưng
  chưa thử trên máy Windows 10 thật. Dùng Windows 10 mà gặp lỗi thì gửi giúp file
  `%LOCALAPPDATA%\MusicBar\musicbar.log`.
- Ô Phụ đề trực tiếp chỉ có từ Windows 11 22H2, máy cũ hơn sẽ không thấy ô này.
- Module taskbar Windows 11 nằm trong Explorer tới khi Explorer khởi động lại, kể cả sau khi
  gỡ MusicBar. Nó không làm gì khi MusicBar không chạy. Phần mềm diệt virus có thể để ý
  chuyện một DLL được nạp vào Explorer.
- Chưa có chữ ký số, nên SmartScreen sẽ hỏi lần đầu.
- Nhiều màn hình mới thử theo log của người dùng, máy làm app chỉ có một màn. Taskbar dọc đã
  chạy thật trên máy thử (dựng bên trái).
- Danh sách Bluetooth chưa được thử với thiết bị thật (máy thử không có Bluetooth). Phần kết
  nối / ngắt tai nghe gửi lệnh thẳng cho driver âm thanh Bluetooth, vì Windows không có API
  công khai cho việc này. Gặp lỗi thì gửi giúp file log.
- Trên Windows 11, nút mạng / âm lượng được nhận ra qua tên hiển thị (Windows tiếng Anh và
  tiếng Việt).
- Ô Ánh sáng đêm ghi vào một giá trị CloudStore không có tài liệu (định dạng do cộng đồng
  giải mã). Gặp trường lạ là MusicBar không ghi.
- Click lên taskbar thì Windows kéo taskbar lên trên cùng. MusicBar đặt lại thứ tự ngay, và
  kiểm tra lại mỗi 250 ms.
- NetEase là nguồn không chính thức. Gọi nhiều quá thì NetEase trả về toàn bài không liên quan;
  MusicBar khi đó báo không có lời chứ không hiện lời sai, nhưng cũng có nghĩa là có lúc NetEase
  "ngủ" một thời gian. Gửi lời lên LRCLIB mới là cách lâu dài để kho lời V-pop đầy đủ.
- Phiên âm tiếng Trung đọc mỗi chữ một âm cố định, nên chữ đa âm (长, 地, 得, 行…) có lúc đọc
  sai. Kanji tiếng Nhật cần Windows có cài tiếng Nhật (máy làm app không có, nên phần này mới
  thử bằng dữ liệu giả).
- Phần phản ứng với pin chưa thử trên laptop thật (máy thử là máy bàn). Banner tổng kết năm thử
  bằng `--recap`, chưa có ai chờ tới 24/12 thật.
- Cập nhật tự động chỉ chạy từ bản 1.4 trở đi: bản cũ hơn không biết đường hỏi GitHub.
- Thanh độ sáng mới chạy thử trên máy bàn có màn không nhận DDC/CI (hàng tự ẩn đúng); phần laptop
  (qua WMI, như Windows) chưa thử trên laptop thật.
- Win + A được bắt bằng một hook bàn phím; MusicBar chen một phím vô hình vào giữa để Windows không
  mở Start khi nhả phím Windows, cách PowerToys vẫn làm.
- Skin chạy trên card đồ họa (pixel shader 3.0). Máy chỉ có đồ họa phần mềm (máy ảo, Remote
  Desktop) chỉ chạy được shader đời cũ hơn, skin dài có thể không dịch nổi; khi đó MusicBar giữ
  nguyên kiểu thanh tiến trình đang chọn.
- Đọc VLC dùng giao diện web có sẵn của VLC: MusicBar bật nó trong file cài đặt của VLC (`vlcrc`, có
  giữ bản sao `vlcrc.musicbar-backup`), chỉ nghe trong máy (127.0.0.1), cổng 47328, mật khẩu ngẫu
  nhiên; cổng hay mật khẩu bạn đặt sẵn thì giữ của bạn. VLC đọc file đó lúc khởi động nên phải mở lại
  một lần. Đang có app khác phát nhạc thì MusicBar ưu tiên app đó, trừ khi bạn ghim VLC.
- Rê chuột lên video trên YouTube làm trình duyệt phát thử: MusicBar đợi 2,5 giây xem có phải đổi
  bài thật không rồi mới đổi lời, nên bấm qua bài mới trong trình duyệt thì lời đổi theo chậm vài
  giây.

## Cảm ơn

[TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) đã chỉ đường vào nền XAML của
taskbar Windows 11. [LRCLIB](https://lrclib.net) cho lời bài hát.
[ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) giữ taskbar kiểu cũ sống tới giờ.
Phông chữ từ [Google Fonts](https://fonts.google.com). Installer dựng bằng
[Inno Setup](https://jrsoftware.org/isinfo.php).

## License

[MIT](LICENSE). Các phông chữ đi kèm dùng giấy phép SIL Open Font License; bản giấy phép nằm
trong `Assets/Fonts/licenses` cạnh app. Bảng pinyin lấy từ cơ sở dữ liệu Unihan của Unicode,
giấy phép Unicode nằm trong `Assets/Data/licenses`.
