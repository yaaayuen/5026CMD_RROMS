BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014090524_S5_Darren_Payments'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Amount] decimal(10,2) NOT NULL,
        [Method] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [StaffId] nvarchar(450) NOT NULL,
        [PaidAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Payments_AmountNotNegative] CHECK ([Amount] >= 0),
        CONSTRAINT [CK_Payments_Method] CHECK ([Method] IN ('Cash', 'CardTerminal', 'EWallet', 'SimulatedOnline')),
        CONSTRAINT [CK_Payments_Status] CHECK ([Status] IN ('Unpaid', 'Paid', 'Voided')),
        CONSTRAINT [FK_Payments_AspNetUsers_StaffId] FOREIGN KEY ([StaffId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Payments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014090524_S5_Darren_Payments'
)
BEGIN
    CREATE INDEX [IX_Payments_StaffId] ON [Payments] ([StaffId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014090524_S5_Darren_Payments'
)
BEGIN
    CREATE INDEX [IX_Payments_Status_PaidAtUtc] ON [Payments] ([Status], [PaidAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014090524_S5_Darren_Payments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Payments_OnePaidPerOrder] ON [Payments] ([OrderId]) WHERE [Status] = ''Paid''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261014090524_S5_Darren_Payments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261014090524_S5_Darren_Payments', N'10.0.12');
END;

COMMIT;
GO

