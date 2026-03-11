-- Bridge: link financial module 'order' table to main 'Order' table
-- Execute manually in Supabase SQL Editor (not managed by EF Core migrations)
-- The financial 'order' table uses snake_case + UUID PK (managed by Dapper/PaymentRepository)
-- 'marketplace_order_id' references the main "Order".id (text PK)

ALTER TABLE "order"
ADD COLUMN IF NOT EXISTS marketplace_order_id text;

CREATE INDEX IF NOT EXISTS idx_order_marketplace_order_id
    ON "order"(marketplace_order_id)
    WHERE marketplace_order_id IS NOT NULL;

COMMENT ON COLUMN "order".marketplace_order_id IS
    'Reference to main Order table (text PK). Bridge between financial snake_case schema and main PascalCase schema.';
