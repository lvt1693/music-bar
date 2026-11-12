# Các bản MusicBar

Mới nhất ở trên. Bản cài và bản portable của từng bản có ở trang
[Releases](https://github.com/lvt1693/music-bar/releases).

## 1.8.5 · 12/11/2026

**Mới**

- **Đăng giao diện bằng tài khoản Google:** bấm Gửi duyệt trong app thì giao diện được tải lên và quét
  như cũ, rồi trình duyệt mở trang của nó; đăng nhập Google ở đó và bấm Gửi duyệt là xong. Tên hiện
  trên kho là tên trong tài khoản (đổi được trên web), nên app không cần đặt nickname nữa. Trang Tài
  khoản trên [musicbar.truclv.com](https://musicbar.truclv.com) cho xem các giao diện đã gửi, cái nào
  đang chờ, đã lên kho hay bị từ chối và vì sao.
- **Skin trên kho lớn tới 4 MB** (trước chỉ 64 KB). Bản 1.8.4 trở về trước chỉ thấy các skin từ 64 KB
  trở xuống.

**Sửa lỗi**

- Bài hát không có ảnh bìa thì nhật ký ghi rõ là trình phát không gửi ảnh hay MusicBar không đọc được
  ảnh, để gửi log là biết lỗi ở đâu.
- Link tải trong README trỏ đúng bản mới nhất (trước đó vẫn trỏ tên file 1.8.0 nên không tải được).

## 1.8.4 · 10/11/2026

**Mới**

- **Lời từ NetEase bật sẵn** cho người mới cài: LRCLIB không có lời chạy theo nhạc thì hỏi thêm NetEase
  (mạnh nhạc Hàn, Hoa, Nhật). Ai cài từ bản cũ vẫn giữ như đã chọn, bật ở Cài đặt → Lời bài hát →
  Tìm thêm trên NetEase.
- **Dùng được bằng bàn phím và trình đọc màn hình:** bấm Tab thấy rõ đang đứng ở nút nào, các nút chỉ
  có icon giờ có tên cho Narrator đọc.
- Cửa sổ Cài đặt, màn hình giới thiệu và recap tự co cho vừa màn hình nhỏ hay phóng to 150% trở lên,
  không còn tràn mất thanh tiêu đề.
- Nhật ký ghi rõ phần nào làm thanh khựng, để gửi log là biết sửa chỗ nào; lúc máy ngủ không bị tính.

**Sửa lỗi**

- Tên cuộc họp, thông báo chat, tên tab web không còn bị gửi đi tìm lời và thông tin bài hát. Chỉ những
  gì giống bài hát mới được tìm; tìm tay thì vẫn tìm mọi thứ.
- MusicBar không còn đứng chờ khi trình phát hay Windows trả lời chậm: đọc trạng thái trình phát và
  icon app giờ chạy ngầm.
- App phát nhạc lần đầu thấy đôi khi không có icon cho tới lúc mở lại MusicBar.
- File cài đặt hỏng không còn làm mất hết cài đặt: MusicBar lấy lại từ bản sao lưu và giữ file hỏng
  bên cạnh.
- Win + A đôi khi ngừng mở bảng điều khiển sau khi máy ngủ dậy hay bị đơ; giờ tự gắn lại.
- Thiết bị âm thanh lỗi liên tục không còn ghi đầy nhật ký.

## 1.8.3 · 10/11/2026

**Mới**

- **Skin lớn chạy được:** skin quá lớn so với cách vẽ cũ (card đồ họa từ chối vẽ) giờ tự chạy bằng
  Direct3D 11, gần như không còn giới hạn độ chi tiết. Skin thường vẫn vẽ như cũ, không tốn thêm gì.
  Đổi được ở Cài đặt → Chung → Bộ vẽ skin. Cách làm skin lớn có trong [docs/skins.md](docs/skins.md).
- **Ô tìm kiếm** trong trang Giao diện: tìm kiểu thanh, visualizer, dải LED, skin theo tên, mô tả hay
  tác giả, gõ không dấu vẫn ra.
- Cập nhật xong mà Explorer còn giữ bộ phận làm trong suốt của bản cũ thì MusicBar tự khởi động lại
  Explorer một lần, để bản mới chạy hết ngay, không phải tự restart.

**Sửa lỗi**

- Lời bài hát đặt ở **khoảng trống lớn nhất** trên taskbar bấm không được nên không mở được ô tìm lời.
  Giờ bấm vào chữ là mở, cuộn chuột trên chữ để chỉnh lời sớm hay muộn.
- Ảnh bìa đôi khi là logo Spotify (hay logo Google khi xem stream) thay vì bìa bài hát (#17).
- Bài không tìm thấy lời giờ được tìm lại sau 1 ngày thay vì 3; bật NetEase là tìm lại ngay.
- Skin quá lớn cho card đồ họa không còn báo lỗi liên tục, chỉ ghi vào nhật ký một lần.

## 1.8.2 · 08/11/2026

**Mới**

- **Taskbar trong suốt trên mọi màn hình, mọi lúc:** bật là taskbar của cả màn không có thanh cũng
  trong suốt, và không còn đặc lại giữa hai bài hay lúc không phát nhạc (#6).
- Bản mới giờ hỏi và tải từ [musicbar.truclv.com](https://musicbar.truclv.com) trước, trang web không
  vào được mới về GitHub.

**Sửa lỗi**

- Thanh và lời khựng nhẹ mỗi 7 đến 8 giây trên máy có nhiều thông báo (#17).
- Bấm icon khay mở Cài đặt rất lâu, thanh đứng hình và CPU lên cao; mở lại app sau khi tắt ngang cũng
  lâu mới hiện (#17). Skin giờ chỉ dịch một lần rồi giữ lại, và dịch ngầm.
- Kho trực tuyến trong app nhẹ hơn: chỉ chạy những ô đang thấy, nạp skin lần lượt (#17).
- Nhật ký ghi lại những lần app bị khựng, để báo lỗi dễ tìm nguyên nhân hơn.

## 1.8.1 · 07/11/2026

**Mới**

- **Lấy log để gửi** (Cài đặt → Giới thiệu): một file đã che tên bài hát, kéo thả vào báo lỗi là xong.
  MusicBar tự ghi lại khi bị tắt ngang hay khi taskbar sập. Cách gửi ở [docs/logs.md](docs/logs.md).
- **An toàn cho Explorer:** Explorer crash vì MusicBar thì MusicBar tự thôi chèn vào Explorer; giữ Shift
  lúc mở để chạy chế độ an toàn.
- **Tăng tốc phần cứng** (Cài đặt → Chung): tắt nếu driver card đồ họa hay gây lỗi.
- Nút **Sao chép lỗi** khi nhập skin hỏng (#17). Món trong kho được sửa trên web thì Áp dụng lại là
  có bản mới.

**Sửa lỗi**

- Đổi loa hay tai nghe thì thanh đứng nhịp tới khi mở lại app (#18).
- Lời, trình phát đè lên Start và Search khi rê chuột lên ô Search (#17); Windows 11 21H2 không trong
  suốt được (#17).
- Thanh không chạy từ sát mép trái trên Windows 11.
- Đóng một tab đang phát nhạc đôi khi làm đứng thông tin bài; ảnh xem trước của skin nặng bị đen.
- Đọc vị trí icon taskbar thưa hơn, nhẹ hơn cho cả MusicBar lẫn Explorer.

## 1.8.0 · 06/11/2026

**Mới**

- **Kho giao diện trực tuyến** (Cài đặt → Kho trực tuyến, và trên web musicbar.truclv.com): skin,
  visualizer, dải LED do mọi người làm, xem trước chạy thật ngay trong app. Mỗi món một nút
  **Áp dụng**: chưa có thì MusicBar tự tải về rồi mặc lên taskbar, có rồi thì mặc luôn (mất mạng vẫn
  mặc được món đã tải). Đăng giao diện của mình (kiểu thanh đang dùng hoặc một skin) dưới nickname tự
  đặt. Mọi món được máy chủ quét tự động rồi duyệt tay; file tải về được kiểm tra SHA-256, skin phải
  dịch được mới giữ. Kho mở màn với 24 món mới: Vapor Sunset, Code Rain, Lightstick Ocean, Gamer RGB…
- **Trang web của MusicBar** (musicbar.truclv.com): giới thiệu, tải bản cài (từ máy chủ hoặc GitHub,
  có SHA-256 và số lượt tải), kho giao diện xem nhanh từng món, đăng giao diện từ web. Bấm **Áp dụng**
  trên web là MusicBar mở ra mặc món đó luôn (link musicbar://, MusicBar tự đăng ký khi chạy và gỡ đi
  khi gỡ cài đặt). Web tự hiện tiếng Việt hay tiếng Anh theo nơi bạn ở, đổi được ở góc trái.
- Cài đặt có một dải banner tài trợ nhỏ ở dưới, để nuôi app miễn phí. Không có quảng cáo nào trên
  taskbar.
- **Phần hướng dẫn lần đầu mở:** đi qua kiểu thanh, nền taskbar, trình phát, lời, cái mặt và vài
  thói quen; chọn gì taskbar đổi ngay cái đó, chưa có nhạc thì một bài mẫu phát để xem thử. Bỏ qua
  bước nào cũng được, mỗi bước chỉ chỗ đổi lại trong cài đặt. Xem lại ở Cài đặt → Giới thiệu.
- **Chấm đỏ báo thông báo mới** (Cài đặt → Taskbar): nút trung tâm điều khiển trên khay hiện một
  chấm đỏ nhỏ thay vì số.
- Thiết lập sẵn mới cho người mới cài: taskbar trong suốt, thanh né các icon và đậm hơn, nút tối
  giản, lời căn giữa to hơn, luôn hiện kể cả khi chưa có nhạc, khởi động cùng Windows, cái mặt tắt.
  Người đang dùng giữ nguyên cài đặt của mình.
- **Đọc VLC** (Cài đặt → Chung → Đọc VLC): VLC không báo cho Windows biết nó đang phát gì nên trước
  giờ MusicBar không thấy (#13). Bật mục này, MusicBar bật giao diện web của chính VLC (chỉ trong
  máy, mật khẩu riêng) rồi đọc bài, vị trí phát, ảnh bìa; nút phát, chuyển bài, tua điều khiển được
  VLC. Bật xong mở lại VLC một lần.
- **Khôi phục cài đặt mặc định** (Cài đặt → Giới thiệu): mọi tùy chọn về như lúc mới cài; ngôn ngữ,
  proxy, thư mục lời và dữ liệu của bạn giữ nguyên (#16).
- Live stream (YouTube live, radio) giờ thanh tiến trình hiện đầy thay vì trống trơn (#14).

**Sửa lỗi**

- **Ăn RAM, càng chạy lâu càng nhiều (#6):** lúc phát nhạc MusicBar giờ giữ quanh 80 MB (cột Memory
  của Task Manager khoảng 50 MB), trước đây nhảy từ 150 tới hơn 300 MB và cứ thế tăng dần. Hai thủ
  phạm:
  - Vẽ thanh bằng card đồ hoạ thì WPF phải đọc ngược từng khung hình về, driver giữ cho việc đó từ 65
    tới 225 MB. Giờ các cửa sổ trên taskbar vẽ bằng CPU (skin vẫn dùng card đồ hoạ), CPU gần như
    không đổi.
  - Dò vị trí icon taskbar (2,5 giây một lần) qua thư viện UI Automation của .NET bị rò bộ nhớ, mỗi
    lần một ít, chạy cả ngày thành mấy trăm MB. Giờ gọi thẳng UI Automation của Windows, tự trả lại
    hết, và chỉ đọc tên các nút khi taskbar đổi.
- Skin chạy lâu ngày (app mở nhiều hôm liền) bị giật dần: đồng hồ đưa vào shader là số thực 32-bit,
  càng lớn càng thô. Giờ nó quay vòng mỗi 68 phút nên luôn mịn.
- Màn hình tần số cao (120, 144, 240 Hz): kiểu thanh nhảy theo nhạc vẽ khoảng 60 khung mỗi giây thay
  vì theo từng khung của màn, đỡ 2 tới 4 lần CPU mà nhìn vẫn mượt.
- **Thanh tiến trình biến mất khi bật taskbar trong suốt:** từ bản 1.6, lần đầu làm trong suốt thì
  được, nhưng sau một lần trả nền lại (tạm dừng lâu, game toàn màn hình, tắt mở lại app, tắt bật
  công tắc) thì module trong Explorer không làm trong suốt được nữa, taskbar đặc lại và che mất
  thanh nằm bên dưới. Giờ lần nào cũng làm được; và nếu taskbar vẫn chưa trong suốt (kể cả khi
  module bị công cụ khác chặn, như Taskbar Styler của Windhawk, #12) thì thanh tự lên nằm trên
  taskbar thay vì trốn bên dưới.
- **Bật tắt "Làm taskbar trong suốt" làm cửa sổ cài đặt treo vài giây, có lúc giật khi game vào
  toàn màn hình (#6):** mỗi lần trả nền cho taskbar, MusicBar báo "đổi màu" cho mọi cửa sổ đang mở
  rồi ngồi chờ từng cái (trên máy test mất gần 0,8 giây mỗi lần, máy nhiều cửa sổ còn lâu hơn). Giờ
  chỉ báo cho taskbar, việc đổi chế độ sáng tối của Windows thì báo ngầm. Dòng "Đang dùng: …" trong
  cài đặt cũng cập nhật liền thay vì giữ nguyên từ lúc mở.
- **Taskbar tự ẩn:**
  - Taskbar trồi lên giữ nền của Windows, thanh nhạc phủ mờ lên trên. Trước đây nền bị làm trong
    suốt, mà cửa sổ ứng dụng tràn xuống tận đáy màn hình nên chữ của nó đè lẫn vào icon, đồng hồ và
    lời.
  - Thanh, trình phát và cái mặt trượt lên xuống cùng taskbar, không còn bật ra khi taskbar đã lên
    hẳn hay tụt lại phía sau.
  - Thẻ thông tin bài hát không còn biến mất khi đưa chuột lên nó.
  - Mỗi lần taskbar trượt không còn ghi một loạt dòng vào log.
- Mở máy đôi khi MusicBar không hiện: lúc vừa đăng nhập, dịch vụ media của Windows có thể chưa sẵn
  sàng, MusicBar trước đây bỏ cuộc luôn; giờ hỏi lại tới khi được.
- Thanh có lúc biến mất sau khi máy ngủ, khóa màn hình hay đổi màn hình: giờ MusicBar vẽ lại hết sau
  những lúc đó. Log ghi rõ mỗi lần thanh ẩn đi và vì sao, để lần sau dò cho nhanh.
- Màn hình có tỉ lệ khác (125 %, 150 %…) có lúc vẽ mờ: cửa sổ vẽ sai tỉ lệ thì MusicBar tự chỉnh lại.
- Thoát app mà taskbar vẫn trong suốt (#15): module trong Explorer giờ trả nền lại cho tới khi được,
  không bỏ qua sau một lần lỗi.
- Chữ Bật / Tắt cạnh các công tắc trong cài đặt rõ hơn.

## 1.7.0 · 06/11/2026

**Mới**

- **Dải LED RGB:** 10 hiệu ứng kiểu dải LED gaming, nhảy theo nhạc: Cầu vồng, Phổ cầu vồng, Nhịp
  thở, Gợn sóng, Đèn quét, Sao lấp lánh, Lửa, Sao băng, Đèn chạy, Đồng hồ VU. Ánh sáng tỏa mềm lên
  taskbar như dải LED có tấm khuếch tán, không lốm đốm từng bóng.
- Thư viện kiểu gọn lại thành 4 tab (Thanh tiến trình, Visualizer, Dải LED RGB, Skin) với ô nhỏ;
  rê chuột lên ô nào thì dòng bên dưới tả ô đó. Menu chuột phải tách thành menu con theo nhóm.
- **Gỡ là trả lại như cũ:** lần đầu chạy, MusicBar ghi lại chế độ sáng / tối, cỡ, kiểu căn
  taskbar và kiểu taskbar của ExplorerPatcher. Gỡ cài đặt thì cái nào MusicBar đã đổi được trả về
  như ban đầu, trừ khi sau đó bạn tự đổi lại.

**Sửa lỗi**

- Mở trang Thanh tiến trình không còn chạy xem trước của mọi kiểu cùng lúc: chỉ tab đang mở chạy,
  đỡ tốn CPU.

## 1.6.1 · 05/11/2026

**Mới**

- **Chỉ chạy ở chỗ trống** (Cài đặt → Thanh tiến trình): thanh tiến trình, visualizer và skin né
  các icon của Windows (Start, ứng dụng, khay hệ thống), chạy nối từ khoảng trống này sang khoảng
  trống kia, mép mờ dần cạnh icon. Mở thêm app thì thanh trượt sang cho vừa.

**Sửa lỗi**

- Lời đứng ở dòng cuối khi lặp lại bài với vài trình phát cứ báo "đang ở cuối bài" (#10).
- Nút Control Center trên khay nổi thành ô tối đè lên game toàn màn hình và bắt nhầm click (#11).

## 1.6.0 · 05/11/2026

**Mới**

- 6 visualizer thay cho thanh tiến trình, chỉ nhảy theo nhạc: Cột sóng, Chân trời, Dao động, Hào
  quang, Đom đóm, Đèn LED. Phổ âm, visualizer và skin vẽ 60 khung / giây khi cắm sạc.
- Bảng màu riêng cho từng kiểu: theo bài hát, 11 bảng phối sẵn, hoặc tự chọn ba màu.
- **Skin tự làm:** một file .hlsl nhỏ vẽ thanh bằng card đồ họa. Có 4 skin mẫu và
  [hướng dẫn](docs/skins.md) viết sẵn để đưa cho ChatGPT / Claude / Gemini làm hộ.
- **Mặt của bạn:** ảnh, GIF hoặc WebP cho từng tâm trạng, tối đa 256 px.
- Thanh độ sáng màn hình trong Control Center, cho laptop và màn rời có DDC/CI.
- Win + A mở Control Center của MusicBar, bấm lại để đóng (#9).
- Icon khay, đồng hồ, Wi-Fi của Windows đổi màu theo nền MusicBar, thoát app là trả lại như cũ.
- Nút chỉnh sớm / trễ trong bảng lời: nhấn giữ là chỉnh liên tục.
- Thẻ bài hát có thanh tua, rê chuột vào là thẻ đứng yên chờ bạn tua (#8).

**Sửa lỗi**

- Trình phát lệch lên trên ở taskbar Windows 11 cao 48 px, giờ nằm đúng giữa (#5).
- Rê chuột lên video YouTube không còn bị tưởng là đổi bài.
- Karaoke tô theo nhịp hát (ước theo số âm tiết), không còn trôi chậm dần so với lời.
- Số lệch lời đọc như phụ đề: dương là lời muộn hơn. Gửi lời đã chỉnh lệch lên LRCLIB không còn báo
  lỗi.
- Bỏ hẳn tùy chọn tự ẩn khi toàn màn hình; MusicBar chỉ nhường chỗ khi taskbar bị che thật.
- Màn hình phụ hiện đủ tên bài; nền trong suốt đúng trên nhiều màn hình (#6, #7).
- Thoát app, kể cả tắt bằng Task Manager, taskbar và icon Windows về lại như cũ, không phải khởi
  động lại Explorer.

## 1.5.0 · 05/11/2026

**Mới**

- Karaoke đúng từng chữ: bài nào trên LRCLIB có thời gian từng chữ thì chữ được tô đúng lúc ca sĩ
  hát nó.
- Gửi lời lên LRCLIB ngay trong bảng lời: file .lrc của bạn mà LRCLIB chưa có, hoặc lời bạn đã chỉnh
  lệch.
- NetEase làm nguồn lời dự phòng (mặc định tắt): hỏi khi LRCLIB không có, mạnh nhạc Hàn, Hoa,
  Nhật. Chỉ nhận đúng ca sĩ, đúng độ dài, không lấy bản live, remix, cover.
- Lăn chuột trên nút mạng / loa ở taskbar, hoặc trên hàng âm lượng trong Control Center, để chỉnh
  âm lượng.
- Thanh tiến trình vẫn trôi nhẹ tại chỗ khi tạm dừng nhạc.

**Sửa lỗi**

- Tìm lyrics không còn nhầm sang bài trùng tên của ca sĩ khác.
- Lăn chuột trên nút loa ở khay không còn mất tác dụng khi bật Control Center.
- LRCLIB báo gọi nhiều quá thì app chờ đúng thời gian rồi thử lại.
- Thanh trượt Pomodoro ghi "phút" thay vì "min" khi dùng tiếng Việt.

## 1.4.0 · 04/11/2026

**Mới**

- Tự báo bản mới: có bản mới thì hỏi ở góc màn hình, bấm Cập nhật là tải, kiểm tra SHA-256, cài
  êm rồi tự mở lại.
- Bảng lời cả bài: dòng đang hát nằm giữa, bấm dòng nào phát từ dòng đó, có nút chỉnh sớm / trễ và
  nút Tìm lyrics khác khi bị ra sai bài.
- Nền Theo Windows, pha màu bài hát; taskbar trong suốt thì chữ đổi màu theo hình nền phía sau
  (chạy cả với Wallpaper Engine).
- Phiên âm bài Hàn, Nhật, Trung, dưới mỗi dòng hoặc thay lời gốc.
- Tìm lyrics cho title YouTube dài tốt hơn hẳn.
- Tổng kết năm âm nhạc kiểu Wrapped, lưu ảnh hoặc copy dán thẳng lên Facebook. Chỉ đếm trên máy.
- Cái mặt được trả lại cái mic, thêm mũ, kính, đồ theo mùa lễ, hẹn giờ Pomodoro, để ý CPU / mạng /
  sạc, và đi dạo dọc taskbar lúc rảnh.

**Sửa lỗi**

- Xóa tất cả thông báo trong Control Center giờ xóa thật.
- Bài có title không kèm tên ca sĩ không còn báo lỗi khi tìm lyrics.
- LRCLIB thỉnh thoảng báo bận: app chờ một chút rồi thử lại.
- Bài lặp lại mà trình phát không báo vị trí mới: lời và thanh tiến trình không còn đứng ở cuối bài.

## 1.3.0 · 04/11/2026

**Mới**

- Cái mặt trong video giới thiệu ở hẳn trên taskbar, 8 kiểu: nhún theo nhạc, ngủ gật về đêm,
  nhìn theo con trỏ, bấm, xoa, cuộn chuột là nó phản ứng.
- Nền taskbar sáng (và sáng pha màu bài hát) cho Windows chế độ sáng (#2).

**Sửa lỗi**

- Control Center không còn tràn khỏi mép trên màn hình khi có nhiều thông báo (#1).
- Nút pin trên khay cũng mở Control Center của MusicBar như nút mạng và âm lượng (#1).

## 1.2.0 · 04/11/2026

**Mới**

- Taskbar dọc: thanh tiến trình chạy từ dưới lên, ảnh bìa và nút xếp chồng, lời bài hát mỗi chữ
  một dòng.
- Thanh tiến trình chạy theo 4 hướng.
- Nhiều màn hình: chọn màn cho thanh tiến trình, chọn riêng màn cho trình phát và lời bài hát.
- Chế độ tối giản: ẩn trình phát mà giữ lời, hoặc chỉ để thanh tiến trình.
- Danh sách Wi-Fi và Bluetooth ngay trong Control Center.
- Cài đặt sắp xếp lại thành 6 trang.

**Sửa lỗi**

- Taskbar ở màn phụ không còn mờ nhòe khi bật taskbar trong suốt.
- Widget không còn chạy về góc sau khi Explorer khởi động lại.

## 1.1.1 · 04/11/2026

**Sửa lỗi**

- Tắt Control Center của MusicBar rồi mở Quick Settings của Windows không còn làm widget nhảy vào
  góc đè lên khay.
- Lúc mới mở app, widget không còn chớp sang góc phải trong vài giây đầu.

## 1.1.0 · 04/11/2026

**Mới**

- Bảy kiểu nút ⏮ ⏯ ⏭, có cả đĩa than xoay và phím cassette.
- Giao diện tiếng Anh (mặc định) và tiếng Việt, đổi là áp dụng ngay.
- Nhẹ RAM hơn: từ khoảng 143 MB xuống còn khoảng 58 MB.
- Vài chỗ chỉnh cho Windows 10.

## 1.0.0 · 04/11/2026

Bản đầu tiên lên GitHub.

- Thanh tiến trình trải hết taskbar, 14 kiểu, nhún nhảy theo nhạc thật.
- Lời bài hát karaoke từ LRCLIB, 10 phông đủ dấu tiếng Việt.
- Taskbar Windows 11 trong suốt, MusicBar tự làm.
- Trung tâm điều khiển gom thông báo, cài đặt nhanh, âm lượng và loa.
- Bản cài, bản portable, và một video giới thiệu có thanh taskbar biết ngáp.
