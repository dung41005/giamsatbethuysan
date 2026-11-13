



# HỆ THỐNG GIÁM SÁT VÀ ĐIỀU KHIỂN BỂ NUÔI THỦY SẢN (IoT + FreeRTOS + ASP.NET Core)

> Bài thực hành **Lab 3 (FreeRTOS) - Đề 4**: Mạch tự động giám sát thông số môi trường cho bể nuôi thủy, hải sản.

## 🎥 Video Demo Kết Quả Thực Nghiệm
> 🎬 *Xem video demo quá trình mô phỏng Wokwi và tương tác realtime trên Web Dashboard:*
<video width="100%" controls>
  <source src="https://github.com/user-attachments/assets/3d4a73b2-2bf1-4206-a411-1f845f051168" type="video/mp4">
  Trình duyệt của bạn không hỗ trợ thẻ video.
</video>
## 📋 1. Thông tin sinh viên & Đề tài

* **Họ và tên:** Lương Anh Dũng
* **Mã sinh viên:** 231422194
* **Lớp:** Kỹ sư kỹ thuật thông tin và truyền thông

## 🔗 2. Liên kết dự án & Báo cáo chi tiết

| Nội dung | Đường dẫn (Link) |
|---|---|
| 📄 **Báo cáo Lab 3 (Word)** | [https://docs.google.com/document/d/1JH2_JYIrqCznUZKSSWmT0yk95mAZdyIH/edit#heading=h.g9n9ctp0ia73](https://docs.google.com/document/d/1JH2_JYIrqCznUZKSSWmT0yk95mAZdyIH/edit#heading=h.g9n9ctp0ia73) *(Chi tiết lý thuyết RTOS, Queue, Semaphore & giải thích code)* |
| 🔌 **Mô phỏng Wokwi** | [https://wokwi.com/projects/480567112267484161](https://wokwi.com/projects/480567112267484161) |
| 🌐 **Website Dashboard (Render)** | [https://giamsatbethuysan.onrender.com](https://giamsatbethuysan.onrender.com) |

> ⚠️ *Lưu ý: Web deploy trên gói Free của Render sẽ "ngủ" sau 15 phút không truy cập. Lần truy cập đầu tiên có thể mất ~50 giây để khởi động lại.*

## ⚙️ 3. Luồng dữ liệu hệ thống

```
ESP32 (Wokwi) ──MQTT publish──► broker.hivemq.com ──MQTT subscribe──► ASP.NET Core (MqttService)
                                                                           │ SignalR
                                                                           ▼
ESP32 ◄──MQTT subscribe── broker.hivemq.com ◄──MQTT publish── HomeController ◄── Trình duyệt (Dashboard)
```

| Topic MQTT | Hướng truyền | Nội dung |
|---|---|---|
| `aquaculture/temp` | ESP32 → Web | Nhiệt độ bể (°C) |
| `aquaculture/level` | ESP32 → Web | Mực nước thực tế (m) |
| `aquaculture/control` | Web → ESP32 | Lệnh: `PUMP_ON`, `PUMP_OFF`, `VALVE_ON`, `VALVE_OFF` |

## 🛠️ 4. Tóm tắt phần cứng & FreeRTOS (Xem chi tiết trong Báo cáo Word)

- **Phần cứng (Wokwi):** ESP32, HC-SR04 (GPIO 4, 16), DS18B20 (GPIO 17), Máy bơm LED PWM (GPIO 14), Van thoát LED (GPIO 27).
- **FreeRTOS Tasks:**
  - `Task_TransmitData` (Core 0): Quản lý kết nối WiFi/MQTT, đồng bộ Queue truyền dữ liệu.
  - `Task_ReadData` (Core 1): Đọc cảm biến định kỳ 2s, đẩy vào `dataQueue` và `alertQueue`.
  - `Task_AlertControl` (Core 1): Xử lý thuật toán tự động theo mực nước và ưu tiên lệnh thủ công (Manual 30s timeout).

## 🚀 5. Công nghệ & Hướng dẫn chạy local

- **Tech Stack:** ASP.NET Core MVC, SignalR, MQTTnet, Chart.js, FreeRTOS, Docker.
- **Chạy trên máy cá nhân (.NET 8):**
  ```bash
  git clone https://github.com/<tên-github>/giamsatbethuysan.git
  cd giamsatbethuysan
  dotnet run
  ```
  *(Cấu hình MQTT broker tại `appsettings.json` và `esp32/sketch.ino`).*
