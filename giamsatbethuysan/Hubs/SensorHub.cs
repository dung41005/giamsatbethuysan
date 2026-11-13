using giamsatbethuysan.Services;
using Microsoft.AspNetCore.SignalR;

namespace giamsatbethuysan.Hubs;

/// <summary>
/// Tên các sự kiện (method phía client) mà server đẩy xuống trình duyệt.
/// Dùng hằng số để tránh gõ sai chuỗi giữa C# và JavaScript.
/// </summary>
public static class SensorHubEvents
{
    /// <summary>Nhận nhiệt độ mới. Tham số: (double value, DateTime timestampUtc).</summary>
    public const string ReceiveTemperature = "ReceiveTemperature";

    /// <summary>Nhận mực nước mới. Tham số: (double value, DateTime timestampUtc).</summary>
    public const string ReceiveLevel = "ReceiveLevel";

    /// <summary>Trạng thái kết nối MQTT của server. Tham số: (bool isConnected).</summary>
    public const string ReceiveMqttStatus = "ReceiveMqttStatus";
}

/// <summary>
/// SignalR Hub: giữ kết nối WebSocket giữa trình duyệt và server.
/// Dữ liệu cảm biến KHÔNG đi qua các method của Hub này; MqttService dùng
/// IHubContext&lt;SensorHub&gt; để chủ động đẩy dữ liệu xuống mọi client đang mở trang.
/// </summary>
public class SensorHub : Hub
{
    private readonly IMqttService _mqttService;
    private readonly ILogger<SensorHub> _logger;

    public SensorHub(IMqttService mqttService, ILogger<SensorHub> logger)
    {
        _mqttService = mqttService;
        _logger = logger;
    }

    /// <summary>
    /// Chạy khi có trình duyệt mới kết nối.
    /// Gửi ngay trạng thái MQTT hiện tại cho client đó để huy hiệu trạng thái hiển thị đúng
    /// mà không cần chờ sự kiện kế tiếp.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client SignalR kết nối: {ConnectionId}", Context.ConnectionId);

        await Clients.Caller.SendAsync(SensorHubEvents.ReceiveMqttStatus, _mqttService.IsConnected);
        await base.OnConnectedAsync();
    }

    /// <summary>Chạy khi trình duyệt ngắt kết nối (đóng tab, mất mạng...).</summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null)
        {
            _logger.LogInformation("Client SignalR ngắt kết nối: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning(exception, "Client SignalR ngắt kết nối do lỗi: {ConnectionId}", Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }
}