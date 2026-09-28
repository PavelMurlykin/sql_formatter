/* Unsupported predicate layout must remain safe. */
SELECT ItemId, DisplayName
FROM dbo.Items
WHERE DisplayName LIKE N'A%' AND DeletedAt IS NULL;
