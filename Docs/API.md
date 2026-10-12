# Hợp đồng giao tiếp — Hệ thống máy bán nước thông minh

**Trạng thái: BẢN NHÁP, bám theo `01-pham-vi-va-luat-nghiep-vu.md` (viết tắt: *File 1*).** Sau khi cả nhóm duyệt, file này là luật: muốn đổi topic, endpoint hoặc JSON phải báo cả nhóm và sửa file này **trước** khi sửa code. Khi hai file mâu thuẫn thì File 1 thắng, sửa `API.md` cho khớp.

Hai đường giao tiếp tách biệt:

| Đường | Ai với ai | Giao thức | Xác thực |
|---|---|---|---|
| **A** | Device ↔ Server | MQTT qua broker Mosquitto | username/password (`device`, `server`) + ACL `%c` |
| **B** | App quản lý ↔ Server | REST + JSON | JWT (`Authorization: Bearer <token>`) |

## 0. Quy ước chung

- JSON dùng `camelCase`, UTF-8. Tiền là số nguyên VNĐ. Thời gian ISO 8601 giờ Việt Nam, ví dụ `2026-10-12T09:30:00+07:00`.
- Trạng thái và loại là chuỗi tiếng Anh viết hoa, đúng như trong `schema.sql` (xem File 1, mục 7 và bảng `orders`, `alerts`...).
- `requestId` là UUID v4 do Device sinh **mới cho mỗi yêu cầu nghiệp vụ**. Gửi lại thì Server không xử lý lần hai mà publish lại `order/status` hiện tại của đơn; cách nhận ra bản trùng của từng topic xem BR-16.
- Server gọi `SweepStaleOrders()` / `CheckAndExpireOrder(orderId)` theo BR-20 ở đầu những xử lý được ghi chú **[lazy]** trong tài liệu này.

---

# PHẦN A — MQTT (Device ↔ Server)

## A1. Kết nối

| Mục | Giá trị |
|---|---|
| Broker | `localhost:1883` (đặt trong file cấu hình, không hard-code) |
| Tài khoản Device | `device` / mật khẩu trong file cấu hình |
| Tài khoản Server | `server` / mật khẩu trong file cấu hình |
| Client id của Device | **đúng bằng mã máy**, ví dụ `M01` (bắt buộc để ACL `%c` hoạt động, BR-19) |
| Last Will (Device) | topic `vending/{m}/status`, payload `{"online": false}`, QoS 1, không retained |
| Server subscribe | `vending/+/heartbeat`, `vending/+/status`, `vending/+/order/create`, `vending/+/payment/callback`, `vending/+/dispense/started`, `vending/+/dispense/result` |
| Device subscribe | `vending/{m}/trays`, `vending/{m}/order/status`, `vending/{m}/dispense/command` |

`{m}` là mã máy, ví dụ `M01`.

## A2. Bảng topic

| Topic | Hướng | QoS | Retained | Mục đích |
|---|---|---|---|---|
| `vending/{m}/heartbeat` | Device → Server | 0 | không | Báo còn sống, mỗi 15 giây (BR-12) |
| `vending/{m}/status` | Device → Broker (LWT) | 1 | không | Báo máy ngắt kết nối đột ngột |
| `vending/{m}/order/create` | Device → Server | 1 | không | Tạo đơn |
| `vending/{m}/order/status` | Server → Device | 1 | không | Phản hồi tạo đơn, và mọi lần đơn đổi trạng thái |
| `vending/{m}/payment/callback` | Device → Server | 1 | không | Nút giả lập thanh toán báo kết quả |
| `vending/{m}/dispense/command` | Server → Device | 1 | không | Lệnh nhả hàng |
| `vending/{m}/dispense/started` | Device → Server | 1 | không | Device xác nhận bắt đầu nhả |
| `vending/{m}/dispense/result` | Device → Server | 1 | không | Kết quả nhả (`OK`/`JAMMED`) |
| `vending/{m}/trays` | Server → Device | 1 | **có** | Danh sách khay, tồn kho, giá |

Vì QoS 1 có thể giao trùng, **cả hai phía phải xử lý trùng an toàn**: Server nhờ `requestId`; Device nhờ so `orderId` + `status` (nhận lại cùng một trạng thái thì bỏ qua).

## A3. Payload từng topic

### `vending/{m}/heartbeat` (Device → Server, QoS 0)
```json
{ "machineCode": "M01", "sentAt": "2026-10-12T09:30:00+07:00" }
```
Server: cập nhật `machines.last_heartbeat = now`. Device gửi ngay một heartbeat khi kết nối xong, rồi mỗi 15 giây.

### `vending/{m}/status` (LWT, do broker publish)
```json
{ "online": false }
```
Server: đặt `machines.last_heartbeat = NULL` (máy hiện `OFFLINE` ngay, BR-12).

### `vending/{m}/order/create` (Device → Server)
```json
{ "requestId": "b7e1c2d4-1f3a-4a5b-9c6d-7e8f9a0b1c2d", "machineCode": "M01", "trayCode": "A1" }
```
Server **[lazy: SweepStaleOrders]**, rồi kiểm tra theo BR-03 và BR-04. Hợp lệ thì tạo `orders` (`PENDING_PAYMENT`, `expires_at = created_at + 120s`, `amount` = giá hiện tại của sản phẩm) và một bản ghi `payments` (`PENDING`), rồi publish `order/status`. Trùng `requestId` thì publish lại kết quả cũ. Xử lý xong thì publish lại `trays` (số chai khả dụng đã đổi).

### `vending/{m}/order/status` (Server → Device)

Một topic, ba dạng phản hồi:

**1. Tạo đơn thành công** (có `requestId` của yêu cầu, có `qrContent`):
```json
{
  "requestId": "b7e1c2d4-1f3a-4a5b-9c6d-7e8f9a0b1c2d",
  "orderId": 1025,
  "status": "PENDING_PAYMENT",
  "amount": 5000,
  "expiresAt": "2026-10-12T09:32:00+07:00",
  "qrContent": "VENDING|M01|1025|5000"
}
```

**2. Yêu cầu bị từ chối** (có `requestId` và `error`, **không có** `orderId`):
```json
{
  "requestId": "b7e1c2d4-1f3a-4a5b-9c6d-7e8f9a0b1c2d",
  "error": { "code": "TRAY_EMPTY", "message": "Khay A1 đã hết hàng" }
}
```

**3. Đơn đổi trạng thái** (`orderId` + `status`; có `requestId` nếu là phản hồi của một yêu cầu Device vừa gửi; do Server tự phát, như dọn đơn quá hạn, thì không có):
```json
{ "requestId": "0a1b2c3d-...", "orderId": 1025, "status": "PAID" }
```
Khi đơn thành `REFUNDED`, kèm thêm lý do và số tiền hoàn:
```json
{ "orderId": 1025, "status": "REFUNDED", "reason": "JAMMED", "refundAmount": 5000 }
```
`reason` nhận: `JAMMED` (Device báo kẹt), `DISPENSE_TIMEOUT` (quá 30s không có kết quả, BR-08), `DEVICE_NO_ACK` (quá 60s Device chưa xác nhận nhả, BR-07).

Bảng các trường:

| Trường | Có khi | Ghi chú |
|---|---|---|
| `requestId` | phản hồi một yêu cầu | để Device ghép phản hồi với yêu cầu |
| `orderId` | đơn đã tồn tại | vắng khi tạo đơn bị từ chối |
| `status` | đơn đã tồn tại | một trong 8 trạng thái ở File 1, mục 7 |
| `amount`, `expiresAt`, `qrContent` | `status = PENDING_PAYMENT` ở lần tạo đơn | |
| `reason`, `refundAmount` | `status = REFUNDED` | |
| `error.code`, `error.message` | bị từ chối | mã ở mục A5 |

Mỗi lần đơn đổi trạng thái, Server publish một message (File 1, mục 7). Khi nhả hàng lỗi, Server publish **hai** message liên tiếp: `DISPENSE_FAILED` rồi `REFUNDED`.

### `vending/{m}/payment/callback` (Device → Server)
Đây là nút "giả lập thanh toán" trong bảng điều khiển kịch bản của Device. `transactionCode` do nút này sinh, không được trùng.
```json
{
  "requestId": "5c4d3e2f-...",
  "orderId": 1025,
  "transactionCode": "TXN-20261012-0001",
  "amount": 5000,
  "result": "SUCCESS"
}
```
`result`: `SUCCESS` hoặc `FAILED`. Server **[lazy: CheckAndExpireOrder]**, rồi xử lý theo BR-05 và BR-06:

| Tình huống | Kết quả |
|---|---|
| Đơn không thuộc máy này | `error: ORDER_NOT_FOUND` |
| Đơn đã `EXPIRED` (đến sau hạn) | publish `status: EXPIRED`, không nhận tiền (`payments` của đơn đã được chuyển `FAILED` lúc hết hạn) |
| `transactionCode` trùng với mã đã ghi cho **chính đơn này** (Device gửi trùng) | publish lại `order/status` hiện tại, không lỗi, không xử lý lần hai |
| Đơn không còn `PENDING_PAYMENT` (trạng thái khác) | `error: INVALID_STATE` |
| `transactionCode` đã dùng cho **đơn khác** | `error: DUPLICATE_TRANSACTION` |
| `result = FAILED`, hoặc `amount` khác `orders.amount` | `payments.status = FAILED`, đơn `PAYMENT_FAILED` |
| `result = SUCCESS` và đúng số tiền | `payments.status = SUCCESS` (ghi `transaction_code`, `paid_at`), đơn `PAID`; publish `order/status` rồi publish `dispense/command` |

### `vending/{m}/dispense/command` (Server → Device)
```json
{ "orderId": 1025, "trayCode": "A1" }
```
Gửi ngay sau khi đơn thành `PAID`. Device phải gửi `dispense/started` trong vòng **60 giây** (BR-07).

### `vending/{m}/dispense/started` (Device → Server)
```json
{ "requestId": "9a8b7c6d-...", "orderId": 1025 }
```
Server **[lazy: CheckAndExpireOrder]**. Đơn đang `PAID` thì chuyển `DISPENSING`, ghi `dispense_started_at = now`, publish `order/status`. Đơn **đã** `DISPENSING` (Device gửi trùng) thì chỉ publish lại `order/status` hiện tại, không lỗi, không ghi lại `dispense_started_at`. Trạng thái khác (ví dụ đã quá 60s nên bị `DISPENSE_FAILED`) thì `error: INVALID_STATE`.

### `vending/{m}/dispense/result` (Device → Server)
```json
{ "requestId": "9f8e7d6c-...", "orderId": 1025, "trayCode": "A1", "result": "JAMMED" }
```
`result` chỉ nhận `OK` hoặc `JAMMED`. Server **[lazy: CheckAndExpireOrder]** trước khi xử lý, rồi ghi `requestId` vào `orders.dispense_request_id` (trùng thì publish lại `order/status` hiện tại). Chỉ hợp lệ khi đơn đang `DISPENSING`; sai khay thì `error: TRAY_MISMATCH`. Nếu đơn đã quá 30s kể từ `dispense_started_at` thì đã bị chuyển `DISPENSE_FAILED`/`REFUNDED` (BR-08), nên kết quả đến muộn bị trả `error: INVALID_STATE`, kể cả khi là `OK`.

| `result` | Server làm (trong một transaction nếu có ghi nhiều bảng) | Publish |
|---|---|---|
| `OK` | đơn `COMPLETED` (`completed_at`), `trays.quantity` − 1 (BR-09); tạo cảnh báo `OUT_OF_STOCK` hoặc `LOW_STOCK` nếu cần (BR-11) | `order/status` (`COMPLETED`), rồi `trays` |
| `JAMMED` | đơn `DISPENSE_FAILED`, tạo cảnh báo `JAMMED` cho khay, tạo `refunds` (`PENDING` → `COMPLETED`), đơn `REFUNDED` (BR-10) | `order/status` (`DISPENSE_FAILED`, rồi `REFUNDED` kèm `reason: "JAMMED"`), rồi `trays` |

### `vending/{m}/trays` (Server → Device, **retained**, QoS 1)
```json
{
  "machineCode": "M01",
  "machineStatus": "ACTIVE",
  "updatedAt": "2026-10-12T09:30:00+07:00",
  "trays": [
    {
      "trayCode": "A1",
      "product": { "name": "Nước suối", "volumeMl": 500, "price": 5000, "imageUrl": "/images/nuoc-suoi.png" },
      "quantity": 8,
      "available": true
    },
    { "trayCode": "B1", "product": { "name": "Pepsi", "volumeMl": 330, "price": 12000, "imageUrl": "/images/pepsi.png" }, "quantity": 0, "available": false },
    { "trayCode": "B4", "product": null, "quantity": 0, "available": false }
  ]
}
```
- `available = true` khi: máy `ACTIVE`, khay có sản phẩm đang bán, số chai khả dụng > 0 (BR-03) và khay không có cảnh báo `JAMMED` chưa xử lý (BR-04).
- `imageUrl` là đường dẫn tương đối; Device tìm ảnh trong `DeviceSimulator/images/`.
- **Server publish lại topic này khi:** khởi động; đơn `COMPLETED`; xác nhận tiếp hàng; tạo đơn mới; dọn đơn quá hạn làm đổi số chai khả dụng; Admin đổi giá, gán sản phẩm, đổi sức chứa/ngưỡng khay hoặc trạng thái máy; cảnh báo `JAMMED` được đóng (File 1, mục 8).

## A4. Luồng mua hàng tóm tắt (để vẽ sequence)

**Thành công**
1. Device khởi động, subscribe → nhận `trays` (retained) → vẽ giao diện.
2. Khách chọn khay → Device publish `order/create`.
3. Server tạo đơn + `payments` (`PENDING`) → `order/status` (`PENDING_PAYMENT`, có `qrContent`). Device hiện QR và tự đếm ngược theo `expiresAt`.
4. Nút giả lập → `payment/callback` (`SUCCESS`) → đơn `PAID`; Server publish `order/status` rồi `dispense/command`.
5. Device → `dispense/started` → `DISPENSING`.
6. Cảm biến quang giả lập → `dispense/result` (`OK`) → `COMPLETED`, tồn kho − 1, `trays` được publish lại.

**Kẹt hàng:** như trên đến bước 5, rồi `dispense/result` (`JAMMED`) → `DISPENSE_FAILED` → hoàn tiền → `REFUNDED`; cảnh báo `JAMMED` khóa khay cho đến khi Nhân viên xử lý (BR-21).

**Hết hạn thanh toán:** khách không trả trong 120s. Device tự về màn hình chính. Lần nào Server chạy `SweepStaleOrders()` tiếp theo thì đơn thành `EXPIRED`. Nếu `payment/callback` đến muộn, Server trả `status: EXPIRED`.

**Máy mất kết nối:** broker publish LWT → `last_heartbeat = NULL` → App thấy máy `OFFLINE`.

## A5. Mã lỗi trong `order/status`

| `error.code` | Khi nào |
|---|---|
| `VALIDATION_ERROR` | Thiếu hoặc sai trường (thiếu `requestId`, `result` không hợp lệ...) |
| `MACHINE_NOT_FOUND` | Mã máy không tồn tại |
| `MACHINE_NOT_ACTIVE` | Máy đang `MAINTENANCE` (BR-04) |
| `TRAY_NOT_FOUND` | Không có khay với mã đó |
| `TRAY_EMPTY` | Không còn chai khả dụng (BR-03) |
| `TRAY_BLOCKED` | Khay có cảnh báo `JAMMED` chưa xử lý (BR-04) |
| `PRODUCT_INACTIVE` | Khay chưa gán sản phẩm hoặc sản phẩm đã ngừng bán (BR-04) |
| `ORDER_NOT_FOUND` | Không có đơn đó của máy này |
| `INVALID_STATE` | Chuyển trạng thái không hợp lệ (File 1, mục 7) |
| `TRAY_MISMATCH` | `trayCode` không khớp đơn |
| `DUPLICATE_TRANSACTION` | `transactionCode` đã được dùng |

---

# PHẦN B — REST (App quản lý ↔ Server)

## B1. Quy ước

- Base URL: `http://localhost:5000/api` (đặt trong file cấu hình App).
- Cột **Quyền**: **A** = chỉ Admin, **S** = Admin và Nhân viên (BR-17).
- Danh sách có phân trang: `?page=1&pageSize=20`; response dạng `{ "items": [ ... ], "page": 1, "pageSize": 20, "total": 57 }`.
- Lỗi có dạng:
```json
{ "error": { "code": "TRAY_EMPTY", "message": "Khay A1 đã hết hàng" } }
```
- Mã HTTP: `200` OK, `201` tạo mới, `400` dữ liệu sai, `401` chưa đăng nhập, `403` không đủ quyền, `404` không tồn tại, `409` sai trạng thái hoặc trùng.

## B2. Đăng nhập (UC01)

### POST `/auth/login`
```json
{ "username": "admin", "password": "Admin@123" }
```
Response `200`:
```json
{
  "token": "eyJhbGciOi...",
  "expiresAt": "2026-10-12T17:30:00+07:00",
  "user": { "id": 1, "username": "admin", "fullName": "Quản trị viên", "role": "ADMIN" }
}
```
Lỗi: `401 INVALID_CREDENTIALS`, `403 ACCOUNT_DISABLED` (`users.is_active = 0`). Không có API quản lý tài khoản (tài khoản lấy từ `seed.sql`).

## B3. Sản phẩm (UC05)

| Method | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/products` | S | Danh sách sản phẩm |
| POST | `/products` | A | Thêm sản phẩm |
| PUT | `/products/{id}` | A | Sửa tên, dung tích, giá, ảnh |
| PATCH | `/products/{id}/active` | A | Ngừng hoặc mở bán, body `{ "isActive": false }` |

Body `POST /products`:
```json
{ "name": "Nước suối", "volumeMl": 500, "price": 5000, "imageUrl": "/images/nuoc-suoi.png" }
```
Đổi giá không ảnh hưởng đơn cũ (BR-02). Sau khi sửa giá, ngừng/mở bán thì Server publish lại `trays` cho các máy liên quan.

## B4. Máy và khay (UC02)

| Method | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/machines` | S | Danh sách máy kèm trạng thái kết nối |
| POST | `/machines` | A | Thêm máy |
| PUT | `/machines/{id}` | A | Sửa tên, vị trí, `status` (`ACTIVE`/`MAINTENANCE`) |
| GET | `/machines/{id}/trays` | S | Danh sách khay của máy |
| POST | `/machines/{id}/trays` | A | Thêm khay |
| PUT | `/trays/{id}` | A | Gán sản phẩm, sức chứa, ngưỡng cảnh báo |

Một phần tử trong `GET /machines`:
```json
{
  "id": 1, "code": "M01", "name": "Máy sảnh A1", "location": "Sảnh tòa A1",
  "status": "ACTIVE",
  "lastHeartbeat": "2026-10-12T09:29:50+07:00",
  "connection": "ONLINE"
}
```
`connection` là giá trị **tính ra lúc đọc**: `OFFLINE` khi `lastHeartbeat` là `NULL` hoặc cách hiện tại hơn 60 giây, ngược lại `ONLINE` (BR-12). Không lưu trong DB.

Body `POST /machines/{id}/trays`:
```json
{ "code": "A1", "productId": 1, "quantity": 8, "capacity": 10, "lowStockThreshold": 2 }
```
`quantity` chỉ dùng được lúc **tạo khay** (nạp ban đầu, mặc định 0). `PUT /trays/{id}` **không** sửa `quantity`; sau đó số lượng chỉ đổi qua đơn hàng và tiếp hàng ("chỉ Server được sửa").
Lỗi: `409 DUPLICATE_CODE` (trùng mã máy hoặc mã khay trong cùng máy), `400 VALIDATION_ERROR`.
Mọi thay đổi ở nhóm này (thêm khay, gán sản phẩm, đổi sức chứa/ngưỡng, đổi trạng thái máy) làm Server publish lại `trays` của máy đó.

## B5. Cảnh báo (UC07)

| Method | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/alerts?resolved=false&machineId=&type=` | S | Danh sách cảnh báo |
| POST | `/alerts/{id}/resolve` | S | Đánh dấu đã xử lý (BR-21) |

`GET /alerts` trả cảnh báo lưu trong bảng `alerts` **cộng thêm** cảnh báo `MACHINE_OFFLINE` ảo cho mỗi máy đang `OFFLINE` (tính ra lúc đọc, không ghi bảng, BR-12). Cảnh báo ảo có `id` là `null` và `virtual: true`:
```json
{ "id": null, "machineId": 2, "trayId": null, "type": "MACHINE_OFFLINE",
  "message": "Máy M02 mất kết nối", "isResolved": false, "virtual": true }
```
`POST /alerts/{id}/resolve` ghi `resolved_at = now`, `resolved_by` = người đăng nhập. Lỗi: `404 ALERT_NOT_FOUND`, `409 INVALID_STATE` (đã xử lý rồi). Đóng cảnh báo `JAMMED` thì khay bán lại được (BR-04) và Server publish lại `trays`.

## B6. Tiếp hàng (UC03, UC08)

| Method | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/restock/suggestions` | A | Máy cần tiếp hàng, sắp theo độ khẩn (BR-15) |
| GET | `/staff` | A | Danh sách nhân viên để chọn khi tạo phiếu |
| POST | `/restock-tickets` | A | Tạo phiếu cho một máy (BR-13) |
| GET | `/restock-tickets?status=&machineId=` | S | Nhân viên chỉ thấy phiếu của mình, Admin thấy tất cả |
| GET | `/restock-tickets/{id}` | S | Chi tiết phiếu và các khay |
| POST | `/restock-tickets/{id}/start` | S | Nhận phiếu: `OPEN` → `IN_PROGRESS` |
| POST | `/restock-tickets/{id}/confirm` | S | Xác nhận đã châm hàng (BR-14) |
| POST | `/restock-tickets/{id}/cancel` | A | Hủy phiếu: → `CANCELLED` |

`GET /restock/suggestions` (mỗi phần tử là một máy, máy khẩn nhất đứng đầu: nhiều khay hết hàng trước, rồi đến sắp hết):
```json
{
  "machineId": 1, "machineCode": "M01", "outOfStockTrays": 1, "lowStockTrays": 1,
  "trays": [ { "trayId": 4, "trayCode": "B1", "productName": "Pepsi", "quantity": 0, "capacity": 10, "plannedQty": 10 } ]
}
```
`POST /restock-tickets`:
```json
{ "machineId": 1, "staffId": 2, "note": "Châm trước 10h" }
```
Server tự lấy các khay của máy có `quantity` ≤ `low_stock_threshold`, `planned_qty = capacity − quantity` (BR-13). Lỗi: `409 NOTHING_TO_RESTOCK` (không khay nào cần châm).

`POST /restock-tickets/{id}/confirm`:
```json
{ "items": [ { "trayId": 3, "actualQty": 9 }, { "trayId": 4, "actualQty": 10 } ] }
```
Server: `quantity += actualQty` cho từng khay, ghi `actual_qty`, phiếu `DONE` (`completed_at`), đóng các cảnh báo `LOW_STOCK`/`OUT_OF_STOCK` của khay đó (`resolved_by` = người đăng nhập), rồi publish lại `trays`. Lỗi: `400 EXCEEDS_CAPACITY` (châm quá sức chứa), `409 INVALID_STATE` (phiếu đã `DONE` hoặc `CANCELLED`), `403 FORBIDDEN` (Nhân viên không phải người được giao).

## B7. Đơn hàng (UC06)

| Method | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/orders?from=&to=&status=&machineId=&page=&pageSize=` | S | Tra cứu đơn hàng **[lazy: SweepStaleOrders]** |
| GET | `/orders/{id}` | S | Chi tiết đơn kèm thanh toán và hoàn tiền **[lazy: CheckAndExpireOrder]** |

`GET /orders/{id}`:
```json
{
  "id": 1025, "machineCode": "M01", "trayCode": "A1", "productName": "Nước suối",
  "amount": 5000, "status": "REFUNDED",
  "createdAt": "2026-10-12T09:30:00+07:00", "paidAt": "2026-10-12T09:30:20+07:00",
  "payment": { "status": "SUCCESS", "method": "QR_SIM", "transactionCode": "TXN-20261012-0001" },
  "refund": { "status": "COMPLETED", "amount": 5000, "reason": "JAMMED" }
}
```
App chỉ **đọc** đơn hàng, không tạo hay sửa trạng thái đơn (việc đó thuộc Device ↔ Server).

## B8. Báo cáo doanh thu (UC04)

### GET `/reports/revenue?from=&to=&groupBy=day` — quyền **A**
`groupBy` nhận `day`, `machine` hoặc `product`. **[lazy: SweepStaleOrders]** chạy trước khi tính.
```json
{
  "from": "2026-10-01", "to": "2026-10-07", "groupBy": "day",
  "totalRevenue": 245000, "totalOrders": 31, "refundedAmount": 24000,
  "rows": [ { "key": "2026-10-01", "orders": 5, "revenue": 42000 } ]
}
```
Doanh thu chỉ tính đơn `COMPLETED`; đơn `REFUNDED` không tính. `refundedAmount` là tổng các `refunds` ở trạng thái `COMPLETED` trong kỳ.

## B9. Mã lỗi REST

| Mã | HTTP | Ý nghĩa |
|---|---|---|
| `INVALID_CREDENTIALS` | 401 | Sai tài khoản hoặc mật khẩu |
| `ACCOUNT_DISABLED` | 403 | Tài khoản bị khóa |
| `FORBIDDEN` | 403 | Không đủ quyền (BR-17) |
| `VALIDATION_ERROR` | 400 | Thiếu hoặc sai trường dữ liệu |
| `MACHINE_NOT_FOUND`, `TRAY_NOT_FOUND`, `PRODUCT_NOT_FOUND`, `ALERT_NOT_FOUND`, `TICKET_NOT_FOUND`, `ORDER_NOT_FOUND` | 404 | Không tìm thấy |
| `DUPLICATE_CODE` | 409 | Trùng mã máy hoặc mã khay |
| `INVALID_STATE` | 409 | Chuyển trạng thái không hợp lệ |
| `NOTHING_TO_RESTOCK` | 409 | Không có khay nào cần châm |
| `EXCEEDS_CAPACITY` | 400 | Châm vượt sức chứa khay |

---

# PHẦN C — Đối chiếu use case và giao tiếp

| Use case | Giao tiếp |
|---|---|
| UC01 Đăng nhập | B2 |
| UC02 Quản lí máy và khay | B4 |
| UC03 Tạo phiếu tiếp hàng | B6 (`/restock/suggestions`, `/staff`, `POST /restock-tickets`) |
| UC04 Xem báo cáo doanh thu | B8 |
| UC05 Quản lí danh mục sản phẩm | B3 |
| UC06 Tra cứu đơn hàng | B7 |
| UC07 Xem và xử lí cảnh báo | B5 |
| UC08 Xác nhận châm hàng | B6 (`start`, `confirm`) |
| UC09 Mua hàng và thanh toán | A3: `order/create`, `order/status`, `payment/callback` |
| UC10 Thực thi nhả hàng | A3: `dispense/command`, `dispense/started`, `dispense/result` |
| UC11 Báo cáo trạng thái | A3: `heartbeat`, `status` (LWT) |
| UC12 Xử lí hoàn tiền | Nội bộ Server, kích hoạt trong `dispense/result` (`JAMMED`) hoặc `SweepStaleOrders()` |

## Các quyết định đã chọn

Các điểm trước đây để ngỏ đã được chốt theo nguyên tắc: ưu tiên cách đơn giản; nếu khó hơn một chút mà kết quả đẹp hơn thì chọn cách đó; quá khó thì bỏ.

1. **`reason` của `REFUNDED`:** giữ 3 giá trị `JAMMED`, `DISPENSE_TIMEOUT`, `DEVICE_NO_ACK`. Chỉ tốn thêm một chuỗi, đổi lại Device hiển thị được thông báo rõ nguyên nhân cho khách.
2. **Endpoint phụ:** giữ `GET /staff`, `POST /restock-tickets/{id}/start`, `POST /restock-tickets/{id}/cancel`. Mỗi cái chỉ vài dòng code, và cần để vòng đời phiếu (`OPEN` → `IN_PROGRESS` → `DONE`/`CANCELLED`) đủ đường đi, vẽ state diagram mới không bị thiếu.
3. **`quantity` ban đầu khi tạo khay:** có. Nếu không thì không có cách nạp hàng lần đầu.
4. **Cổng:** `5000` cho REST của Server, `1883` cho Mosquitto. Trùng với chương trình khác trên máy thì đổi trong file cấu hình, không cần sửa code.
