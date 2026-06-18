namespace PubSubLib.Client;

public interface IProvider
{
    string UnitType { get; }

    // Luồng IPubSubClient nhận dữ liệu tự MyConnection -> MyConnection sẽ phân bổ
    // dữ liệu từ sự kiện enter theo đúng UnitType cho Provider tương ứng
    // MyConnection sẽ call CreateObejct -> nhận về 1 đối tượng là GameObject (Unity/trong môi trường Dev và kiểm thử
    // sử dụng GameObjectTest để tạm thời thay thế), sau đó tạo 1 IUnit tham chiếu yếu và GameObejct
    // sau khi có Unit sẽ thêm vào danh sách quản lý của IPubSubClient để quản lý ping ngầm định kỳ thông qua tick

#if UNITY_ENGINE
    UnityEngine.GameObject CreateObject(long unitId, int version, byte[] data);
#else
    GameObjectTest CreateObject(long unitId, int version, byte[] data);
#endif


    // tương tự như create Obejct, nhưng hàm này sẽ xử lý destroy
    // IPubSubClient sẽ vào trong danh sách các Unit mà nó quản lý để lấy GameObject mà nó giữ
    // gửi vào trong DestroyObject để chạy logic xóa GameObject
    // vì là tham chiếu yếu nên khi GameObject thì xóa thì Unit cũng sẽ bị clear khỏi danh sách quản lý
    // của IPubSubClient trong quá trình xử lý ping
#if UNITY_ENGINE
    void DestroyObject(long unitId, UnityEngine.GameObject obj);
#else
    void DestroyObject(long unitId, GameObjectTest obj);
#endif

#if UNITY_ENGINE
    void OnEvent(long unitId, UnityEngine.GameObject obj, string eventName, byte[] data, EventMeta meta);
#else
    void OnEvent(long unitId, GameObjectTest obj, string eventName, byte[] data, EventMeta meta);
#endif

}