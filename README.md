# VendingMachine_Project
Hệ thống máy bán nước thông minh (mô phỏng)
Đồ án mô phỏng hệ thống quản lý chuỗi máy bán nước đóng chai tự động: khách mua qua máy, Server xử lý đơn/thanh toán/tồn kho/hoàn tiền/cảnh báo, Admin và Nhân viên quản lý qua ứng dụng.

Công nghệ
Thành phần	Công nghệ
Server	ASP.NET Core Web API (.NET 8, C#), Dapper + MySqlConnector
Database	MySQL 8.0
Device Simulator	C# WinForms (.NET 8), MQTTnet 4.3.x
App quản lý	C# WinForms (.NET 8), REST + JWT
Broker	Mosquitto
Giao tiếp Device ↔ Server	MQTT, xác thực username/password
Giao tiếp App ↔ Server	HTTP/REST + JWT
IDE	Visual Studio Community 2022
Cấu trúc thư mục
text
.
├── Server/                 # ASP.NET Core Web API
│   ├── Controllers/        # REST cho App
│   ├── Mqtt/               # Handler cho từng topic MQTT
│   ├── Services/           # Nghiệp vụ
│   ├── Repositories/       # Câu SQL (Dapper)
│   ├── Models/             # Entity và DTO
│   ├── Infrastructure/     # DB, JWT, IClock, lỗi
│   └── Database/           # schema.sql, seed.sql
├── DeviceSimulator/        # WinForms mô phỏng máy bán nước
│   └── images/             # Ảnh sản phẩm
├── AdminApp/               # WinForms quản lý (Admin + Nhân viên)
├── Server.Tests/           # xUnit test
├── Docs/                   # Tài liệu dự án
│   ├── 01-pham-vi-va-luat-nghiep-vu.md
│   ├── 02-quy-uoc-ky-thuat.md
│   ├── API.md
│   └── DB_sql.xlsx
├── appsettings.Local.example.json
├── .gitignore
└── README.md
Phân công
Thành viên	Phụ trách
          	Server/
          	DeviceSimulator/
          	AdminApp/
          	Tài liệu, kiểm thử, ghép nối
Tài liệu dự án
Đọc theo thứ tự trước khi code:

Docs/01-pham-vi-va-luat-nghiep-vu.md — Luật của dự án: phạm vi, actor, use case, luật nghiệp vụ (BR-01 → BR-21), vòng đời đơn, quy ước, công nghệ. File này thắng khi mâu thuẫn với các file khác.

Docs/API.md — Hợp đồng giao tiếp: topic MQTT (Device ↔ Server) và endpoint REST (App ↔ Server), payload, mã lỗi.

Docs/02-quy-uoc-ky-thuat.md — Quy ước code: kiến trúc, chống tranh chấp, xử lý lỗi MQTT, cấu hình, thứ tự làm.

Docs/DB_sql.xlsx — Bảng thiết kế DB dạng bảng cho người đọc.

Server/Database/schema.sql và seed.sql — Script chạy DB.

Cài đặt môi trường
1. Cài .NET 8 SDK và Visual Studio
Tải Visual Studio Community 2022 tại https://visualstudio.microsoft.com/vs/community/

Khi cài, tích 2 workload:

.NET desktop development (cho WinForms)

ASP.NET and web development (cho Server)

Cài .NET 8 SDK tại https://dotnet.microsoft.com/download/dotnet/8.0

Kiểm tra: mở Command Prompt, gõ dotnet --version → phải ra 8.x.x.

2. Cài MySQL 8.0
Tải MySQL Installer tại https://dev.mysql.com/downloads/installer/

Chọn MySQL Server 8.0 + MySQL Workbench.

Đặt mật khẩu root (nhớ để điền vào cấu hình).

Mở MySQL Workbench, kết nối localhost, chạy lần lượt:

File → Open SQL Script → chọn Server/Database/schema.sql → bấm tia sét.
File → Open SQL Script → chọn Server/Database/seed.sql → bấm tia sét.
Kiểm tra: gõ SELECT * FROM vending.users; → phải thấy 3 tài khoản.

3. Cài Mosquitto
Tải Mosquitto tại https://mosquitto.org/download/ (chọn bản Windows).

Cài mặc định, ví dụ vào C:\Program Files\mosquitto\.

Tạo 3 file cấu hình trong thư mục cài (theo File 02, mục 9):

mosquitto.conf

text
listener 1883 localhost
allow_anonymous false
password_file passwd
acl_file acl
acl

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
Tạo tài khoản (mở Command Prompt với quyền Admin, cd vào thư mục Mosquitto):

text
mosquitto_passwd -c passwd device
mosquitto_passwd passwd server
Nhập mật khẩu khi được hỏi. Ghi lại 2 mật khẩu này để điền vào cấu hình Server và Device.

Chạy broker để kiểm tra:

text
mosquitto -c mosquitto.conf -v
Log hiện Opening ipv4 listen socket on port 1883 là OK. Giữ cửa sổ này mở trong lúc phát triển.

4. Cấu hình bí mật cục bộ
Mỗi người tự tạo file appsettings.Local.json trên máy mình (đã bị .gitignore, không commit).

Copy từ appsettings.Local.example.json rồi điền mật khẩu thật:

Server: mật khẩu MySQL root, mật khẩu MQTT của user server, JWT secret (chuỗi dài ≥ 32 ký tự).

DeviceSimulator: mật khẩu MQTT của user device, mã máy (M01 hoặc M02).

AdminApp: URL của Server (mặc định http://localhost:5000/api).

Chạy dự án
Thứ tự chạy khi phát triển:

MySQL — chạy ngầm sau khi cài.

Mosquitto — mosquitto -c mosquitto.conf -v.

Server — mở Server/ bằng Visual Studio, F5. Server lắng nghe REST ở http://localhost:5000 và kết nối broker MQTT.

DeviceSimulator — mở, chọn mã máy, bấm Kết nối.

AdminApp — mở, đăng nhập bằng tài khoản seed.

Tài khoản demo
Username	  Password	  Vai trò
admin	      Admin@123	  Quản trị viên
staff1	    Staff@123  	Nhân viên tiếp hàng
staff2	    Staff@123	  Nhân viên tiếp hàng
Chỉ dùng để demo. Không có chức năng quản lý tài khoản trên App.

Quy ước quan trọng
Khi tài liệu mâu thuẫn: 01-pham-vi-va-luat-nghiep-vu.md thắng. Sửa file kia cho khớp.

Khi phát hiện tài liệu thiếu: dừng, hỏi nhóm, bổ sung tài liệu trước khi code.

Không commit bí mật: mật khẩu, JWT secret, connection string có mật khẩu → chỉ để trong appsettings.Local.json.

Không commit bin/, obj/, .vs/, logs/ (đã có trong .gitignore).

Trước khi push: chạy git status, đảm bảo không có file bí mật nào bị stage.

Thứ tự làm việc
Chia theo mốc M0 → M4, mỗi mốc là một lát dọc chạy được đầu-cuối (chi tiết ở Docs/02-quy-uoc-ky-thuat.md, mục 8).

Mốc	   Nội dung
M0	   Khung project, kết nối DB, đăng nhập JWT
M1	   Nối MQTT, heartbeat, publish/subscribe trays
M2	   Luồng mua hàng thành công đầu-cuối
M3	   Nhánh lỗi: JAMMED, hết hạn, LWT, cảnh báo
M4	   Tiếp hàng, tra cứu đơn, báo cáo doanh thu
Cuối   mỗi mốc: cả nhóm ghép nối, chạy được luồng của mốc trên một máy, rồi sang mốc kế.

Xử lý sự cố thường gặp
Vấn đề	    Cách xử lý
Server      không kết nối được MySQL	Kiểm tra mật khẩu trong appsettings.Local.json, MySQL đã chạy chưa
Mosquitto   từ chối kết nối	Kiểm tra allow_anonymous false + file passwd có đúng user chưa
Device      không nhận trays	Kiểm tra Server đã publish retained chưa, kiểm tra ACL cho phép read vending/%c/trays
App báo 401	Token JWT hết hạn (8 giờ), đăng nhập lại
Port 5000 hoặc 1883 bị chiếm	Đổi trong file cấu hình, không cần sửa code
