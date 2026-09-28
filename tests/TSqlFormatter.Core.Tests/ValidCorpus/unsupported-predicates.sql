/* Predicate formatting must preserve comments and literals. */
SELECT ItemId, DisplayName
FROM dbo.Items
WHERE DisplayName LIKE N'A%' AND DeletedAt IS NULL;
