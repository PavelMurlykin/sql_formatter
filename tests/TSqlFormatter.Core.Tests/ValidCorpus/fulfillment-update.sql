BEGIN TRANSACTION;
-- Mark orders ready for export
UPDATE dbo.Orders
SET StatusCode = 'READY', UpdatedAt = SYSUTCDATETIME()
WHERE StatusCode = 'PENDING' AND BatchId = 42;
INSERT INTO dbo.OrderAudit (OrderId, EventCode)
SELECT OrderId, N'export-ready' FROM dbo.Orders WHERE BatchId = 42;
COMMIT TRANSACTION;
