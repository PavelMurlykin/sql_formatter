SELECT OrderId, -- stable identifier
-- used by downstream exports
StatusCode, /* preserved source field */ UpdatedAt
FROM dbo.Orders
-- only pending work
WHERE StatusCode = 'PENDING';
