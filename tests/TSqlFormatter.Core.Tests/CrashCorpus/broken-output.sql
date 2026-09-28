UPDATE dbo.Items SET Quantity = OUTPUT inserted.Quantity WHERE ItemId = 1;
