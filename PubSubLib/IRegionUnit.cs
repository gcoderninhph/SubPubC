using Google.Protobuf;

namespace PubSubLib;

// Đây là 1 interface wrapper IUnit
// đối tượng này sẽ được generate code và tự động triển khai bằng attribute UnitMirrorServer, attribute này sẽ có
// logic mirror giống hệt với MirrorProto
// đối tượng này sẽ giữ 1 IUnit bên trong nó
public interface IRegionUnit<T>
{
    // # Luồng Start
    // khởi động ngay sau khi IRegionModule.CreateUnit/IRegionModule.CreateUnitAsync hoàn thành
    // ## luồng
    // --> IRegionModule.CreateUnit/IRegionModule.CreateUnitAsync
    // --> chạy hàm ISetRegionUnit.SetRegionUnit (nếu đối tượng T có kế thừa ISetRegionUnit)
    // ---> chạy IRegionUnitOnStart.OnUnitStart của đối tượng T (nếu đối tượng T có kế thừa IRegionUnitOnStart)
    
    // # Luồng Destroy
    // Khởi động sau khi IRegionModule.DestroyUnit/IRegionUnit.Destroy
    // ## Luồng
    // --> IRegionModule.DestroyUnit/IRegionUnit.Destroy
    // --> chạy IRegionUnitOnDestroy.OnUnitDestroy của đối tượng T (nếu đối tượng T có kế thừa IRegionUnitOnDestroy)
    
    // ---------------------------------------------------------------------------------------------------------------

    long Id { get; }
    
    // khi generate sẽ tạo ra 1 trường static string _unitType, lúc đó sẽ UnitType => _unitType
    string UnitType { get; }
    
    // khi generate lấy Positon từ unit Position get => _unit.Position và set Position {_unit.Position = value} 
    Vector2 Position { get; set; }

    // trả về đối tượng mà IUnit đang giữ và parse về đúng kiểu
    T Get();
    
    // chuyển vào channel xử lý (MirrorProtoBus) -> ghi các thông tin vào protobuf mà nó mirror
    // -> tạo thành byte[] -> set byte[] cho IUnit -> đóng gói lại vào 1 proto commit mới
    // -> gửi event commit chứa thông tin commit và số byte[] trong IUnit thông qua IUnit.PublishEvent ("commit", dataBytes, true)
    void Commit(string commit);
    // chuyển vào channel xử lý (MirrorProtoBus) -> chuyển thành byte[] -> đóng gói lại 1 proto message mới
    // -> gửi event thành chứa thông tin subject + byte thông qua IUnit.PublishEvent ("message", dataBytes, reliable)
    void SendMessage<TProto>(string subject, TProto message, bool reliable) where TProto : class, IMessage<TProto>, new();
    
    // chỉ đơn giản là IUnit.Positon = value, không gửi bất cứ sự kiện nào tới client, tương tự cách xử lý với Position
    void SetPosition(float x, float y);
    void SetPosition(Vector2  position);

    // Xóa IRegionUnit và IUnit trong toàn bộ hệ thống IPubSub & IRegionUnit, khởi động luồng Destroy
    void Destroy();
}