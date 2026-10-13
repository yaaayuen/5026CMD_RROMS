BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013005216_S4_Alex_AnnouncementsFeedback'
)
BEGIN
    CREATE TABLE [Announcements] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(120) NOT NULL,
        [Message] nvarchar(2000) NOT NULL,
        [IsPromotion] bit NOT NULL,
        [IsPublished] bit NOT NULL,
        [StartUtc] datetime2 NOT NULL,
        [EndUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Announcements] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Announcements_Dates] CHECK ([EndUtc] > [StartUtc])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013005216_S4_Alex_AnnouncementsFeedback'
)
BEGIN
    CREATE TABLE [Feedback] (
        [Id] int NOT NULL IDENTITY,
        [ReservationId] int NOT NULL,
        [Rating] int NOT NULL,
        [Comment] nvarchar(1000) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Feedback] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Feedback_Rating] CHECK ([Rating] BETWEEN 1 AND 5),
        CONSTRAINT [FK_Feedback_Reservations_ReservationId] FOREIGN KEY ([ReservationId]) REFERENCES [Reservations] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013005216_S4_Alex_AnnouncementsFeedback'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Feedback_ReservationId] ON [Feedback] ([ReservationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013005216_S4_Alex_AnnouncementsFeedback'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013005216_S4_Alex_AnnouncementsFeedback', N'10.0.12');
END;

COMMIT;
GO

