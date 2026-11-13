using System.Globalization;
using giamsatbethuysan.Hubs;
using giamsatbethuysan.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace giamsatbethuysan.Services;

/// <summary>
/// Dịch vụ chạy nền (IHostedService) đảm nhiệm toàn bộ việc giao tiếp MQTT:
///   1. Kết nối tới Broker và tự động kết nối lại khi mất kết nối.
///   2. Subscribe topic nhiệt độ / mực nước từ ESP32.
///   3. Mỗi khi có dữ liệu, đẩy ngay lên trình duyệt qua SignalR.
///   4. Cung cấp hàm Publish lệnh điều khiển xuống ESP32 (IMqttService).
/// Yêu cầu NuGet: MQTTnet 4.3.x.
/// </summary>
public sealed class MqttService : IMqttService, IHostedService, IDisposable
{
    private readonly MqttSettings _settings;
    private readonly IHubContext<SensorHub> _hubContext;
    private readonly ILogger<MqttService> _logger;

    private readonly MqttFactory _mqttFactory = new();
    private readonly IMqttClient _mqttClient;
    private readonly MqttClientOptions _clientOptions;

    private CancellationTokenSource? _stoppingCts;
    private Task? _connectLoopTask;

    public MqttService(
        IOptions<MqttSettings> options,
        IHubContext<SensorHub> hubContext,
        ILogger<MqttService> logger)
    {
        _settings = options.Value;
        _hubContext = hubContext;
        _logger = logger;

        _mqttClient = _mqttFactory.CreateMqttClient();

        // ClientId có hậu tố ngẫu nhiên: broker công cộng sẽ đá client cũ ra nếu trùng ClientId.
        _clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
            .WithClientId($"{_settings.ClientIdPrefix}-{Guid.NewGuid():N}")
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_settings.KeepAliveSeconds))
            .WithCleanSession()
            .Build();
    }

    /// <inheritdoc />
    public bool IsConnected => _mqttClient.IsConnected;

    // ============================================================
    // VÒNG ĐỜI IHostedService
    // ============================================================

    /// <summary>
    /// Chạy khi ứng dụng khởi động. Đăng ký sự kiện và mở vòng lặp kết nối ở nền.
    /// Không chặn quá trình khởi động web, kể cả khi Broker đang không truy cập được.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();

        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

        _connectLoopTask = Task.Run(() => ConnectLoopAsync(_stoppingCts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    /// <summary>Chạy khi ứng dụng tắt: dừng vòng lặp và ngắt kết nối Broker một cách êm ái.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stoppingCts is null)
        {
            return;
        }

        _stoppingCts.Cancel();

        if (_connectLoopTask is not null)
        {
            try
            {
                await _connectLoopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Hết thời gian chờ shutdown: bỏ qua và tiếp tục dọn dẹp.
            }
        }

        // Gỡ sự kiện trước khi ngắt để không bắn thông báo "mất kết nối" khi đang tắt app.
        _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
        _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;

        if (_mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions(), cancellationToken);
        }

        _logger.LogInformation("Đã dừng MQTT service.");
    }

    // ============================================================
    // KẾT NỐI / KẾT NỐI LẠI
    // ============================================================

    /// <summary>
    /// Vòng lặp nền: cứ mỗi ReconnectDelaySeconds giây kiểm tra kết nối,
    /// nếu đang mất kết nối thì thử kết nối lại.
    /// </summary>
    private async Task ConnectLoopAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(1, _settings.ReconnectDelaySeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!_mqttClient.IsConnected)
            {
                await TryConnectAsync(cancellationToken);
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Thực hiện một lần kết nối: Connect, Subscribe rồi báo trạng thái "đã kết nối" cho web.
    /// Mọi lỗi đều được ghi log và để vòng lặp thử lại ở chu kỳ sau.
    /// </summary>
    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Đang kết nối MQTT Broker {Host}:{Port}...", _settings.BrokerHost, _settings.BrokerPort);

            await _mqttClient.ConnectAsync(_clientOptions, cancellationToken);

            // CleanSession = true nên sau mỗi lần kết nối lại phải subscribe lại.
            await SubscribeTopicsAsync(cancellationToken);

            _logger.LogInformation("Đã kết nối MQTT Broker và subscribe topic thành công.");
            await BroadcastMqttStatusAsync(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ứng dụng đang tắt, không cần xử lý.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kết nối MQTT thất bại, sẽ thử lại sau {Seconds} giây.", _settings.ReconnectDelaySeconds);

            // Nếu đã connect được nhưng subscribe lỗi: ngắt hẳn để vòng lặp làm lại từ đầu.
            await SafeDisconnectAsync();
        }
    }

    /// <summary>Đăng ký nhận dữ liệu từ 2 topic: nhiệt độ và mực nước (QoS 1).</summary>
    private async Task SubscribeTopicsAsync(CancellationToken cancellationToken)
    {
        var subscribeOptions = _mqttFactory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f
                .WithTopic(_settings.TempTopic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .WithTopicFilter(f => f
                .WithTopic(_settings.LevelTopic)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

        await _mqttClient.SubscribeAsync(subscribeOptions, cancellationToken);
    }

    /// <summary>Ngắt kết nối và nuốt mọi lỗi phát sinh (dùng trong luồng xử lý lỗi).</summary>
    private async Task SafeDisconnectAsync()
    {
        try
        {
            if (_mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync(new MqttClientDisconnectOptions());
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lỗi khi ngắt kết nối MQTT (bỏ qua).");
        }
    }

    // ============================================================
    // XỬ LÝ SỰ KIỆN MQTT
    // ============================================================

    /// <summary>
    /// Chạy mỗi khi Broker chuyển tới một tin nhắn thuộc topic đã subscribe.
    /// Phân loại theo topic, chuyển payload thành số, rồi đẩy lên trình duyệt qua SignalR.
    /// </summary>
    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString();

            if (!TryParseNumber(payload, out var value))
            {
                _logger.LogWarning("Payload không phải số hợp lệ. Topic: {Topic}, Payload: {Payload}", topic, payload);
                return;
            }

            // Gửi giờ UTC để trình duyệt tự đổi sang múi giờ của người xem.
            var timestampUtc = DateTime.UtcNow;

            if (string.Equals(topic, _settings.TempTopic, StringComparison.Ordinal))
            {
                await _hubContext.Clients.All.SendAsync(SensorHubEvents.ReceiveTemperature, value, timestampUtc);
            }
            else if (string.Equals(topic, _settings.LevelTopic, StringComparison.Ordinal))
            {
                await _hubContext.Clients.All.SendAsync(SensorHubEvents.ReceiveLevel, value, timestampUtc);
            }
        }
        catch (Exception ex)
        {
            // Không để lỗi của một tin nhắn làm hỏng luồng nhận của MQTT client.
            _logger.LogError(ex, "Lỗi khi xử lý tin nhắn MQTT.");
        }
    }

    /// <summary>Chạy khi mất kết nối Broker: báo cho web biết, vòng lặp nền sẽ tự kết nối lại.</summary>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("Mất kết nối MQTT Broker. Lý do: {Reason}", e.Reason);
        await BroadcastMqttStatusAsync(false);
    }

    /// <summary>Thông báo trạng thái kết nối MQTT tới tất cả trình duyệt đang mở.</summary>
    private Task BroadcastMqttStatusAsync(bool isConnected) =>
        _hubContext.Clients.All.SendAsync(SensorHubEvents.ReceiveMqttStatus, isConnected);

    // ============================================================
    // PUBLISH LỆNH ĐIỀU KHIỂN
    // ============================================================

    /// <inheritdoc />
    public async Task PublishControlAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!_mqttClient.IsConnected)
        {
            throw new InvalidOperationException("Server chưa kết nối được tới MQTT Broker.");
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(_settings.ControlTopic)
            .WithPayload(command)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        var result = await _mqttClient.PublishAsync(message, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Broker từ chối lệnh '{command}': {result.ReasonCode}");
        }

        _logger.LogInformation("Đã publish lệnh {Command} lên topic {Topic}", command, _settings.ControlTopic);
    }

    // ============================================================
    // HÀM TIỆN ÍCH
    // ============================================================

    /// <summary>
    /// Chuyển chuỗi payload từ ESP32 thành số thực.
    /// Dùng InvariantCulture và đổi dấu ',' thành '.' để "27,5" và "27.5" đều đọc được,
    /// không phụ thuộc cài đặt vùng của máy chủ.
    /// </summary>
    private static bool TryParseNumber(string? payload, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var normalized = payload.Trim().Replace(',', '.');

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value);
    }

    public void Dispose()
    {
        _stoppingCts?.Dispose();
        _mqttClient.Dispose();
    }
}