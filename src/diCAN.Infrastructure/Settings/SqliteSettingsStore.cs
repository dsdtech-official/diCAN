using DiCAN.Core.Settings;
using DiCAN.Infrastructure.Devices;
using Microsoft.Data.Sqlite;

namespace DiCAN.Infrastructure.Settings;

// Manages sqlite settings store.
public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;

    // Initializes this instance.
    public SqliteSettingsStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (Path.GetDirectoryName(Path.GetFullPath(databasePath)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    // Loads saved data.
    public async Task<DisplaySettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT key, value FROM settings;";

        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            stored[reader.GetString(0)] = reader.GetString(1);
        }

        return new DisplaySettings
        {
            ShowDeltaColumn = Read(stored, DisplaySettingKeys.ShowDeltaColumn),
            ShowMinMaxDeltaColumns = Read(stored, DisplaySettingKeys.ShowMinMaxDeltaColumns),
            ShowOwnFramesInStream = Read(stored, DisplaySettingKeys.ShowOwnFramesInStream),
            DecimalIds = Read(stored, DisplaySettingKeys.DecimalIds),
            SpacedData = Read(stored, DisplaySettingKeys.SpacedData),
            HighlightChanges = Read(stored, DisplaySettingKeys.HighlightChanges),
            StreamDeltaTime = Read(stored, DisplaySettingKeys.StreamDeltaTime),

            FontSize = DisplaySettingKeys.FontSize.Clamp(
                Number(stored, DisplaySettingKeys.FontSize.Name, DisplaySettingKeys.FontSize.Default)),
        };
    }

    // Sets the requested value.
    public async Task SetAsync(
        BoolSetting setting, bool value, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = UpsertOne;
        command.Parameters.AddWithValue("$k", setting.Name);
        command.Parameters.AddWithValue("$v", BoolSetting.Render(value));
        command.Parameters.AddWithValue("$now", Now());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string UpsertOne =
        """
        INSERT INTO settings (key, value, chosen_at) VALUES ($k, $v, $now)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value, chosen_at = excluded.chosen_at;
        """;

    // Gets the current timestamp.
    private static string Now() =>
        DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    // Loads options.
    public async Task<AppOptions> LoadOptionsAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> stored = await ReadAllAsync(cancellationToken);

        return new AppOptions(
            Whole(stored, AppSettingKeys.DevicePollMs),
            Whole(stored, AppSettingKeys.StreamCapacity),
            Read(stored, AppSettingKeys.AutoReconnect),
            Whole(stored, AppSettingKeys.ReconnectAttempts),
            Whole(stored, AppSettingKeys.ReconnectDelayMs),
            Read(stored, AppSettingKeys.RecordRawText),
            Read(stored, AppSettingKeys.RecordFilteredOnly)).Clamped();
    }

    // Saves options.
    public async Task SaveOptionsAsync(
        AppOptions options, CancellationToken cancellationToken = default)
    {
        AppOptions clamped = options.Clamped();
        Dictionary<string, string> stored = await ReadAllAsync(cancellationToken);

        await WriteIfChangedAsync(
            AppSettingKeys.DevicePollMs, clamped.DevicePollMs, stored, cancellationToken);
        await WriteIfChangedAsync(
            AppSettingKeys.StreamCapacity, clamped.StreamCapacity, stored, cancellationToken);
        await WriteIfChangedAsync(
            AppSettingKeys.ReconnectAttempts, clamped.ReconnectAttempts, stored, cancellationToken);
        await WriteIfChangedAsync(
            AppSettingKeys.ReconnectDelayMs, clamped.ReconnectDelayMs, stored, cancellationToken);

        await WriteFlagIfChangedAsync(
            AppSettingKeys.AutoReconnect, clamped.AutoReconnect, stored, cancellationToken);
        await WriteFlagIfChangedAsync(
            AppSettingKeys.RecordRawText, clamped.RecordRawText, stored, cancellationToken);
        await WriteFlagIfChangedAsync(
            AppSettingKeys.RecordFilteredOnly, clamped.RecordFilteredOnly, stored, cancellationToken);
    }

    // Writes flag if changed.
    private async Task WriteFlagIfChangedAsync(
        BoolSetting setting,
        bool value,
        Dictionary<string, string> stored,
        CancellationToken cancellationToken)
    {
        string flag = BoolSetting.Render(value);

        if (!stored.TryGetValue(setting.Name, out string? current) ||
            !string.Equals(current, flag, StringComparison.Ordinal))
        {
            await SetAsync(setting, value, cancellationToken);
        }
    }

    // Writes if changed.
    private async Task WriteIfChangedAsync(
        IntSetting setting,
        int value,
        IReadOnlyDictionary<string, string> stored,
        CancellationToken cancellationToken)
    {
        string text = IntSetting.Render(value);

        if (stored.TryGetValue(setting.Name, out string? current) &&
            string.Equals(current, text, StringComparison.Ordinal))
        {
            return;
        }

        await SaveTextAsync(setting.Name, text, cancellationToken);
    }

    // Gets an integer value.
    private static int Whole(
        IReadOnlyDictionary<string, string> stored, IntSetting setting) =>
        stored.TryGetValue(setting.Name, out string? raw)
        && int.TryParse(
            raw,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int value)
            ? value
            : setting.Default;

    // Synchronizes the current view.
    public async Task SyncAsync(
        IReadOnlyDictionary<string, SettingsRowInfo> catalogue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        if (catalogue.Count == 0)
        {
            return;
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using SqliteCommand seed = connection.CreateCommand();
        seed.Transaction = transaction;
        seed.CommandText = SeedOne;
        SqliteParameter seedKey = seed.Parameters.Add("$k", SqliteType.Text);
        SqliteParameter seedValue = seed.Parameters.Add("$default", SqliteType.Text);
        SqliteParameter seedNote = seed.Parameters.Add("$note", SqliteType.Text);
        SqliteParameter seedType = seed.Parameters.Add("$type", SqliteType.Text);

        await using SqliteCommand refresh = connection.CreateCommand();
        refresh.Transaction = transaction;
        refresh.CommandText = RefreshOne;
        SqliteParameter refreshKey = refresh.Parameters.Add("$k", SqliteType.Text);
        SqliteParameter refreshValue = refresh.Parameters.Add("$default", SqliteType.Text);
        SqliteParameter refreshNote = refresh.Parameters.Add("$note", SqliteType.Text);
        SqliteParameter refreshType = refresh.Parameters.Add("$type", SqliteType.Text);

        foreach ((string key, SettingsRowInfo row) in catalogue)
        {
            seedKey.Value = key;
            seedValue.Value = row.DefaultValue;
            seedNote.Value = row.Note;
            seedType.Value = row.ValueType;
            await seed.ExecuteNonQueryAsync(cancellationToken);

            refreshKey.Value = key;
            refreshValue.Value = row.DefaultValue;
            refreshNote.Value = row.Note;
            refreshType.Value = row.ValueType;
            await refresh.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private const string SeedOne =
        """
        INSERT INTO settings (key, value, note, value_type, chosen_at)
        VALUES ($k, $default, $note, $type, '')
        ON CONFLICT(key) DO NOTHING;
        """;

    private const string RefreshOne =
        """
        UPDATE settings SET
            note       = $note,
            value_type = $type,
            value      = CASE WHEN chosen_at = '' THEN $default ELSE value END
        WHERE key = $k
          AND (note <> $note
               OR value_type <> $type
               OR (chosen_at = '' AND value <> $default));
        """;

    // Loads window.
    public async Task<WindowPlacement> LoadWindowAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, string> stored = await ReadAllAsync(cancellationToken);

        return new WindowPlacement(
            Number(stored, "window.x", double.NaN),
            Number(stored, "window.y", double.NaN),
            Number(stored, "window.width", WindowPlacement.Default.Width),
            Number(stored, "window.height", WindowPlacement.Default.Height),
            stored.TryGetValue("window.maximized", out string? max) && bool.TryParse(max, out bool m) && m,
            Number(stored, "window.bottomPanel", WindowPlacement.DefaultBottomPanelHeight),

            Number(stored, "window.sidebarWidth", WindowPlacement.NotRemembered),

            !stored.TryGetValue("window.sidebarOpen", out string? open)
                || !bool.TryParse(open, out bool o) || o);
    }

    // Saves window.
    public async Task SaveWindowAsync(
        WindowPlacement placement, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);

        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = UpsertOne;

        SqliteParameter key = command.Parameters.Add("$k", SqliteType.Text);
        SqliteParameter value = command.Parameters.Add("$v", SqliteType.Text);
        command.Parameters.AddWithValue("$now", Now());

        var culture = System.Globalization.CultureInfo.InvariantCulture;

        foreach ((string name, string text) in new[]
        {
            ("window.x", placement.X.ToString(culture)),
            ("window.y", placement.Y.ToString(culture)),
            ("window.width", placement.Width.ToString(culture)),
            ("window.height", placement.Height.ToString(culture)),
            ("window.maximized", placement.Maximized.ToString(culture)),
            ("window.bottomPanel", placement.BottomPanelHeight.ToString(culture)),
            ("window.sidebarWidth", placement.SidebarWidth.ToString(culture)),
            ("window.sidebarOpen", placement.SidebarOpen.ToString(culture)),
        })
        {
            key.Value = name;
            value.Value = text;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // Reads all.
    private async Task<Dictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT key, value FROM settings;";

        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            stored[reader.GetString(0)] = reader.GetString(1);
        }

        return stored;
    }

    // Gets the numeric value.
    private static double Number(IReadOnlyDictionary<string, string> stored, string key, double fallback) =>
        stored.TryGetValue(key, out string? raw)
        && double.TryParse(
            raw,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value)
            ? value
            : fallback;

    // Reads input data.
    private static bool Read(IReadOnlyDictionary<string, string> stored, BoolSetting setting) =>
        stored.TryGetValue(setting.Name, out string? raw) && bool.TryParse(raw, out bool value)
            ? value
            : setting.Default;

    // Opens the requested resource.
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (Volatile.Read(ref _ready))
        {
            return connection;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!_ready)
            {
                await UserDataSchema.CreateAsync(connection, cancellationToken);
                Volatile.Write(ref _ready, true);
            }
        }
        finally
        {
            _gate.Release();
        }

        return connection;
    }

    // Loads text.
    public async Task<string?> LoadTextAsync(
        string key, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = "SELECT value FROM settings WHERE key = $k;";
        command.Parameters.AddWithValue("$k", key);

        object? value = await command.ExecuteScalarAsync(cancellationToken);

        return value as string;
    }

    // Saves text.
    public async Task SaveTextAsync(
        string key, string value, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();

        command.CommandText = UpsertOne;

        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$v", value);
        command.Parameters.AddWithValue("$now", Now());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}