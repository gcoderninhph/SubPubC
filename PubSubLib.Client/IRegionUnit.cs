namespace PubSubLib.Client;

// Cách hoạt động gần giống với IPubSubClient
// Nhận unit từ IRegionModule.CreateUnit (server side) được invoke thông qua IRegionClientModule.OnCreateUnit (client side)
// Nhận thông tin đồng bộ từ IRegionUnit<T>.Commit (server side) xuống đối tượng


// đối tượng này sẽ được generate code và tự động triển khai bằng attribute UnitMirrorClient, attribute này sẽ có
// logic mirror giống hệt với MirrorProtoClient
// đối tượng này sẽ giữ 1 IUnit bên trong nó


// # Các interface sẽ có hiệu quả với đối tượng T
// IAlive cho phép T ghi đè logic sống của đối tượng, nếu không kế thừa, đối tượng này sẽ sống vĩnh viễn cho đến khi 
//          IRegionClientModule.Destroy/event Destroy từ server
// IRegionOnDestroy cho phép T triển khai hàm OnUnitDestroy, hàm này sẽ chạy ngay khi sự kiện destroy IRegionUnit xảy ra
//          khi IRegionClientModule.Destroy/event Destroy từ server
//          Luồng: IRegionClientModule.Destroy/event Destroy từ server -> IRegionOnDestroy.OnDestroyUnit (nếu có)
// ISetRegionUnit cho phép T được set IRegionUnit đang giữ nó
//          Luồng: IRegionClientModule.OnCreateUnit -> ISetRegionUnit.SetRegionUnit (nếu có) -> IRegionUnitOnStart.OnUnitStart (nếu có)
// IRegionUnitOnStart cho phép T triên khai hàm OnUnitStart, hàm này sẽ chạy ngay sau khi IRegionClientModule.OnCreateUnit
//          hoàn thành
//          Luồng: IRegionClientModule.OnCreateUnit -> IRegionUnitOnStart.OnUnitStart (nếu có)
// IRegionOnCommit cho phép T nhận thông tin commit khi server commit unit
//          Luồng: IRegionUnit<T>.Commit (server side) -> IRegionUnit đồng bộ dữ liệu mirror -> chạy IRegionOnCommit.OnUnitCommit (nếu có)


public interface IRegionUnit<T>
{
    // Khi generate sẽ tạo ra 1 string static _type, được generate từ attribute (mặc định lấy tên của class)
    // nếu trong proto buff cũng có trường type thì trường mirror type của proto sẽ là _Type
    string Type { get; }
    long Id { get; }

    // get đối tượng T mà nó giữ thông qua tham chiếu của IUnit.Target -> parse to T
    T Get();
}