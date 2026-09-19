using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CreateVanillaPlusPlusLauncher
{
    public partial class MainWindow : Window
    {
        // ============================================================
        // ОСНОВНЫЕ НАСТРОЙКИ
        // ============================================================

        private const string CurrentVersion = "1.2";

        private const string GitHubOwner =
            "XHNORT82";

        private const string GitHubRepository =
            "Create_Vanilla_PlusPlus_Modpack";

        private const string PineconeSetupAssetName =
            "PineconeMC-Setup.exe";

        private const string InstallAssetName =
            "install.zip";

        private const string UpdateAssetName =
            "update.zip";

        // ============================================================
        // СТАТУС СЕРВЕРА
        // ============================================================

        // Укажи здесь IP/домен игрового сервера.
        // Порт твоего Minecraft-сервера: 34656.
        // Можно указать как "IP:34656", так и просто "IP".
        private const string ServerAddress =
            "213.171.18.150:34656";

        private const int DefaultMinecraftPort =
            25565;

        private const int ServerStatusTimeoutMilliseconds =
            5000;

        private const int ServerStatusRefreshSeconds =
            30;

        // ============================================================
        // ИНСТАНС
        // ============================================================

        private const string InstanceName =
            "Create- Vanilla++";

        // ============================================================
        // ELYPRISM / PINECONEMC
        // ============================================================

        private static readonly string ElyPrismLauncherDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "ElyPrismLauncher");

        private static readonly string ElyPrismLauncherPath =
            Path.Combine(
                ElyPrismLauncherDirectory,
                "elyprismlauncher.exe");

        // ============================================================
        // ELYPRISM DATA
        // ============================================================

        private static readonly string ElyPrismDataDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "ElyPrismLauncher");

        private static readonly string InstancesDirectory =
            Path.Combine(
                ElyPrismDataDirectory,
                "instances");

        private static readonly string InstancePath =
            Path.Combine(
                InstancesDirectory,
                InstanceName);

        private static readonly string MinecraftPath =
            Path.Combine(
                InstancePath,
                "minecraft");

        // ============================================================
        // OFFLINE ACCOUNTS PINECONEMC
        // ============================================================

        private static readonly string PineconeAccountsPath =
            Path.Combine(
                ElyPrismDataDirectory,
                "accounts.json");

        // ============================================================
        // НАСТРОЙКИ ЛАУНЧЕРА
        // ============================================================

        private static readonly string LauncherSettingsDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "CreateVanillaPlusPlusLauncher");

        private static readonly string LauncherSettingsPath =
            Path.Combine(
                LauncherSettingsDirectory,
                "settings.json");

        private bool _loadingSettings = true;

        private LauncherSettings _launcherSettings =
            new LauncherSettings();

        private sealed class LauncherSettings
        {
            public string MemoryMode { get; set; } = "auto";
            public int CustomMemoryGb { get; set; } = 8;
            public bool JavaAuto { get; set; } = true;
            public string JavaPath { get; set; } = "";
            public bool CheckUpdatesOnLauncherStart { get; set; } = true;
            public bool CheckUpdatesBeforeLaunch { get; set; } = true;
            public bool CloseLauncherAfterLaunch { get; set; } = false;
            public string Resolution { get; set; } = "default";
            public bool LaunchMaximized { get; set; } = false;
            public int ServerRefreshSeconds { get; set; } = 30;
        }

        private readonly List<OfflineAccountInfo> _offlineAccounts =
            new List<OfflineAccountInfo>();

        private sealed class OfflineAccountInfo
        {
            public string Name { get; init; } = "";
            public string Uuid { get; init; } = "";
            public string Status { get; set; } = "";
        }

        // ============================================================
        // BACKUP
        // ============================================================

        private const string BackupDirectoryName =
            ".createvanillaplusplus-backups";

        private const string VersionFileName =
            ".createvanillaplusplus-version";

        private static readonly string BackupRoot =
            Path.Combine(
                InstancePath,
                BackupDirectoryName);

        // ============================================================
        // TEMP
        // ============================================================

        private static readonly string TempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                "CreateVanillaPlusPlusLauncher");

        // ============================================================
        // HTTP
        // ============================================================

        private static readonly HttpClient HttpClient =
            CreateHttpClient();

        private CancellationTokenSource? _serverStatusCancellation;
        private bool _serverStatusMonitoringStarted;

        // ============================================================
        // SERVER STATUS UI
        // ============================================================

        // Эти элементы находятся непосредственно в MainWindow.xaml.
        // Никаких динамических карточек поверх интерфейса не создаём.

        private static HttpClient CreateHttpClient()
        {
            HttpClient client =
                new HttpClient();

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "CreateVanillaPlusPlusLauncher/1.0");

            return client;
        }

        // ============================================================
        // КОНСТРУКТОР
        // ============================================================

        public MainWindow()
        {
            InitializeComponent();

            LoadLauncherSettings();
            PopulateSettingsUi();
            _loadingSettings = false;

            LoadOfflineAccounts();

            string installedVersion =
                GetInstalledVersion();

            CurrentVersionText.Text =
                $"Версия сборки: {installedVersion}";

            BottomUpdateText.Text =
                $"Create: Vanilla++ • {installedVersion}";

            UpdateStatusText.Text =
                "Проверяем установку...";

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        // ============================================================
        // ЗАГРУЗКА
        // ============================================================

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            UpdateServerStatusUi(ServerStatus.Checking());
            StartServerStatusMonitoring();

            await CheckInstallationAsync();

            try
            {
                ApplyLauncherSettingsToInstance();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "Не удалось применить сохранённые настройки:");
                Debug.WriteLine(ex);
            }
        }

        // ============================================================
        // ПРОВЕРКА УСТАНОВКИ
        // ============================================================

        private async Task CheckInstallationAsync()
        {
            try
            {
                PlayButton.IsEnabled = false;
                UpdateButton.IsEnabled = false;

                string? launcherPath =
                    FindElyPrismLauncher();

                bool launcherInstalled =
                    launcherPath != null;

                bool modpackInstalled =
                    IsMinecraftInstalled();

                if (launcherInstalled &&
                    modpackInstalled)
                {
                    SetReadyState();

                    if (_launcherSettings.CheckUpdatesOnLauncherStart)
                    {
                        await CheckForUpdatesSilentlyAsync();
                    }

                    StartServerStatusMonitoring();

                    return;
                }

                if (!launcherInstalled &&
                    modpackInstalled)
                {
                    UpdateStatusText.Text =
                        "Не найден PineconeMC";

                    BottomUpdateText.Text =
                        "Требуется установка PineconeMC";

                    PlayButton.Content =
                        "УСТАНОВИТЬ";

                    PlayButton.IsEnabled =
                        true;

                    return;
                }

                if (launcherInstalled &&
                    !modpackInstalled)
                {
                    UpdateStatusText.Text =
                        "Сборка не установлена";

                    BottomUpdateText.Text =
                        "Требуется установить Create: Vanilla++";

                    PlayButton.Content =
                        "УСТАНОВИТЬ";

                    PlayButton.IsEnabled =
                        true;

                    return;
                }

                UpdateStatusText.Text =
                    "Требуется установка";

                BottomUpdateText.Text =
                    "PineconeMC и сборка не найдены";

                PlayButton.Content =
                    "УСТАНОВИТЬ";

                PlayButton.IsEnabled =
                    true;
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text =
                    "Ошибка проверки";

                BottomUpdateText.Text =
                    "Не удалось проверить установку";

                MessageBox.Show(
                    "Не удалось проверить установку.\n\n" +
                    $"Ошибка:\n{ex.Message}",
                    "Create: Vanilla++",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Debug.WriteLine(ex);
            }
        }

        // ============================================================
        // ТИХАЯ ПРОВЕРКА ОБНОВЛЕНИЯ
        // ============================================================

        private async Task CheckForUpdatesSilentlyAsync()
        {
            try
            {
                string installedVersion =
                    GetInstalledVersion();

                GitHubRelease? release =
                    await GetLatestReleaseAsync();


                if (release != null)
                {
                    string latestVersion =
                        NormalizeVersion(
                            release.TagName);

                    if (IsNewerVersion(
                        latestVersion,
                        installedVersion))
                    {

                        UpdateStatusText.Text =
                            $"Доступно обновление {latestVersion}";

                        BottomUpdateText.Text =
                            $"Установлена {installedVersion} • " +
                            $"доступна {latestVersion}";
                    }
                }

                UpdateButton.IsEnabled = true;
            }
            catch (Exception ex)
            {

                Debug.WriteLine(
                    "Ошибка проверки обновлений:");

                Debug.WriteLine(ex);

                UpdateButton.IsEnabled = true;
            }
        }

        // ============================================================
        // СТАТУС СЕРВЕРА — UI
        // ============================================================

        private void UpdateServerStatusCardUi(
            ServerStatus status)
        {
            ServerStatusAddressText.Text =
                GetDisplayServerAddress();

            ServerStatusRefreshText.Text =
                $"Обновлено {DateTime.Now:HH:mm:ss}";

            if (status.IsNotConfigured)
            {
                ServerStatusIndicator.Fill =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(112, 112, 112));

                ServerStatusTitleText.Text =
                    "СЕРВЕР НЕ НАСТРОЕН";

                ServerStatusPlayersText.Text =
                    "Игроки: —";

                return;
            }

            if (status.IsChecking)
            {
                ServerStatusIndicator.Fill =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(112, 112, 112));

                ServerStatusTitleText.Text =
                    "ПРОВЕРЯЕМ СЕРВЕР...";

                ServerStatusPlayersText.Text =
                    "Игроки: —";

                return;
            }

            if (status.IsOnline)
            {
                ServerStatusIndicator.Fill =
                    new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(91, 190, 104));

                ServerStatusTitleText.Text =
                    "СЕРВЕР ОНЛАЙН";

                ServerStatusPlayersText.Text =
                    $"Игроки: {status.OnlinePlayers} / {status.MaxPlayers}";

                return;
            }

            ServerStatusIndicator.Fill =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(218, 73, 73));

            ServerStatusTitleText.Text =
                "СЕРВЕР ОФФЛАЙН";

            ServerStatusPlayersText.Text =
                $"Игроки: —  •  {status.Message}";
        }

        // ============================================================
        // МОНИТОРИНГ СТАТУСА MINECRAFT-СЕРВЕРА
        // ============================================================

        private void StartServerStatusMonitoring()
        {
            if (_serverStatusMonitoringStarted)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ServerAddress) ||
                ServerAddress.StartsWith(
                    "IP_СЕРВЕРА",
                    StringComparison.OrdinalIgnoreCase))
            {
                UpdateServerStatusUi(
                    ServerStatus.NotConfigured());

                return;
            }

            if (_launcherSettings.ServerRefreshSeconds <= 0)
            {
                UpdateServerStatusUi(
                    ServerStatus.NotConfigured());
                return;
            }

            _serverStatusMonitoringStarted = true;

            _serverStatusCancellation =
                new CancellationTokenSource();

            _ = ServerStatusLoopAsync(
                _serverStatusCancellation.Token);
        }

        private async Task ServerStatusLoopAsync(
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    ServerStatus status =
                        await QueryMinecraftServerStatusAsync(
                            ServerAddress,
                            cancellationToken);

                    Dispatcher.Invoke(() =>
                    {
                        UpdateServerStatusUi(status);
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        "Ошибка проверки статуса сервера:");

                    Debug.WriteLine(ex);

                    Dispatcher.Invoke(() =>
                    {
                        UpdateServerStatusUi(
                            ServerStatus.Offline(
                                "Нет соединения"));
                    });
                }

                try
                {
                    int refreshSeconds =
                        Math.Max(5, _launcherSettings.ServerRefreshSeconds);

                    await Task.Delay(
                        TimeSpan.FromSeconds(refreshSeconds),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            try
            {
                _serverStatusCancellation?.Cancel();
                _serverStatusCancellation?.Dispose();
                _serverStatusCancellation = null;
            }
            catch
            {
            }
        }

        private void UpdateServerStatusUi(
            ServerStatus status)
        {
            // Статус сервера отображается только в блоке "СЕРВЕР".
            // Не меняем карточки версии и обновлений, чтобы интерфейс
            // оставался чистым и информация не дублировалась.
            UpdateServerStatusCardUi(status);
        }

        private static string GetDisplayServerAddress()
        {
            return ServerAddress;
        }

        private static async Task<ServerStatus> QueryMinecraftServerStatusAsync(
            string address,
            CancellationToken cancellationToken)
        {
            ParseServerAddress(
                address,
                out string host,
                out int port);

            using TcpClient client =
                new TcpClient();

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutCts.CancelAfter(
                ServerStatusTimeoutMilliseconds);

            try
            {
                await client.ConnectAsync(
                    host,
                    port,
                    timeoutCts.Token);

                using NetworkStream stream =
                    client.GetStream();

                // Minecraft Java Edition protocol 1.20.1.
                // Status ping работает независимо от Forge-модов.
                await WriteVarIntPacketAsync(
                    stream,
                    0x00,
                    BuildHandshakePayload(
                        763,
                        host,
                        port),
                    timeoutCts.Token);

                await WriteVarIntPacketAsync(
                    stream,
                    0x00,
                    Array.Empty<byte>(),
                    timeoutCts.Token);

                int packetLength =
                    await ReadVarIntAsync(
                        stream,
                        timeoutCts.Token);

                if (packetLength <= 0 ||
                    packetLength > 1024 * 1024)
                {
                    throw new InvalidDataException(
                        "Сервер вернул некорректный пакет статуса.");
                }

                byte[] packet =
                    await ReadExactAsync(
                        stream,
                        packetLength,
                        timeoutCts.Token);

                int offset = 0;

                int packetId =
                    ReadVarInt(
                        packet,
                        ref offset);

                if (packetId != 0x00)
                {
                    throw new InvalidDataException(
                        "Сервер вернул неизвестный status packet.");
                }

                int jsonLength =
                    ReadVarInt(
                        packet,
                        ref offset);

                if (jsonLength <= 0 ||
                    jsonLength > packet.Length - offset)
                {
                    throw new InvalidDataException(
                        "Сервер вернул некорректный JSON статуса.");
                }

                string json =
                    Encoding.UTF8.GetString(
                        packet,
                        offset,
                        jsonLength);

                using JsonDocument document =
                    JsonDocument.Parse(json);

                JsonElement root =
                    document.RootElement;

                int online =
                    0;

                int max =
                    0;

                if (root.TryGetProperty(
                    "players",
                    out JsonElement players))
                {
                    if (players.TryGetProperty(
                        "online",
                        out JsonElement onlineElement) &&
                        onlineElement.TryGetInt32(
                            out int parsedOnline))
                    {
                        online = Math.Max(
                            0,
                            parsedOnline);
                    }

                    if (players.TryGetProperty(
                        "max",
                        out JsonElement maxElement) &&
                        maxElement.TryGetInt32(
                            out int parsedMax))
                    {
                        max = Math.Max(
                            0,
                            parsedMax);
                    }
                }

                return ServerStatus.Online(
                    online,
                    max);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return ServerStatus.Offline(
                    "Тайм-аут");
            }
            catch (SocketException)
            {
                return ServerStatus.Offline(
                    "Нет соединения");
            }
            catch (IOException)
            {
                return ServerStatus.Offline(
                    "Нет соединения");
            }
            catch (JsonException)
            {
                return ServerStatus.Offline(
                    "Некорректный ответ");
            }
        }

        private static byte[] BuildHandshakePayload(
            int protocolVersion,
            string host,
            int port)
        {
            using MemoryStream stream =
                new MemoryStream();

            WriteVarInt(
                stream,
                protocolVersion);

            WriteString(
                stream,
                host);

            stream.WriteByte(
                (byte)((port >> 8) & 0xFF));

            stream.WriteByte(
                (byte)(port & 0xFF));

            WriteVarInt(
                stream,
                1);

            return stream.ToArray();
        }

        private static async Task WriteVarIntPacketAsync(
            NetworkStream stream,
            int packetId,
            byte[] payload,
            CancellationToken cancellationToken)
        {
            using MemoryStream packet =
                new MemoryStream();

            WriteVarInt(
                packet,
                packetId);

            if (payload.Length > 0)
            {
                packet.Write(
                    payload,
                    0,
                    payload.Length);
            }

            byte[] packetData =
                packet.ToArray();

            using MemoryStream output =
                new MemoryStream();

            WriteVarInt(
                output,
                packetData.Length);

            byte[] lengthData =
                output.ToArray();

            await stream.WriteAsync(
                lengthData,
                cancellationToken);

            await stream.WriteAsync(
                packetData,
                cancellationToken);

            await stream.FlushAsync(
                cancellationToken);
        }

        private static async Task<int> ReadVarIntAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            int result = 0;
            int shift = 0;

            for (int i = 0; i < 5; i++)
            {
                byte[] oneByte =
                    await ReadExactAsync(
                        stream,
                        1,
                        cancellationToken);

                byte value =
                    oneByte[0];

                result |=
                    (value & 0x7F) << shift;

                if ((value & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }

            throw new InvalidDataException(
                "Слишком длинный VarInt.");
        }

        private static int ReadVarInt(
            byte[] data,
            ref int offset)
        {
            int result = 0;
            int shift = 0;

            for (int i = 0; i < 5; i++)
            {
                if (offset >= data.Length)
                {
                    throw new InvalidDataException(
                        "Неожиданный конец VarInt.");
                }

                byte value =
                    data[offset++];

                result |=
                    (value & 0x7F) << shift;

                if ((value & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }

            throw new InvalidDataException(
                "Слишком длинный VarInt.");
        }

        private static async Task<byte[]> ReadExactAsync(
            NetworkStream stream,
            int length,
            CancellationToken cancellationToken)
        {
            byte[] buffer =
                new byte[length];

            int offset = 0;

            while (offset < length)
            {
                int read =
                    await stream.ReadAsync(
                        buffer.AsMemory(
                            offset,
                            length - offset),
                        cancellationToken);

                if (read == 0)
                {
                    throw new IOException(
                        "Соединение с сервером закрыто.");
                }

                offset += read;
            }

            return buffer;
        }

        private static void WriteVarInt(
            Stream stream,
            int value)
        {
            uint unsignedValue =
                unchecked((uint)value);

            while (true)
            {
                if ((unsignedValue & ~0x7Fu) == 0)
                {
                    stream.WriteByte(
                        (byte)unsignedValue);

                    return;
                }

                stream.WriteByte(
                    (byte)((unsignedValue & 0x7F) | 0x80));

                unsignedValue >>= 7;
            }
        }

        private static void WriteString(
            Stream stream,
            string value)
        {
            byte[] bytes =
                Encoding.UTF8.GetBytes(value);

            WriteVarInt(
                stream,
                bytes.Length);

            stream.Write(
                bytes,
                0,
                bytes.Length);
        }

        private static void ParseServerAddress(
            string address,
            out string host,
            out int port)
        {
            address =
                address.Trim();

            if (address.StartsWith("[", StringComparison.Ordinal))
            {
                int closingBracket =
                    address.IndexOf(']');

                if (closingBracket <= 0)
                {
                    throw new FormatException(
                        "Некорректный IPv6-адрес сервера.");
                }

                host =
                    address.Substring(
                        1,
                        closingBracket - 1);

                port =
                    DefaultMinecraftPort;

                if (address.Length > closingBracket + 1 &&
                    address[closingBracket + 1] == ':')
                {
                    if (!int.TryParse(
                        address.Substring(closingBracket + 2),
                        out port))
                    {
                        throw new FormatException(
                            "Некорректный порт сервера.");
                    }
                }
            }
            else
            {
                int firstColon =
                    address.IndexOf(':');

                int lastColon =
                    address.LastIndexOf(':');

                if (firstColon > 0 &&
                    firstColon == lastColon)
                {
                    host =
                        address.Substring(
                            0,
                            firstColon);

                    if (!int.TryParse(
                        address.Substring(firstColon + 1),
                        out port))
                    {
                        throw new FormatException(
                            "Некорректный порт сервера.");
                    }
                }
                else
                {
                    host = address;
                    port = DefaultMinecraftPort;
                }
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                throw new FormatException(
                    "IP/домен сервера не указан.");
            }

            if (port < 1 || port > 65535)
            {
                throw new FormatException(
                    "Порт сервера должен быть от 1 до 65535.");
            }
        }


        // ============================================================
        // ПОИСК ELYPRISM LAUNCHER
        // ============================================================

        private static string? FindElyPrismLauncher()
        {
            if (File.Exists(ElyPrismLauncherPath))
            {
                return ElyPrismLauncherPath;
            }

            if (!Directory.Exists(
                ElyPrismLauncherDirectory))
            {
                return null;
            }

            try
            {
                string[] executables =
                    Directory.GetFiles(
                        ElyPrismLauncherDirectory,
                        "*.exe",
                        SearchOption.AllDirectories);

                string? ely =
                    executables.FirstOrDefault(
                        x =>
                            string.Equals(
                                Path.GetFileName(x),
                                "elyprismlauncher.exe",
                                StringComparison.OrdinalIgnoreCase));

                if (ely != null)
                {
                    return ely;
                }
            }
            catch
            {
            }

            return null;
        }

        // ============================================================
        // ПРОВЕРКА СБОРКИ
        // ============================================================

        private static bool IsMinecraftInstalled()
        {
            if (!Directory.Exists(InstancePath))
            {
                return false;
            }

            string instanceConfigPath =
                Path.Combine(
                    InstancePath,
                    "instance.cfg");

            string mmcPackPath =
                Path.Combine(
                    InstancePath,
                    "mmc-pack.json");

            string packIgnorePath =
                Path.Combine(
                    InstancePath,
                    ".packignore");

            if (!File.Exists(instanceConfigPath))
            {
                return false;
            }

            if (!File.Exists(mmcPackPath))
            {
                return false;
            }

            if (!File.Exists(packIgnorePath))
            {
                return false;
            }

            if (!Directory.Exists(MinecraftPath))
            {
                return false;
            }

            string modsPath =
                Path.Combine(
                    MinecraftPath,
                    "mods");

            if (!Directory.Exists(modsPath))
            {
                return false;
            }

            bool hasMods =
                Directory.GetFiles(
                    modsPath,
                    "*.jar",
                    SearchOption.TopDirectoryOnly)
                .Length > 0;

            return hasMods;
        }

        // ============================================================
        // ГОТОВ К ИГРЕ
        // ============================================================

        private void SetReadyState()
        {
            string installedVersion =
                GetInstalledVersion();

            PlayButton.Content =
                "ИГРАТЬ";

            PlayButton.IsEnabled =
                true;

            UpdateButton.IsEnabled =
                true;

            UpdateStatusText.Text =
                "Сборка готова к запуску";

            BottomUpdateText.Text =
                $"Версия {installedVersion} установлена";

            CurrentVersionText.Text =
                $"Версия сборки: {installedVersion}";
        }

        // ============================================================
        // TITLE BAR
        // ============================================================

        private void TitleBar_MouseLeftButtonDown(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton ==
                System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        // ============================================================
        // ОКНО
        // ============================================================

        private void MinimizeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            WindowState =
                WindowState.Minimized;
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        // ============================================================
        // НАВИГАЦИЯ
        // ============================================================

        private void SetActiveNavigation(Button activeButton)
        {
            NavHome.Tag = string.Empty;
            NavAccount.Tag = string.Empty;
            NavSettings.Tag = string.Empty;

            activeButton.Tag = "active";
        }

        private void NavHome_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetActiveNavigation(NavHome);

            HomePage.Visibility =
                Visibility.Visible;

            AccountPage.Visibility =
                Visibility.Collapsed;

            SettingsPage.Visibility =
                Visibility.Collapsed;
        }

        private void NavAccount_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetActiveNavigation(NavAccount);

            HomePage.Visibility =
                Visibility.Collapsed;

            AccountPage.Visibility =
                Visibility.Visible;

            SettingsPage.Visibility =
                Visibility.Collapsed;

            LoadOfflineAccounts();
        }

        // ============================================================
        // OFFLINE ACCOUNTS
        // ============================================================

        private void LoadOfflineAccounts()
        {
            try
            {
                UpdateProfileDisplay();
                _offlineAccounts.Clear();

                if (!File.Exists(PineconeAccountsPath))
                {
                    RefreshOfflineAccountsList();
                    return;
                }

                using JsonDocument document =
                    JsonDocument.Parse(
                        File.ReadAllText(
                            PineconeAccountsPath,
                            Encoding.UTF8));

                if (!document.RootElement.TryGetProperty(
                        "accounts",
                        out JsonElement accounts) ||
                    accounts.ValueKind != JsonValueKind.Array)
                {
                    RefreshOfflineAccountsList();
                    return;
                }

                foreach (JsonElement account in accounts.EnumerateArray())
                {
                    if (!account.TryGetProperty(
                            "type",
                            out JsonElement typeElement) ||
                        !string.Equals(
                            typeElement.GetString(),
                            "Offline",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!account.TryGetProperty(
                            "profile",
                            out JsonElement profile))
                    {
                        continue;
                    }

                    string name =
                        profile.TryGetProperty(
                            "name",
                            out JsonElement nameElement)
                            ? nameElement.GetString() ?? ""
                            : "";

                    string uuid =
                        profile.TryGetProperty(
                            "id",
                            out JsonElement idElement)
                            ? idElement.GetString() ?? ""
                            : "";

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _offlineAccounts.Add(
                            new OfflineAccountInfo
                            {
                                Name = name,
                                Uuid = uuid
                            });
                    }
                }

                RefreshOfflineAccountsList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);

                _offlineAccounts.Clear();
                RefreshOfflineAccountsList();

                if (OfflineAccountStatusText != null)
                {
                    OfflineAccountStatusText.Text =
                        "Не удалось прочитать accounts.json.";
                }
            }
        }

        private void RefreshOfflineAccountsList()
        {
            if (OfflineAccountsList == null)
            {
                return;
            }

            string activeName =
                GetActiveOfflineAccountName();

            foreach (OfflineAccountInfo account in _offlineAccounts)
            {
                account.Status =
                    string.Equals(
                        account.Name,
                        activeName,
                        StringComparison.OrdinalIgnoreCase)
                        ? "ВЫБРАН"
                        : "";
            }

            OfflineAccountsList.ItemsSource = null;
            OfflineAccountsList.ItemsSource = _offlineAccounts;

            if (OfflineAccountStatusText != null)
            {
                OfflineAccountStatusText.Text =
                    _offlineAccounts.Count == 0
                        ? "Создай первый офлайн-профиль."
                        : $"Активный профиль: {activeName}";
            }

            UpdateProfileDisplay();
        }

        private void UpdateProfileDisplay()
        {
            if (ProfileNameText == null)
            {
                return;
            }

            string activeName = GetActiveOfflineAccountName();

            ProfileNameText.Text =
                string.IsNullOrWhiteSpace(activeName)
                    ? "Offline Player"
                    : activeName;
        }

        private string GetActiveOfflineAccountName()
        {
            try
            {
                if (!File.Exists(PineconeAccountsPath))
                {
                    return "";
                }

                using JsonDocument document =
                    JsonDocument.Parse(
                        File.ReadAllText(
                            PineconeAccountsPath,
                            Encoding.UTF8));

                if (!document.RootElement.TryGetProperty(
                        "accounts",
                        out JsonElement accounts) ||
                    accounts.ValueKind != JsonValueKind.Array)
                {
                    return "";
                }

                foreach (JsonElement account in accounts.EnumerateArray())
                {
                    if (account.TryGetProperty(
                            "active",
                            out JsonElement active) &&
                        active.ValueKind == JsonValueKind.True &&
                        account.TryGetProperty(
                            "type",
                            out JsonElement type) &&
                        string.Equals(
                            type.GetString(),
                            "Offline",
                            StringComparison.OrdinalIgnoreCase) &&
                        account.TryGetProperty(
                            "profile",
                            out JsonElement profile) &&
                        profile.TryGetProperty(
                            "name",
                            out JsonElement name))
                    {
                        return name.GetString() ?? "";
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private void OfflineAccountNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                AddOfflineAccountButton_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void AddOfflineAccountButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string name =
                OfflineAccountNameTextBox.Text.Trim();

            if (!IsValidMinecraftUsername(name))
            {
                OfflineAccountStatusText.Text =
                    "Ник: 3–16 символов, только A–Z, 0–9 и _.";

                return;
            }

            if (_offlineAccounts.Any(
                    x => string.Equals(
                        x.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase)))
            {
                OfflineAccountStatusText.Text =
                    "Такой профиль уже существует.";

                return;
            }

            try
            {
                AddOfflineAccountToPinecone(name);

                OfflineAccountNameTextBox.Clear();
                LoadOfflineAccounts();

                OfflineAccountStatusText.Text =
                    $"Профиль «{name}» создан и выбран.";

                await Task.Delay(1200);

                if (OfflineAccountStatusText != null)
                {
                    OfflineAccountStatusText.Text =
                        $"Активный профиль: {GetActiveOfflineAccountName()}";
                }
            }
            catch (Exception ex)
            {
                OfflineAccountStatusText.Text =
                    "Не удалось создать профиль.";

                MessageBox.Show(
                    "Не удалось создать офлайн-профиль.\n\n" +
                    $"Ошибка:\n{ex.Message}",
                    "Create: Vanilla++",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Debug.WriteLine(ex);
            }
        }

        private void SetOfflineAccountButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (OfflineAccountsList.SelectedItem is not OfflineAccountInfo selected)
            {
                OfflineAccountStatusText.Text =
                    "Сначала выбери профиль.";

                return;
            }

            try
            {
                SetActiveOfflineAccount(selected.Name);
                LoadOfflineAccounts();

                OfflineAccountStatusText.Text =
                    $"Активный профиль: {selected.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось выбрать профиль.\n\n" +
                    $"Ошибка:\n{ex.Message}",
                    "Create: Vanilla++",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Debug.WriteLine(ex);
            }
        }

        private void DeleteOfflineAccountButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (OfflineAccountsList.SelectedItem is not OfflineAccountInfo selected)
            {
                OfflineAccountStatusText.Text =
                    "Сначала выбери профиль.";

                return;
            }

            MessageBoxResult result =
                MessageBox.Show(
                    $"Удалить офлайн-профиль «{selected.Name}»?",
                    "Удаление профиля",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                DeleteOfflineAccount(selected.Name);
                LoadOfflineAccounts();

                OfflineAccountStatusText.Text =
                    "Профиль удалён.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось удалить офлайн-профиль.\n\n" +
                    $"Ошибка:\n{ex.Message}",
                    "Create: Vanilla++",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Debug.WriteLine(ex);
            }
        }

        private void OfflineAccountsList_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (OfflineAccountsList.SelectedItem is OfflineAccountInfo selected)
            {
                OfflineAccountStatusText.Text =
                    $"Выбран: {selected.Name}";
            }
        }

        private static bool IsValidMinecraftUsername(
            string name)
        {
            if (name.Length < 3 ||
                name.Length > 16)
            {
                return false;
            }

            foreach (char character in name)
            {
                bool valid =
                    character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' ||
                    character == '_';

                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetOfflineUuid(
            string username)
        {
            byte[] hash =
                MD5.HashData(
                    Encoding.UTF8.GetBytes(
                        $"OfflinePlayer:{username}"));

            hash[6] =
                (byte)((hash[6] & 0x0F) | 0x30);

            hash[8] =
                (byte)((hash[8] & 0x3F) | 0x80);

            return
                $"{hash[0]:x2}{hash[1]:x2}{hash[2]:x2}{hash[3]:x2}-" +
                $"{hash[4]:x2}{hash[5]:x2}-" +
                $"{hash[6]:x2}{hash[7]:x2}-" +
                $"{hash[8]:x2}{hash[9]:x2}-" +
                $"{hash[10]:x2}{hash[11]:x2}{hash[12]:x2}{hash[13]:x2}{hash[14]:x2}{hash[15]:x2}";
        }

        private void AddOfflineAccountToPinecone(
            string username)
        {
            JsonObject root =
                LoadAccountsJson();

            JsonArray accounts =
                root["accounts"] as JsonArray
                ?? new JsonArray();

            root["accounts"] = accounts;

            foreach (JsonNode? node in accounts)
            {
                if (node is not JsonObject account)
                {
                    continue;
                }

                JsonObject? profile =
                    account["profile"] as JsonObject;

                string? existingName =
                    profile?["name"]?.GetValue<string>();

                if (string.Equals(
                        account["type"]?.GetValue<string>(),
                        "Offline",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        existingName,
                        username,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Профиль с таким ником уже существует.");
                }
            }

            foreach (JsonNode? node in accounts)
            {
                if (node is JsonObject account)
                {
                    account["active"] = false;
                }
            }

            accounts.Add(
                new JsonObject
                {
                    ["active"] = true,
                    ["profile"] =
                        new JsonObject
                        {
                            ["capes"] = new JsonArray(),
                            ["id"] = GetOfflineUuid(username),
                            ["name"] = username,
                            ["skin"] =
                                new JsonObject
                                {
                                    ["id"] = "",
                                    ["url"] = "",
                                    ["variant"] = ""
                                }
                        },
                    ["type"] = "Offline",
                    ["ygg"] =
                        new JsonObject
                        {
                            ["extra"] =
                                new JsonObject
                                {
                                    ["clientToken"] =
                                        Guid.NewGuid().ToString("N"),
                                    ["userName"] = username
                                },
                            ["iat"] =
                                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            ["token"] = "0"
                        }
                });

            root["formatVersion"] = 3;

            SaveAccountsJson(root);
        }

        private void SetActiveOfflineAccount(
            string username)
        {
            JsonObject root =
                LoadAccountsJson();

            if (root["accounts"] is not JsonArray accounts)
            {
                throw new InvalidDataException(
                    "accounts.json не содержит список аккаунтов.");
            }

            bool found = false;

            foreach (JsonNode? node in accounts)
            {
                if (node is not JsonObject account)
                {
                    continue;
                }

                JsonObject? profile =
                    account["profile"] as JsonObject;

                string? name =
                    profile?["name"]?.GetValue<string>();

                bool selected =
                    string.Equals(
                        account["type"]?.GetValue<string>(),
                        "Offline",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        name,
                        username,
                        StringComparison.OrdinalIgnoreCase);

                account["active"] = selected;

                if (selected)
                {
                    found = true;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    "Профиль не найден.");
            }

            SaveAccountsJson(root);
        }

        private void DeleteOfflineAccount(
            string username)
        {
            JsonObject root =
                LoadAccountsJson();

            if (root["accounts"] is not JsonArray accounts)
            {
                return;
            }

            JsonNode? target = null;

            foreach (JsonNode? node in accounts.ToList())
            {
                if (node is not JsonObject account)
                {
                    continue;
                }

                JsonObject? profile =
                    account["profile"] as JsonObject;

                string? name =
                    profile?["name"]?.GetValue<string>();

                if (string.Equals(
                        account["type"]?.GetValue<string>(),
                        "Offline",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        name,
                        username,
                        StringComparison.OrdinalIgnoreCase))
                {
                    target = node;
                    break;
                }
            }

            if (target != null)
            {
                accounts.Remove(target);
            }

            if (accounts.Count > 0 &&
                !accounts.Any(
                    node =>
                        node is JsonObject account &&
                        account["active"]?.GetValue<bool>() == true))
            {
                JsonObject? firstOffline =
                    accounts
                        .OfType<JsonObject>()
                        .FirstOrDefault(
                            account =>
                                string.Equals(
                                    account["type"]?.GetValue<string>(),
                                    "Offline",
                                    StringComparison.OrdinalIgnoreCase));

                if (firstOffline != null)
                {
                    firstOffline["active"] = true;
                }
            }

            SaveAccountsJson(root);
        }

        private static JsonObject LoadAccountsJson()
        {
            Directory.CreateDirectory(
                ElyPrismDataDirectory);

            if (!File.Exists(PineconeAccountsPath))
            {
                return new JsonObject
                {
                    ["accounts"] = new JsonArray(),
                    ["formatVersion"] = 3
                };
            }

            string json =
                File.ReadAllText(
                    PineconeAccountsPath,
                    Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonObject
                {
                    ["accounts"] = new JsonArray(),
                    ["formatVersion"] = 3
                };
            }

            JsonNode? node =
                JsonNode.Parse(json);

            if (node is not JsonObject root)
            {
                throw new InvalidDataException(
                    "accounts.json имеет неправильный формат.");
            }

            if (root["accounts"] is null)
            {
                root["accounts"] = new JsonArray();
            }

            root["formatVersion"] = 3;

            return root;
        }

        private static void SaveAccountsJson(
            JsonObject root)
        {
            Directory.CreateDirectory(
                ElyPrismDataDirectory);

            string tempPath =
                PineconeAccountsPath + ".tmp";

            string backupPath =
                PineconeAccountsPath + ".bak";

            string json =
                root.ToJsonString(
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            File.WriteAllText(
                tempPath,
                json,
                new UTF8Encoding(false));

            if (File.Exists(PineconeAccountsPath))
            {
                File.Copy(
                    PineconeAccountsPath,
                    backupPath,
                    true);
            }

            File.Move(
                tempPath,
                PineconeAccountsPath,
                true);
        }

        private void NavSettings_Click(
            object sender,
            RoutedEventArgs e)
        {
            SetActiveNavigation(NavSettings);

            HomePage.Visibility =
                Visibility.Collapsed;

            AccountPage.Visibility =
                Visibility.Collapsed;

            SettingsPage.Visibility =
                Visibility.Visible;

            PopulateSettingsUi();
        }

        // ============================================================
        // НАСТРОЙКИ — ЗАГРУЗКА / СОХРАНЕНИЕ
        // ============================================================

        private void LoadLauncherSettings()
        {
            try
            {
                Directory.CreateDirectory(
                    LauncherSettingsDirectory);

                if (!File.Exists(LauncherSettingsPath))
                {
                    _launcherSettings = new LauncherSettings();
                    return;
                }

                string json =
                    File.ReadAllText(
                        LauncherSettingsPath,
                        Encoding.UTF8);

                LauncherSettings? settings =
                    JsonSerializer.Deserialize<LauncherSettings>(
                        json);

                _launcherSettings =
                    settings ?? new LauncherSettings();

                NormalizeLauncherSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "Не удалось загрузить настройки лаунчера:");

                Debug.WriteLine(ex);
                _launcherSettings =
                    new LauncherSettings();
            }
        }

        private void SaveLauncherSettings()
        {
            try
            {
                NormalizeLauncherSettings();

                Directory.CreateDirectory(
                    LauncherSettingsDirectory);

                string tempPath =
                    LauncherSettingsPath + ".tmp";

                string json =
                    JsonSerializer.Serialize(
                        _launcherSettings,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                File.WriteAllText(
                    tempPath,
                    json,
                    new UTF8Encoding(false));

                File.Move(
                    tempPath,
                    LauncherSettingsPath,
                    true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "Не удалось сохранить настройки лаунчера:");

                Debug.WriteLine(ex);
            }
        }

        private void NormalizeLauncherSettings()
        {
            if (_launcherSettings.CustomMemoryGb < 2)
            {
                _launcherSettings.CustomMemoryGb = 2;
            }

            if (_launcherSettings.CustomMemoryGb > 32)
            {
                _launcherSettings.CustomMemoryGb = 32;
            }

            int[] allowedRefresh = { 0, 10, 30, 60, 120 };

            if (!allowedRefresh.Contains(
                    _launcherSettings.ServerRefreshSeconds))
            {
                _launcherSettings.ServerRefreshSeconds = 30;
            }

            if (!new[] { "auto", "4", "6", "8", "10", "12", "custom" }
                    .Contains(_launcherSettings.MemoryMode))
            {
                _launcherSettings.MemoryMode = "auto";
            }

            if (!new[] { "default", "1280x720", "1600x900", "1920x1080" }
                    .Contains(_launcherSettings.Resolution))
            {
                _launcherSettings.Resolution = "default";
            }

            if (string.IsNullOrWhiteSpace(_launcherSettings.JavaPath))
            {
                _launcherSettings.JavaPath = "";
                _launcherSettings.JavaAuto = true;
            }
        }

        private void PopulateSettingsUi()
        {
            if (SettingsMemoryComboBox == null)
            {
                return;
            }

            NormalizeLauncherSettings();

            SettingsMemoryComboBox.SelectedValue =
                _launcherSettings.MemoryMode;

            SettingsCustomMemoryTextBox.Text =
                _launcherSettings.CustomMemoryGb.ToString();

            SettingsJavaAutoCheckBox.IsChecked =
                _launcherSettings.JavaAuto;

            SettingsJavaPathTextBox.Text =
                _launcherSettings.JavaPath;

            SettingsJavaPathTextBox.IsEnabled =
                !_launcherSettings.JavaAuto;

            SettingsBrowseJavaButton.IsEnabled =
                !_launcherSettings.JavaAuto;

            SettingsCheckUpdatesStartCheckBox.IsChecked =
                _launcherSettings.CheckUpdatesOnLauncherStart;

            SettingsCheckUpdatesLaunchCheckBox.IsChecked =
                _launcherSettings.CheckUpdatesBeforeLaunch;

            SettingsCloseAfterLaunchCheckBox.IsChecked =
                _launcherSettings.CloseLauncherAfterLaunch;

            SettingsResolutionComboBox.SelectedValue =
                _launcherSettings.Resolution;

            SettingsMaximizedCheckBox.IsChecked =
                _launcherSettings.LaunchMaximized;

            SettingsServerRefreshComboBox.SelectedValue =
                _launcherSettings.ServerRefreshSeconds.ToString();

            UpdateSettingsMemoryUi();
            UpdateSettingsStatusUi();
        }

        private void UpdateSettingsMemoryUi()
        {
            bool custom =
                string.Equals(
                    _launcherSettings.MemoryMode,
                    "custom",
                    StringComparison.OrdinalIgnoreCase);

            SettingsCustomMemoryTextBox.IsEnabled = custom;
            SettingsCustomMemoryTextBox.Opacity = custom ? 1.0 : 0.55;
        }

        private void UpdateSettingsStatusUi()
        {
            if (SettingsSaveStatusText == null)
            {
                return;
            }

            string memoryText =
                _launcherSettings.MemoryMode == "auto"
                    ? "Автоматическая память"
                    : _launcherSettings.MemoryMode == "custom"
                        ? $"Память: {_launcherSettings.CustomMemoryGb} ГБ"
                        : $"Память: {_launcherSettings.MemoryMode} ГБ";

            string javaText =
                _launcherSettings.JavaAuto
                    ? "Java: автоматически"
                    : "Java: собственный путь";

            SettingsSaveStatusText.Text =
                $"{memoryText}  •  {javaText}";
        }

        private void SaveSettingsAndApply()
        {
            if (_loadingSettings)
            {
                return;
            }

            NormalizeLauncherSettings();
            SaveLauncherSettings();

            try
            {
                ApplyLauncherSettingsToInstance();
                UpdateSettingsStatusUi();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "Не удалось применить настройки к инстансу:");
                Debug.WriteLine(ex);
            }
        }

        private void SettingsMemoryComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loadingSettings ||
                SettingsMemoryComboBox.SelectedValue == null)
            {
                return;
            }

            _launcherSettings.MemoryMode =
                SettingsMemoryComboBox.SelectedValue.ToString() ?? "auto";

            UpdateSettingsMemoryUi();
            SaveSettingsAndApply();
        }

        private void SettingsCustomMemoryTextBox_LostFocus(
            object sender,
            RoutedEventArgs e)
        {
            if (_loadingSettings)
            {
                return;
            }

            if (!int.TryParse(
                    SettingsCustomMemoryTextBox.Text.Trim(),
                    out int gb))
            {
                gb = 8;
            }

            _launcherSettings.CustomMemoryGb =
                Math.Clamp(gb, 2, 32);

            SettingsCustomMemoryTextBox.Text =
                _launcherSettings.CustomMemoryGb.ToString();

            SaveSettingsAndApply();
        }

        private void SettingsJavaAutoCheckBox_Checked(
            object sender, RoutedEventArgs e)
        {
            if (_loadingSettings)
            {
                return;
            }

            _launcherSettings.JavaAuto = true;
            SettingsJavaPathTextBox.IsEnabled = false;
            SettingsBrowseJavaButton.IsEnabled = false;
            SaveSettingsAndApply();
        }

        private void SettingsJavaAutoCheckBox_Unchecked(
            object sender, RoutedEventArgs e)
        {
            if (_loadingSettings)
            {
                return;
            }

            _launcherSettings.JavaAuto = false;
            SettingsJavaPathTextBox.IsEnabled = true;
            SettingsBrowseJavaButton.IsEnabled = true;
            SaveSettingsAndApply();
        }

        private void SettingsJavaPathTextBox_LostFocus(
            object sender, RoutedEventArgs e)
        {
            if (_loadingSettings)
            {
                return;
            }

            _launcherSettings.JavaPath =
                SettingsJavaPathTextBox.Text.Trim();

            SaveSettingsAndApply();
        }

        private void SettingsBrowseJavaButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog =
                new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Выбери java.exe",
                    Filter = "Java (java.exe)|java.exe|Все файлы|*.*",
                    CheckFileExists = true
                };

            if (dialog.ShowDialog() == true)
            {
                _launcherSettings.JavaAuto = false;
                _launcherSettings.JavaPath = dialog.FileName;

                SettingsJavaAutoCheckBox.IsChecked = false;
                SettingsJavaPathTextBox.Text = dialog.FileName;
                SettingsJavaPathTextBox.IsEnabled = true;
                SettingsBrowseJavaButton.IsEnabled = true;

                SaveSettingsAndApply();
            }
        }

        private void SettingsCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_loadingSettings)
            {
                return;
            }

            _launcherSettings.CheckUpdatesOnLauncherStart =
                SettingsCheckUpdatesStartCheckBox.IsChecked == true;

            _launcherSettings.CheckUpdatesBeforeLaunch =
                SettingsCheckUpdatesLaunchCheckBox.IsChecked == true;

            _launcherSettings.CloseLauncherAfterLaunch =
                SettingsCloseAfterLaunchCheckBox.IsChecked == true;

            SaveSettingsAndApply();
        }

        private void SettingsResolutionComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loadingSettings ||
                SettingsResolutionComboBox.SelectedValue == null)
            {
                return;
            }

            _launcherSettings.Resolution =
                SettingsResolutionComboBox.SelectedValue.ToString() ?? "default";

            SaveSettingsAndApply();
        }

        private void SettingsMaximizedCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (_loadingSettings)
            {
                return;
            }

            _launcherSettings.LaunchMaximized =
                SettingsMaximizedCheckBox.IsChecked == true;

            SaveSettingsAndApply();
        }

        private void SettingsServerRefreshComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loadingSettings ||
                SettingsServerRefreshComboBox.SelectedValue == null)
            {
                return;
            }

            if (int.TryParse(
                    SettingsServerRefreshComboBox.SelectedValue.ToString(),
                    out int seconds))
            {
                _launcherSettings.ServerRefreshSeconds = seconds;
                SaveLauncherSettings();

                RestartServerStatusMonitoring();
            }
        }

        private void RestartServerStatusMonitoring()
        {
            try
            {
                _serverStatusCancellation?.Cancel();
                _serverStatusCancellation?.Dispose();
                _serverStatusCancellation = null;
                _serverStatusMonitoringStarted = false;

                if (_launcherSettings.ServerRefreshSeconds > 0)
                {
                    StartServerStatusMonitoring();
                }
                else
                {
                    UpdateServerStatusUi(
                        ServerStatus.NotConfigured());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось открыть папку.\n\n{ex.Message}",
                    "Create: Vanilla++",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenPineconeFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFolder(ElyPrismDataDirectory);
        }

        private void OpenMinecraftFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFolder(MinecraftPath);
        }

        private void OpenBackupFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFolder(BackupRoot);
        }

        private async void VerifyInstallationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await CheckInstallationAsync();

            MessageBox.Show(
                IsMinecraftInstalled()
                    ? "Основные файлы сборки найдены.\n\nГлубокая проверка содержимого модов не выполняется этим инструментом."
                    : "Сборка не прошла базовую проверку.\n\nВернись на главную страницу и установи или восстанови сборку.",
                "Проверка сборки",
                MessageBoxButton.OK,
                IsMinecraftInstalled()
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }

        private void ResetLauncherSettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult result =
                MessageBox.Show(
                    "Сбросить настройки лаунчера к значениям по умолчанию?\n\nАккаунты и файлы Minecraft не будут удалены.",
                    "Сброс настроек",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _launcherSettings =
                new LauncherSettings();

            SaveLauncherSettings();

            _loadingSettings = true;
            PopulateSettingsUi();
            _loadingSettings = false;

            ApplyLauncherSettingsToInstance();
            RestartServerStatusMonitoring();

            MessageBox.Show(
                "Настройки лаунчера сброшены.",
                "Create: Vanilla++",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ApplyLauncherSettingsToInstance()
        {
            if (!File.Exists(
                    Path.Combine(
                        InstancePath,
                        "instance.cfg")))
            {
                return;
            }

            string cfgPath =
                Path.Combine(
                    InstancePath,
                    "instance.cfg");

            List<string> lines =
                File.ReadAllLines(
                    cfgPath,
                    Encoding.UTF8).ToList();

            void RemoveKey(string key)
            {
                lines.RemoveAll(
                    line => line.StartsWith(
                        key + "=",
                        StringComparison.OrdinalIgnoreCase));
            }

            void SetKey(string key, string value)
            {
                RemoveKey(key);
                lines.Add($"{key}={value}");
            }

            // Память
            if (_launcherSettings.MemoryMode == "auto")
            {
                RemoveKey("OverrideMemory");
                RemoveKey("MinMemAlloc");
                RemoveKey("MaxMemAlloc");
            }
            else
            {
                int gb =
                    _launcherSettings.MemoryMode == "custom"
                        ? _launcherSettings.CustomMemoryGb
                        : int.Parse(_launcherSettings.MemoryMode);

                int mb = gb * 1024;

                SetKey("OverrideMemory", "true");
                SetKey("MinMemAlloc", mb.ToString());
                SetKey("MaxMemAlloc", mb.ToString());
            }

            // Java
            if (_launcherSettings.JavaAuto)
            {
                RemoveKey("OverrideJavaLocation");
                RemoveKey("JavaPath");
                SetKey("AutomaticJava", "true");
            }
            else if (!string.IsNullOrWhiteSpace(_launcherSettings.JavaPath) &&
                     File.Exists(_launcherSettings.JavaPath))
            {
                SetKey("OverrideJavaLocation", "true");
                SetKey("JavaPath", _launcherSettings.JavaPath);
                SetKey("AutomaticJava", "false");
            }

            // Окно Minecraft
            if (_launcherSettings.Resolution == "default" &&
                !_launcherSettings.LaunchMaximized)
            {
                RemoveKey("OverrideWindow");
                RemoveKey("LaunchMaximized");
                RemoveKey("MinecraftWinWidth");
                RemoveKey("MinecraftWinHeight");
            }
            else
            {
                SetKey("OverrideWindow", "true");
                SetKey(
                    "LaunchMaximized",
                    _launcherSettings.LaunchMaximized ? "true" : "false");

                if (TryParseResolution(
                        _launcherSettings.Resolution,
                        out int width,
                        out int height))
                {
                    SetKey("MinecraftWinWidth", width.ToString());
                    SetKey("MinecraftWinHeight", height.ToString());
                }
            }

            File.WriteAllLines(
                cfgPath,
                lines,
                new UTF8Encoding(false));
        }

        private static bool TryParseResolution(
            string resolution,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;

            string[] parts =
                resolution.Split(
                    'x',
                    StringSplitOptions.RemoveEmptyEntries);

            return parts.Length == 2 &&
                   int.TryParse(parts[0], out width) &&
                   int.TryParse(parts[1], out height);
        }

        // ============================================================
        // PLAY / INSTALL
        // ============================================================

        private async void PlayButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                PlayButton.IsEnabled = false;

                UpdateStatusText.Text =
                    "Проверяем готовность к запуску...";

                BottomUpdateText.Text =
                    "Проверяем PineconeMC и Minecraft";

                string? launcherPath =
                    FindElyPrismLauncher();

                bool launcherInstalled =
                    launcherPath != null;

                bool modpackInstalled =
                    IsMinecraftInstalled();

                // ----------------------------------------------------
                // УСТАНОВКА
                // ----------------------------------------------------

                if (!launcherInstalled ||
                    !modpackInstalled)
                {
                    UpdateStatusText.Text =
                        "Устанавливаем недостающие компоненты...";

                    BottomUpdateText.Text =
                        "Подготавливаем PineconeMC и Create: Vanilla++";

                    await InstallMissingComponentsAsync();

                    launcherPath =
                        FindElyPrismLauncher();

                    launcherInstalled =
                        launcherPath != null;

                    modpackInstalled =
                        IsMinecraftInstalled();

                    if (!launcherInstalled ||
                        !modpackInstalled)
                    {
                        throw new InvalidOperationException(
                            "После установки необходимые компоненты " +
                            "не найдены.\n\n" +
                            "Ожидается:\n" +
                            "instance.cfg\n" +
                            "mmc-pack.json\n" +
                            ".packignore\n" +
                            "minecraft\\mods\\*.jar");
                    }

                    SetReadyState();

                    MessageBox.Show(
                        "Create: Vanilla++ успешно установлена!\n\n" +
                        "Теперь можно нажать «ИГРАТЬ».",
                        "Установка завершена",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                // ----------------------------------------------------
                // ПРОВЕРКА ОБНОВЛЕНИЯ ПЕРЕД ЗАПУСКОМ
                // ----------------------------------------------------

                if (_launcherSettings.CheckUpdatesBeforeLaunch)
                {
                    bool launchAllowed =
                        await CheckAndInstallUpdateBeforeLaunchAsync();

                    if (!launchAllowed)
                    {
                        return;
                    }
                }

                UpdateStatusText.Text =
                    "Подготавливаем запуск...";

                BottomUpdateText.Text =
                    "Применяем настройки Minecraft";

                ApplyLauncherSettingsToInstance();

                // ----------------------------------------------------
                // ЗАПУСК
                // ----------------------------------------------------

                UpdateStatusText.Text =
                    "Запускаем Minecraft...";

                BottomUpdateText.Text =
                    "Передаём запуск PineconeMC";

                LaunchPinecone(
                    launcherPath!);

                UpdateStatusText.Text =
                    "Minecraft запускается";

                BottomUpdateText.Text =
                    "PineconeMC запущен";

                if (_launcherSettings.CloseLauncherAfterLaunch)
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text =
                    "Ошибка";

                BottomUpdateText.Text =
                    "Операция не выполнена";

                MessageBox.Show(
                    "Произошла ошибка.\n\n" +
                    $"Ошибка:\n{ex.Message}",
                    "Create: Vanilla++",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Debug.WriteLine(ex);
            }
            finally
            {
                PlayButton.IsEnabled = true;
            }
        }

        private async Task<bool> CheckAndInstallUpdateBeforeLaunchAsync()
        {
            try
            {
                UpdateStatusText.Text =
                    "Проверяем обновление перед запуском...";

                GitHubRelease? release =
                    await GetLatestReleaseAsync();

                if (release == null)
                {
                    // Если GitHub временно недоступен, не блокируем запуск.
                    UpdateStatusText.Text =
                        "Проверка обновления недоступна — запускаем игру";
                    return true;
                }

                string installedVersion =
                    GetInstalledVersion();

                string latestVersion =
                    NormalizeVersion(release.TagName);

                if (!IsNewerVersion(
                        latestVersion,
                        installedVersion))
                {
                    return true;
                }

                GitHubAsset? updateAsset =
                    release.Assets.FirstOrDefault(
                        x => string.Equals(
                            x.Name,
                            UpdateAssetName,
                            StringComparison.OrdinalIgnoreCase));

                if (updateAsset == null ||
                    string.IsNullOrWhiteSpace(updateAsset.BrowserDownloadUrl))
                {
                    MessageBox.Show(
                        $"Для версии {latestVersion} найден релиз, но update.zip недоступен.\n\nИгра будет запущена без обновления.",
                        "Обновление",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return true;
                }

                MessageBoxResult result =
                    MessageBox.Show(
                        $"Доступно обновление Create: Vanilla++ {latestVersion}.\n\n" +
                        $"Сейчас установлена версия {installedVersion}.\n\n" +
                        "Установить обновление перед запуском?",
                        "Доступно обновление",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return true;
                }

                return await InstallUpdateAsync(
                    updateAsset.BrowserDownloadUrl,
                    latestVersion);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "Ошибка проверки обновления перед запуском:");
                Debug.WriteLine(ex);

                MessageBox.Show(
                    "Не удалось проверить обновление.\n\n" +
                    "Minecraft будет запущен без обновления.\n\n" +
                    $"Ошибка: {ex.Message}",
                    "Проверка обновления",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return true;
            }
        }

        // ============================================================
        // УСТАНОВКА ОТСУТСТВУЮЩИХ КОМПОНЕНТОВ
        // ============================================================

        private async Task InstallMissingComponentsAsync()
        {
            UpdateStatusText.Text =
                "Подключаемся к GitHub...";

            BottomUpdateText.Text =
                "Получаем последний релиз...";

            GitHubRelease? release =
                await GetLatestReleaseAsync();

            if (release == null)
            {
                throw new InvalidOperationException(
                    "Не удалось получить последний GitHub Release.");
            }

            string releaseVersion =
                NormalizeVersion(
                    release.TagName);

            // ========================================================
            // PINECONEMC
            // ========================================================

            if (FindElyPrismLauncher() == null)
            {
                GitHubAsset? pinecone =
                    release.Assets.FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Name,
                                PineconeSetupAssetName,
                                StringComparison.OrdinalIgnoreCase));

                if (pinecone == null)
                {
                    string availableAssets =
                        string.Join(
                            "\n",
                            release.Assets
                                .Where(x =>
                                    !string.IsNullOrWhiteSpace(x.Name))
                                .Select(x => x.Name));

                    throw new InvalidOperationException(
                        $"В GitHub Release не найден файл:\n" +
                        $"{PineconeSetupAssetName}\n\n" +
                        $"Файлы, которые реально вернул GitHub:\n" +
                        availableAssets);
                }

                if (string.IsNullOrWhiteSpace(
                    pinecone.BrowserDownloadUrl))
                {
                    throw new InvalidOperationException(
                        $"GitHub нашёл {PineconeSetupAssetName}, " +
                        "но не вернул ссылку на скачивание.");
                }

                await InstallPineconeAsync(
                    pinecone.BrowserDownloadUrl);
            }

            // ========================================================
            // СБОРКА
            // ========================================================

            if (!IsMinecraftInstalled())
            {
                GitHubAsset? install =
                    release.Assets.FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Name,
                                InstallAssetName,
                                StringComparison.OrdinalIgnoreCase));

                if (install == null)
                {
                    string availableAssets =
                        string.Join(
                            "\n",
                            release.Assets
                                .Where(x =>
                                    !string.IsNullOrWhiteSpace(x.Name))
                                .Select(x => x.Name));

                    throw new InvalidOperationException(
                        "В GitHub Release не найден install.zip.\n\n" +
                        "Файлы, которые реально вернул GitHub:\n" +
                        availableAssets);
                }

                if (string.IsNullOrWhiteSpace(
                    install.BrowserDownloadUrl))
                {
                    throw new InvalidOperationException(
                        "GitHub нашёл install.zip, " +
                        "но не вернул ссылку на скачивание.");
                }

                await InstallModpackAsync(
                    install.BrowserDownloadUrl,
                    releaseVersion);
            }
        }

        // ============================================================
        // УСТАНОВКА PINECONEMC
        // ============================================================

        private async Task InstallPineconeAsync(
            string downloadUrl)
        {
            Directory.CreateDirectory(
                TempDirectory);

            string installerPath =
                Path.Combine(
                    TempDirectory,
                    PineconeSetupAssetName);

            try
            {
                if (File.Exists(installerPath))
                {
                    File.Delete(installerPath);
                }
            }
            catch
            {
            }

            UpdateStatusText.Text =
                "Скачиваем PineconeMC...";

            BottomUpdateText.Text =
                "Подготовка установщика...";

            await DownloadFileAsync(
                downloadUrl,
                installerPath,
                "PineconeMC");

            UpdateStatusText.Text =
                "Устанавливаем PineconeMC...";

            BottomUpdateText.Text =
                "Автоматическая установка...";

            await RunPineconeInstallerAsync(
                installerPath);

            UpdateStatusText.Text =
                "Проверяем PineconeMC...";

            BottomUpdateText.Text =
                "Ожидаем завершение установки...";

            bool installed =
                await WaitForElyPrismLauncherAsync(
                    TimeSpan.FromMinutes(2));

            if (!installed)
            {
                throw new InvalidOperationException(
                    "Установщик PineconeMC завершился, " +
                    "но elyprismlauncher.exe не найден.\n\n" +
                    "Ожидаемый путь:\n" +
                    ElyPrismLauncherPath);
            }

            try
            {
                if (File.Exists(installerPath))
                {
                    File.Delete(installerPath);
                }
            }
            catch
            {
            }
        }

        // ============================================================
        // АВТОМАТИЗАЦИЯ УСТАНОВЩИКА
        // ============================================================

        private async Task RunPineconeInstallerAsync(
            string installerPath)
        {
            ProcessStartInfo startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        installerPath,

                    WorkingDirectory =
                        Path.GetDirectoryName(
                            installerPath) ?? "",

                    UseShellExecute =
                        true
                };

            Process? installer =
                Process.Start(startInfo);

            if (installer == null)
            {
                throw new InvalidOperationException(
                    "Не удалось запустить PineconeMC Setup.");
            }

            DateTime timeout =
                DateTime.UtcNow.AddMinutes(5);

            bool installFinished = false;

            while (DateTime.UtcNow < timeout)
            {
                await Task.Delay(500);

                try
                {
                    installer.Refresh();

                    if (installer.HasExited)
                    {
                        installFinished = true;
                        break;
                    }
                }
                catch
                {
                }

                if (File.Exists(
                    ElyPrismLauncherPath))
                {
                    installFinished = true;
                    break;
                }

                try
                {
                    AutomationElement? window =
                        FindInstallerWindow(
                            installer.Id);

                    if (window == null)
                    {
                        continue;
                    }

                    bool accepted =
                        await ClickInstallerButtonAsync(
                            window,
                            new[]
                            {
                                "I Agree",
                                "I Accept",
                                "Accept",
                                "Agree",
                                "Принимаю",
                                "Согласен",
                                "Принять"
                            });

                    if (accepted)
                    {
                        await Task.Delay(700);
                        continue;
                    }

                    bool installed =
                        await ClickInstallerButtonAsync(
                            window,
                            new[]
                            {
                                "Install",
                                "Установить"
                            });

                    if (installed)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    bool next =
                        await ClickInstallerButtonAsync(
                            window,
                            new[]
                            {
                                "Next",
                                "Next >",
                                "Далее",
                                "Далее >",
                                "Continue",
                                "Продолжить"
                            });

                    if (next)
                    {
                        await Task.Delay(700);
                        continue;
                    }

                    bool finished =
                        await ClickInstallerButtonAsync(
                            window,
                            new[]
                            {
                                "Finish",
                                "Готово",
                                "Завершить"
                            });

                    if (finished)
                    {
                        await Task.Delay(1000);

                        if (File.Exists(
                            ElyPrismLauncherPath))
                        {
                            installFinished = true;
                            break;
                        }
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch
                {
                }
            }

            if (!installFinished &&
                !File.Exists(ElyPrismLauncherPath))
            {
                try
                {
                    if (!installer.HasExited)
                    {
                        installer.Kill();
                    }
                }
                catch
                {
                }

                throw new TimeoutException(
                    "Автоматическая установка PineconeMC " +
                    "не завершилась за 5 минут.");
            }

            try
            {
                if (!installer.HasExited)
                {
                    await Task.Run(() =>
                        installer.WaitForExit(10000));
                }
            }
            catch
            {
            }
        }

        // ============================================================
        // ПОИСК ОКНА УСТАНОВЩИКА
        // ============================================================

        private static AutomationElement? FindInstallerWindow(
            int processId)
        {
            AutomationElement root =
                AutomationElement.RootElement;

            System.Windows.Automation.Condition condition =
                new System.Windows.Automation.PropertyCondition(
                    AutomationElement.ProcessIdProperty,
                    processId);

            AutomationElementCollection windows =
                root.FindAll(
                    TreeScope.Children,
                    condition);

            for (int i = 0; i < windows.Count; i++)
            {
                AutomationElement window =
                    windows[i];

                try
                {
                    if (window.Current.ControlType ==
                        ControlType.Window)
                    {
                        return window;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        // ============================================================
        // НАЖАТИЕ КНОПКИ УСТАНОВЩИКА
        // ============================================================

        private static async Task<bool>
            ClickInstallerButtonAsync(
                AutomationElement window,
                string[] buttonNames)
        {
            AutomationElementCollection buttons =
                window.FindAll(
                    TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Button));

            for (int i = 0; i < buttons.Count; i++)
            {
                AutomationElement button =
                    buttons[i];

                try
                {
                    string name =
                        button.Current.Name?.Trim() ?? "";

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    bool match =
                        buttonNames.Any(
                            x =>
                                string.Equals(
                                    name,
                                    x,
                                    StringComparison.OrdinalIgnoreCase));

                    if (!match)
                    {
                        continue;
                    }

                    if (button.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out object pattern))
                    {
                        ((InvokePattern)pattern).Invoke();

                        await Task.Delay(300);

                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        // ============================================================
        // ОЖИДАНИЕ ELYPRISM
        // ============================================================

        private static async Task<bool>
            WaitForElyPrismLauncherAsync(
                TimeSpan timeout)
        {
            DateTime end =
                DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < end)
            {
                if (File.Exists(
                    ElyPrismLauncherPath))
                {
                    return true;
                }

                string? found =
                    FindElyPrismLauncher();

                if (found != null)
                {
                    return true;
                }

                await Task.Delay(1000);
            }

            return false;
        }

        // ============================================================
        // УСТАНОВКА СБОРКИ
        // ============================================================

        private async Task InstallModpackAsync(
            string downloadUrl,
            string version)
        {
            Directory.CreateDirectory(
                InstancesDirectory);

            Directory.CreateDirectory(
                InstancePath);

            Directory.CreateDirectory(
                TempDirectory);

            string zipPath =
                Path.Combine(
                    TempDirectory,
                    InstallAssetName);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            UpdateStatusText.Text =
                "Скачиваем сборку...";

            BottomUpdateText.Text =
                "Подготовка загрузки...";

            await DownloadFileAsync(
                downloadUrl,
                zipPath,
                "Сборка Create: Vanilla++");

            UpdateStatusText.Text =
                "Распаковываем сборку...";

            BottomUpdateText.Text =
                "Это может занять несколько минут...";

            string extractionDirectory =
                Path.Combine(
                    TempDirectory,
                    "install-extracted");

            if (Directory.Exists(
                extractionDirectory))
            {
                Directory.Delete(
                    extractionDirectory,
                    true);
            }

            Directory.CreateDirectory(
                extractionDirectory);

            ExtractZipSafely(
                zipPath,
                extractionDirectory);

            string sourceInstanceConfig =
                Path.Combine(
                    extractionDirectory,
                    "instance.cfg");

            string sourceMmcPack =
                Path.Combine(
                    extractionDirectory,
                    "mmc-pack.json");

            string sourcePackIgnore =
                Path.Combine(
                    extractionDirectory,
                    ".packignore");

            string sourceMinecraft =
                Path.Combine(
                    extractionDirectory,
                    "minecraft");

            // ========================================================
            // ПРОВЕРКИ
            // ========================================================

            if (!File.Exists(
                sourceInstanceConfig))
            {
                throw new InvalidDataException(
                    "install.zip имеет неправильную структуру.\n\n" +
                    "Не найден:\n" +
                    "instance.cfg");
            }

            if (!File.Exists(
                sourceMmcPack))
            {
                throw new InvalidDataException(
                    "install.zip имеет неправильную структуру.\n\n" +
                    "Не найден:\n" +
                    "mmc-pack.json");
            }

            if (!File.Exists(
                sourcePackIgnore))
            {
                throw new InvalidDataException(
                    "install.zip имеет неправильную структуру.\n\n" +
                    "Не найден:\n" +
                    ".packignore");
            }

            if (!Directory.Exists(
                sourceMinecraft))
            {
                throw new InvalidDataException(
                    "install.zip имеет неправильную структуру.\n\n" +
                    "Не найдена папка:\n" +
                    "minecraft\\");
            }

            string sourceMods =
                Path.Combine(
                    sourceMinecraft,
                    "mods");

            if (!Directory.Exists(sourceMods))
            {
                throw new InvalidDataException(
                    "В install.zip отсутствует:\n\n" +
                    "minecraft\\mods\\");
            }

            bool sourceHasMods =
                Directory.GetFiles(
                    sourceMods,
                    "*.jar",
                    SearchOption.TopDirectoryOnly)
                .Length > 0;

            if (!sourceHasMods)
            {
                throw new InvalidDataException(
                    "В install.zip не найдено ни одного мода.");
            }

            // ========================================================
            // УДАЛЯЕМ СТАРУЮ MINECRAFT ПАПКУ
            // ========================================================

            if (Directory.Exists(MinecraftPath))
            {
                Directory.Delete(
                    MinecraftPath,
                    true);
            }

            Directory.CreateDirectory(
                InstancePath);

            // ========================================================
            // КОПИРУЕМ ФАЙЛЫ ИНСТАНЦИИ
            // ========================================================

            File.Copy(
                sourceInstanceConfig,
                Path.Combine(
                    InstancePath,
                    "instance.cfg"),
                true);

            File.Copy(
                sourceMmcPack,
                Path.Combine(
                    InstancePath,
                    "mmc-pack.json"),
                true);

            File.Copy(
                sourcePackIgnore,
                Path.Combine(
                    InstancePath,
                    ".packignore"),
                true);

            // ========================================================
            // КОПИРУЕМ MINECRAFT
            // ========================================================

            UpdateStatusText.Text =
                "Устанавливаем Minecraft...";

            BottomUpdateText.Text =
                "Копируем моды и конфигурацию...";

            CopyDirectoryTree(
                sourceMinecraft,
                MinecraftPath);

            // ========================================================
            // ВЕРСИЯ
            // ========================================================

            SaveInstalledVersion(
                version);

            // ========================================================
            // ФИНАЛЬНАЯ ПРОВЕРКА
            // ========================================================

            if (!IsMinecraftInstalled())
            {
                throw new InvalidOperationException(
                    "Сборка распакована, но структура " +
                    "инстанции неправильная.\n\n" +
                    "Должно быть:\n\n" +
                    $"{InstancePath}\\instance.cfg\n" +
                    $"{InstancePath}\\mmc-pack.json\n" +
                    $"{InstancePath}\\.packignore\n" +
                    $"{MinecraftPath}\\mods\\*.jar");
            }

            // ========================================================
            // CLEANUP
            // ========================================================

            try
            {
                File.Delete(zipPath);

                if (Directory.Exists(
                    extractionDirectory))
                {
                    Directory.Delete(
                        extractionDirectory,
                        true);
                }
            }
            catch
            {
            }

            UpdateStatusText.Text =
                "Сборка установлена";

            BottomUpdateText.Text =
                $"Версия {version}";
        }

        // ============================================================
        // GITHUB
        // ============================================================

        private static async Task<GitHubRelease?>
            GetLatestReleaseAsync()
        {
            string url =
                $"https://api.github.com/repos/" +
                $"{GitHubOwner}/" +
                $"{GitHubRepository}/" +
                $"releases/latest";

            using HttpResponseMessage response =
                await HttpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json =
                await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<GitHubRelease>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }

        // ============================================================
        // DOWNLOAD
        // ============================================================

        private async Task DownloadFileAsync(
            string url,
            string destination,
            string description)
        {
            string? directory =
                Path.GetDirectoryName(destination);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using HttpResponseMessage response =
                await HttpClient.GetAsync(
                    url,
                    HttpCompletionOption.ResponseHeadersRead);

            response.EnsureSuccessStatusCode();

            long? totalBytes =
                response.Content.Headers.ContentLength;

            await using Stream input =
                await response.Content.ReadAsStreamAsync();

            await using FileStream output =
                new FileStream(
                    destination,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);

            byte[] buffer =
                new byte[1024 * 1024];

            long totalRead = 0;

            int bytesRead;

            while ((bytesRead =
                await input.ReadAsync(
                    buffer,
                    0,
                    buffer.Length)) > 0)
            {
                await output.WriteAsync(
                    buffer,
                    0,
                    bytesRead);

                totalRead += bytesRead;

                if (totalBytes.HasValue &&
                    totalBytes.Value > 0)
                {
                    double progress =
                        totalRead /
                        (double)totalBytes.Value *
                        100.0;

                    long downloadedMB =
                        totalRead /
                        1024 /
                        1024;

                    long totalMB =
                        totalBytes.Value /
                        1024 /
                        1024;

                    UpdateStatusText.Text =
                        $"Загрузка: {description}";

                    BottomUpdateText.Text =
                        $"{downloadedMB} / {totalMB} MB " +
                        $"({progress:0}%)";
                }
                else
                {
                    long downloadedMB =
                        totalRead /
                        1024 /
                        1024;

                    UpdateStatusText.Text =
                        $"Загрузка: {description}";

                    BottomUpdateText.Text =
                        $"{downloadedMB} MB";
                }
            }
        }

        // ============================================================
        // SAFE ZIP EXTRACTION
        // ============================================================

        private static void ExtractZipSafely(
            string zipPath,
            string destinationDirectory)
        {
            string fullDestination =
                Path.GetFullPath(
                    destinationDirectory);

            if (!fullDestination.EndsWith(
                Path.DirectorySeparatorChar))
            {
                fullDestination +=
                    Path.DirectorySeparatorChar;
            }

            using ZipArchive archive =
                ZipFile.OpenRead(zipPath);

            foreach (ZipArchiveEntry entry in
                archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(
                    entry.FullName))
                {
                    continue;
                }

                string destinationPath =
                    Path.GetFullPath(
                        Path.Combine(
                            destinationDirectory,
                            entry.FullName));

                if (!destinationPath.StartsWith(
                    fullDestination,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "ZIP содержит недопустимый путь:\n" +
                        entry.FullName);
                }

                if (entry.FullName.EndsWith(
                        "/",
                        StringComparison.Ordinal) ||
                    entry.FullName.EndsWith(
                        "\\",
                        StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(
                        destinationPath);

                    continue;
                }

                string? directory =
                    Path.GetDirectoryName(
                        destinationPath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(
                        directory);
                }

                entry.ExtractToFile(
                    destinationPath,
                    true);
            }
        }

        // ============================================================
        // КОПИРОВАНИЕ ДЕРЕВА
        // ============================================================

        private static void CopyDirectoryTree(
            string sourceDirectory,
            string destinationDirectory)
        {
            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Не найдена исходная папка:\n" +
                    sourceDirectory);
            }

            Directory.CreateDirectory(
                destinationDirectory);

            string[] directories;

            try
            {
                directories =
                    Directory.GetDirectories(
                        sourceDirectory,
                        "*",
                        SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "Не удалось прочитать структуру папки:\n" +
                    sourceDirectory +
                    "\n\n" +
                    ex.Message,
                    ex);
            }

            foreach (string directory in directories)
            {
                string relativePath =
                    Path.GetRelativePath(
                        sourceDirectory,
                        directory);

                string destination =
                    Path.Combine(
                        destinationDirectory,
                        relativePath);

                Directory.CreateDirectory(
                    destination);
            }

            string[] files;

            try
            {
                files =
                    Directory.GetFiles(
                        sourceDirectory,
                        "*",
                        SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "Не удалось получить список файлов:\n" +
                    sourceDirectory +
                    "\n\n" +
                    ex.Message,
                    ex);
            }

            foreach (string file in files)
            {
                string relativePath =
                    Path.GetRelativePath(
                        sourceDirectory,
                        file);

                string destination =
                    Path.Combine(
                        destinationDirectory,
                        relativePath);

                string? parent =
                    Path.GetDirectoryName(destination);

                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                try
                {
                    File.Copy(
                        file,
                        destination,
                        true);
                }
                catch (Exception ex)
                {
                    throw new IOException(
                        "Не удалось скопировать файл.\n\n" +
                        $"Источник:\n{file}\n\n" +
                        $"Назначение:\n{destination}\n\n" +
                        $"Ошибка:\n{ex.Message}",
                        ex);
                }
            }
        }

        // ============================================================
        // СОВМЕСТИМОСТЬ СО СТАРЫМИ МЕТОДАМИ
        // ============================================================

        private static void CopyDirectoryContents(
            string sourceDirectory,
            string destinationDirectory)
        {
            CopyDirectoryTree(
                sourceDirectory,
                destinationDirectory);
        }

        private static void CopyDirectory(
            string sourceDirectory,
            string destinationDirectory)
        {
            CopyDirectoryTree(
                sourceDirectory,
                destinationDirectory);
        }

        // ============================================================
        // ЗАПУСК MINECRAFT
        // ============================================================

        private static void LaunchPinecone(
            string launcherPath)
        {
            if (!File.Exists(launcherPath))
            {
                throw new FileNotFoundException(
                    "PineconeMC / ElyPrismLauncher не найден.",
                    launcherPath);
            }

            if (!Directory.Exists(InstancePath))
            {
                throw new DirectoryNotFoundException(
                    "Инстанс Create: Vanilla++ не найден:\n\n" +
                    InstancePath);
            }

            if (!IsMinecraftInstalled())
            {
                throw new InvalidOperationException(
                    "Инстанция Create: Vanilla++ неполная.\n\n" +
                    "Необходимы:\n" +
                    "instance.cfg\n" +
                    "mmc-pack.json\n" +
                    ".packignore\n" +
                    "minecraft\\mods\\*.jar");
            }

            ProcessStartInfo startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        launcherPath,

                    Arguments =
                        $"--launch \"{InstanceName}\"",

                    WorkingDirectory =
                        Path.GetDirectoryName(
                            launcherPath) ?? "",

                    UseShellExecute =
                        true
                };

            Process? process =
                Process.Start(startInfo);

            if (process == null)
            {
                throw new InvalidOperationException(
                    "Windows не смог запустить PineconeMC.");
            }
        }

        // ============================================================
        // UPDATE
        // ============================================================

        private async void UpdateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                UpdateButton.IsEnabled =
                    false;

                UpdateStatusText.Text =
                    "Проверяем обновления...";

                BottomUpdateText.Text =
                    "Подключение к GitHub...";

                GitHubRelease? release =
                    await GetLatestReleaseAsync();

                if (release == null)
                {
                    throw new InvalidOperationException(
                        "Не удалось получить последний GitHub Release.");
                }

                string installedVersion =
                    GetInstalledVersion();

                string latestVersion =
                    NormalizeVersion(
                        release.TagName);

                if (!IsNewerVersion(
                    latestVersion,
                    installedVersion))
                {
                    UpdateStatusText.Text =
                        "Установлена последняя версия";

                    BottomUpdateText.Text =
                        $"Версия {installedVersion} актуальна";

                    MessageBox.Show(
                        $"Установлена последняя версия.\n\n" +
                        $"Текущая версия: {installedVersion}\n" +
                        $"Последняя версия: {latestVersion}",
                        "Create: Vanilla++",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                GitHubAsset? updateAsset =
                    release.Assets.FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Name,
                                UpdateAssetName,
                                StringComparison.OrdinalIgnoreCase));

                if (updateAsset == null)
                {
                    throw new InvalidOperationException(
                        "В Release не найден update.zip.");
                }

                if (string.IsNullOrWhiteSpace(
                    updateAsset.BrowserDownloadUrl))
                {
                    throw new InvalidOperationException(
                        "GitHub нашёл update.zip, " +
                        "но не вернул ссылку на скачивание.");
                }

                MessageBoxResult result =
                    MessageBox.Show(
                        $"Доступно обновление!\n\n" +
                        $"Текущая версия: {installedVersion}\n" +
                        $"Новая версия: {latestVersion}\n\n" +
                        "Перед обновлением будет создана " +
                        "полная резервная копия сборки.\n\n" +
                        "Установить обновление?",
                        "Create: Vanilla++",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                bool updated =
                    await InstallUpdateAsync(
                        updateAsset.BrowserDownloadUrl,
                        latestVersion);

                if (updated)
                {
                    MessageBoxResult launchResult =
                        MessageBox.Show(
                            $"Обновление до версии {latestVersion} " +
                            "успешно установлено.\n\n" +
                            "Запустить Minecraft сейчас?",
                            "Create: Vanilla++",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);

                    if (launchResult ==
                        MessageBoxResult.Yes)
                    {
                        string? launcherPath =
                            FindElyPrismLauncher();

                        if (launcherPath != null)
                        {
                            ApplyLauncherSettingsToInstance();

                            LaunchPinecone(
                                launcherPath);

                            if (_launcherSettings.CloseLauncherAfterLaunch)
                            {
                                Close();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text =
                    "Ошибка обновления";

                BottomUpdateText.Text =
                    "Не удалось обновить сборку";

                MessageBox.Show(
                    "Ошибка обновления.\n\n" +
                    $"Ошибка:\n{ex.Message}",
                    "Create: Vanilla++",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Debug.WriteLine(ex);
            }
            finally
            {
                UpdateButton.IsEnabled =
                    true;
            }
        }

        // ============================================================
        // УСТАНОВКА UPDATE
        // ============================================================

        private async Task<bool> InstallUpdateAsync(
            string downloadUrl,
            string newVersion)
        {
            if (IsMinecraftRunning())
            {
                MessageBox.Show(
                    "Minecraft или Java сейчас запущены.\n\n" +
                    "Закрой Minecraft перед обновлением.",
                    "Обновление",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return false;
            }

            // ========================================================
            // BACKUP
            // ========================================================

            Directory.CreateDirectory(
                BackupRoot);

            string backupPath =
                Path.Combine(
                    BackupRoot,
                    DateTime.Now.ToString(
                        "yyyy-MM-dd_HH-mm-ss"));

            Directory.CreateDirectory(
                backupPath);

            UpdateStatusText.Text =
                "Создаём резервную копию...";

            BottomUpdateText.Text =
                "Сохраняем текущую версию...";

            BackupFullMinecraft(
                backupPath);

            // ========================================================
            // DOWNLOAD
            // ========================================================

            Directory.CreateDirectory(
                TempDirectory);

            string zipPath =
                Path.Combine(
                    TempDirectory,
                    UpdateAssetName);

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            UpdateStatusText.Text =
                "Скачиваем обновление...";

            BottomUpdateText.Text =
                "Подготовка загрузки...";

            await DownloadFileAsync(
                downloadUrl,
                zipPath,
                "Обновление");

            // ========================================================
            // EXTRACT
            // ========================================================

            string extractionDirectory =
                Path.Combine(
                    TempDirectory,
                    "update-extracted");

            if (Directory.Exists(
                extractionDirectory))
            {
                Directory.Delete(
                    extractionDirectory,
                    true);
            }

            Directory.CreateDirectory(
                extractionDirectory);

            UpdateStatusText.Text =
                "Распаковываем обновление...";

            BottomUpdateText.Text =
                "Проверяем файлы...";

            ExtractZipSafely(
                zipPath,
                extractionDirectory);

            // ========================================================
            // ПРОВЕРЯЕМ UPDATE
            // ========================================================

            string sourceMinecraft =
                Path.Combine(
                    extractionDirectory,
                    "minecraft");

            if (!Directory.Exists(sourceMinecraft))
            {
                throw new InvalidDataException(
                    "update.zip имеет неправильную структуру.\n\n" +
                    "Не найдена папка:\n" +
                    "minecraft\\");
            }

            // ========================================================
            // MANIFEST
            // ========================================================

            string manifestPath =
                Path.Combine(
                    extractionDirectory,
                    "manifest.json");

            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException(
                    "В update.zip не найден manifest.json.\n\n" +
                    "Для новых накопительных обновлений нужен " +
                    "manifest.json формата 2.");
            }

            string manifestJson =
                await File.ReadAllTextAsync(
                    manifestPath);

            UpdateManifest? manifest =
                JsonSerializer.Deserialize<UpdateManifest>(
                    manifestJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (manifest == null)
            {
                throw new InvalidDataException(
                    "Не удалось прочитать manifest.json.");
            }

            // ========================================================
            // ПРОВЕРКА ФОРМАТА MANIFEST
            // ========================================================

            if (manifest.FormatVersion != 2)
            {
                throw new InvalidDataException(
                    "Неподдерживаемый формат update.zip.\n\n" +
                    $"Формат: {manifest.FormatVersion}\n" +
                    "Ожидается формат: 2.");
            }

            if (string.IsNullOrWhiteSpace(manifest.BaseVersion))
            {
                throw new InvalidDataException(
                    "manifest.json не содержит baseVersion.");
            }

            if (string.IsNullOrWhiteSpace(manifest.TargetVersion))
            {
                throw new InvalidDataException(
                    "manifest.json не содержит targetVersion.");
            }

            string installedVersion =
                GetInstalledVersion();

            string manifestBaseVersion =
                NormalizeVersion(
                    manifest.BaseVersion);

            string manifestTargetVersion =
                NormalizeVersion(
                    manifest.TargetVersion);

            if (Version.TryParse(
                    manifestBaseVersion,
                    out Version? baseVersionNumber)
                &&
                Version.TryParse(
                    manifestTargetVersion,
                    out Version? targetVersionNumber)
                &&
                targetVersionNumber <= baseVersionNumber)
            {
                throw new InvalidDataException(
                    "manifest.json имеет некорректный диапазон версий.\n\n" +
                    $"Base: {manifestBaseVersion}\n" +
                    $"Target: {manifestTargetVersion}");
            }

            // Target в manifest должен совпадать с версией GitHub Release.
            if (!string.Equals(
                manifestTargetVersion,
                NormalizeVersion(newVersion),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Версия обновления не совпадает с manifest.json.\n\n" +
                    $"Версия Release: {newVersion}\n" +
                    $"Target в manifest: {manifestTargetVersion}");
            }

            // Накопительное обновление может применяться:
            // 1.1) непосредственно с baseVersion;
            // 2. с любой промежуточной версии между baseVersion и targetVersion.
            //
            // Но версию старше target или ниже base использовать нельзя.
            if (IsNewerVersion(
                installedVersion,
                manifestTargetVersion))
            {
                throw new InvalidOperationException(
                    "Установленная версия новее версии обновления.\n\n" +
                    $"Установлена: {installedVersion}\n" +
                    $"Обновление: {manifestTargetVersion}");
            }

            if (string.Equals(
                installedVersion,
                manifestTargetVersion,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsNewerVersion(
                manifestBaseVersion,
                installedVersion))
            {
                throw new InvalidOperationException(
                    "Это накопительное обновление не подходит для " +
                    "установленной версии.\n\n" +
                    $"Установлена: {installedVersion}\n" +
                    $"Базовая версия обновления: {manifestBaseVersion}\n" +
                    $"Целевая версия: {manifestTargetVersion}\n\n" +
                    "Сначала установите полную версию сборки, " +
                    "начиная с указанной базовой версии.");
            }

            // ========================================================
            // ПРИМЕНЕНИЕ ОБНОВЛЕНИЯ
            // ========================================================

            try
            {
                // ----------------------------------------------------
                // СНАЧАЛА УДАЛЯЕМ ФАЙЛЫ ИЗ MANIFEST
                // ----------------------------------------------------

                if (manifest.DeletedFiles != null)
                {
                    foreach (string path in manifest.DeletedFiles)
                    {
                        DeleteManagedPath(path);
                    }
                }

                // ----------------------------------------------------
                // ПОТОМ КОПИРУЕМ ФАЙЛЫ ИЗ UPDATE.ZIP
                // ----------------------------------------------------
                //
                // update.zip является накопительным:
                // внутри находятся ВСЕ файлы, которые должны быть
                // добавлены/изменены для перехода от baseVersion
                // к targetVersion.
                //
                // Поэтому мы копируем только то, что реально есть
                // внутри archive. Остальные файлы Minecraft не трогаем.

                UpdateStatusText.Text =
                    "Устанавливаем обновление...";

                BottomUpdateText.Text =
                    "Копируем новые и изменённые файлы...";

                Directory.CreateDirectory(
                    MinecraftPath);

                CopyDirectoryTree(
                    sourceMinecraft,
                    MinecraftPath);

                // ----------------------------------------------------
                // OPTIONS.TXT — ОПЦИОНАЛЬНО
                // ----------------------------------------------------
                //
                // Если builder включил options.txt в update.zip,
                // он будет скопирован обычным CopyDirectoryTree().
                //
                // Если файл не изменился относительно базовой версии,
                // его может не быть в archive — и это НОРМАЛЬНО.
                //
                // Никакой ошибки из-за отсутствия options.txt быть
                // не должно.

                string sourceOptionsPath =
                    Path.Combine(
                        sourceMinecraft,
                        "config",
                        "fancymenu",
                        "options.txt");

                if (File.Exists(sourceOptionsPath))
                {
                    UpdateStatusText.Text =
                        "Обновляем FancyMenu...";

                    BottomUpdateText.Text =
                        "options.txt включён в обновление";
                }

                // ====================================================
                // ПРОВЕРКА
                // ====================================================

                if (!IsMinecraftInstalled())
                {
                    throw new InvalidOperationException(
                        "После обновления сборка " +
                        "не прошла проверку.");
                }

                // ----------------------------------------------------
                // ВЕРСИЯ
                // ----------------------------------------------------

                SaveInstalledVersion(
                    manifestTargetVersion);
            }
            catch (Exception updateException)
            {
                // ====================================================
                // ОТКАТ
                // ====================================================

                UpdateStatusText.Text =
                    "Ошибка обновления";

                BottomUpdateText.Text =
                    "Восстанавливаем резервную копию...";

                try
                {
                    RestoreFullMinecraft(
                        backupPath);
                }
                catch (Exception restoreException)
                {
                    throw new AggregateException(
                        "Обновление завершилось ошибкой, " +
                        "и автоматический откат также не удался.",
                        updateException,
                        restoreException);
                }

                throw new InvalidOperationException(
                    "Обновление не удалось.\n\n" +
                    "Предыдущая версия полностью " +
                    "восстановлена из резервной копии.\n\n" +
                    $"Причина:\n{updateException.Message}",
                    updateException);
            }

            // ========================================================
            // CLEANUP
            // ========================================================

            try
            {
                File.Delete(zipPath);

                if (Directory.Exists(
                    extractionDirectory))
                {
                    Directory.Delete(
                        extractionDirectory,
                        true);
                }
            }
            catch
            {
            }

            // ========================================================
            // UI
            // ========================================================

            UpdateStatusText.Text =
                $"Обновление до {manifestTargetVersion} завершено";

            BottomUpdateText.Text =
                $"Установлена версия {manifestTargetVersion}";

            CurrentVersionText.Text =
                $"Версия сборки: {manifestTargetVersion}";

            SetReadyState();

            Debug.WriteLine(
                $"Create: Vanilla++ updated to {manifestTargetVersion}");

            return true;
        }

        // ============================================================
        // ПОЛНЫЙ BACKUP MINECRAFT
        // ============================================================

        private static void BackupFullMinecraft(
            string backupPath)
        {
            if (!Directory.Exists(MinecraftPath))
            {
                throw new DirectoryNotFoundException(
                    "Minecraft-папка для резервной копии " +
                    "не найдена:\n" +
                    MinecraftPath);
            }

            string minecraftBackup =
                Path.Combine(
                    backupPath,
                    "minecraft");

            Directory.CreateDirectory(
                minecraftBackup);

            CopyDirectoryTree(
                MinecraftPath,
                minecraftBackup);
        }

        // ============================================================
        // ПОЛНОЕ ВОССТАНОВЛЕНИЕ
        // ============================================================

        private static void RestoreFullMinecraft(
            string backupPath)
        {
            string minecraftBackup =
                Path.Combine(
                    backupPath,
                    "minecraft");

            if (!Directory.Exists(
                minecraftBackup))
            {
                throw new DirectoryNotFoundException(
                    "Полная резервная копия Minecraft " +
                    "не найдена:\n" +
                    minecraftBackup);
            }

            if (Directory.Exists(MinecraftPath))
            {
                Directory.Delete(
                    MinecraftPath,
                    true);
            }

            Directory.CreateDirectory(
                MinecraftPath);

            CopyDirectoryTree(
                minecraftBackup,
                MinecraftPath);

            if (!Directory.Exists(MinecraftPath))
            {
                throw new InvalidOperationException(
                    "Не удалось восстановить Minecraft-папку.");
            }
        }

        // ============================================================
        // DELETE PATH FROM MANIFEST
        // ============================================================

        private static void DeleteManagedPath(
            string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return;
            }

            string normalized =
                relativePath
                    .Replace('\\', '/')
                    .TrimStart('/');

            // --------------------------------------------------------
            // Запрещаем выход из minecraft через ../
            // --------------------------------------------------------

            if (normalized.Contains("../") ||
                normalized.Contains("..\\") ||
                normalized.Equals(
                    "..",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Недопустимый путь удаления:\n" +
                    relativePath);
            }

            string fullPath =
                Path.GetFullPath(
                    Path.Combine(
                        MinecraftPath,
                        normalized.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

            string root =
                Path.GetFullPath(
                    MinecraftPath);

            if (!root.EndsWith(
                Path.DirectorySeparatorChar))
            {
                root +=
                    Path.DirectorySeparatorChar;
            }

            if (!fullPath.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Недопустимый путь удаления:\n" +
                    relativePath);
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(
                    fullPath,
                    true);
            }
        }

        // ============================================================
        // ПРОЦЕССЫ
        // ============================================================

        private static bool IsMinecraftRunning()
        {
            string[] processNames =
            {
                "java",
                "javaw",
                "minecraft"
            };

            foreach (string name in processNames)
            {
                try
                {
                    if (Process.GetProcessesByName(
                        name).Length > 0)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        // ============================================================
        // VERSION
        // ============================================================

        private static string GetInstalledVersion()
        {
            try
            {
                string versionPath =
                    Path.Combine(
                        MinecraftPath,
                        VersionFileName);

                if (File.Exists(versionPath))
                {
                    string version =
                        File.ReadAllText(
                            versionPath).Trim();

                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return NormalizeVersion(
                            version);
                    }
                }
            }
            catch
            {
            }

            return CurrentVersion;
        }

        private static string NormalizeVersion(
            string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return "0.0.0";
            }

            return version
                .Trim()
                .TrimStart('v', 'V');
        }

        private static bool IsNewerVersion(
            string latest,
            string current)
        {
            if (Version.TryParse(
                    latest,
                    out Version? latestVersion)
                &&
                Version.TryParse(
                    current,
                    out Version? currentVersion))
            {
                return latestVersion >
                       currentVersion;
            }

            return !string.Equals(
                latest,
                current,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void SaveInstalledVersion(
            string version)
        {
            if (!Directory.Exists(MinecraftPath))
            {
                Directory.CreateDirectory(
                    MinecraftPath);
            }

            string path =
                Path.Combine(
                    MinecraftPath,
                    VersionFileName);

            File.WriteAllText(
                path,
                NormalizeVersion(version));
        }

        private sealed class ServerStatus
        {
            public bool IsOnline { get; private init; }
            public bool IsChecking { get; private init; }

            public bool IsNotConfigured { get; private init; }
            public int OnlinePlayers { get; private init; }
            public int MaxPlayers { get; private init; }
            public string Message { get; private init; } = "";

            public static ServerStatus Online(
                int onlinePlayers,
                int maxPlayers)
            {
                return new ServerStatus
                {
                    IsOnline = true,
                    OnlinePlayers = onlinePlayers,
                    MaxPlayers = maxPlayers
                };
            }

            public static ServerStatus Offline(
                string message)
            {
                return new ServerStatus
                {
                    IsOnline = false,
                    Message = message
                };
            }

            public static ServerStatus Checking()
            {
                return new ServerStatus
                {
                    IsChecking = true
                };
            }

            public static ServerStatus NotConfigured()
            {
                return new ServerStatus
                {
                    IsNotConfigured = true,
                    Message = "Не настроен"
                };
            }
        }

        // ============================================================
        // GITHUB MODELS
        // ============================================================

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("assets")]
            public GitHubAsset[] Assets { get; set; } =
                Array.Empty<GitHubAsset>();
        }

        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }

        private sealed class UpdateManifest
        {
            [JsonPropertyName("formatVersion")]
            public int FormatVersion { get; set; }

            [JsonPropertyName("baseVersion")]
            public string? BaseVersion { get; set; }

            [JsonPropertyName("targetVersion")]
            public string? TargetVersion { get; set; }

            [JsonPropertyName("deletedFiles")]
            public string[]? DeletedFiles { get; set; }
        }
    }
}
