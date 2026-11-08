# Tự làm skin cho MusicBar

Skin là một file `.hlsl` nhỏ: một **pixel shader** vẽ thanh dưới taskbar bằng card đồ họa, giống
Shadertoy. MusicBar đưa vào cho skin thời gian, vị trí bài đang phát, độ to, nhịp, phổ âm, dạng sóng
và ba màu. Skin chỉ việc trả về màu cho từng điểm ảnh.

Skin chỉ vẽ được, không đọc file và không vào mạng, nên nhận skin của người khác cũng an toàn.

**Cài skin:** Cài đặt → Thanh tiến trình → Skin của bạn → **Nhập skin…**, chọn file `.hlsl`.
Skin hiện ngay trong thư viện với bản xem trước chạy thật. Nếu file có lỗi, MusicBar báo đúng dòng lỗi.
Bấm **Thêm skin mẫu** để có 4 skin làm sẵn (Liquid Chrome, Comet, Plasma, Synthwave) mà xem và sửa.

> Nhờ AI làm skin? Kéo xuống mục [Nhờ AI viết skin](#nhờ-ai-viết-skin): có sẵn câu lệnh để dán.

---

## Một skin trông như thế nào

```hlsl
//! name: Neon Fill
//! author: Tên bạn
//! kind: progress
//! description: Dải neon tô tới chỗ đang phát

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float played = step(uv.x, progress);              // 1 ở phần đã phát
    float glow = exp(-rise(uv) * 4.0) * (0.6 + 0.4 * level);
    float3 color = lerp(colorB.rgb, colorA.rgb, uv.x);
    return paint(color, played * glow);
}
```

- Mấy dòng `//!` ở đầu là phần giới thiệu skin:
  - `name`: tên hiện trong thư viện.
  - `author`: tác giả.
  - `kind`: `progress` (thanh tiến trình) hoặc `visualizer` (chỉ nhảy theo nhạc).
  - `description`: một câu mô tả.
- Hàm `main` chạy cho **từng điểm ảnh** và trả về màu của điểm đó.
- MusicBar tự thêm phần khai báo đầu vào ở trên file, bạn không cần viết lại.

## Đầu vào

| Tên | Kiểu | Ý nghĩa |
|---|---|---|
| `uv` | `float2` | Vị trí điểm ảnh, 0 đến 1. `uv.x` chạy dọc taskbar từ trái sang phải (taskbar dọc: từ dưới lên). `uv.y` từ mép trên xuống mép dưới của thanh. |
| `size` | `float2` | Kích thước thanh, đơn vị pixel (thường khoảng 1920 × 32 đến 48). Dùng `uv * size` khi cần đơn vị pixel. |
| `time` | `float` | Giây, tăng đều. Khi tạm dừng nhạc nó chạy chậm lại, nên skin vẫn trôi nhẹ. |
| `progress` | `float` | Đã phát bao nhiêu của bài, 0 đến 1 (0 nếu trình phát không báo). |
| `level` | `float` | Độ to lúc này, 0 đến 1, đã tự cân theo bài (nhạc nhỏ vẫn nhảy). |
| `beat` | `float` | 1 ngay lúc có nhịp mạnh (trống, bass), giảm về 0 trong khoảng 0,25 giây. |
| `playing` | `float` | 1 khi đang phát, 0 khi tạm dừng. |
| `colorA`, `colorB`, `colorC` | `float4` | Ba màu của bảng màu: màu chính, màu phụ, màu nhấn. Lấy theo bài hát, hoặc bảng màu người dùng chọn cho skin. Dùng `.rgb`. |
| `bottomEdge` | `float` | 1 khi mép màn hình ở dưới thanh (taskbar nằm dưới), 0 khi ở trên. Thường chỉ cần `rise(uv)`. |

## Hàm có sẵn

| Hàm | Trả về |
|---|---|
| `spectrum(x)` | Phổ âm tại `x` (0 là trầm nhất, 1 là bổng nhất), giá trị 0 đến 1. |
| `wave(x)` | Dạng sóng tại `x` (0 đến 1 dọc khoảng 15 ms âm thanh gần nhất), giá trị −1 đến 1, đã chỉnh cho đủ cao. |
| `rise(uv)` | Khoảng cách từ mép màn hình, 0 đến 1: 0 sát mép màn hình, 1 ở phía màn hình nền. Dùng để thứ gì đó "mọc lên" từ mép. |
| `paint(rgb, alpha)` | Màu trả về đúng cách (alpha nhân sẵn). **Luôn trả về bằng `paint`.** |

Ngoài ra dùng được mọi hàm của HLSL: `sin`, `cos`, `frac`, `floor`, `abs`, `pow`, `exp`, `length`,
`dot`, `lerp`, `smoothstep`, `step`, `saturate`, `clamp`, `min`, `max`…

## Quy tắc

1. Chỉ một hàm vào là `float4 main(float2 uv : TEXCOORD) : COLOR`. Được viết thêm hàm phụ ở trên nó.
2. Trả về bằng `paint(màu, độ_đậm)`. Chỗ nào không vẽ thì trả `paint(0, 0)` hoặc độ đậm 0.
3. Shader được dịch theo chuẩn `ps_3_0`. Vòng lặp nên dưới khoảng 64 lần, và phải có số lần cố định.
   Chuẩn này có trần: khoảng 210 hằng số, 32 thanh ghi tạm, và card đồ họa từ chối shader quá khoảng
   3.000 lệnh. Skin vượt trần (hay dùng thứ `ps_3_0` không có, như mảng đọc theo chỉ số tính lúc chạy)
   được MusicBar tự dịch theo shader model 5 và vẽ bằng Direct3D 11 (Cài đặt → Chung → Bộ vẽ skin: Tự
   động). Skin muốn ai cũng chạy được (máy không có Direct3D 11, người chọn Chỉ cách cũ, bản cũ) thì vẫn
   nên giữ trong trần `ps_3_0`.
4. Không đặt tên biến trùng từ khóa của HLSL: `line`, `point`, `triangle`, `sample`, `texture`,
   `vector`, `matrix`, `half`, `in`, `out`, `inline`, `string`.
5. Không dùng `#include`, texture riêng hay biến toàn cục có giá trị khởi tạo (ngoài `const`).
6. Phần chữ lyrics nằm trên thanh: MusicBar tự làm mờ skin dưới chữ, nhưng skin nên để chừa nhiều
   chỗ tối hoặc trong suốt. Một dải màu đặc kín cả thanh sẽ khó nhìn.
7. Kiểu `progress` nên thể hiện được `progress`. Kiểu `visualizer` thì không bắt buộc.

## Mẹo cho đẹp

- Mọc từ mép màn hình: dùng `rise(uv)` làm "độ cao".
- Đối xứng như các visualizer của MusicBar (trầm ở giữa, bổng ra hai đầu): `spectrum(abs(uv.x - 0.5) * 2)`.
- Nhịp: cho độ sáng hoặc kích thước nhân thêm `(1 + 0.3 * beat)`.
- Chuyển màu mượt: `lerp(colorB.rgb, colorA.rgb, t)`, rồi điểm `colorC` ở chỗ sáng nhất.
- Phát sáng: `exp(-khoảng_cách / bán_kính)` cho quầng sáng mềm.
- Đơn vị pixel giúp hình không bị kéo giãn: `float2 px = uv * size;`.

## Ví dụ: visualizer cột đối xứng

```hlsl
//! name: Mirror Bars
//! kind: visualizer
//! description: Cột nhạc đối xứng, trầm ở giữa

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float columns = floor(size.x / 6.0);                  // mỗi cột rộng 6 px
    float column = floor(uv.x * columns) / columns;
    float inColumn = frac(uv.x * columns);
    float value = spectrum(abs(column - 0.5) * 2.0) * (1.0 + 0.25 * beat);
    float bar = step(rise(uv), value) * step(inColumn, 0.6);
    float3 color = lerp(colorB.rgb, colorA.rgb, rise(uv));
    return paint(color, bar * 0.9);
}
```

Muốn xem thêm, đọc 4 skin mẫu trong thư mục [`skins/`](../skins) của repo.

---

## Skin lớn (Direct3D 11)

Skin bình thường chạy theo chuẩn `ps_3_0` như ở trên. Skin nào vượt trần của chuẩn đó thì MusicBar tự dịch
nó theo **shader model 5** và vẽ bằng **Direct3D 11** (Cài đặt → Chung → Bộ vẽ skin: Tự động). Bạn không
phải khai báo gì: cùng file `.hlsl`, cùng đầu vào, cùng `main`, cùng `paint()`.

Với Direct3D 11 bạn có thêm:

- **Vòng lặp dài**: hàng trăm bước mỗi điểm ảnh. Đủ cho cảnh 3D thật bằng raymarching (bắn tia qua cảnh),
  mây thể tích, nước phản chiếu.
- **Số nguyên và phép bit**: `int`, `uint`, `<<`, `^`, `asuint()`. Dùng để làm hàm ngẫu nhiên ổn định.
- **Mảng** đọc và ghi theo chỉ số tính lúc chạy, và mảng hằng `static const float a[] = { ... };`.
- Không còn trần khoảng 210 hằng số, 32 thanh ghi và 3.000 lệnh.

Cần nhớ:

- Skin như vậy **chỉ chạy được trên máy có Direct3D 11**, với bản MusicBar có mục Bộ vẽ skin. Người chọn
  "Chỉ cách cũ (WPF)" hay dùng bản cũ sẽ thấy skin báo lỗi dịch. Nên ghi rõ "Needs Direct3D 11" trong
  `description`.
- **Nặng card đồ họa**: thanh 1920 × 48 điểm ảnh vẽ 30 lần mỗi giây là gần 3 triệu điểm ảnh mỗi giây. Một
  cảnh mây 3D 40 bước tốn khoảng 5% của một card RTX 2060; card tích hợp đời cũ chậm hơn chừng 15 lần.
- Hàm ngẫu nhiên kiểu `frac(sin(x) * 43758.5)` cho kết quả **khác nhau** giữa hai bộ vẽ (hàm `sin` tính
  khác nhau). Skin lớn nên dùng hàm băm số nguyên, máy nào cũng ra như nhau:

```hlsl
uint pcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

// Một số ngẫu nhiên từ 0 đến 1 cho mỗi ô lưới (ví dụ vị trí một ngôi sao).
float hash2(int2 p) { return pcg(asuint(p.x) + pcg(asuint(p.y))) * (1.0 / 4294967295.0); }
```

Muốn nhờ AI viết skin kiểu này thì thêm vào câu lệnh ở dưới: "skin lớn, dùng shader model 5 (Direct3D 11),
được dùng vòng lặp dài và số nguyên".

---

## Nhờ AI viết skin

Dán **toàn bộ file này** vào ChatGPT, Claude, Gemini…, kèm câu lệnh sau (sửa phần mô tả theo ý bạn):

```
Đây là tài liệu skin của MusicBar. Hãy viết cho tôi một skin MusicBar (một file .hlsl
đúng theo tài liệu) với mô tả: [mô tả của bạn, ví dụ: "những giọt mưa neon rơi xuống,
rơi nhanh hơn khi nhạc to, mỗi nhịp trống làm mặt nước gợn sóng"].
Kiểu: [progress hoặc visualizer].
Yêu cầu: có đủ dòng //! name, kind, description; chỉ dùng các đầu vào và hàm trong tài liệu;
trả về bằng paint(); không đặt tên biến trùng từ khóa HLSL; chừa chỗ tối cho lyrics dễ đọc.
Chỉ trả về nội dung file .hlsl.
```

Lưu câu trả lời thành file `tên-skin.hlsl` rồi nhập vào MusicBar. Nếu MusicBar báo lỗi, dán
nguyên dòng lỗi lại cho AI và nhờ nó sửa.
