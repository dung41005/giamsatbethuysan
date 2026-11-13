namespace giamsatbethuysan.Models;

/// <summary>
/// Cấu hình kết nối MQTT, được nạp từ mục "MqttSettings" trong appsettings.json.
/// </summary>
public class MqttSettings
{
    /// <summary>Tên section trong appsettings.json.</summary>
    public const string SectionName = "MqttSettings";

    /// <summary>Địa chỉ MQTT Broker (mặc định: broker.hivemq.com).</summary>
    public string BrokerHost { get; set; } = "broker.hivemq.com";

    /// <summary>Cổng TCP của Broker (không mã hóa: 1883).</summary>
    public int BrokerPort { get; set; } = 1883;

    /// <summary>
    /// Tiền tố ClientId. Service sẽ nối thêm chuỗi ngẫu nhiên phía sau để tránh trùng ClientId
    /// (broker công cộng sẽ ngắt kết nối cũ nếu có 2 client cùng ClientId).
    /// </summary>
    public string ClientIdPrefix { get; set; } = "giamsatbethuysan-web";

    /// <summary>Chu kỳ keep-alive (giây) để broker biết client còn sống.</summary>
    public int KeepAliveSeconds { get; set; } = 30;

    /// <summary>Thời gian chờ (giây) giữa các lần thử kết nối lại khi mất kết nối.</summary>
    public int ReconnectDelaySeconds { get; set; } = 5;

    /// <summary>Topic nhận nhiệt độ từ ESP32.</summary>
    public string TempTopic { get; set; } = "aquaculture/temp";

    /// <summary>Topic nhận mực nước từ ESP32.</summary>
    public string LevelTopic { get; set; } = "aquaculture/level";

    /// <summary>Topic gửi lệnh điều khiển xuống ESP32.</summary>
    public string ControlTopic { get; set; } = "aquaculture/control";
}