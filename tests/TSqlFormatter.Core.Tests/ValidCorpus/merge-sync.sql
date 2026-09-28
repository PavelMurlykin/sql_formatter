MERGE dbo.Inventory AS target
USING (SELECT 1 AS ItemId, 5 AS Quantity) AS source
ON target.ItemId = source.ItemId
WHEN MATCHED THEN UPDATE SET target.Quantity = source.Quantity
WHEN NOT MATCHED THEN INSERT (ItemId, Quantity) VALUES (source.ItemId, source.Quantity);
