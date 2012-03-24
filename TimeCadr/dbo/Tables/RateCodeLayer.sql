CREATE TABLE [dbo].[RateCodeLayer] (
    [Id]       INT          IDENTITY (1, 1) NOT NULL,
    [Name]     VARCHAR (50) NOT NULL,
    [Priority] INT          NOT NULL,
    CONSTRAINT [PK_RateCodeLayer] PRIMARY KEY CLUSTERED ([Id] ASC)
);




GO
CREATE TRIGGER dbo.traRateCodeLayer 
   ON  dbo.RateCodeLayer 
   AFTER DELETE,UPDATE
AS 
SET NOCOUNT ON;

IF EXISTS(SELECT * FROM deleted WHERE Id = 1 AND Priority = 1) OR
   EXISTS(SELECT * FROM deleted WHERE Id = 2 AND Priority = 2) 
BEGIN
  ROLLBACK TRAN
  RAISERROR('Deleting of built-in layers (Shift and Day) is not allowed.',16,1)
  RETURN
END
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'0', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'RateCodeLayer';

