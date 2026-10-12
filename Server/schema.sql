-- =====================================================================
-- Hệ thống máy bán nước thông minh (mô phỏng) - Schema MySQL 8.0+
-- Khớp với Docs/01-pham-vi-va-luat-nghiep-vu.md và Docs/API.md.
-- Cách chạy (MySQL Workbench): File > Open SQL Script > chọn file > bấm tia sét.
-- Chạy file này TRƯỚC, rồi chạy seed.sql.
-- CẢNH BÁO: file này xóa sạch dữ liệu cũ (DROP TABLE) rồi tạo lại.
--
-- (audit_logs có trong lệnh DROP chỉ để dọn bản cũ nếu từng tạo; bảng này đã bị loại.)
-- Quy ước: tên bảng/cột snake_case tiếng Anh; tiền là INT (VNĐ);
--          thời gian DATETIME theo giờ Việt Nam (UTC+7);
--          trạng thái/loại là chuỗi IN HOA, được CHECK ở đây.
-- =====================================================================

CREATE DATABASE IF NOT EXISTS vending
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE vending;

-- Dùng giờ Việt Nam cho DEFAULT CURRENT_TIMESTAMP trong phiên này.
-- Server (C#) nên set time_zone = '+07:00' khi mở kết nối, hoặc tự ghi
-- thời gian khi INSERT/UPDATE, để mọi nơi dùng cùng một múi giờ.
SET time_zone = '+07:00';

SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS audit_logs, alerts, restock_ticket_items, restock_tickets,
                     refunds, payments, orders, trays, products, machines, users;
SET FOREIGN_KEY_CHECKS = 1;

-- ---------------------------------------------------------------------
-- 1. users: tài khoản đăng nhập (Admin, Nhân viên). Tạo bằng seed.sql.
-- ---------------------------------------------------------------------
CREATE TABLE users (
    id            INT           NOT NULL AUTO_INCREMENT,
    username      VARCHAR(50)   NOT NULL,
    password_hash VARCHAR(255)  NOT NULL COMMENT 'BCrypt, không lưu mật khẩu thô',
    full_name     VARCHAR(100)  NOT NULL,
    role          VARCHAR(20)   NOT NULL COMMENT 'ADMIN / STAFF',
    is_active     TINYINT(1)    NOT NULL DEFAULT 1,
    created_at    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_users_username (username),
    CONSTRAINT ck_users_role CHECK (role IN ('ADMIN', 'STAFF'))
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 2. machines: máy bán nước
-- status chỉ lưu ACTIVE / MAINTENANCE. ONLINE/OFFLINE là giá trị TÍNH RA
-- từ last_heartbeat (quá 60 giây hoặc NULL = OFFLINE), không lưu (BR-12).
-- ---------------------------------------------------------------------
CREATE TABLE machines (
    id             INT           NOT NULL AUTO_INCREMENT,
    code           VARCHAR(20)   NOT NULL COMMENT 'Ví dụ M01; cũng là client id MQTT',
    name           VARCHAR(100)  NOT NULL,
    location       VARCHAR(200)  NULL COMMENT 'Vị trí đặt máy',
    status         VARCHAR(20)   NOT NULL DEFAULT 'ACTIVE' COMMENT 'ACTIVE / MAINTENANCE',
    last_heartbeat DATETIME      NULL COMMENT 'NULL = chưa có hoặc vừa nhận LWT',
    created_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_machines_code (code),
    CONSTRAINT ck_machines_status CHECK (status IN ('ACTIVE', 'MAINTENANCE'))
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 3. products: sản phẩm
-- ---------------------------------------------------------------------
CREATE TABLE products (
    id         INT           NOT NULL AUTO_INCREMENT,
    name       VARCHAR(100)  NOT NULL,
    volume_ml  INT           NOT NULL COMMENT 'Dung tích (ml)',
    price      INT           NOT NULL COMMENT 'VNĐ, không số lẻ',
    image_url  VARCHAR(255)  NULL COMMENT 'Đường dẫn tương đối, ví dụ /images/nuoc-suoi.png',
    is_active  TINYINT(1)    NOT NULL DEFAULT 1,
    created_at DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    CONSTRAINT ck_products_volume CHECK (volume_ml > 0),
    CONSTRAINT ck_products_price  CHECK (price >= 0)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 4. trays: khay hàng của từng máy (tồn kho nằm ở đây)
-- ---------------------------------------------------------------------
CREATE TABLE trays (
    id                  INT         NOT NULL AUTO_INCREMENT,
    machine_id          INT         NOT NULL,
    code                VARCHAR(10) NOT NULL COMMENT 'Ví dụ A1',
    product_id          INT         NULL COMMENT 'NULL nếu khay trống/chưa gán',
    quantity            INT         NOT NULL DEFAULT 0 COMMENT 'Chỉ Server được sửa',
    capacity            INT         NOT NULL COMMENT 'Sức chứa tối đa',
    low_stock_threshold INT         NOT NULL DEFAULT 2 COMMENT 'Bằng hoặc dưới mức này thì cảnh báo',
    PRIMARY KEY (id),
    UNIQUE KEY uq_trays_machine_code (machine_id, code),
    KEY ix_trays_product (product_id),
    CONSTRAINT fk_trays_machine FOREIGN KEY (machine_id) REFERENCES machines (id),
    CONSTRAINT fk_trays_product FOREIGN KEY (product_id) REFERENCES products (id),
    CONSTRAINT ck_trays_capacity  CHECK (capacity > 0),
    CONSTRAINT ck_trays_quantity  CHECK (quantity >= 0 AND quantity <= capacity),
    CONSTRAINT ck_trays_threshold CHECK (low_stock_threshold >= 0)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 5. orders: đơn hàng (mỗi đơn đúng 1 chai)
-- 8 trạng thái, xem Docs/01 mục 7:
--   PENDING_PAYMENT -> PAID -> DISPENSING -> COMPLETED
--   nhánh: EXPIRED, PAYMENT_FAILED, DISPENSE_FAILED -> REFUNDED
-- Các mốc thời gian phục vụ việc dọn đơn quá hạn (BR-20):
--   PENDING_PAYMENT: quá expires_at (120s kể từ created_at)
--   PAID:            quá 60s kể từ paid_at mà chưa DISPENSING
--   DISPENSING:      quá 30s kể từ dispense_started_at mà chưa có kết quả
-- ---------------------------------------------------------------------
CREATE TABLE orders (
    id                  INT          NOT NULL AUTO_INCREMENT,
    request_id          CHAR(36)     NOT NULL COMMENT 'UUID Device gửi khi tạo đơn (chống tạo trùng)',
    machine_id          INT          NOT NULL,
    tray_id             INT          NOT NULL,
    product_id          INT          NOT NULL,
    amount              INT          NOT NULL COMMENT 'VNĐ, chốt giá tại thời điểm tạo đơn',
    status              VARCHAR(20)  NOT NULL DEFAULT 'PENDING_PAYMENT',
    dispense_request_id CHAR(36)     NULL COMMENT 'UUID gói báo kết quả nhả hàng (chống xử lý trùng)',
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at          DATETIME     NOT NULL COMMENT 'Hạn thanh toán = created_at + 120s',
    paid_at             DATETIME     NULL,
    dispense_started_at DATETIME     NULL COMMENT 'Mốc bắt đầu nhả, để tính timeout 30s',
    completed_at        DATETIME     NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_orders_request_id (request_id),
    UNIQUE KEY uq_orders_dispense_request_id (dispense_request_id),
    KEY ix_orders_machine_status (machine_id, status),
    KEY ix_orders_tray_status (tray_id, status),
    KEY ix_orders_status_expires (status, expires_at),
    KEY ix_orders_status_paid (status, paid_at),
    KEY ix_orders_status_dispense (status, dispense_started_at),
    KEY ix_orders_created_at (created_at),
    CONSTRAINT fk_orders_machine FOREIGN KEY (machine_id) REFERENCES machines (id),
    CONSTRAINT fk_orders_tray    FOREIGN KEY (tray_id)    REFERENCES trays (id),
    CONSTRAINT fk_orders_product FOREIGN KEY (product_id) REFERENCES products (id),
    CONSTRAINT ck_orders_amount  CHECK (amount >= 0),
    CONSTRAINT ck_orders_status  CHECK (status IN (
        'PENDING_PAYMENT', 'PAID', 'DISPENSING', 'COMPLETED',
        'EXPIRED', 'PAYMENT_FAILED', 'DISPENSE_FAILED', 'REFUNDED'))
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 6. payments: giao dịch thanh toán (mô phỏng)
-- Server tạo bản ghi PENDING khi tạo đơn; payment/callback cập nhật
-- thành SUCCESS hoặc FAILED.
-- ---------------------------------------------------------------------
CREATE TABLE payments (
    id               INT          NOT NULL AUTO_INCREMENT,
    order_id         INT          NOT NULL,
    method           VARCHAR(20)  NOT NULL DEFAULT 'QR_SIM',
    transaction_code VARCHAR(50)  NULL COMMENT 'Mã giao dịch từ nút giả lập thanh toán',
    amount           INT          NOT NULL COMMENT 'VNĐ',
    status           VARCHAR(20)  NOT NULL DEFAULT 'PENDING',
    created_at       DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    paid_at          DATETIME     NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_payments_transaction_code (transaction_code),
    KEY ix_payments_order (order_id),
    CONSTRAINT fk_payments_order  FOREIGN KEY (order_id) REFERENCES orders (id),
    CONSTRAINT ck_payments_amount CHECK (amount >= 0),
    CONSTRAINT ck_payments_status CHECK (status IN ('PENDING', 'SUCCESS', 'FAILED'))
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 7. refunds: hoàn tiền
-- reason dùng cùng bộ mã với order/status: JAMMED / DISPENSE_TIMEOUT / DEVICE_NO_ACK
-- ---------------------------------------------------------------------
CREATE TABLE refunds (
    id           INT           NOT NULL AUTO_INCREMENT,
    order_id     INT           NOT NULL,
    payment_id   INT           NOT NULL,
    amount       INT           NOT NULL COMMENT 'VNĐ',
    reason       VARCHAR(100)  NOT NULL COMMENT 'JAMMED / DISPENSE_TIMEOUT / DEVICE_NO_ACK',
    status       VARCHAR(20)   NOT NULL DEFAULT 'PENDING',
    created_at   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME      NULL,
    PRIMARY KEY (id),
    KEY ix_refunds_order (order_id),
    KEY ix_refunds_payment (payment_id),
    CONSTRAINT fk_refunds_order   FOREIGN KEY (order_id)   REFERENCES orders (id),
    CONSTRAINT fk_refunds_payment FOREIGN KEY (payment_id) REFERENCES payments (id),
    CONSTRAINT ck_refunds_amount  CHECK (amount >= 0),
    CONSTRAINT ck_refunds_status  CHECK (status IN ('PENDING', 'COMPLETED'))
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 8. restock_tickets: phiếu tiếp hàng
-- ---------------------------------------------------------------------
CREATE TABLE restock_tickets (
    id           INT           NOT NULL AUTO_INCREMENT,
    machine_id   INT           NOT NULL,
    staff_id     INT           NULL COMMENT 'NULL nếu chưa phân công',
    status       VARCHAR(20)   NOT NULL DEFAULT 'OPEN' COMMENT 'OPEN / IN_PROGRESS / DONE / CANCELLED',
    note         VARCHAR(255)  NULL,
    created_at   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME      NULL,
    PRIMARY KEY (id),
    KEY ix_restock_tickets_machine (machine_id),
    KEY ix_restock_tickets_staff (staff_id),
    CONSTRAINT fk_restock_tickets_machine FOREIGN KEY (machine_id) REFERENCES machines (id),
    CONSTRAINT fk_restock_tickets_staff   FOREIGN KEY (staff_id)   REFERENCES users (id),
    CONSTRAINT ck_restock_tickets_status  CHECK (status IN ('OPEN', 'IN_PROGRESS', 'DONE', 'CANCELLED'))
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 9. restock_ticket_items: chi tiết phiếu tiếp hàng (theo từng khay)
-- ---------------------------------------------------------------------
CREATE TABLE restock_ticket_items (
    id          INT NOT NULL AUTO_INCREMENT,
    ticket_id   INT NOT NULL,
    tray_id     INT NOT NULL,
    planned_qty INT NOT NULL COMMENT 'Số lượng cần châm = capacity - quantity',
    actual_qty  INT NULL COMMENT 'Nhân viên nhập khi xác nhận đã châm',
    PRIMARY KEY (id),
    UNIQUE KEY uq_restock_items_ticket_tray (ticket_id, tray_id),
    KEY ix_restock_items_tray (tray_id),
    CONSTRAINT fk_restock_items_ticket FOREIGN KEY (ticket_id) REFERENCES restock_tickets (id),
    CONSTRAINT fk_restock_items_tray   FOREIGN KEY (tray_id)   REFERENCES trays (id),
    CONSTRAINT ck_restock_items_planned CHECK (planned_qty > 0),
    CONSTRAINT ck_restock_items_actual  CHECK (actual_qty IS NULL OR actual_qty >= 0)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 10. alerts: cảnh báo lưu trong DB (LOW_STOCK, OUT_OF_STOCK, JAMMED).
-- MACHINE_OFFLINE KHÔNG lưu ở đây: Server tính ra lúc đọc (BR-12).
-- ---------------------------------------------------------------------
CREATE TABLE alerts (
    id          INT           NOT NULL AUTO_INCREMENT,
    machine_id  INT           NOT NULL,
    tray_id     INT           NULL COMMENT 'NULL nếu cảnh báo cả máy',
    type        VARCHAR(30)   NOT NULL COMMENT 'LOW_STOCK / OUT_OF_STOCK / JAMMED',
    message     VARCHAR(255)  NOT NULL,
    is_resolved TINYINT(1)    NOT NULL DEFAULT 0,
    created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    resolved_at DATETIME      NULL,
    resolved_by INT           NULL,
    PRIMARY KEY (id),
    KEY ix_alerts_resolved_machine (is_resolved, machine_id),
    KEY ix_alerts_tray_type (tray_id, type, is_resolved),
    CONSTRAINT fk_alerts_machine FOREIGN KEY (machine_id)  REFERENCES machines (id),
    CONSTRAINT fk_alerts_tray    FOREIGN KEY (tray_id)     REFERENCES trays (id),
    CONSTRAINT fk_alerts_user    FOREIGN KEY (resolved_by) REFERENCES users (id),
    CONSTRAINT ck_alerts_type CHECK (type IN ('LOW_STOCK', 'OUT_OF_STOCK', 'JAMMED'))
) ENGINE=InnoDB;
