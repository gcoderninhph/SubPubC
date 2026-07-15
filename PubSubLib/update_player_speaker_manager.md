

# 1
private readonly ConcurrentDictionary<PlayerDataKey, IPlayerData> _data = new(); 
chuyển thành
private readonly Dictionary<long playerId, Dictionary<string dataName, IPlayerData> _data = new();

# 2
private readonly ConcurrentDictionary<PlayerDataKey, long> _lastActiveTicks = new(); -> bỏ đi

khi client ping, chỉ ping theo playerId, không ping theo từng data
nếu ping hết hạn thì toàn bộ các IPlayerData.IsOnline của player -> false 

Sử dụng "dotnet add package Gcoder.Collections --version 1.3.0" để cập nhật tick, call back hết hạn -> thực hiện logic chuyển isOnline về false
ITimedCollection<TKey, TValue>.NewTimeSortSetSingleThread 

- Tạo thêm 1 Class nhỏ (Tạm gọi là PlayerMetaData) ghi thông tin Player thời gian ping gần nhất, tạm thời chỉ cần 1 trường duy nhất
- Trong manager bổ sung thêm 1 Dictionary<long playerId, PlayerMetaData> _playerMetaData để quản lý trạng thái Player
- Khi player Ping nhưng _playerMetaData không có Player -> thêm metadata -> gửi time ITimedCollection -> Báo OnClientConnect vào tất cả IPlayerData của player đó
- Khi player off hoặc hết hạn ping (call back hết hạn từ ITimedCollection) -> xóa metaData -> Báo OnClientDisconnect vào tất cả IPlayerData của player đó

# 3 private readonly ConcurrentQueue<Action> _mainThreadActions = new(); chuyển chức năng sang chỉ nhận dữ liệu từ Natify
Tất cả PlayerSpeaksManager sẽ hoạt động trên 1 luồng duy nhất để tránh race condition
- Queue nhận tất cả các sự kiện từ INatifyClient
- IPlayerSpeaksManager bổ sung chức năng hàm Tick() -> Tick() để chạy queue và đẩy bánh xe để check hết hạn của ITimedCollection

# 4. Loại bỏ void OnRemove<T>(Func<T, Task>? callback) where T : class, IPlayerData, new();
- Không cần thiết vì đã có interface IOnRemove

# 5.Chuyển sang đồng bộ Task<bool> RemoveAsync(long playerId); -> bool Remove(long playerId)

# 6. void OnDefault<T>(Func<T, Task>? callback) where T : class, IPlayerData, new(); 
- chuyển thành void OnDefault<T>(Func<T, Task<Action>> callback) where T : class, IPlayerData, new();
- khi sử dung sẽ như sau
```csharp
OnDefault<PlayerMirrorData>(async(mirrorEmpty)=> {
    var dataDb = await GetFromDb(mirrorEmpty.PlayerId);
    return ()=> 
    {
        mirrorEmpty set data from dataDb
    }
});
```

OnDefault bên trong sẽ chạy Func và lấy action từ Task bất đồng bộ (không sử dụng async/await) và đưa action vào
_mainThreadActions để chờ xử lý -> khi action xử lý xong lúc này mới được chạy DoneInit(ở mục 7) -> run các hook onCreate/onConnect

# 7. IPlayerData bổ sung thêm 1 hàm DoneInit
- Có tác dụng xác nhận tạo IPlayerData xong -> sau đó sẽ khởi động các hook onCreate/onConnect (nếu client có kết nối)
- Trường hợp IPlayerData tạo và DoneInit sau khi player đã kết nối và muốn kiểm tra client có online hay không thì chỉ cần check 
_playerMetaData có player trong đó hay không để chạy OnClientConnect
