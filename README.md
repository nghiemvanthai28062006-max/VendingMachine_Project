Hệ thống máy bán nước thông minh (mô phỏng)
Đồ án mô phỏng hệ thống quản lý chuỗi máy bán nước đóng chai tự động: khách mua qua máy, Server xử lý đơn/thanh toán/tồn kho/hoàn tiền/cảnh báo, Admin và Nhân viên quản lý qua ứng dụng.

1. Công nghệ
Server: ASP.NET Core Web API (.NET 8, C#), Dapper + MySqlConnector

Database: MySQL 8.0

Device Simulator: C# WinForms (.NET 8), MQTTnet 4.3.x

App quản lý: C# WinForms (.NET 8), REST + JWT

Broker: Mosquitto

Giao tiếp Device ↔ Server: MQTT, xác thực username/password

Giao tiếp App ↔ Server: HTTP/REST + JWT

IDE: Visual Studio Community 2022

2. Cấu trúc thư mục
Server/: ASP.NET Core Web API

Server/Controllers/: REST cho App

Server/Mqtt/: Handler cho từng topic MQTT

Server/Services/: Nghiệp vụ

Server/Repositories/: Câu SQL (Dapper)

Server/Models/: Entity và DTO

Server/Infrastructure/: DB, JWT, IClock, lỗi

Server/Database/: schema.sql, seed.sql

Server/appsettings.Local.example.json: File mẫu cấu hình cho Server

DeviceSimulator/: WinForms mô phỏng máy bán nước

DeviceSimulator/images/: Ảnh sản phẩm

DeviceSimulator/appsettings.Local.example.json: File mẫu cấu hình cho Device

AdminApp/: WinForms quản lý (Admin + Nhân viên)

AdminApp/appsettings.Local.example.json: File mẫu cấu hình cho App

Server.Tests/: xUnit test

Docs/: Tài liệu dự án

.gitignore: Danh sách file Git bỏ qua

README.md: File này

3. Phân công
Server: (điền tên)

DeviceSimulator: (điền tên)

AdminApp: (điền tên)

Tài liệu, kiểm thử, ghép nối: (điền tên)

Tài liệu dự án
Đọc theo thứ tự trước khi code:

Docs/01-pham-vi-va-luat-nghiep-vu.md: Luật của dự án. Gồm phạm vi, actor, use case, luật nghiệp vụ (BR-01 → BR-21), vòng đời đơn, quy ước, công nghệ. File này thắng khi mâu thuẫn với các file khác.

Docs/API.md: Hợp đồng giao tiếp. Gồm topic MQTT (Device ↔ Server) và endpoint REST (App ↔ Server), payload, mã lỗi.

Docs/02-quy-uoc-ky-thuat.md: Quy ước code. Gồm kiến trúc, chống tranh chấp, xử lý lỗi MQTT, cấu hình, thứ tự làm.

Docs/DB_sql.xlsx: Bảng thiết kế DB dạng bảng cho người đọc.

Server/Database/schema.sql và seed.sql: Script chạy DB.

4. Cài đặt môi trường
1. Cài .NET 8 SDK và Visual Studio
Tải Visual Studio Community 2022 tại: https://visualstudio.microsoft.com/vs/community/

Khi cài, tích 2 workload: ".NET desktop development" (cho WinForms) và "ASP.NET and web development" (cho Server).

Cài .NET 8 SDK tại: https://dotnet.microsoft.com/download/dotnet/8.0

Kiểm tra: mở Command Prompt, gõ dotnet --version, phải ra 8.x.x.

2. Cài MySQL 8.0
Tải MySQL Installer tại: https://dev.mysql.com/downloads/installer/

Chọn MySQL Server 8.0 + MySQL Workbench.

Đặt mật khẩu root, nhớ để điền vào cấu hình.

Mở MySQL Workbench, kết nối localhost, chạy lần lượt:

File → Open SQL Script → chọn Server/Database/schema.sql → bấm tia sét.

File → Open SQL Script → chọn Server/Database/seed.sql → bấm tia sét.

Kiểm tra: gõ SELECT * FROM vending.users;, phải thấy 3 tài khoản.

3. Cài Mosquitto
Tải Mosquitto tại: https://mosquitto.org/download/ (chọn bản Windows), cài mặc định.

Dừng dịch vụ mặc định: Mở Command Prompt bằng quyền Admin, gõ net stop mosquitto. (Nếu báo "service name is invalid" thì bỏ qua bước này).

Tạo thư mục C:\mosquitto-config và tạo 3 file trong đó. (Kiểm tra kỹ không để bị đuôi .txt ẩn, xem File 02 mục 10.8).

Nội dung file mosquitto.conf:

text
listener 1883 localhost
allow_anonymous false
password_file C:\mosquitto-config\passwd
acl_file C:\mosquitto-config\acl
Nội dung file acl:

text
user device
pattern write vending/%c/heartbeat
pattern write vending/%c/status
pattern write vending/%c/order/create
pattern write vending/%c/payment/callback
pattern write vending/%c/dispense/started
pattern write vending/%c/dispense/result
pattern read  vending/%c/trays
pattern read  vending/%c/order/status
pattern read  vending/%c/dispense/command

user server
topic readwrite vending/#
Tạo tài khoản: Mở Command Prompt bằng quyền Admin, chạy 2 lệnh sau (lệnh đầu tiên có -c để tạo file, nhập mật khẩu khi được hỏi):

text
"C:\Program Files\mosquitto\mosquitto_passwd.exe" -c C:\mosquitto-config\passwd device
"C:\Program Files\mosquitto\mosquitto_passwd.exe" C:\mosquitto-config\passwd server
Ghi lại 2 mật khẩu này để điền vào cấu hình Server và Device.

Chạy broker: Mở Command Prompt bằng quyền Admin (để tránh lỗi quyền), chạy:

text
"C:\Program Files\mosquitto\mosquitto.exe" -c C:\mosquitto-config\mosquitto.conf -v
Log hiện Opening ipv4 listen socket on port 1883 là OK. Giữ cửa sổ này mở trong lúc phát triển.

4. Cấu hình bí mật cục bộ
Mỗi project có cấu hình riêng. Mỗi người tự tạo file appsettings.Local.json đặt cạnh file appsettings.json trong thư mục của project mình. File này đã bị .gitignore, không commit.

Server: Copy từ Server/appsettings.Local.example.json. Điền mật khẩu MySQL root, mật khẩu MQTT của user server, JWT secret (chuỗi dài ≥ 32 ký tự).

DeviceSimulator: Copy từ DeviceSimulator/appsettings.Local.example.json. Điền mật khẩu MQTT của user device, mã máy (M01 hoặc M02).

AdminApp: Copy từ AdminApp/appsettings.Local.example.json. Kiểm tra lại URL của Server (mặc định http://localhost:5000/api).

Chạy dự án
Thứ tự chạy khi phát triển:

MySQL: chạy ngầm sau khi cài.

Mosquitto: Chạy lệnh trong Command Prompt quyền Admin (như mục 3.5).

Server: mở Server/ bằng Visual Studio, F5. Server lắng nghe REST ở http://localhost:5000 và kết nối broker MQTT.

DeviceSimulator: mở, chọn mã máy, bấm "Kết nối".

AdminApp: mở, đăng nhập bằng tài khoản seed.

Tài khoản demo
admin / Admin@123: Quản trị viên

staff1 / Staff@123: Nhân viên tiếp hàng

staff2 / Staff@123: Nhân viên tiếp hàng

Chỉ dùng để demo. Không có chức năng quản lý tài khoản trên App.

5. Quy ước quan trọng
Khi tài liệu mâu thuẫn: 01-pham-vi-va-luat-nghiep-vu.md thắng. Sửa file kia cho khớp.

Khi phát hiện tài liệu thiếu: dừng, hỏi nhóm, bổ sung tài liệu trước khi code.

Không commit bí mật: mật khẩu, JWT secret, connection string có mật khẩu, chỉ để trong appsettings.Local.json.

Không commit bin/, obj/, .vs/, logs/ (đã có trong .gitignore).

Trước khi push: chạy git status, đảm bảo không có file bí mật nào bị stage.

6. Thứ tự làm việc
Chia theo mốc M0 → M4, mỗi mốc là một lát dọc chạy được đầu-cuối (chi tiết ở Docs/02-quy-uoc-ky-thuat.md, mục 8).

M0: Khung project, kết nối DB, đăng nhập JWT.

M1: Nối MQTT, heartbeat, publish/subscribe trays.

M2: Luồng mua hàng thành công đầu-cuối.

M3: Nhánh lỗi: JAMMED, hết hạn, LWT, cảnh báo.

M4: Tiếp hàng, tra cứu đơn, báo cáo doanh thu.

Cuối mỗi mốc: cả nhóm ghép nối, chạy được luồng của mốc trên một máy, rồi sang mốc kế.

7. Xử lý sự cố thường gặp
Server không kết nối được MySQL: kiểm tra mật khẩu trong appsettings.Local.json của Server, kiểm tra MySQL đã chạy chưa.

Mosquitto từ chối kết nối hoặc báo lỗi port 1883: kiểm tra đã net stop mosquitto chưa, kiểm tra allow_anonymous false + file passwd có đúng user chưa.

Device không nhận trays: kiểm tra Server đã publish retained chưa, kiểm tra ACL cho phép read vending/%c/trays.

App báo 401: token JWT hết hạn (8 giờ), đăng nhập lại.

Port 5000 hoặc 1883 bị chiếm: đổi trong file cấu hình, không cần sửa code.

8. Các bước pull request
Code trong thư mục của mình, trên đúng nhánh của mình (ví dụ feature/server).

Commit và Push bằng Visual Studio: mở Git Changes, ghi một dòng mô tả, bấm Commit All, rồi bấm Push.

Mở pull request trên GitHub: tab Pull requests → New pull request, chọn base: main ← compare: nhánh của mình, bấm Create pull request.

Nhờ một bạn khác vào PR bấm Approve.

Gộp: bấm Merge pull request, chọn Create a merge commit, rồi Confirm. Không bấm Delete branch.

Kéo main về nhánh mình sau mỗi lần gộp: tạo PR base: nhánh của mình ← compare: main rồi bấm Merge. Nếu PR đang mở có nút Update branch thì bấm nút đó.

