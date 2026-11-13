#include <Arduino.h>
#include <WiFi.h>
#include <PubSubClient.h>
#include <OneWire.h>
#include <DallasTemperature.h>

// ---------------- CẤU HÌNH PIN ----------------
#define TRIG_PIN 4
#define ECHO_PIN 16
#define ONE_WIRE_BUS 17
#define PUMP_PIN 14
#define VALVE_PIN 27
// Cấu hình PWM cho máy bơm (điều chỉnh công suất)
#define PWM_CHANNEL 0
#define PWM_FREQ 5000
#define PWM_RES 8

// ---------------- WIFI & MQTT ----------------
const char* ssid = "Wokwi-GUEST";
const char* password = "";
const char* mqtt_server = "broker.hivemq.com"; // Broker test miễn phí

// Các topic MQTT (phải trùng với appsettings.json của web)
const char* TOPIC_TEMP = "aquaculture/temp";
const char* TOPIC_LEVEL = "aquaculture/level";
const char* TOPIC_CONTROL = "aquaculture/control";

// Sau lệnh thủ công cuối cùng từ web, thiết bị giữ chế độ THỦ CÔNG trong khoảng này,
// hết thời gian sẽ tự trở về chế độ TỰ ĐỘNG theo mực nước.
const unsigned long MANUAL_HOLD_MS = 30000;

WiFiClient espClient;
PubSubClient client(espClient);

// Khởi tạo cảm biến nhiệt độ
OneWire oneWire(ONE_WIRE_BUS);
DallasTemperature sensors(&oneWire);

// ---------------- KIỂU DỮ LIỆU & HÀNG ĐỢI (QUEUES) ----------------
struct SensorData {
  float temperature;
  float waterLevel;
};

// Các lệnh điều khiển thủ công nhận từ Web
enum ControlCommand { CMD_PUMP_ON, CMD_PUMP_OFF, CMD_VALVE_ON, CMD_VALVE_OFF };

QueueHandle_t dataQueue;     // Task đọc data -> Task truyền data (gửi MQTT)
QueueHandle_t alertQueue;    // Task đọc data -> Task cảnh báo (mực nước)
QueueHandle_t controlQueue;  // Task truyền data (nhận MQTT) -> Task cảnh báo (lệnh thủ công)

// Trạng thái chế độ thủ công của từng thiết bị (chỉ Task_AlertControl đọc/ghi)
bool manualPump = false;
bool manualValve = false;

// ---------------- HÀM TIỆN ÍCH ----------------

// Gửi vào queue mà KHÔNG chặn Task. Nếu queue đầy thì bỏ phần tử cũ nhất để giữ số liệu mới nhất.
// Nhờ vậy khi mất WiFi/MQTT, Task đọc data vẫn chạy và Task cảnh báo vẫn nhận được mực nước.
template <typename T>
void sendLatest(QueueHandle_t queue, const T &item) {
  if (xQueueSend(queue, &item, 0) != pdPASS) {
    T oldest;
    xQueueReceive(queue, &oldest, 0);
    xQueueSend(queue, &item, 0);
  }
}

// Điều khiển bơm/van theo thuật toán TỰ ĐỘNG: bị bỏ qua khi thiết bị đang ở chế độ thủ công.
void autoSetPump(uint8_t duty) {
  if (!manualPump) ledcWrite(PUMP_PIN, duty);
}

void autoSetValve(bool open) {
  if (!manualValve) digitalWrite(VALVE_PIN, open ? HIGH : LOW);
}

// Hàm này được PubSubClient gọi (bên trong client.loop()) mỗi khi có tin nhắn tới topic đã subscribe.
// Chỉ đổi chuỗi lệnh thành enum rồi đẩy vào controlQueue, việc điều khiển thật do Task_AlertControl làm.
void mqttCallback(char *topic, byte *payload, unsigned int length) {
  String msg;
  msg.reserve(length);
  for (unsigned int i = 0; i < length; i++) {
    msg += (char)payload[i];
  }
  msg.trim();

  ControlCommand cmd;
  if (msg == "PUMP_ON") cmd = CMD_PUMP_ON;
  else if (msg == "PUMP_OFF") cmd = CMD_PUMP_OFF;
  else if (msg == "VALVE_ON") cmd = CMD_VALVE_ON;
  else if (msg == "VALVE_OFF") cmd = CMD_VALVE_OFF;
  else {
    Serial.print("Lệnh không hợp lệ: ");
    Serial.println(msg);
    return;
  }

  Serial.print("Nhận lệnh từ Web: ");
  Serial.println(msg);
  xQueueSend(controlQueue, &cmd, 0);
}

// ---------------- TASK TRUYỀN DATA (Core 0) ----------------
void Task_TransmitData(void *pvParameters) {
  // Kết nối WiFi
  WiFi.begin(ssid, password);
  while (WiFi.status() != WL_CONNECTED) {
    vTaskDelay(500 / portTICK_PERIOD_MS);
  }
  Serial.println("WiFi Connected!");

  client.setServer(mqtt_server, 1883);
  client.setCallback(mqttCallback);  // Đăng ký hàm nhận lệnh điều khiển

  SensorData data;
  for (;;) {
    if (!client.connected()) {
      // Reconnect MQTT
      while (!client.connected()) {
        String clientId = "ESP32Client-" + String(random(0xffff), HEX);
        if (client.connect(clientId.c_str())) {
          Serial.println("MQTT Connected");
          // Phải subscribe lại sau MỖI lần kết nối lại
          client.subscribe(TOPIC_CONTROL);
        } else {
          vTaskDelay(2000 / portTICK_PERIOD_MS);
        }
      }
    }
    client.loop();  // Duy trì kết nối và nhận tin nhắn (gọi mqttCallback)

    // Nhận dữ liệu từ dataQueue và gửi lên MQTT Broker
    if (xQueueReceive(dataQueue, &data, 0) == pdPASS) {
      char tempStr[10];
      char levelStr[10];
      dtostrf(data.temperature, 1, 2, tempStr);
      dtostrf(data.waterLevel, 1, 2, levelStr);

      client.publish(TOPIC_TEMP, tempStr);
      client.publish(TOPIC_LEVEL, levelStr);
    }
    vTaskDelay(100 / portTICK_PERIOD_MS);
  }
}

// ---------------- TASK ĐỌC DATA (Core 1) ----------------
void Task_ReadData(void *pvParameters) {
  sensors.begin();
  pinMode(TRIG_PIN, OUTPUT);
  pinMode(ECHO_PIN, INPUT);

  SensorData dataToSend;

  for (;;) {
    // Đọc nhiệt độ
    sensors.requestTemperatures();
    dataToSend.temperature = sensors.getTempCByIndex(0);

    // Đọc khoảng cách từ HC-SR04
    digitalWrite(TRIG_PIN, LOW);
    delayMicroseconds(2);
    digitalWrite(TRIG_PIN, HIGH);
    delayMicroseconds(10);
    digitalWrite(TRIG_PIN, LOW);

    long duration = pulseIn(ECHO_PIN, HIGH, 30000);

    // duration = 0: không nhận được tiếng vọng; nhiệt độ = -127: mất cảm biến DS18B20.
    // Bỏ qua lần đo lỗi, tránh báo mực nước = 0 làm van thoát mở nhầm.
    if (duration == 0 || dataToSend.temperature == DEVICE_DISCONNECTED_C) {
      Serial.println("Cảm biến lỗi hoặc ngoài tầm đo, bỏ qua lần đo này");
      vTaskDelay(2000 / portTICK_PERIOD_MS);
      continue;
    }

    // Theo đề: "mực nước (khoảng cách)" nên dùng khoảng cách đo được, đổi ra mét (m)
    float distanceCm = duration * 0.034 / 2;
    dataToSend.waterLevel = distanceCm / 100.0;

    // Hiển thị Serial
    Serial.print("Nhiệt độ: "); Serial.print(dataToSend.temperature); Serial.println(" C");
    Serial.print("Mực nước: "); Serial.print(dataToSend.waterLevel); Serial.println(" m");

    // Gửi vào hàng đợi (không chặn Task kể cả khi MQTT đang mất kết nối)
    sendLatest(dataQueue, dataToSend);
    sendLatest(alertQueue, dataToSend.waterLevel);

    vTaskDelay(2000 / portTICK_PERIOD_MS); // Đọc mỗi 2 giây
  }
}

// ---------------- TASK CẢNH BÁO / ĐIỀU KHIỂN (Core 1) ----------------
void Task_AlertControl(void *pvParameters) {
  ledcAttach(PUMP_PIN, PWM_FREQ, PWM_RES);

  pinMode(VALVE_PIN, OUTPUT);
  digitalWrite(VALVE_PIN, LOW);

  float currentLevel;

  // Biến theo dõi thời gian của thuật toán tự động
  unsigned long pumpStartTime = 0;
  unsigned long valveStartTime = 0;
  bool isPumping = false;
  bool isValving = false;
  unsigned long pumpDuration = 0;
  unsigned long valveDuration = 0;

  // Mốc thời gian lệnh thủ công gần nhất
  unsigned long manualPumpStart = 0;
  unsigned long manualValveStart = 0;

  for (;;) {
    // ---- (1) Xử lý lệnh THỦ CÔNG từ Web (ưu tiên cao hơn tự động) ----
    ControlCommand cmd;
    while (xQueueReceive(controlQueue, &cmd, 0) == pdPASS) {
      switch (cmd) {
        case CMD_PUMP_ON:
          manualPump = true;
          manualPumpStart = millis();
          isPumping = false;
          ledcWrite(PUMP_PIN, 255);  // Thủ công: bơm 100%
          Serial.println("THỦ CÔNG: Bật máy bơm 100%");
          break;
        case CMD_PUMP_OFF:
          manualPump = true;
          manualPumpStart = millis();
          isPumping = false;
          ledcWrite(PUMP_PIN, 0);
          Serial.println("THỦ CÔNG: Tắt máy bơm");
          break;
        case CMD_VALVE_ON:
          manualValve = true;
          manualValveStart = millis();
          isValving = false;
          digitalWrite(VALVE_PIN, HIGH);
          Serial.println("THỦ CÔNG: Mở van thoát");
          break;
        case CMD_VALVE_OFF:
          manualValve = true;
          manualValveStart = millis();
          isValving = false;
          digitalWrite(VALVE_PIN, LOW);
          Serial.println("THỦ CÔNG: Đóng van thoát");
          break;
      }
    }

    // ---- (2) Hết thời gian giữ chế độ thủ công -> trở về tự động ----
    if (manualPump && millis() - manualPumpStart >= MANUAL_HOLD_MS) {
      manualPump = false;
      isPumping = false;
      ledcWrite(PUMP_PIN, 0);
      Serial.println("Bơm: hết chế độ thủ công, trở về TỰ ĐỘNG");
    }
    if (manualValve && millis() - manualValveStart >= MANUAL_HOLD_MS) {
      manualValve = false;
      isValving = false;
      digitalWrite(VALVE_PIN, LOW);
      Serial.println("Van: hết chế độ thủ công, trở về TỰ ĐỘNG");
    }

    // ---- (3) Thuật toán TỰ ĐỘNG theo mực nước ----
    // Lấy dữ liệu với thời gian chờ nhỏ nhất để không block Task
    if (xQueueReceive(alertQueue, &currentLevel, 10 / portTICK_PERIOD_MS) == pdPASS) {

      if (currentLevel >= 1.0 && currentLevel <= 1.5) {
        if (!isPumping || pumpDuration != 10000) {  // Bắt đầu mới hoặc đổi từ mức khác sang
          Serial.println("CẢNH BÁO: Mực nước 1m-1.5m -> Bơm 50% trong 10s");
          autoSetValve(false);
          autoSetPump(127);  // 127/255 ≈ 50% công suất
          pumpStartTime = millis();
          pumpDuration = 10000;
          isPumping = true;
          isValving = false;
        }
      } else if (currentLevel > 1.5 && currentLevel <= 2.0) {
        if (!isPumping || pumpDuration != 20000) {
          Serial.println("CẢNH BÁO: Mực nước 1.5m-2m -> Bơm 100% trong 20s");
          autoSetValve(false);
          autoSetPump(255);
          pumpStartTime = millis();
          pumpDuration = 20000;
          isPumping = true;
          isValving = false;
        }
      } else if (currentLevel < 0.5) {
        if (!isValving) {
          Serial.println("CẢNH BÁO: Mực nước <0.5m -> Mở van thoát 10s");
          autoSetPump(0);
          autoSetValve(true);
          valveStartTime = millis();
          valveDuration = 10000;
          isValving = true;
          isPumping = false;
        }
      } else {
        // Mức an toàn (0.5 -> 0.99) hoặc > 2.0
        autoSetPump(0);
        autoSetValve(false);
        isPumping = false;
        isValving = false;
      }
    }

    // ---- (4) Kiểm tra thời gian để tắt Bơm / đóng Van (tự động) ----
    if (isPumping && (millis() - pumpStartTime >= pumpDuration)) {
      autoSetPump(0);
      isPumping = false;
    }

    if (isValving && (millis() - valveStartTime >= valveDuration)) {
      autoSetValve(false);
      isValving = false;
    }

    // Nhường CPU cho các Task khác
    vTaskDelay(50 / portTICK_PERIOD_MS);
  }
}

// ---------------- SETUP & LOOP ----------------
void setup() {
  Serial.begin(115200);

  // Khởi tạo Queues
  dataQueue = xQueueCreate(5, sizeof(SensorData));
  alertQueue = xQueueCreate(5, sizeof(float));
  controlQueue = xQueueCreate(5, sizeof(ControlCommand));

  // Tạo các Task chạy trên các Core tương ứng
  // Task truyền data tăng stack lên 8192 vì dùng WiFi + MQTT + String
  xTaskCreatePinnedToCore(Task_TransmitData, "TaskTransmit", 8192, NULL, 1, NULL, 0); // Chạy Core 0
  xTaskCreatePinnedToCore(Task_ReadData, "TaskRead", 4096, NULL, 1, NULL, 1);         // Chạy Core 1
  xTaskCreatePinnedToCore(Task_AlertControl, "TaskAlert", 4096, NULL, 2, NULL, 1);    // Chạy Core 1 (Ưu tiên cao hơn)
}

void loop() {
  // FreeRTOS quản lý, không viết code vào loop()
}
