SELECT CustomerId,
       ROW_NUMBER() OVER (PARTITION BY RegionId ORDER BY TotalAmount DESC) AS RankInRegion,
       SUM(TotalAmount) OVER (PARTITION BY RegionId) AS RegionTotal
FROM dbo.CustomerTotals
WHERE TotalAmount > 0;
