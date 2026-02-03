# SubPubC

Hệ thống Pub/Sub chạy nền bằng .NET 9.0 sử dụng NATS để phân phối sự kiện giữa **Unit** (đối tượng di chuyển) và **Watcher** (vùng quan sát). Dịch vụ này không cần ASP.NET, chỉ là một tiến trình console.

## Chuẩn bị

1. Cài [.NET SDK 9.0](https://dotnet.microsoft.com/).
2. Cài và chạy [NATS Server](https://docs.nats.io/running-a-nats-service/introduction) trên `nats://localhost:4222` (hoặc thay địa chỉ trong `Program.cs`).
3. (Tùy chọn) Cài Redis nếu bạn muốn lưu thêm trạng thái khác.

## Khởi chạy dịch vụ

```bash
cd SubPubC
 dotnet run --project SubPubC/SubPubC.csproj
```

Khi chạy thành công, log console sẽ hiển thị "Pub/Sub console đã khởi động. Nhấn Ctrl+C để thoát.". Ứng dụng vẫn chạy cho tới khi bạn nhấn `Ctrl+C`.

## Định dạng thông điệp NATS

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

- `Watcher.{watcherId}.Unit.Enter` – danh sách Unit vào vùng quan sát dạng chuỗi (`id1,id2,...`).
- `Watcher.{watcherId}.Unit.Exit` – danh sách Unit rời vùng quan sát dạng chuỗi (`id1,id2,...`).
- `Watcher.{watcherId}.Unit.Event.{event_name}` – thông báo Unit gửi event trong vùng quan sát dạng chuỗi (`unitId`).

Bạn chỉ cần subscribe vào các chủ đề trên để nhận kết quả.

## Tùy biến

- Thay đổi kích thước ô lưới hoặc URL NATS bằng cách sửa tham số khi gọi `AddPubSubC` trong `Program.cs`.
- Các chiến lược tối ưu hóa (shard dictionary, cấu trúc dữ liệu) nằm trong mã nguồn `SubPubC/*.cs` nếu bạn muốn tinh chỉnh thêm.
