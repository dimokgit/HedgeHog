CREATE TABLE [dbo].[RateCodeRule] (
    [Id]       INT          NOT NULL,
    [Name]     VARCHAR (32) NOT NULL,
    [IsSystem] BIT          CONSTRAINT [DF_RateCodeRule_IsSyatem] DEFAULT ((1)) NOT NULL,
    CONSTRAINT [PK_RateCodeRule] PRIMARY KEY CLUSTERED ([Id] ASC)
);


GO
CREATE TRIGGER tr_RateCodeRule
   ON  RateCodeRule
   AFTER INSERT,DELETE,UPDATE
AS 
BEGIN
	SET NOCOUNT ON;

IF EXISTS(SELECT * FROM(SELECT * FROM inserted UNION SELECT * FROM deleted)T WHERE IsSystem = 1)BEGIN
  ROLLBACK
  RAISERROR('System RateCode rules are readonly.',16,1)
  RETURN
END

END