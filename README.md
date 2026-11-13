# Giám sát bể nuôi thủy sản (giamsatbethuysan)

Hệ thống giám sát và điều khiển bể nuôi thủy sản theo thời gian thực. ESP32 đọc **mực nước** (cảm biến siêu âm HC-SR04) và **nhiệt độ** (DS18B20), gửi lên MQTT Broker. Website ASP.NET Core nhận dữ liệu, hiển thị trực tiếp trên Dashboard và cho phép bật/tắt **máy bơm nước Vào** và **van thoát nước Ra** bằng tay. Phần mềm trên ESP32 chạy bằng **FreeRTOS** với 3 Task và các hàng đợi (Queue).

Bài thực hành Lab 3 (FreeRTOS), Đề 4: Mạch tự động giám sát thông số môi trường cho bể nuôi thủy, hải sản.

## Liên kết

| Nội dung | Link |
|---|---|
| Mô phỏng ESP32 trên Wokwi | https://wokwi.com/projects/480567112267484161 |
| Website đã deploy (Render) | https://giamsatbethuysan.onrender.com |

> Website chạy trên gói miễn phí của Render nên sẽ "ngủ" sau 15 phút không có người truy cập. Lần mở đầu tiên có thể mất khoảng 1 phút để khởi động lại.

## Sinh viên thực hiện

- Họ tên: Lương Anh Dũng
- MSV:231422194
- Lớp: Kỹ sư kỹ thuật thông tin và truyền thông

## Cách hệ thống hoạt động

```
ESP32 (Wokwi) ──MQTT publish──► broker.hivemq.com ──MQTT subscribe──► ASP.NET Core (MqttService)
                                                                              │ SignalR
                                                                              ▼
ESP32 ◄──MQTT subscribe── broker.hivemq.com ◄──MQTT publish── HomeController ◄── Trình duyệt (nút Bật/Tắt)
```

| Topic MQTT | Hướng | Nội dung |
|---|---|---|
| `aquaculture/temp` | ESP32 → Web | Nhiệt độ bể (°C), ví dụ `27.50` |
| `aquaculture/level` | ESP32 → Web | Mực nước (m), ví dụ `1.76` |
| `aquaculture/control` | Web → ESP32 | `PUMP_ON`, `PUMP_OFF`, `VALVE_ON`, `VALVE_OFF` |

## Chức năng của website

- Hai thẻ số lớn: **Mực nước hiện tại (m)** và **Nhiệt độ bể (°C)**, cập nhật tức thì bằng SignalR, không cần tải lại trang.
- Hình bể nước và nhiệt kế minh họa giá trị đang đo, hiển thị vùng điều khiển tự động hiện tại.
- Hai biểu đồ đường (Chart.js) theo thời gian thực, giữ 30 lần đo gần nhất.
- Bảng điều khiển bật/tắt **Máy bơm nước Vào** và **Van thoát nước Ra** (chế độ thủ công 30 giây, sau đó ESP32 tự trở về chế độ tự động).
- Huy hiệu trạng thái kết nối: Web (SignalR), MQTT và độ "tươi" của dữ liệu từ ESP32.

## Phần cứng mô phỏng trên Wokwi

| Linh kiện | Chân ESP32 |
|---|---|
| HC-SR04 (TRIG) | GPIO 4 |
| HC-SR04 (ECHO) | GPIO 16 |
| DS18B20 (DATA, kéo lên 4,7 kΩ) | GPIO 17 |
| Máy bơm (LED, điều khiển công suất bằng PWM) | GPIO 14 |
| Van thoát (LED) | GPIO 27 |

Mã nguồn ESP32: [`esp32/sketch.ino`](esp32/sketch.ino).

## Hệ thống FreeRTOS trên ESP32

| Task | Core | Ưu tiên | Nhiệm vụ |
|---|---|---|---|
| `Task_TransmitData` | 0 | 1 | Kết nối WiFi và MQTT, lấy dữ liệu từ `dataQueue` rồi publish; nhận lệnh điều khiển từ web đưa vào `controlQueue` |
| `Task_ReadData` | 1 | 1 | Đọc HC-SR04 và DS18B20, in Serial, gửi dữ liệu đầy đủ vào `dataQueue` và mực nước vào `alertQueue` |
| `Task_AlertControl` | 1 | 2 | Đọc `alertQueue` và `controlQueue`, điều khiển máy bơm và van thoát |

Thuật toán điều khiển tự động theo mực nước (theo đề bài, giá trị mực nước là khoảng cách đo từ cảm biến):

| Mực nước | Hành động |
|---|---|
| 1 m đến 1,5 m | Bơm 50% công suất trong 10 giây |
| Trên 1,5 m đến 2 m | Bơm 100% công suất trong 20 giây |
| Dưới 0,5 m | Mở van thoát trong 10 giây |
| Các mức còn lại | Tắt bơm và đóng van |

## Công nghệ sử dụng

- ASP.NET Core MVC (C#), SignalR
- MQTTnet 4.3.x (MQTT client chạy nền bằng `IHostedService`)
- Bootstrap 5, Chart.js 4
- ESP32, FreeRTOS, PubSubClient, Wokwi
- Docker, Render.com

## Chạy trên máy cá nhân

Yêu cầu: .NET SDK trùng với phiên bản trong file `.csproj` (đã thử với .NET 8).

```bash
git clone https://github.com/<tên-github>/giamsatbethuysan.git
cd giamsatbethuysan
dotnet run
```

Mở địa chỉ hiện trong terminal (ví dụ `https://localhost:7023`). Sau đó bấm **Start** trên dự án Wokwi để ESP32 bắt đầu gửi dữ liệu.

Cấu hình MQTT nằm trong `appsettings.json` (mục `MqttSettings`). Lưu ý `broker.hivemq.com` là broker công cộng, nên nếu bị lẫn dữ liệu của người khác hãy đổi tên topic thành tên riêng ở cả `appsettings.json` và `esp32/sketch.ino`.

## Cấu trúc thư mục

```
giamsatbethuysan/
├── Controllers/HomeController.cs   # Trang chính và API gửi lệnh điều khiển
├── Hubs/SensorHub.cs               # SignalR Hub
├── Models/MqttSettings.cs          # Cấu hình MQTT
├── Services/
│   ├── IMqttService.cs
│   └── MqttService.cs              # Dịch vụ nền: kết nối, subscribe, publish MQTT
├── Views/Home/Index.cshtml         # Giao diện Dashboard
├── wwwroot/css/dashboard.css
├── esp32/sketch.ino                # Mã nguồn ESP32 (FreeRTOS)
├── Dockerfile                      # Dùng để deploy lên Render
├── appsettings.json
└── Program.cs
```
