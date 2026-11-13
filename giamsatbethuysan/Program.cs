using giamsatbethuysan.Hubs;
using giamsatbethuysan.Models;
using giamsatbethuysan.Services;

var builder = WebApplication.CreateBuilder(args);

// Các nền tảng đám mây (Render, Heroku...) cho biết cổng cần lắng nghe qua biến môi trường PORT.
// Chạy trên máy cá nhân thì không có biến này, ứng dụng dùng cổng trong launchSettings.json.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// ============================================================
// 1. ĐĂNG KÝ DỊCH VỤ (Dependency Injection)
// ============================================================

// MVC: Controller + View (Razor)
builder.Services.AddControllersWithViews();

// SignalR: đẩy dữ liệu realtime từ server xuống trình duyệt
builder.Services.AddSignalR();

// Đọc mục "MqttSettings" trong appsettings.json và ánh xạ vào class MqttSettings.
// Các class khác nhận cấu hình này thông qua IOptions<MqttSettings>.
builder.Services.Configure<MqttSettings>(
    builder.Configuration.GetSection(MqttSettings.SectionName));

// MqttService vừa là dịch vụ chạy nền (IHostedService) vừa là dịch vụ để
// Controller gọi publish lệnh (IMqttService). Cả hai phải trỏ về CÙNG MỘT instance,
// nên đăng ký Singleton trước, sau đó map interface và HostedService về instance đó.
builder.Services.AddSingleton<MqttService>();
builder.Services.AddSingleton<IMqttService>(sp => sp.GetRequiredService<MqttService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttService>());

var app = builder.Build();

// ============================================================
// 2. CẤU HÌNH PIPELINE XỬ LÝ REQUEST
// ============================================================

if (app.Environment.IsDevelopment())
{
    // Chạy trên máy cá nhân: chuyển http sang https cho khớp với cấu hình của Visual Studio.
    app.UseHttpsRedirection();
}
else
{
    // Môi trường thật (Render): Render đã xử lý HTTPS ở lớp ngoài, container chỉ nhận HTTP
    // nên không bật chuyển hướng HTTPS ở đây.
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

// Route mặc định của MVC: /{controller=Home}/{action=Index}/{id?}
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Endpoint SignalR Hub. Trình duyệt kết nối tới đường dẫn "/sensorHub".
app.MapHub<SensorHub>("/sensorHub");

app.Run();