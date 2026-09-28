CREATE PROCEDURE dbo.GetOpenOrders @CustomerId int
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        SELECT OrderId, StatusCode
        FROM dbo.Orders
        WHERE CustomerId = @CustomerId;
    END TRY
    BEGIN CATCH
        -- Keep the original error for the caller
        THROW;
    END CATCH
END;
