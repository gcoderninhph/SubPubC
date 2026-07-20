using System;
using System.Collections.Generic;

namespace PubSubLib
{
    /// <summary>
    /// Quản lý các <see cref="IRegionUnit{TR}"/> trong một region dựa trên PubSub.
    /// Chịu trách nhiệm tạo, truy xuất, xóa unit và xử lý sự kiệnenter/leave qua watcher.
    /// </summary>
    /// <remarks>
    /// Phải gọi <see cref="Tick"/> định kỳ (mỗi frame/tick) để xử lý hàng đợi sự kiện unit.
    /// Kế thừa <see cref="IAsyncDisposable"/> để giải phóng tài nguyên PubSub và NAT adapter.
    /// </remarks>
    public interface IRegionModule : IAsyncDisposable
    {
        /// <summary>
        /// Tạo một <see cref="IRegionModule"/> instance mới từ cấu hình cho trước.
        /// </summary>
        /// <param name="config">Cấu hình PubSub và tùy chọn NAT adapter.</param>
        /// <returns>Instance của <see cref="IRegionModule"/>.</returns>
        public static IRegionModule Create(RegionConfig config)
        {
            return new RegionModule(config);
        }

        /// <summary>
        /// Tạo một unit mới một cách bất đồng bộ.
        /// Sau khi tạo thành công, hệ thống sẽ gọi <c>ISetRegionUnit.SetRegionUnit</c>
        /// (nếu <typeparamref name="T"/> triển khai) rồi gọi <c>IRegionUnitOnStart.OnUnitStart</c>.
        /// </summary>
        /// <typeparam name="T">Kiểu wrapper <see cref="IRegionUnit{TR}"/>.</typeparam>
        /// <typeparam name="TR">Kiểu đối tượng mục tiêu bên trong unit, phải triển khai <see cref="IAlive"/>.</typeparam>
        /// <param name="id">ID duy nhất của unit.</param>
        /// <param name="position">Vị trí ban đầu của unit.</param>
        /// <param name="target">Đối tượng mục tiêu (data) mà unit sẽ giữ.</param>
        /// <param name="setDefaultValue">hàm set dữ liệu mặc định cho T</param>
        /// <returns>Task chứa wrapper <typeparamref name="T"/> đã được tạo.</returns>
        T CreateUnit<T, TR>(long id, Vector2 position, TR target, Action<T>? setDefaultValue = null)
            where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;


        /// <summary>
        /// Lấy unit theo ID. Trả về <c>null</c> nếu không tìm thấy.
        /// </summary>
        /// <typeparam name="T">Kiểu wrapper <see cref="IRegionUnit{TR}"/>.</typeparam>
        /// <typeparam name="TR">Kiểu đối tượng mục tiêu bên trong unit.</typeparam>
        /// <param name="id">ID của unit cần tìm.</param>
        /// <returns>Wrapper <typeparamref name="T"/> hoặc <c>null</c> nếu không tồn tại.</returns>
        T? GetUnit<T, TR>(long id) where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

        /// <summary>
        /// Thử lấy unit theo ID.
        /// </summary>
        /// <typeparam name="T">Kiểu wrapper <see cref="IRegionUnit{TR}"/>.</typeparam>
        /// <typeparam name="TR">Kiểu đối tượng mục tiêu bên trong unit.</typeparam>
        /// <param name="id">ID của unit cần tìm.</param>
        /// <param name="unit">Wrapper <typeparamref name="T"/> nếu tìm thấy, ngược lại là <c>null</c>.</param>
        /// <returns><c>true</c> nếu tìm thấy, ngược lại là <c>false</c>.</returns>
        bool TryGetUnit<T, TR>(long id, out T unit) where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

        /// <summary>
        /// Lấy tất cả unit trong region theo kiểu wrapper.
        /// </summary>
        /// <typeparam name="T">Kiểu wrapper <see cref="IRegionUnit{TR}"/>.</typeparam>
        /// <typeparam name="TR">Kiểu đối tượng mục tiêu bên trong unit.</typeparam>
        /// <returns>Danh sách các wrapper <typeparamref name="T"/>.</returns>
        IList<T> GetUnits<T, TR>() where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

        /// <summary>
        /// Hủy một unit theo ID. Kích hoạt luồng Destroy (gọi <c>IRegionUnitOnDestroy.OnUnitDestroy</c>
        /// nếu có) rồi xóa unit khỏi hệ thống.
        /// </summary>
        /// <typeparam name="T">Kiểu wrapper <see cref="IRegionUnit{TR}"/>.</typeparam>
        /// <typeparam name="TR">Kiểu đối tượng mục tiêu bên trong unit.</typeparam>
        /// <param name="id">ID của unit cần hủy.</param>
        void DestroyUnit<T, TR>(long id) where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

        /// <summary>
        /// Xử lý hàng đợi sự kiện unit. Phải được gọi định kỳ (mỗi frame/tick)
        /// để đảm bảo các thao tác CreateUnit/DestroyUnit được thực thi trên thread chính.
        /// </summary>
        void Tick();
    }
}