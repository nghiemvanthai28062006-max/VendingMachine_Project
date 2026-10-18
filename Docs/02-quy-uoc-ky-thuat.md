Quy ước kỹ thuật — Hệ thống máy bán nước thông minh
Trạng thái: BẢN NHÁP, bổ sung cho 01-pham-vi-va-luat-nghiep-vu.md (File 1) và API.md. File này trả lời các câu hỏi "code thế nào" mà hai file kia không quy định. Nguyên tắc chọn: đơn giản, dễ làm; nếu khó hơn một chút mà kết quả đẹp hơn thì chọn; quá khó thì bỏ. Mâu thuẫn với File 1 thì File 1 thắng.

Có code song song được chưa?
Được ngay. Hợp đồng giao tiếp (API.md) và DB (schema.sql) đã đủ. Chỉ có 3 thứ cần cả nhóm thống nhất trước khi mỗi người bắt đầu, và đều nằm trong file này:

Tên khóa cấu hình (mục 7), để mỗi người cấu hình đúng một kiểu.

Cấu hình Mosquitto (mục 9), để ai cũng chạy được broker giống nhau.

Dạng lỗi trả về (mục 4).

Các mục còn lại là quyết định riêng của từng thành phần, người phụ trách cứ làm theo.

1. Kiến trúc code (Server)
Chọn: ASP.NET Core Web API, 3 lớp, truy cập DB bằng Dapper + MySqlConnector.

Lý do không dùng EF Core: DB đã có sẵn schema.sql, không cần migrations; và các ca chống tranh chấp (mục 2) cần viết đúng câu SQL (FOR UPDATE, UPDATE ... WHERE), Dapper cho kiểm soát trực tiếp.

text
Server/
├── Controllers/        # REST cho App (mỏng: nhận request, gọi Service, trả response)
├── Mqtt/               # MqttService (kết nối), các Handler cho từng topic (mỏng)
├── Services/           # NGHIỆP VỤ: OrderService, TrayService, AlertService, RestockService, ReportService...
├── Repositories/       # CHỈ câu SQL (Dapper): OrderRepository, TrayRepository...
├── Models/             # Entity (khớp bảng) và DTO (khớp JSON)
├── Infrastructure/     # Kết nối DB, JWT, IClock, định dạng lỗi
├── Database/           # schema.sql, seed.sql
└── Program.cs          # đăng ký DI
Luật phụ thuộc, chỉ đi một chiều Controller/Handler → Service → Repository:

Controller và Handler MQTT không chứa nghiệp vụ, chỉ chuyển dữ liệu vào Service và đổi kết quả thành JSON/MQTT.

Service không biết HTTP hay MQTT. Hàm Service trả về một kết quả gồm "thành công" hoặc "mã lỗi" (ví dụ TRAY_EMPTY), không ném exception cho lỗi nghiệp vụ.

Service cần đẩy dữ liệu xuống Device thì dùng interface IDeviceNotifier (đặt trong Services/), MqttService implement nó. Xem 10.5.

Repository chỉ có SQL, không có if nghiệp vụ.

DI: dùng DI có sẵn của ASP.NET Core. Service và Repository đăng ký Scoped; MqttService là BackgroundService/IHostedService (singleton), mỗi message tạo một scope riêng để lấy Service.

Thư viện: Dapper, MySqlConnector, BCrypt.Net-Next, Microsoft.AspNetCore.Authentication.JwtBearer, MQTTnet và MQTTnet.Extensions.ManagedClient (cùng bản 4.3.x).

Dapper: bật DefaultTypeMap.MatchNamesWithUnderscores = true để cột snake_case tự ánh xạ vào thuộc tính PascalCase.

Thời gian: không gọi NOW() trong SQL, Service lấy giờ từ IClock rồi truyền vào làm tham số (để test được, và để cùng múi giờ UTC+7).

Device Simulator và App (WinForms) làm theo tinh thần tương tự: Form chỉ lo giao diện, logic gọi API/MQTT để ở lớp riêng (MqttClientService cho Device, ApiClient cho App). Cho App một interface IApiClient có bản FakeApiClient trả JSON mẫu trong API.md, để làm giao diện trước khi Server xong.

2. Chống tranh chấp dữ liệu (race condition)
Chọn: khóa dòng khay khi tạo đơn, và mọi chuyển trạng thái/trừ kho dùng UPDATE ... WHERE kiểm tra điều kiện, đọc "số dòng bị tác động". Không đọc số lượng lên C# rồi cộng trừ rồi ghi lại.

Tạo đơn (BR-03), trong một transaction:

sql
SELECT id, quantity, product_id FROM trays
 WHERE machine_id = @machineId AND code = @trayCode FOR UPDATE;   -- khóa dòng khay: các lần tạo đơn cùng khay xếp hàng

SELECT COUNT(*) FROM orders
 WHERE tray_id = @trayId
   AND (status IN ('PAID','DISPENSING')
        OR (status = 'PENDING_PAYMENT' AND expires_at > @now));    -- số đơn đang giữ chỗ
-- khả dụng = quantity - số đơn giữ chỗ; > 0 mới INSERT đơn
Hai Device cùng giành chai cuối cùng: người thứ hai bị chặn ở FOR UPDATE, đợi người thứ nhất commit rồi mới đếm lại và thấy khả dụng = 0 → TRAY_EMPTY.

Hoàn tất đơn OK (BR-09), một transaction, khóa theo thứ tự khay trước, đơn sau (tránh khóa chéo):

sql
SELECT id FROM trays WHERE id = @trayId FOR UPDATE;
UPDATE orders SET status='COMPLETED', completed_at=@now
 WHERE id=@orderId AND status='DISPENSING';        -- phải ảnh hưởng đúng 1 dòng, không thì INVALID_STATE
UPDATE trays SET quantity = quantity - 1
 WHERE id=@trayId AND quantity > 0;                -- phải ảnh hưởng đúng 1 dòng, không thì rollback
Quy tắc chung:

Mọi chuyển trạng thái đơn viết dạng UPDATE orders SET status=@mới ... WHERE id=@id AND status=@cũ và kiểm tra số dòng bị tác động = 1. Nhờ vậy SweepStaleOrders() và payment/callback đến cùng lúc cũng không đè nhau (chỉ một bên thắng).

Tiếp hàng: UPDATE trays SET quantity = quantity + @n WHERE id=@id AND quantity + @n <= capacity.

Gặp lỗi khóa chéo (mã MySQL 1213) thì thử lại transaction một lần.

3. Xử lý lỗi MQTT
Chọn: dùng ManagedMqttClient (tự kết nối lại) cho cả Server và Device; ghi DB trước, publish sau; Device gửi lại yêu cầu nếu không thấy phản hồi.

Tình huống	Cách xử lý
Mất kết nối broker	ManagedMqttClient tự kết nối lại, AutoReconnectDelay = 5 giây. Không viết vòng retry tự chế
Kết nối lại xong	Subscribe lại tất cả topic trong sự kiện ConnectedAsync (bắt buộc vì phiên có thể mất). Server còn phải: chạy SweepStaleOrders() và publish lại trays của mọi máy. Device: gửi ngay một heartbeat
Publish thất bại (Server)	Ghi log mức Warning. DB mới là nguồn sự thật: trạng thái đơn đã lưu trước khi publish. Device gửi lại yêu cầu thì Server publish lại order/status (BR-16)
Device không nhận được phản hồi	Sau 5 giây chưa có order/status cho yêu cầu vừa gửi, gửi lại cùng requestId; tối đa 3 lần, rồi hiện "Mất kết nối, vui lòng thử lại" và quay về màn hình chính
Payload sai định dạng (JSON hỏng, thiếu trường)	Server không đổ vỡ: bắt exception, ghi log Warning, trả error: VALIDATION_ERROR nếu đọc được requestId, không thì bỏ qua
Exception bất ngờ trong handler	Bắt ở tầng Handler, ghi log Error, không để làm sập tiến trình
Server chưa kết nối được broker lúc khởi động	Vẫn chạy REST bình thường, MQTT tự thử lại. Ghi log rõ
4. Dạng lỗi khi dữ liệu sai
REST (bổ sung cho API.md mục B1): khi nhiều trường cùng sai thì trả hết trong một lần để form tô đỏ được nhiều ô:

json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Dữ liệu không hợp lệ",
    "details": [
      { "field": "price", "message": "Giá phải lớn hơn hoặc bằng 0" },
      { "field": "name",  "message": "Tên sản phẩm không được để trống" }
    ]
  }
}
details chỉ có với VALIDATION_ERROR. Các lỗi khác (như TRAY_EMPTY) không có details. Cách làm trong ASP.NET Core: kiểm tra bằng DataAnnotations, rồi cấu hình InvalidModelStateResponseFactory để đổi sang dạng trên.

MQTT (order/status): chỉ trả lỗi đầu tiên tìm được (không có details), vì Device chỉ cần một thông báo cho khách.

Thông báo message viết tiếng Việt, ngắn, hiển thị được trực tiếp trên giao diện.

5. Kiểm thử
Chọn: xUnit, một project Server.Tests, tập trung vào logic nghiệp vụ thuần.

Để test được, đặt logic quyết định vào các hàm/lớp thuần (không đụng DB, không đụng MQTT), nhận thời gian qua IClock:

Cần kiểm thử	Luật
Bảng chuyển trạng thái đơn hợp lệ/không hợp lệ	File 1, mục 7
Tính số chai khả dụng (trừ đơn giữ chỗ, bỏ qua đơn quá hạn)	BR-03
Điều kiện từ chối tạo đơn (máy bảo trì, khay chặn, hết hàng...)	BR-04
Quá hạn: 120s, 60s từ paid_at, 30s từ dispense_started_at	BR-05, 07, 08
Quyết định tạo cảnh báo (hết/sắp hết, không tạo trùng)	BR-11
connection ONLINE/OFFLINE tính ra từ last_heartbeat (kể cả NULL)	BR-12
planned_qty, giới hạn sức chứa khi châm	BR-13, 14
Sắp xếp độ khẩn của máy cần tiếp hàng	BR-15
Tính doanh thu (chỉ đơn COMPLETED)	API.md B8
Tối thiểu 15–20 test case chạy xanh, chụp ảnh kết quả đưa vào báo cáo. Không bắt buộc viết test tích hợp với MySQL thật; thay vào đó kiểm thử thủ công các ca tranh chấp và ghi vào bảng test của báo cáo (chạy hai Device cùng giành chai cuối).

6. Ghi log
Chọn: Serilog (Serilog.AspNetCore), ghi ra console và file logs/server-.log (mỗi ngày một file).

Mỗi lần đơn đổi trạng thái ghi một dòng mức Information: Order 1025 PENDING_PAYMENT -> PAID (payment/callback, máy M01). Cái này thay vai trò nhật ký thao tác (không có audit_logs) và dùng để đối chiếu khi test.

Lỗi nghiệp vụ (TRAY_EMPTY...) ghi Information; bất thường (payload hỏng, mất MQTT) ghi Warning; exception bất ngờ ghi Error.

Không ghi mật khẩu, JWT, chuỗi kết nối.

logs/ nằm trong .gitignore.

Device Simulator: có một ô hiển thị log trên giao diện (các message MQTT gửi/nhận) để demo và gỡ lỗi.

7. Cấu hình
Chọn: appsettings.json (commit, giá trị mặc định không bí mật) + appsettings.Local.json (không commit, mỗi người tự tạo, ghi đè bí mật). Repo commit thêm appsettings.Local.example.json làm mẫu. Thêm appsettings.Local.json vào .gitignore (BR-18).

Cả nhóm dùng đúng tên khóa sau:

Server

json
{
  "ConnectionStrings": { "Default": "Server=localhost;Port=3306;Database=vending;User ID=root;Password=...;" },
  "Mqtt": { "Host": "localhost", "Port": 1883, "Username": "server", "Password": "...", "ClientId": "vending-server" },
  "Jwt":  { "Secret": "...(chuỗi dài ít nhất 32 ký tự)...", "ExpiryHours": 8 },
  "Urls": "http://localhost:5000"
}
Device Simulator

json
{
  "Mqtt": { "Host": "localhost", "Port": 1883, "Username": "device", "Password": "..." },
  "Device": { "MachineCode": "M01" }
}
(ClientId của Device bằng MachineCode, bắt buộc để ACL %c chạy đúng, BR-19.)

App quản lý

json
{ "Api": { "BaseUrl": "http://localhost:5000/api" } }
8. Thứ tự làm và ghép nối
Bốn người, bốn cột: Server-A (MQTT và đơn hàng), Server-B (nền tảng và REST), Device, AdminApp. Người làm Device còn phụ trách phần báo cáo (sơ đồ, bảng test case) song song suốt dự án.
Các mốc M0 → M4 là điểm ghép nối, không phải cổng chặn (luật chi tiết trong README.md). Mỗi người đi hết phần của mình theo tốc độ riêng; mỗi mốc có một buổi ghép nối vào ngày cố định, cả nhóm chạy đầu-cuối trên một máy.
Cách chia nhánh và file cho Server (hai người làm chung một project):
Server-A làm trên nhánh feature/server-mqtt, Server-B làm trên nhánh feature/server-rest. Mỗi người mở pull request riêng vào main và kéo main về nhánh mình thường xuyên.
Program.cs là chỗ dễ đụng nhau nhất. Mỗi người đăng ký DI trong một hàm riêng (AddMqttModule() của A, AddAdminModule() của B), Program.cs chỉ gọi hai dòng.
Làm khi chưa có phần kia:
Device chưa có Server: dùng MQTT Explorer tự publish trays, order/status mẫu (theo API.md).
AdminApp chưa có Server: dùng FakeApiClient.
Server chưa có Device: dùng MQTT Explorer gửi order/create... và Postman cho REST.
M0: Nền tảng
Người	Việc	Xong khi
Server-A	Ngày đầu: định nghĩa interface IDeviceNotifier, DTO order/status, các enum trạng thái (một file nhỏ để B không bị chặn). Sau đó viết logic thuần: OrderStateMachine, tính số chai khả dụng, hàm kiểm tra quá hạn, kèm unit test.	Test bảng chuyển trạng thái đơn chạy xanh.
Server-B	Khung project: Solution, thư mục, đọc appsettings.Local.json, DI, kết nối DB, JsonDefaults, DateTimeOffsetHandler, IClock, định dạng lỗi có details, JWT và đăng nhập, Serilog, NullDeviceNotifier tạm, /health.	Login admin ra token, sai mật khẩu ra 401, /health chạy, máy người khác chạy được theo README.
Device	Khung WinForms, đọc cấu hình, lưới khay từ file JSON mẫu và ảnh trong images/.	Lưới hiện đủ ảnh, giá, tồn kho; khay hết hàng bị mờ.
AdminApp	Khung WinForms, IApiClient với FakeApiClient và ApiClient, màn hình đăng nhập, menu theo vai trò.	Đăng nhập admin và staff1 (dữ liệu giả) thấy menu khác nhau.
Phối hợp: Server-B đẩy khung lên main sớm nhất có thể, mục tiêu trong 1–2 ngày đầu, vì Server-A cần chỗ để gắn code. Trong lúc chờ, A làm logic thuần trên nhánh của mình, không phụ thuộc ASP.NET hay MQTT. Khi khung lên main, A kéo main về rồi chép file vào đúng thư mục. Interface IDeviceNotifier do A định nghĩa; B đăng ký NullDeviceNotifier tạm, sau thay bằng MqttService thật.
M1: Nối MQTT và dữ liệu nền
Người	Việc	Xong khi
Server-A	MqttService (ManagedMqttClient, subscribe lại khi kết nối lại), nhận heartbeat, xử lý LWT, publish trays (retained) khi khởi động.	MQTT Explorer publish heartbeat thì last_heartbeat đổi; subscribe vending/M01/trays thấy danh sách khay.
Server-B	REST sản phẩm (thêm, sửa, ngừng/mở bán), máy và khay, GET /machines có connection tính ra, phân quyền A/S; gọi IDeviceNotifier mỗi khi đổi dữ liệu.	Postman thêm sản phẩm và gán vào khay thì (khi A xong MqttService) MQTT Explorer thấy trays đổi.
Device	MqttClientService (ClientId = mã máy, LWT), heartbeat 15 giây, subscribe trays và vẽ lưới, ô log.	Lưới đổi theo dữ liệu Server; tắt Device đột ngột thì máy thành OFFLINE ngay.
AdminApp	Màn hình sản phẩm, máy và khay (dữ liệu giả), hiển thị lỗi details tô đỏ từng ô.	Thêm, sửa, ngừng bán sản phẩm trên dữ liệu giả.
Phối hợp: Server-A và Device chốt dữ liệu topic trays, cùng bám ví dụ JSON ở API.md mục A3. Cuối mốc chạy thử chéo: Device vẽ đúng từ message của Server thật, kể cả available: false và product: null. Server-B và AdminApp bám API.md mục B3, B4.
M2: Mua hàng thành công
Người	Việc	Xong khi
Server-A	OrderService: order/create (khóa FOR UPDATE, BR-03/04), payment/callback, dispense/command, dispense/started, dispense/result OK (trừ kho, cảnh báo tồn kho), publish order/status, chống trùng.	Chạy trọn luồng bằng MQTT Explorer ra đơn COMPLETED, tồn kho −1; gửi trùng không xử lý hai lần; hai đơn giành chai cuối chỉ một đơn thắng.
Server-B	GET /orders, GET /orders/{id} (có CheckAndExpireOrder).	Đơn tạo bằng MQTT hiện đúng trong API.
Device	Luồng mua: chọn khay → order/create (requestId) → QR và đếm ngược theo expiresAt → nút giả lập thanh toán → nhận lệnh nhả → dispense/started → hiệu ứng → dispense/result OK.	Mua trọn luồng với Server thật.
AdminApp	Chuyển sang Server thật cho đăng nhập, sản phẩm, máy, khay.	Thêm sản phẩm trên App thì Device thấy đổi.
Phối hợp: Server-A và Server-B làm song song. B thử GET /orders bằng đơn chèn tay trong DB cho đến khi luồng của A chạy được. Device và Server-A bám đúng order/status và requestId ở API.md mục A3.
M3: Nhánh lỗi và cảnh báo
Người	Việc	Xong khi
Server-A	JAMMED và hoàn tiền, SweepStaleOrders() và CheckAndExpireOrder() (3 điều kiện; payments thành FAILED khi đơn hết hạn), cảnh báo JAMMED, publish lại trays đủ các trường hợp.	Kẹt hàng ra REFUNDED; 120s không trả ra EXPIRED; callback muộn bị từ chối; kết quả nhả đến muộn bị INVALID_STATE.
Server-B	REST cảnh báo: danh sách (kèm cảnh báo ảo MACHINE_OFFLINE), đóng cảnh báo (đóng JAMMED thì publish lại trays).	Đóng cảnh báo kẹt thì khay bán lại được.
Device	Bảng điều khiển kịch bản (thanh toán lỗi, kẹt hàng, ngắt mạng), gửi lại sau 5 giây tối đa 3 lần, hiển thị error, EXPIRED, REFUNDED.	Mọi kịch bản lỗi hiện đúng thông báo.
AdminApp	Màn hình cảnh báo (lọc, đánh dấu đã xử lý, hiện MACHINE_OFFLINE), tự làm mới mỗi 10 giây.	Device báo kẹt thì App thấy cảnh báo.
Phối hợp (cảnh báo, ba bên cùng chạm vào): Server-A tạo bản ghi cảnh báo, Server-B đọc và đóng, AdminApp hiển thị. Chốt dạng JSON của alerts ở API.md mục B5 trước khi làm. Kiểm tra cuối mốc: Device báo kẹt → App thấy cảnh báo → đóng cảnh báo → khay bán lại được.
M4: Tiếp hàng, báo cáo, hoàn thiện
Người	Việc	Xong khi
Server-A	Gỡ lỗi, hoàn thiện unit test (tối thiểu 15 ca), hỗ trợ B phần publish trays khi xác nhận tiếp hàng.	Toàn bộ test xanh.
Server-B	Tiếp hàng (gợi ý, GET /staff, tạo phiếu, start, confirm, cancel) và báo cáo doanh thu.	Xác nhận phiếu thì tồn kho tăng, cảnh báo tự đóng, trays được publish lại.
Device	Hoàn thiện giao diện, chạy cùng lúc hai máy (M01, M02). Cộng báo cáo: sequence, activity, state, class diagram, bảng test case.	Hai máy chạy độc lập.
AdminApp	Tiếp hàng (Admin tạo phiếu; Nhân viên nhận và xác nhận), tra cứu đơn, báo cáo doanh thu.	Xác nhận phiếu thì tồn kho và cảnh báo đổi đúng.
Phối hợp: Server-B và AdminApp chốt JSON gợi ý và phiếu ở API.md mục B6 trước khi làm màn hình Tiếp hàng; hai bên phối hợp chặt suốt mốc này.
Ghép nối cuối mỗi mốc
Cả nhóm chạy thử đầu-cuối trên một máy, chụp ảnh kết quả cho báo cáo, ghi lỗi tìm được. Phần nào chưa kịp thì chuyển sang mốc sau, không kéo dài buổi ghép nối.

9. Cài Mosquitto (cả nhóm làm giống nhau)
Mỗi người cài Mosquitto trên máy mình, tạo ba file cấu hình đặt cùng thư mục (ví dụ thư mục cài đặt Mosquitto):

mosquitto.conf

text
listener 1883 localhost
allow_anonymous false
password_file passwd
acl_file acl
acl: dùng đúng nội dung ở File 1, BR-19.

Tạo tài khoản (nhập mật khẩu khi được hỏi; -c chỉ dùng cho tài khoản đầu tiên vì nó tạo mới file):

text
mosquitto_passwd -c passwd device
mosquitto_passwd passwd server
Chạy broker để xem log: mosquitto -c mosquitto.conf -v. listener 1883 localhost chỉ cho kết nối từ chính máy này (đủ để phát triển). Khi demo nhiều máy mới đổi sang cho phép kết nối từ ngoài và mở tường lửa.

10. Bẫy thường gặp khi code
10.1. Server không tự đọc appsettings.Local.json.
Mặc định .NET 8 chỉ nạp appsettings.json và appsettings.{Environment}.json. Thêm thủ công trong Program.cs sau các file mặc định:

csharp
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
10.2. WinForms (Device/App) không có WebApplication.CreateBuilder.
Device và App phải tự dựng cấu hình thủ công (cần gói Microsoft.Extensions.Configuration.Json):

csharp
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .Build();
Bẫy Visual Studio: Chọn từng file appsettings*.json → Properties → Copy to Output Directory = Copy if newer. Không làm bước này, file sẽ không nằm cạnh .exe, chạy lên báo lỗi không tìm thấy file.

10.3. Bẫy DateTimeOffset với MySQL.
MySqlConnector trả về System.DateTime, nếu khai báo DateTimeOffset sẽ lỗi InvalidCastException. Viết TypeHandler ép 2 chiều và đăng ký SqlMapper.AddTypeHandler(...).

Khi ghi: dùng value.DateTime (giờ treo tường +07:00), tuyệt đối không dùng UtcDateTime, nếu không hạn 120s/60s/30s sẽ bị lệch 7 tiếng.

Test kỹ các cột cho phép NULL (paid_at, completed_at...) với kiểu DateTimeOffset?.

Đoạn code mẫu:

csharp
public class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value) =>
        new(DateTime.SpecifyKind((DateTime)value, DateTimeKind.Unspecified), TimeSpan.FromHours(7));

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) =>
        parameter.Value = DateTime.SpecifyKind(value.DateTime, DateTimeKind.Unspecified);
}
value.DateTime giữ nguyên giờ treo tường +07:00, còn SpecifyKind chặn lỗi múi giờ kép.

10.4. DI khi gọi Service từ MqttService.
MqttService là Singleton, các Service/Repository là Scoped. Inject trực tiếp Scoped vào Singleton sẽ sập app. Chỉ inject IServiceScopeFactory, tạo scope mới cho mỗi message:

csharp
using var scope = _scopeFactory.CreateScope();
var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();
10.5. Giao tiếp một chiều: Dùng IDeviceNotifier.
Service không được biết MQTT là gì. Không inject thẳng MqttService vào ProductService. Tạo interface IDeviceNotifier (chứa PublishTraysAsync, PublishOrderStatusAsync), để MqttService implement nó. Các Service chỉ inject IDeviceNotifier.

csharp
builder.Services.AddSingleton<MqttService>();
builder.Services.AddSingleton<IDeviceNotifier>(sp => sp.GetRequiredService<MqttService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttService>());
10.6. Cấu hình JSON dùng chung REST và MQTT.
AddJsonOptions của Controller không tự áp dụng cho MQTT. Tạo 1 JsonDefaults.Options (dùng WhenWritingNull, CamelCase, UnsafeRelaxedJsonEscaping) và dùng chung cho cả 2 chỗ.
Lưu ý API.md: Vì bật WhenWritingNull, các trường null sẽ vắng mặt. Cần thêm dòng sau vào API.md mục 0: "Trường có giá trị null có thể vắng mặt trong JSON; người đọc coi trường vắng mặt như null."

10.7. WinForms (Device/App) phải deserialize đúng chuẩn Web.
Nếu không cấu hình, C# WinForms đọc JSON camelCase (orderId) vào property PascalCase (OrderId) sẽ ra 0 mà không báo lỗi. Bắt buộc dùng chung bộ JsonDefaults.Options từ mục 10.6 (hoặc dùng new JsonSerializerOptions(JsonSerializerDefaults.Web) nếu muốn tách biệt) để cả hệ thống thống nhất một quy ước JSON.

10.8. Đuôi .txt ẩn trên Windows.
Notepad lưu mosquitto.conf thành mosquitto.conf.txt. Tương tự với file acl và passwd. Bật View → Show → File name extensions để kiểm tra. Mosquitto sẽ báo lỗi "cannot open file" nếu dính bẫy này.
