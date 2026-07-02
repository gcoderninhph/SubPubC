using MyConnection;

namespace PubSubLib.Client;

public interface IRegionClientModule : IClientModule
{
    static IRegionClientModule Create(Config config)
    {
        throw new NotImplementedException();
    }

    // Lấy ra 1 đối tượng T cụ thể thông qua id của đối tượng
    T GetUnit<T, TR>(long id) where T : IRegionUnit<TR>;
    // Lấy toàn bộ các đối tượng thuộc class T mà IRegionClientModule quản lý
    IList<T> GetUnits<T, TR>() where T : IRegionUnit<TR>;

    // Khi trên server tạo 1 đối tượng Mirror tương ứng với T qua IRegionModule.CreateUnit -> 
    void OnCreateUnit<T, TR>(Func<T, TR> unit) where T : IRegionUnit<TR>;
    void Destroy<T, TR>(T unit) where T : IRegionUnit<TR>;
    // Tương tự như IPubSubClient.Tick(), nó sẽ gửi lệnh watcher ping unit lên server nhằm đồng bộ các unit trên bản đồ
    void Tick();
}