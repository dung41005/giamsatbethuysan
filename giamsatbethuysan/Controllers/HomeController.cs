using System.Diagnostics;
using giamsatbethuysan.Models;
using giamsatbethuysan.Services;
using Microsoft.AspNetCore.Mvc;

namespace giamsatbethuysan.Controllers;

/// <summary>
/// Dữ liệu JSON mà trình duyệt gửi lên khi bấm nút: { "isOn": true } hoặc { "isOn": false }.
/// </summary>
/// <param name="IsOn">true = bật thiết bị, false = tắt thiết bị.</param>
public record ToggleRequest(bool IsOn);

/// <summary>
/// Controller chính: hiển thị Dashboard và nhận lệnh điều khiển từ nút bấm trên web.
/// </summary>
public class HomeController : Controller
{
    // Các lệnh ESP32 hiểu được (payload gửi lên topic "aquaculture/control").
    private const string PumpOn = "PUMP_ON";
    private const string PumpOff = "PUMP_OFF";
    private const string ValveOn = "VALVE_ON";
    private const string ValveOff = "VALVE_OFF";

    private readonly IMqttService _mqttService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IMqttService mqttService, ILogger<HomeController> logger)
    {
        _mqttService = mqttService;
        _logger = logger;
    }

    /// <summary>GET /  — Trang Dashboard giám sát.</summary>
    public IActionResult Index()
    {
        return View();
    }

    /// <summary>
    /// POST /Home/TogglePump — Bật/tắt máy bơm nước vào bể.
    /// Body JSON: { "isOn": true|false }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> TogglePump([FromBody] ToggleRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Thiếu dữ liệu yêu cầu." });
        }

        return await SendCommandAsync(request.IsOn ? PumpOn : PumpOff);
    }

    /// <summary>
    /// POST /Home/ToggleValve — Bật/tắt van thoát nước ra khỏi bể.
    /// Body JSON: { "isOn": true|false }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ToggleValve([FromBody] ToggleRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { success = false, message = "Thiếu dữ liệu yêu cầu." });
        }

        return await SendCommandAsync(request.IsOn ? ValveOn : ValveOff);
    }

    /// <summary>
    /// Hàm dùng chung: publish lệnh xuống ESP32 qua MQTT và trả kết quả JSON cho trình duyệt.
    ///   - 200: đã publish thành công.
    ///   - 503: server chưa kết nối được MQTT Broker.
    ///   - 500: lỗi không xác định.
    /// </summary>
    private async Task<IActionResult> SendCommandAsync(string command)
    {
        try
        {
            await _mqttService.PublishControlAsync(command, HttpContext.RequestAborted);

            return Ok(new
            {
                success = true,
                command,
                message = $"Đã gửi lệnh {command}."
            });
        }
        catch (InvalidOperationException ex)
        {
            // Lỗi nghiệp vụ do MqttService ném ra (chưa kết nối, Broker từ chối...).
            _logger.LogWarning(ex, "Không gửi được lệnh {Command}.", command);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi gửi lệnh {Command}.", command);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Không gửi được lệnh. Vui lòng thử lại."
            });
        }
    }

    /// <summary>Trang lỗi mặc định của template MVC (dùng với UseExceptionHandler trong Program.cs).</summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}