﻿CREATE FUNCTION [const].[ShiftTimeout](
)RETURNS int WITH SCHEMABINDING
AS
BEGIN
RETURN 60
END