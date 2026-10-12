-- =====================================================================
-- Dữ liệu mẫu để demo. Chạy SAU schema.sql.
-- Muốn reset demo: chạy lại schema.sql rồi seed.sql.
--
-- Tài khoản demo (CHỈ DÙNG ĐỂ DEMO, mật khẩu đã băm BCrypt cost 10):
--   admin   / Admin@123   (ADMIN)
--   staff1  / Staff@123   (STAFF)
--   staff2  / Staff@123   (STAFF)   (để demo phân công phiếu tiếp hàng)
-- Không có chức năng quản lý tài khoản trên App (đã loại khỏi phạm vi).
-- =====================================================================

USE vending;
SET time_zone = '+07:00';

-- ---------------------------------------------------------------------
-- users
-- ---------------------------------------------------------------------
INSERT INTO users (id, username, password_hash, full_name, role) VALUES
(1, 'admin',  '$2b$10$.Tp3qfWS4HnCdty6lSk9oO5GcGCEfl.ZaNGQ2igFpLcZCcUYB/8z6', 'Quản trị viên',          'ADMIN'),
(2, 'staff1', '$2b$10$Cu8lq35i8gIZEVuOqrFtdOrkylCBRZoLEOEB.9ZAdegDnJP2.rj3K', 'Nhân viên tiếp hàng 1',  'STAFF'),
(3, 'staff2', '$2b$10$Cu8lq35i8gIZEVuOqrFtdOrkylCBRZoLEOEB.9ZAdegDnJP2.rj3K', 'Nhân viên tiếp hàng 2',  'STAFF');

-- ---------------------------------------------------------------------
-- machines
-- last_heartbeat = NOW() chỉ để máy hiện ONLINE lúc vừa nạp dữ liệu.
-- Quá 60 giây không có heartbeat thì Server tính ra OFFLINE (BR-12);
-- Device Simulator chạy lên sẽ gửi heartbeat lại.
-- ---------------------------------------------------------------------
INSERT INTO machines (id, code, name, location, status, last_heartbeat) VALUES
(1, 'M01', 'Máy sảnh A1',  'Sảnh tòa A1',       'ACTIVE', NOW()),
(2, 'M02', 'Máy thư viện', 'Thư viện Tạ Quang Bửu', 'ACTIVE', NOW());

-- ---------------------------------------------------------------------
-- products (ảnh để trong DeviceSimulator/images/)
-- ---------------------------------------------------------------------
INSERT INTO products (id, name, volume_ml, price, image_url) VALUES
(1, 'Nước suối',   500,  5000, '/images/nuoc-suoi.png'),
(2, 'Trà xanh',    455, 10000, '/images/tra-xanh.png'),
(3, 'Coca-Cola',   330, 12000, '/images/coca.png'),
(4, 'Pepsi',       330, 12000, '/images/pepsi.png'),
(5, 'Nước cam',    350, 15000, '/images/nuoc-cam.png'),
(6, 'Cà phê lon',  235, 15000, '/images/ca-phe-lon.png');

-- ---------------------------------------------------------------------
-- trays: có khay sắp hết (M01-A3, M02-A3) và hết hàng (M01-B1)
-- để demo cảnh báo và tiếp hàng. Ngưỡng cảnh báo mặc định 2.
-- ---------------------------------------------------------------------
INSERT INTO trays (id, machine_id, code, product_id, quantity, capacity, low_stock_threshold) VALUES
(1,  1, 'A1', 1,  8, 10, 2),
(2,  1, 'A2', 2,  5, 10, 2),
(3,  1, 'A3', 3,  1, 10, 2),
(4,  1, 'B1', 4,  0, 10, 2),
(5,  1, 'B2', 5,  7, 10, 2),
(6,  1, 'B3', 6,  9, 10, 2),
(7,  2, 'A1', 1, 10, 10, 2),
(8,  2, 'A2', 3,  4,  8, 2),
(9,  2, 'A3', 4,  2,  8, 2),
(10, 2, 'A4', 2,  6,  8, 2);

-- ---------------------------------------------------------------------
-- alerts: khớp với tồn kho ở trên (BR-11)
-- ---------------------------------------------------------------------
INSERT INTO alerts (machine_id, tray_id, type, message) VALUES
(1, 3, 'LOW_STOCK',    'Máy M01 khay A3 (Coca-Cola) sắp hết hàng: còn 1'),
(1, 4, 'OUT_OF_STOCK', 'Máy M01 khay B1 (Pepsi) đã hết hàng'),
(2, 9, 'LOW_STOCK',    'Máy M02 khay A3 (Pepsi) sắp hết hàng: còn 2');
