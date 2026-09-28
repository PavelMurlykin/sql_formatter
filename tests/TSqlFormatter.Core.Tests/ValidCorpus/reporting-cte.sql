-- Monthly order summary for active customers
WITH MonthlyOrders AS (
    SELECT c.CustomerId, SUM(o.TotalAmount) AS TotalAmount
    FROM dbo.Customers AS c
    INNER JOIN dbo.Orders AS o ON o.CustomerId = c.CustomerId
    WHERE o.CreatedAt >= '2025-01-01' AND c.IsActive = 1
    GROUP BY c.CustomerId
)
SELECT m.CustomerId, m.TotalAmount
FROM MonthlyOrders AS m
WHERE m.TotalAmount > 100
ORDER BY m.TotalAmount DESC;
