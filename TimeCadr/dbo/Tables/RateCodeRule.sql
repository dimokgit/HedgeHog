CREATE TABLE [dbo].[RateCodeRule] (
    [Id]       INT          NOT NULL,
    [Name]     VARCHAR (32) NOT NULL,
    [IsSystem] BIT          CONSTRAINT [DF_RateCodeRule_IsSyatem] DEFAULT ((1)) NOT NULL,
    CONSTRAINT [PK_RateCodeRule] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE TRIGGER [dbo].[tr_RateCodeRule]
   ON  [dbo].[RateCodeRule]
   AFTER INSERT,DELETE,UPDATE
AS 
BEGIN
	SET NOCOUNT ON;

IF EXISTS(SELECT * FROM deleted WHERE IsSystem = 1) AND NOT EXISTS(SELECT * FROM inserted WHERE IsSystem = 1) BEGIN
  ROLLBACK
  RAISERROR('System Rate Code Rules can not be deleted.',16,1)
  RETURN
END

IF UPDATE(Id) BEGIN
  ROLLBACK
  RAISERROR('System Rate Code Rules IDs can not be changed.',16,1)
  RETURN
END

END