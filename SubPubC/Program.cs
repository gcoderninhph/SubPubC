using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SubPubC;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";

var services = new ServiceCollection()
    .AddPubSubC(
        gridSize: 100,
        nats: natsUrl);

Console.WriteLine("""

  /$$$$$$            /$$            /$$ /$$$$$$$            /$$              /$$$$$$ 
 /$$__  $$          | $$           /$$/| $$__  $$          | $$             /$$__  $$
| $$  \__/ /$$   /$$| $$$$$$$     /$$/ | $$  \ $$ /$$   /$$| $$$$$$$       | $$  \__/
|  $$$$$$ | $$  | $$| $$__  $$   /$$/  | $$$$$$$/| $$  | $$| $$__  $$      | $$      
 \____  $$| $$  | $$| $$  \ $$  /$$/   | $$____/ | $$  | $$| $$  \ $$      | $$      
 /$$  \ $$| $$  | $$| $$  | $$ /$$/    | $$      | $$  | $$| $$  | $$      | $$    $$
|  $$$$$$/|  $$$$$$/| $$$$$$$//$$/     | $$      |  $$$$$$/| $$$$$$$/      |  $$$$$$/
 \______/  \______/ |_______/|__/      |__/       \______/ |_______/        \______/                                                                                                                                                        

Dịch vụ đăng ký các chủ đề sau:

| Chủ đề                                | Ý nghĩa                           | Payload ASCII         |
|---------------------------------------|-----------------------------------|-----------------------|
| `Unit.Enter`                          | Unit xuất hiện                    | `unitId,x,y`          |
| `Unit.Move`                           | Unit di chuyển                    | `unitId,x,y`          |
| `Unit.Event`                          | Unit gửi event                    | `unitId,event_name`   |
| `Unit.Exit`                           | Unit rời khỏi bản đồ              | `unitId`              |
| `Watcher.Enter`                       | Watcher bắt đầu quan sát          | `watcherId,x,y,range` |
| `Watcher.Move`                        | Watcher di chuyển phạm vi quan sát| `watcherId,x,y,range` |
| `Watcher.Exit`                        | Watcher dừng quan sát             | `watcherId`           |

Giá trị `x`, `y`, `range` là số thực (`float`). Các giá trị cách nhau dấu phẩy, không có khoảng trắng.

## Nhận sự kiện trả về

Dịch vụ sẽ publish ngược lên NATS theo chuẩn:

- `Watcher.{watcherId}.Unit.Enter` – danh sách Unit (dạng chuỗi `id1,id2,...`) vừa vào vùng quan sát.
- `Watcher.{watcherId}.Unit.Exit` – danh sách Unit vừa rời khỏi vùng quan sát.
- `Watcher.{watcherId}.Unit.Event.{event_name}` – thông báo Unit gửi event trong vùng quan sát, payload dạng `unitId`.
                                                                                    
""");

Log.Information("Starting SubPubC service...");
Log.Information("Connecting to NATS server at {NatsUrl}", natsUrl);

await Task.Delay(Timeout.Infinite);