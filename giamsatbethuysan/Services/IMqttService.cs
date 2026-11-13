namespace giamsatbethuysan.Services;

/// <summary>
/// Hợp đồng làm việc với MQTT dành cho các thành phần khác (Controller, Hub).
/// Giúp Controller không phụ thuộc trực tiếp vào thư viện MQTTnet.
/// </summary>
public interface IMqttService
{
    /// <summary>Trạng thái kết nối hiện tại tới MQTT Broker.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Publish một lệnh điều khiển (ví dụ "PUMP_ON") lên topic điều khiển của ESP32.
    /// </summary>
    /// <param name="command">Nội dung lệnh: PUMP_ON, PUMP_OFF, VALVE_ON, VALVE_OFF.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <exception cref="InvalidOperationException">Ném ra khi chưa kết nối tới Broker.</exception>
    Task PublishControlAsync(string command, CancellationToken cancellationToken = default);
}