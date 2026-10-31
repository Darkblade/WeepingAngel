using Blink.Models;
using DontBlink.Services;
using System.Diagnostics;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace DontBlink;

public partial class Form1 : Form
{
    private readonly Label _authStatus = new(){AutoSize=true,AccessibleName="Blink authentication status"};
    private readonly Label _connectionStatus = new(){AutoSize=true,Text="Blink: signed out"};
    private readonly Label _pageStatus = new(){AutoSize=true,Text="Clips"};
    private readonly Label _phoneStatus = new(){AutoSize=true};
    private readonly Label _diagnosticSummary = new(){AutoSize=true,Text="No recent events",AutoEllipsis=true};
    private Control? _verificationField;
    private bool _authenticationBusy, _authenticationError;
    private readonly BlinkAuthService _auth = new();
    private readonly Label _loginCooldownLabel = new(){Name="authentication-cooldown",AutoSize=true,MaximumSize=new Size(460,0),Padding=new Padding(12)};
    private bool _loginCooldownWasActive;
    private readonly System.Windows.Forms.Timer _loginCooldownTimer = new(){Interval=1000};
    private readonly List<Button> _loginButtons = [];
    private readonly CredentialStore _credentialStore = new();
    private readonly CheckBox _autoArchive = new ThemedCheckBox() { Text = "Auto archive → verify → clear USB", AutoSize = true };
    private readonly Label _autoArchiveState = new() { AutoSize = true, Padding = new Padding(5) };
    private readonly CheckBox _remember = new ThemedCheckBox() { Text = "Save username and password", AutoSize = true };
    private readonly ClipService _clips;
    private readonly TextBox _email = new() { Width = 220, PlaceholderText = "Blink email" };
    private readonly TextBox _password = new() { Width = 180, PlaceholderText = "Password", UseSystemPasswordChar = true };
    private readonly TextBox _pin = new() { Width = 100, PlaceholderText = "2FA PIN" };
    private readonly ComboBox _modules = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _filter = new() { Name="usb-camera-filter", Width = 220, DropDownStyle=ComboBoxStyle.DropDownList, AccessibleName="Filter USB clips by camera" };
    private readonly DataGridView _source = Grid();
    private readonly DataGridView _archive = Grid();
    private bool _bindingArchive;
    private List<ArchiveItem> _archiveItems = [];
    private int _archiveLoadVersion;
    private readonly HashSet<Form> _players = new();
    private readonly ComboBox _archiveCamera = new() { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _archiveDay = ArchiveDate(), _archiveFrom = ArchiveDate(), _archiveTo = ArchiveDate();
    private readonly Label _archiveSummary = new() { AutoSize = true, Padding = new Padding(6) };
    private readonly Dictionary<string, Image?> _thumbnailImages = new();
    private readonly CancellationTokenSource _thumbnailCancellation = new();
    private readonly ThumbnailService _thumbnails;
    private readonly DataGridView _cameras = Grid();
    private readonly Panel _cameraHost = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _cameraCards = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
    private readonly ComboBox _cameraView = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _cameraAuto = new ThemedCheckBox() { Text = "Refresh thumbnails every", AutoSize = true };
    private readonly NumericUpDown _cameraInterval = new() { Minimum = 1, Maximum = 1440, Value = 5, Width = 65 };
    private readonly ComboBox _cameraUnits = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _recordLive = new ThemedCheckBox() { Text = "Save live to archive", AutoSize = true };
    private readonly System.Windows.Forms.Timer _cameraTimer = new() { Interval = 15000 };
    private readonly Dictionary<(int,int), Image> _cameraImages = new();
    private CameraOverview[] _cameraItems = [];
    private bool _cameraRefreshBusy;
    private bool? _lastApiAvailable;
    private DateTime _nextCameraRefresh = DateTime.UtcNow;
    private readonly CameraMediaService _cameraMedia;
    private readonly LiveViewService _live;
    private readonly TextBox _archiveLocation = new() { ReadOnly = true, Width = 420 };
    private readonly ComboBox _themes = new() { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Theme" };
    private readonly ClientAccessStore _clients = new();
    private readonly DataGridView _clientGrid = Grid();
    private readonly System.Windows.Forms.Timer _clientTimer = new() { Interval = 5000 };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly CheckBox _lan = new ThemedCheckBox() { Text = "Allow LAN access (trusted Wi-Fi, HTTP)", AutoSize = true };
    private readonly ComboBox _serverUrls = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
    private readonly TextBox _pairingCode = new() { Width = 290, ReadOnly = true, PlaceholderText = "Pairing code", AccessibleName = "Pairing code" };
    private readonly Button _copyUrl = new ThemedButton() { Text = "Copy URL", AutoSize = true, Enabled = false };
    private readonly Button _copyCode = new ThemedButton() { Text = "Copy pairing code", AutoSize = true, Enabled = false };
    private readonly List<Button> _actions = new();
    private readonly Button _cancel = new ThemedButton() { Text = "Cancel operation", AutoSize = true, Enabled = false };
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _archiveCancellation;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitor;
    private MobileServer? _server;
    private Action? _phoneUiChanged;
    private Action<bool>? _setDebugLogCollapsed;
    private readonly ToolTip _cameraTips = new();
    private bool _closing;
    private List<ClipItem> _items = new();

    public Form1()
    {
        InitializeComponent();EnableCameraDrop(_cameraCards);
        _auth.Diagnostic+=Log;
        string root = AppSettings.Load().ArchiveFolder ?? Environment.GetEnvironmentVariable("DONTBLINK_ARCHIVE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DontBlink");
        _clips = new(new BlinkClipSource(_auth), root, CameraSnapshotStore.DefaultPath);
        _cameraItems=_clips.LastKnownCameras;
        _thumbnails = new(_clips);
        _cameraMedia = new(_clips); _live = new(_clips); _live.Log += Log; _live.Diagnostic+=LogAt; _live.ProtocolObserved+=frame=>_lanDevices.ActivityMonitor.TestLog.Record("immis_frame",frame);
        _live.ConfirmRelayCertificate = ConfirmRelayCertificateAsync;
        _clips.Log += Log;_clips.Diagnostic+=LogAt;_clips.ProgressChanged+=CopyProgress;
        _archiveLocation.Text = _clips.ArchiveRoot;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Absolute, 130));
        var login = Flow();
        login.Controls.AddRange([_email, _password, _pin, _remember]);
        _themes.Items.AddRange(ThemePalette.All.Keys.ToArray());
        _themes.SelectedItem = ThemePalette.Migrate(AppSettings.Load().Theme);
        Add(login, "Login / Verify PIN", LoginAsync);
        Add(login, "New login / resend SMS", async () =>
        {
            if (_monitor != null) throw new InvalidOperationException("Stop auto archive before starting a new login.");
            _auth.Cooldown.EnsureAllowed();
            _auth.Reset();
            _pin.Clear();
            await LoginAsync();
        });
        _loginButtons.AddRange(login.Controls.OfType<Button>());
        if(_auth.Cooldown.Remaining>TimeSpan.Zero) Log(_auth.Cooldown.Diagnostic);
        _loginCooldownTimer.Tick+=(_,_)=>UpdateLoginCooldown();_loginCooldownTimer.Start();
        Disposed+=(_,_)=>_loginCooldownTimer.Dispose();
        layout.Controls.Add(login);
        var tools = Flow();
        tools.Controls.Add(_modules);
        Add(tools, "Reload modules", LoadModulesAsync);
        Add(tools, "Refresh USB clips", RefreshAsync);
        tools.Controls.Add(_filter);
        _filter.TextChanged += (_, _) => BindSource();
        Add(tools, "Archive & remove from USB", () => BatchAsync(false));
        Add(tools, "Delete selected from USB", () => BatchAsync(true));
        tools.Controls.Add(_cancel);
        _cancel.Click += (_, _) => { if(_operation!=null)_operation.Cancel();else _archiveCancellation?.Cancel(); Log("Cancellation requested; an in-flight Blink command may still finish."); };
        layout.Controls.Add(tools);
        var tabs = new TabControl { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, ItemSize = new Size(150, 36) };
        tabs.DrawItem += (_, e) =>
        {
            var palette = ThemePalette.Resolve((string)_themes.SelectedItem!);
            bool selected = e.Index == tabs.SelectedIndex;
            using var brush = new SolidBrush(palette.ColorOf(selected ? palette.Raised : palette.Background));
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, e.Bounds,
                palette.ColorOf(selected ? palette.Accent : palette.Text), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        var usbTab = new TabPage("Sync Module USB clips");
        usbTab.Controls.Add(_source);
        var archiveTab = new TabPage("Downloaded archive — available without Blink login");
        var archiveLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        archiveLayout.RowStyles.Add(new(SizeType.AutoSize)); archiveLayout.RowStyles.Add(new(SizeType.AutoSize)); archiveLayout.RowStyles.Add(new(SizeType.Percent, 100));
        var archiveFilters = Flow();
        archiveFilters.Controls.AddRange([new Label { Text = "Camera", AutoSize = true }, _archiveCamera, new Label { Text = "Day", AutoSize = true }, _archiveDay, new Label { Text = "From", AutoSize = true }, _archiveFrom, new Label { Text = "Through", AutoSize = true }, _archiveTo]);
        Add(archiveFilters, "Clear filters", () => { _archiveCamera.SelectedIndex = 0; _archiveDay.Checked = _archiveFrom.Checked = _archiveTo.Checked = false; BindArchive(); return Task.CompletedTask; });
        archiveLayout.Controls.Add(archiveFilters); archiveLayout.Controls.Add(_archiveSummary); archiveLayout.Controls.Add(_archive); archiveTab.Controls.Add(archiveLayout);
        _archiveCamera.SelectedIndexChanged += (_, _) => BindArchive();
        _archiveDay.ValueChanged += (_, _) => BindArchive(); _archiveFrom.ValueChanged += (_, _) => BindArchive(); _archiveTo.ValueChanged += (_, _) => BindArchive();
        _archive.CellFormatting += ArchiveCellFormatting; ConfigureArchiveMenu();
        var cameraTab = new TabPage("Cameras");
        var cameraLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        cameraLayout.RowStyles.Add(new(SizeType.AutoSize)); cameraLayout.RowStyles.Add(new(SizeType.Percent, 100));
        var cameraTools = Flow();_systemScope.Items.Add(new SystemChoice("blink",null,"Blink / All systems"));_systemScope.SelectedIndex=0;_systemScope.SelectedIndexChanged+=(_,_)=>{if(!_bindingSystemScope)BindCameraOverview();};cameraTools.Controls.Add(_systemScope);Add(cameraTools,"Services & devices",()=>OpenManagementAsync());
        Add(cameraTools, "Refresh cameras", LoadCamerasAsync);
        _cameraView.Items.AddRange(["Grid", "List"]); _cameraUnits.Items.AddRange(["Minutes", "Hours"]);
        var cameraSettings = AppSettings.Load();
        _cameraView.SelectedIndex = cameraSettings.CameraGrid ? 0 : 1;
        _cameraAuto.Checked = cameraSettings.RefreshCameraImages;
        _cameraInterval.Value = Math.Clamp(cameraSettings.CameraRefreshValue, 1, 1440);
        _cameraUnits.SelectedIndex = cameraSettings.CameraRefreshUnit == "Hours" ? 1 : 0;
        _recordLive.Checked = cameraSettings.RecordLive;
        cameraTools.Controls.AddRange([_cameraView, _cameraAuto, _cameraInterval, _cameraUnits, _recordLive]);cameraTools.Controls.Add(_systemSummary);cameraTools.SetFlowBreak(_systemSummary,true);
        void SaveCameraSettings()
        {
            try { (AppSettings.Load() with { CameraGrid = _cameraView.SelectedIndex == 0, RefreshCameraImages = _cameraAuto.Checked, CameraRefreshValue = (int)_cameraInterval.Value, CameraRefreshUnit = (string)_cameraUnits.SelectedItem!, RecordLive = _recordLive.Checked }).Save(); }
            catch (Exception ex) { Log($"Camera settings: {ex.Message}"); }
            _nextCameraRefresh = DateTime.UtcNow;
            BuildCameraViews();
        }
        _cameraView.SelectedIndexChanged += (_, _) => SaveCameraSettings(); _cameraAuto.CheckedChanged += (_, _) => SaveCameraSettings();
        _cameraInterval.ValueChanged += (_, _) => SaveCameraSettings(); _cameraUnits.SelectedIndexChanged += (_, _) => SaveCameraSettings(); _recordLive.CheckedChanged += (_, _) => SaveCameraSettings();
        _cameraHost.Controls.AddRange([_cameras, _cameraCards]);
        ApplyCameraView(_cameraView.SelectedIndex == 0);
        _cameraTimer.Tick += async (_, _) =>
        {
            if (!_cameraAuto.Checked || _cameraView.SelectedIndex != 0 || !_cameraHost.Visible || !_auth.IsAuthenticated || _cameraRefreshBusy || _operation != null || DateTime.UtcNow < _nextCameraRefresh) return;
            _cameraRefreshBusy = true;
            try { foreach (var camera in _cameraItems) if (!_live.IsActive(camera.NetworkId, camera.Id)) await RefreshCameraImageAsync(camera, true); }
            catch (Exception ex) { Log($"Camera refresh: {ex.Message}"); }
            finally { _cameraRefreshBusy = false; _nextCameraRefresh = DateTime.UtcNow.AddMinutes((double)_cameraInterval.Value * (_cameraUnits.SelectedIndex == 1 ? 60 : 1)); }
        };
        _cameraTimer.Start();
        _cameras.CellDoubleClick += async (_, _) => { if (_cameras.CurrentRow?.DataBoundItem is CameraOverview camera) await RunAsync(() => OpenLiveWindowAsync(camera)); };
        cameraLayout.Controls.Add(cameraTools); cameraLayout.Controls.Add(_cameraHost); cameraTab.Controls.Add(cameraLayout);
        tabs.TabPages.AddRange([usbTab, archiveTab, cameraTab]);
        tabs.SelectedTab = archiveTab;
        tabs.SelectedIndexChanged += async (_, _) =>
        {
            foreach (var player in _players.ToArray()) player.Close();
            if (tabs.SelectedTab == cameraTab)
                await RunAsync(LoadCamerasAsync);
        };
        _cameras.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _cameras.Columns[e.ColumnIndex].Name == "Preview" && _cameras.Rows[e.RowIndex].DataBoundItem is CameraOverview camera)
            { e.Value = _cameraImages.GetValueOrDefault((camera.NetworkId, camera.Id)); e.FormattingApplied = true; }
            else if(e.RowIndex>=0&&_cameras.Rows[e.RowIndex].DataBoundItem is CameraOverview details){if(_cameras.Columns[e.ColumnIndex].Name=="Battery"){e.Value=CameraBattery.Label(details);e.FormattingApplied=true;}else if(_cameras.Columns[e.ColumnIndex].Name=="Motion"){e.Value=CameraPreview.MotionLabel(details);e.FormattingApplied=true;}}
        };
        var clientTab = new TabPage("Connected clients");
        var clientLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        clientLayout.RowStyles.Add(new(SizeType.AutoSize)); clientLayout.RowStyles.Add(new(SizeType.Percent, 100));
        var clientTools = Flow();
        Add(clientTools, "Refresh clients", () => { BindClients(); return Task.CompletedTask; });
        Add(clientTools, "Trust selected (30 days)", () => UpdateClientsAsync(true, false));
        Add(clientTools, "Remove trust", () => UpdateClientsAsync(false, false));
        Add(clientTools, "Revoke selected", () => UpdateClientsAsync(false, true));
        var clientNote=new Label { AutoSize = true, Text = "Active = request within 5 minutes. Trust belongs to a browser, not its IP address.", Padding = new Padding(5),Anchor=AnchorStyles.Left };clientLayout.RowCount=3;clientLayout.RowStyles.Add(new(SizeType.AutoSize));clientLayout.Controls.Add(clientNote,0,2);
        clientLayout.Controls.Add(clientTools,0,0); clientLayout.Controls.Add(_clientGrid,0,1); clientTab.Controls.Add(clientLayout); tabs.TabPages.Add(clientTab);
        _source.CellDoubleClick += async (_, _) => await RunAsync(PlaySourceAsync);
        _archive.CellDoubleClick += async (_, _) => await RunAsync(PlayArchiveAsync);
        layout.Controls.Add(tabs);
        var archiveTools = Flow();
        Add(archiveTools, "Reload archive", ReloadArchiveAsync);
        Add(archiveTools, "Import USB / Pi folder", ImportAsync);
        Add(archiveTools, "Open archive folder", () => { Directory.CreateDirectory(_clips.ArchiveRoot); Open(_clips.ArchiveRoot); return Task.CompletedTask; });
        Add(archiveTools, "Change archive folder", ChangeArchiveFolderAsync);
        archiveTools.Controls.Add(_archiveLocation);
        archiveTools.Controls.AddRange([_autoArchive, _autoArchiveState]);
        _autoArchive.Checked = AppSettings.Load().AutoArchive;
        _autoArchiveState.Text = _autoArchive.Checked ? "Waiting for Blink login" : "Off";
        _autoArchive.CheckedChanged += async (_, _) =>
        {
            _autoArchive.Enabled = false;
            try
            {
                (AppSettings.Load() with { AutoArchive = _autoArchive.Checked }).Save();
                if (_autoArchive.Checked) await StartMonitorAsync(); else await StopMonitorAsync();
            }
            catch (Exception ex) { Log($"Auto archive: {ex.Message}"); }
            finally { if (!_closing) _autoArchive.Enabled = true; }
        };
        var live = new ThemedButton { Text = "Live view status", AutoSize = true };
        live.Click += (_, _) => MessageBox.Show(this,
            "Open the Cameras tab and choose Live view. Select Save live to archive to record the session. The player uses Blink's on-demand cloud relay; session length is controlled by Blink.", "Live view");
        archiveTools.Controls.Add(live);
        Add(archiveTools, "Recording overlays", async () => { if (_server == null) await StartServerAsync(); await OpenPlayerWindowAsync("Recording overlays", "/recording-settings"); });
        layout.Controls.Add(archiveTools);
        var mobile = Flow();
        mobile.Controls.Add(_lan);
        Add(mobile, "Start phone interface", StartServerAsync);
        Add(mobile, "Stop phone interface", StopServerAsync);
        Add(mobile, "Secure phone setup", async () => {
            foreach(var player in _players.ToArray())player.Close();
            await StopServerAsync();
            (AppSettings.Load() with { SecurePhone = true }).Save(); _lan.Checked=true;
            await StartServerAsync();
            using var cert=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(PhoneHttps.CertificatePath);
            MessageBox.Show(this,"On the paired phone, open /phone-certificate at the HTTP app address to download your local CA certificate. Install it in Android Settings → Security → More security settings → Install from device storage → CA certificate. Then use the HTTPS address on port 5188 and pair again.\n\nVerify this SHA-256 fingerprint before installing:\n"+cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)+"\n\nCertificate: "+PhoneHttps.CertificatePath+"\n\nOnly the public certificate is shared; its private key stays encrypted on this PC. Reopen the app if the PC's LAN address changes.","Phone microphone setup");
        });
        _serverUrls.AccessibleName = "Phone interface URL";
        mobile.Controls.AddRange([_serverUrls, _copyUrl, _pairingCode, _copyCode]);

        _copyUrl.Click += (_, _) => CopyConnectionText(_serverUrls.SelectedItem as string);
        _copyCode.Click += (_, _) => CopyConnectionText(_pairingCode.Text);
        layout.Controls.Add(mobile);
        var debugArea = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        debugArea.RowStyles.Add(new(SizeType.AutoSize)); debugArea.RowStyles.Add(new(SizeType.Percent, 100));
        var debugTools = Flow(); debugArea.Controls.Add(debugTools); debugArea.Controls.Add(_log);
        layout.Controls.Add(debugArea);
        Controls.Add(layout);
        BuildWorkspace(layout,tabs,login,tools,archiveTools,mobile,debugArea,usbTab,archiveTab,cameraTab,clientTab);
        Font = new Font("Segoe UI", (float)(10*Math.Clamp(AppSettings.Load().TextScale,1,2)));
        ApplyTheme(this, ThemePalette.Resolve((string)_themes.SelectedItem!));
        _themes.SelectedIndexChanged += (_, _) =>
        {
            ApplyTheme(this, ThemePalette.Resolve((string)_themes.SelectedItem!));
            tabs.Invalidate();
            try { (AppSettings.Load() with { Theme = (string)_themes.SelectedItem! }).Save(); }
            catch (Exception) { Log("Could not remember the selected theme."); }
        };
        BindCameraOverview();ApplyBlinkAvailability();
        _clientTimer.Tick += (_, _) => {BindClients();ApplyBlinkAvailability();foreach(var p in _cameraCards.Controls.Cast<Control>().SelectMany(c=>c.Controls.OfType<CameraPreview>()))p.Invalidate();}; _clientTimer.Start();
        Disposed += (_, _) => { _clientTimer.Dispose(); _cameraTimer.Dispose(); _cameraTips.Dispose(); _thumbnailCancellation.Cancel(); foreach (var image in _thumbnailImages.Values) image?.Dispose(); foreach (var image in _cameraImages.Values) image.Dispose(); };
        BindClients();
        try
        {
            if (_credentialStore.Load() is { } saved)
            { _email.Text = saved.Email; _password.Text = saved.Password; _remember.Checked = true; }
        }
        catch (Exception) { Log("Saved credentials could not be read. Enter them again, or uncheck Save username and password to remove them."); }
        _remember.CheckedChanged += (_, _) =>
        {
            if (!_remember.Checked)
            {
                try { _credentialStore.Delete(); Log("Saved credentials removed."); }
                catch (Exception) { Log("Could not remove saved credentials. Check access to the DontBlink application data folder."); }
            }
        };
        Shown += async (_, _) => {
            if(_restoreRouterMonitoring!=null)await _restoreRouterMonitoring();
            await ReloadArchiveAsync();
            await RunAsync(async()=>{if(await _auth.RestoreSessionAsync()){await LoadModulesAsync();if(_autoArchive.Checked)await StartMonitorAsync();}});
        };
        Log($"Archive: {root}. Select rows with Ctrl/Shift. Double-click a clip to play.");
    }

    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Padding=new Padding(8), Margin=new Padding(0,0,0,8) };
    private static void ApplyTheme(Control control, ThemePalette palette)
    {
        bool surface=false;for(Control? parent=control.Parent;parent!=null;parent=parent.Parent)if(parent is ConsoleSection||parent.Name is "console-header" or "console-sidebar" or "console-status"){surface=true;break;}
        control.BackColor = palette.ColorOf(surface?palette.Surface:palette.Background); control.ForeColor = palette.ColorOf(palette.Text);
        if(control.Name is "page-heading" or "application-brand") control.ForeColor=palette.ColorOf(palette.Accent);
        if (control is TextBox or ComboBox or NumericUpDown) {control.BackColor=palette.ColorOf(palette.Token("inputs.background",palette.Surface));control.ForeColor=palette.ColorOf(palette.Token("inputs.foreground",palette.Text));}
        if(control is ListBox)control.BackColor=palette.ColorOf(palette.Surface);
        if(control.Name=="authentication-cooldown")control.BackColor=palette.ColorOf(palette.Token("status.warning.background",palette.Raised));
        if(control is ComboBox combo){combo.FlatStyle=FlatStyle.Flat;combo.DrawMode=DrawMode.OwnerDrawFixed;combo.Tag=palette;combo.DrawItem-=DrawThemedCombo;combo.DrawItem+=DrawThemedCombo;}
        if (control is TabPage page) page.UseVisualStyleBackColor = false;
        if (control is ThemedButton themed){themed.Palette=palette;if(new[]{"Show growing-file fallback","Hide growing-file fallback","Use external network events","Save router settings","Generate module filter","Start router monitoring","Stop monitoring","Open capture for replay","Clear replay","Refresh devices","New device","Save device","Remove device","Test connection","Start phone interface","Stop phone interface","Copy URL","Copy pairing code","Secure phone setup","Save recording overlays"}.Contains(themed.Text))themed.Presentation="Compact";}
        if(control is CameraPreview preview){preview.Palette=palette;preview.Invalidate();}
        if(control is ThemedCheckBox check){check.Palette=palette;check.UseVisualStyleBackColor=false;check.Invalidate();}
        if(control is LinkLabel link){link.LinkColor=palette.ColorOf(palette.Accent);link.ActiveLinkColor=link.LinkColor;link.VisitedLinkColor=link.LinkColor;link.BackColor=control.Parent is CameraPreview?CameraPreview.StatusBackground:palette.ColorOf(palette.Surface);}
        if(control is SettingsTabLink tabLink){tabLink.Palette=palette;tabLink.BackColor=palette.ColorOf(tabLink.IconName==null?palette.Background:palette.Surface);tabLink.RefreshColors();}
        if (control is Button button) { button.FlatStyle = FlatStyle.Flat; button.BackColor = palette.ColorOf(palette.Token("buttons.standard.normal.background",palette.Surface));button.ForeColor=palette.ColorOf(palette.Token("buttons.standard.normal.foreground",palette.Text));button.Padding = new Padding(8,3,8,3);button.Margin=new Padding(4);button.MinimumSize=new Size(0,control is ThemedButton{Presentation:"Compact"}?24:AppSettings.Load().ComfortableControls?44:34); if(control is ThemedButton{Presentation:"Compact"}){button.Padding=new Padding(6,0,6,0);button.Margin=new Padding(4,2,4,2);} button.FlatAppearance.BorderColor = palette.ColorOf(palette.Token("buttons.standard.normal.border",palette.Raised)); }
        if (control is DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false; grid.BackgroundColor = palette.ColorOf(palette.Background);
            grid.BorderStyle = BorderStyle.None; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = palette.ColorOf(palette.Raised); grid.RowTemplate.Height = 34;
            grid.DefaultCellStyle.BackColor = palette.ColorOf(palette.Surface); grid.DefaultCellStyle.ForeColor = palette.ColorOf(palette.Text);
            grid.DefaultCellStyle.SelectionBackColor = palette.ColorOf(palette.Selection); grid.DefaultCellStyle.SelectionForeColor = palette.ColorOf(palette.Token("tables.selected_foreground",palette.Text));
            grid.DefaultCellStyle.BackColor=palette.ColorOf(palette.Token("tables.row_even_background",palette.Surface));
            grid.AlternatingRowsDefaultCellStyle.BackColor = palette.ColorOf(palette.Token("tables.row_odd_background",palette.Background));
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.ColorOf(palette.Raised); grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.ColorOf(palette.Text);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=palette.ColorOf(palette.Selection);grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=palette.ColorOf(palette.Accent);
            grid.ColumnHeadersDefaultCellStyle.ForeColor=palette.ColorOf(palette.Accent);grid.ColumnHeadersHeight = 44;
            grid.DataBindingComplete -= FormatDates; grid.DataBindingComplete += FormatDates;
        }
        if(control is ConsoleInput input){input.Palette=palette;input.BackColor=palette.ColorOf(palette.Token("inputs.background",palette.Surface));input.Invalidate();}
        if(control is ConsoleSection section){section.Palette=palette;section.BackColor=palette.ColorOf(palette.Surface);section.Invalidate();}
        if(control.Name is "console-sidebar" or "console-header" or "console-status")control.BackColor=palette.ColorOf(palette.Surface);
        if(control.Name is "supporting-text" or "blink-account-help")control.ForeColor=palette.ColorOf(palette.Muted);
        foreach (Control child in control.Controls){ApplyTheme(child,palette);if(control is ConsoleSection&&child.Name!="authentication-cooldown"&&child is not Button&&child is not TextBox&&child is not ComboBox)child.BackColor=control.BackColor;}
        control.Invalidate();
    }
    private static void DrawThemedCombo(object? sender,DrawItemEventArgs e)
    {
        if(sender is not ComboBox combo||combo.Tag is not ThemePalette palette)return;
        bool selected=(e.State&DrawItemState.Selected)!=0;using var bg=new SolidBrush(palette.ColorOf(selected?palette.Selection:palette.Surface));e.Graphics.FillRectangle(bg,e.Bounds);
        string text=(e.Index>=0?combo.GetItemText(combo.Items[e.Index]):combo.Text)??"";TextRenderer.DrawText(e.Graphics,text,e.Font,e.Bounds,palette.ColorOf(selected?palette.Token("tables.selected_foreground",palette.Text):palette.Text),TextFormatFlags.Left|TextFormatFlags.VerticalCenter);e.DrawFocusRectangle();
    }
    private static DateTimePicker ArchiveDate() => new() { Width = 145, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy.MM.dd", ShowCheckBox = true, Checked = false };
    private static DataGridView Grid() => new SortableGrid()
    {
        Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false
    };
    private void Add(FlowLayoutPanel panel, string text, Func<Task> action)
    {
        var button = new ThemedButton { Text = text, AutoSize = true };
        button.Click += async (_, _) => {if(text is "Archive & remove from USB" or "Delete selected from USB")await RunArchiveAsync(action);else await RunAsync(action);};
        _actions.Add(button);
        panel.Controls.Add(button);
    }
    private async Task RunArchiveAsync(Func<Task> action)
    {
        if(_archiveCancellation!=null){Log("An archive batch is already running.");return;}
        using var cancellation=new CancellationTokenSource();_archiveCancellation=cancellation;_cancel.Enabled=true;
        try{Log("Archive runs in the background; queued user commands take priority between clips.");await action();}
        catch(OperationCanceledException){Log("Archive cancelled. Completed files are retained.");}
        catch(Exception ex){Log("Archive error: "+ex.Message);}
        finally{_archiveCancellation=null;if(!_closing){_cancel.Enabled=_operation!=null;ApplyBlinkAvailability(true);}}
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_operation != null) return;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        _actions.ForEach(b => b.Enabled = false);
        _lan.Enabled=false;
        _modules.Enabled = false;
        _cancel.Enabled = true;
        try { await action(); }
        catch (OperationCanceledException) { Log("Operation cancelled. Completed files and commands are retained."); }
        catch (Exception ex) { Log($"Error: {ex.Message}"); }
        finally
        {
            _operation = null;
            if (!_closing)
            {
                _actions.ForEach(b => b.Enabled = true);
                _modules.Enabled = true;
                _cancel.Enabled = _archiveCancellation!=null;
                _lan.Enabled=true;
                _phoneUiChanged?.Invoke();ApplyBlinkAvailability(true);UpdateLoginCooldown();
            }
        }
    }
    private CancellationToken Token => _operation?.Token ?? CancellationToken.None;
    private int ModuleId => _modules.SelectedItem is ModuleChoice choice ? choice.Id
        : throw new InvalidOperationException("Login and choose a Sync Module first.");

    private void UpdateLoginCooldown()
    {
        bool waiting=_auth.Cooldown.Remaining>TimeSpan.Zero;
        if(_loginCooldownWasActive&&!waiting)Log("Sign-in cooldown ended. Sign-in is allowed again; Blink will confirm server availability on the next attempt.");
        _loginCooldownWasActive=waiting;
        foreach(var button in _loginButtons)button.Enabled=!waiting&&_operation==null;
        var remaining=_auth.Cooldown.Remaining;
        _loginCooldownLabel.Text=waiting?$"Sign-in temporarily unavailable. Try again in {(int)remaining.TotalMinutes} min {remaining.Seconds:00} sec.":"";
        _loginCooldownLabel.Visible=waiting;UpdateAuthenticationPresentation();
    }
    private void UpdateAuthenticationPresentation()
    {
        if(_verificationField!=null)_verificationField.Visible=_auth.RequiresPin;
        string state=_auth.Cooldown.Remaining>TimeSpan.Zero?"Sign-in cooldown":_authenticationBusy?"Signing in…":_auth.RequiresPin?"Verification required":_auth.IsAuthenticated?"Connected":_authenticationError?"Sign-in failed. See Diagnostics for details.":"Signed out";
        _authStatus.Text=state;_connectionStatus.Text="Blink: "+state;foreach(var help in Controls.Find("blink-account-help",true).OfType<Label>())help.Text=_auth.IsAuthenticated?"Connected to Blink. Your devices are available.":_auth.RequiresPin?"Enter the verification code to finish connecting.":"Sign in to connect your Blink devices.";
        if(_loginButtons.Count>0){_loginButtons[0].Text=_auth.RequiresPin?"Verify code":_auth.IsAuthenticated?"Reconnect":"Sign in";_loginButtons[0].AccessibleDescription=_auth.Cooldown.Remaining>TimeSpan.Zero?"Temporarily unavailable until the sign-in cooldown ends.":"Connect using the saved session or your Blink credentials.";}
    }
    private async Task LoginAsync()
    {
        _authenticationBusy=true;_authenticationError=false;UpdateAuthenticationPresentation();
        try{await LoginCoreAsync();_authenticationError=!_auth.IsAuthenticated&&!_auth.RequiresPin;}
        catch{_authenticationError=true;throw;}
        finally{_authenticationBusy=false;UpdateAuthenticationPresentation();}
    }
    private async Task LoginCoreAsync()
    {
        if (_monitor != null) throw new InvalidOperationException("Stop auto archive before changing the Blink session.");

        bool ok = _auth.RequiresPin ? await _auth.VerifyPinAsync(_pin.Text.Trim())
            : await _auth.LoginAsync(_email.Text.Trim(), _password.Text);
        if (!ok) { Log(_auth.RequiresPin ? "Blink did not confirm the SMS PIN. Use the latest code from this login, or select New login / resend SMS." : "Blink login was not accepted."); return; }
        if (_auth.RequiresPin) { Log("Enter the Blink 2FA PIN, then click Login / Verify PIN again."); return; }
        if (_remember.Checked)
        {
            try { _credentialStore.Save(_email.Text.Trim(), _password.Text); }
            catch (Exception) { Log("Login succeeded, but Windows could not save the credentials."); }
        }
        else _password.Clear();
        _pin.Clear();
        Log("Authenticated.");
        await LoadModulesAsync();
        Log("USB clips load on request. Open Cameras to load camera previews.");
        if (_autoArchive.Checked) await StartMonitorAsync();
    }
    private void BindCameraOverview(){
        ((SortableGrid)_cameras).Bind(CameraLayout.Order(_cameraItems.Where(CameraInScope),AppSettings.Load().CameraOrder).ToList());
        if(!_cameras.Columns.Contains("Preview"))_cameras.Columns.Insert(0,new DataGridViewImageColumn{Name="Preview",ImageLayout=DataGridViewImageCellLayout.Zoom,Width=128,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,DefaultCellStyle=new DataGridViewCellStyle{NullValue=null}});
        BuildCameraViews();
    }
    private void ApplyBlinkAvailability(bool force=false){
        bool available=_clips.ApiAvailable;if(!force&&_lastApiAvailable==available)return;_lastApiAvailable=available;
        if(!available&&_cameraItems.Length==0&&_clips.LastKnownCameras.Length>0){_cameraItems=_clips.LastKnownCameras;BindCameraOverview();}
        _source.Enabled=available;_cameras.Enabled=available;
        foreach(var action in _actions.Where(b=>b.Text is "Reload modules" or "Refresh USB clips" or "Archive & remove from USB" or "Delete selected from USB" or "Refresh cameras"))action.Enabled=available&&_operation==null;
        foreach(var preview in _cameraCards.Controls.Cast<Control>().SelectMany(c=>c.Controls.OfType<CameraPreview>())){preview.LiveAvailable=available;foreach(var motion in preview.Controls.OfType<CameraMotionButton>())motion.Enabled=available&&_operation==null;preview.Invalidate();}
    }
    private async Task LoadCamerasAsync(){
        var overview=await _clips.CameraOverviewAsync(Token);_cameraItems=overview.Cameras;BindCameraOverview();ApplyBlinkAvailability(true);
        foreach(var camera in _cameraItems){
            if(!_clips.ApiAvailable&&_cameraMedia.CachedImage(camera.NetworkId,camera.Id)==null)continue;
            try{await RefreshCameraImageAsync(camera,false);}catch(Exception ex){Log($"Thumbnail {camera.Name}: {ex.Message}");}
        }
        await UpdateSystemSummaryAsync();
        Log(overview.ApiAvailable?$"Camera overview: {_cameraItems.Length} cameras.":$"Showing {_cameraItems.Length} saved cameras. Sign in to Blink to enable controls.");
    }
    private async Task RequestCameraImageUpdateAsync(CameraOverview camera)
    {
        if(_operation!=null){Log(camera.Name+": Update image not started: another GUI operation is running. Wait for it to finish or cancel it, then retry.");return;}
        await RunAsync(()=>RefreshCameraImageAsync(camera,true));
    }
    private async Task RequestCameraVideoAsync(CameraOverview camera)
    {
        if(_operation!=null){Log(camera.Name+": wait for the current GUI operation before requesting a clip.");return;}
        await RunAsync(async()=>{
            _lanDevices.ActivityMonitor.TestLog.Marker("Request video clip","requested",camera.NetworkId,camera.Id);
            try{await _clips.RequestCameraVideoAsync(camera.NetworkId,camera.Id,_operation!.Token);_lanDevices.ActivityMonitor.TestLog.Marker("Request video clip","command_completed",camera.NetworkId,camera.Id);}
            catch{_lanDevices.ActivityMonitor.TestLog.Marker("Request video clip","failed",camera.NetworkId,camera.Id);throw;}
        });
    }
    private async Task RefreshCameraImageAsync(CameraOverview camera, bool capture)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        if(capture)_lanDevices.ActivityMonitor.TestLog.Marker("Update Image","requested",camera.NetworkId,camera.Id);
        if(capture)Log(camera.Name+": Update image requested; waiting for Blink capture and image download.");
        using var imageCancellation=CancellationTokenSource.CreateLinkedTokenSource(Token,_thumbnailCancellation.Token);
        byte[] bytes;try{bytes=await _cameraMedia.ImageAsync(camera.NetworkId,camera.Id,capture,imageCancellation.Token);}catch{if(capture)_lanDevices.ActivityMonitor.TestLog.Marker("Update Image","failed",camera.NetworkId,camera.Id);throw;}
        if(capture)_lanDevices.ActivityMonitor.TestLog.Marker("Update Image","completed",camera.NetworkId,camera.Id);
        if (_closing) return;
        using var stream = new MemoryStream(bytes); using var decoded = Image.FromStream(stream);
        var key = (camera.NetworkId, camera.Id);
        _cameraImages.TryGetValue(key, out var previous); _cameraImages[key] = new Bitmap(decoded);
        foreach(var picture in _cameraCards.Controls.Cast<Control>().SelectMany(c=>c.Controls.OfType<CameraPreview>()).Where(p=>p.Camera is {} current&&current.NetworkId==camera.NetworkId&&current.Id==camera.Id)){picture.Image=_cameraImages[key];picture.Invalidate();}foreach(DataGridViewRow row in _cameras.Rows)if(row.DataBoundItem is CameraOverview current&&current.NetworkId==camera.NetworkId&&current.Id==camera.Id&&_cameras.Columns.Contains("Preview"))row.Cells["Preview"].Value=_cameraImages[key];_cameras.Invalidate();previous?.Dispose();
        if(capture){var saved=_clips.LastKnownCameras.FirstOrDefault(c=>c.NetworkId==camera.NetworkId&&c.Id==camera.Id);if(saved!=null){_cameraItems=_cameraItems.Select(c=>c.NetworkId==camera.NetworkId&&c.Id==camera.Id?saved:c).ToArray();BindCameraOverview();}}
        if(capture)Log($"{camera.Name}: Update image complete; {bytes.Length:N0} bytes, {decoded.Width}×{decoded.Height}, {watch.Elapsed.TotalSeconds:0.0} seconds. Preview updated.");
    }
    private const string CameraDragFormat="WeepingAngel.Camera";
    private void MoveCamera(string key,string targetKey)
    {
        var before=CameraLayout.Order(_cameraItems,AppSettings.Load().CameraOrder).Select(CameraLayout.Key).ToArray();
        var order=CameraLayout.Reorder(before,key,targetKey);if(before.SequenceEqual(order))return;
        (AppSettings.Load() with{CameraOrder=order}).Save();BindCameraOverview();
    }
    private void CameraDragOver(object? sender,DragEventArgs e)
    {
        e.Effect=e.Data?.GetDataPresent(CameraDragFormat)==true&&e.Data.GetData(CameraDragFormat) is string key&&_cameraItems.Any(c=>CameraLayout.Key(c)==key)?DragDropEffects.Move:DragDropEffects.None;
    }
    private void CameraDrop(object? sender,DragEventArgs e)
    {
        if(e.Data?.GetData(CameraDragFormat) is not string key||sender is not Control target)return;
        if(target==_cameraCards)target=_cameraCards.GetChildAtPoint(_cameraCards.PointToClient(new Point(e.X,e.Y)))??target;
        while(target.Parent!=null&&target.Parent!=_cameraCards)target=target.Parent;
        if(target.Parent!=_cameraCards||target.Tag is not string destination)return;
        try{MoveCamera(key,destination);}catch(Exception ex){Log("Could not save camera order: "+ex.Message);}
    }
    private void EnableCameraDrop(Control control){control.AllowDrop=true;control.DragEnter+=CameraDragOver;control.DragOver+=CameraDragOver;control.DragDrop+=CameraDrop;}
    private void BuildCameraViews()
    {
        ApplyCameraView(_cameraView.SelectedIndex==0);
        var ordered=CameraLayout.Order(_cameraItems.Where(CameraInScope),AppSettings.Load().CameraOrder).ToArray();
        var cards=_cameraCards.Controls.Cast<Control>().ToDictionary(c=>(string)c.Tag!);
        _cameraCards.SuspendLayout();
        try{
            foreach(var old in cards.Where(pair=>!ordered.Any(c=>CameraLayout.Key(c)==pair.Key)).ToArray()){_cameraCards.Controls.Remove(old.Value);old.Value.Dispose();cards.Remove(old.Key);}
            for(int index=0;index<ordered.Length;index++){
                var camera=ordered[index];string key=CameraLayout.Key(camera);
                if(!cards.TryGetValue(key,out var card)){
                    card=new RoundedPanel{Tag=key,Width=360,Height=200+Math.Max(0,Font.Height-17)*2,Margin=new Padding(8),Padding=new Padding(6)};
                    var picture=new CameraPreview(camera){Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Cursor=Cursors.Hand};
                    picture.Click+=async(_,_)=>await RunAsync(()=>OpenLiveWindowAsync(picture.Camera!));
                    picture.ReorderDragRequested+=(_,_)=>picture.DoDragDrop(new DataObject(CameraDragFormat,key),DragDropEffects.Move);EnableCameraDrop(card);EnableCameraDrop(picture);
                    var options=new LinkLabel{Text="…",AccessibleName="Camera options for "+camera.Name,Anchor=AnchorStyles.Left|AnchorStyles.Bottom,Size=new Size(32,28),TextAlign=ContentAlignment.MiddleCenter,Font=new Font(Font.FontFamily,17),TabStop=true};
                    var menu=new ContextMenuStrip{BackColor=BackColor,ForeColor=ForeColor,ShowImageMargin=false};options.ContextMenuStrip=menu;options.Disposed+=(_,_)=>menu.Dispose();
                    menu.Opening+=(_,_)=>{
                        var current=picture.Camera!;menu.BackColor=BackColor;menu.ForeColor=ForeColor;foreach(ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray())item.Dispose();menu.Items.Clear();
                        var update=menu.Items.Add("Update image");update.Enabled=_clips.ApiAvailable;update.Click+=async(_,_)=>await RequestCameraImageUpdateAsync(current);
                        var captureVideo=menu.Items.Add("Request video clip");captureVideo.Enabled=_clips.ApiAvailable&&current.CanRequestVideo;captureVideo.Click+=async(_,_)=>await RequestCameraVideoAsync(current);
                        var settings=menu.Items.Add("Settings");settings.Click+=async(_,_)=>await RunAsync(()=>OpenManagementAsync(current));


                    };
                    options.LinkClicked+=(_,_)=>menu.Show(options,new Point(0,options.Height));
                    var motionToggle=new CameraMotionButton{AccessibleName="Toggle motion recording for "+camera.Name,Size=new Size(48,30),Anchor=AnchorStyles.Right|AnchorStyles.Bottom,TabStop=true};
                    motionToggle.Click+=async(_,_)=>{
                        if(_operation!=null||!_clips.ApiAvailable)return;var current=picture.Camera!;bool enabled=current.Motion!="Enabled";motionToggle.Enabled=false;
                        try{await RunAsync(async()=>{_lanDevices.ActivityMonitor.TestLog.Marker("Motion recording "+(enabled?"On":"Off"),"requested",current.NetworkId,current.Id);Log(current.Name+": motion recording "+(enabled?"On":"Off")+" requested.");await _clips.CameraOperationAsync(async source=>{await source.SetMotionAsync(current.NetworkId,current.Id,enabled);return true;},_operation!.Token,current.NetworkId);_lanDevices.ActivityMonitor.TestLog.Marker("Motion recording "+(enabled?"On":"Off"),"command_completed",current.NetworkId,current.Id);await LoadCamerasAsync();var actual=picture.Camera!;Log(actual.Name+": "+CameraPreview.MotionLabel(actual)+" confirmed by Blink overview.");});}
                        finally{if(!motionToggle.IsDisposed)motionToggle.Enabled=_clips.ApiAvailable;}
                    };
                    EnableCameraDrop(motionToggle);EnableCameraDrop(options);
                    card.Controls.Add(picture);picture.Controls.Add(options);picture.Controls.Add(motionToggle);void PositionOptions(){options.Location=new Point(8,picture.ClientSize.Height-picture.StatusHeight+(picture.StatusHeight-options.Height)/2);motionToggle.Location=new Point(picture.ClientSize.Width-108,picture.ClientSize.Height-picture.StatusHeight+(picture.StatusHeight-motionToggle.Height)/2);}picture.SizeChanged+=(_,_)=>PositionOptions();motionToggle.SizeChanged+=(_,_)=>PositionOptions();PositionOptions();
                    _cameraCards.Controls.Add(card);cards.Add(key,card);
                }
                var preview=card.Controls.OfType<CameraPreview>().Single();preview.UpdateCamera(camera);preview.Image=_cameraImages.GetValueOrDefault((camera.NetworkId,camera.Id));
                preview.AccessibleName="Open live view for "+camera.Name;preview.AccessibleDescription=CameraBattery.Description(camera)+"; "+CameraPreview.MotionLabel(camera);_cameraTips.SetToolTip(preview,preview.AccessibleDescription);
                card.Enabled=true;preview.LiveAvailable=_clips.ApiAvailable;var motionButton=preview.Controls.OfType<CameraMotionButton>().Single();motionButton.Camera=camera;motionButton.Palette=ThemePalette.Resolve((string)_themes.SelectedItem!);motionButton.Enabled=_clips.ApiAvailable&&_operation==null;motionButton.AccessibleDescription=CameraPreview.MotionLabel(camera)+". Activate to toggle.";_cameraTips.SetToolTip(motionButton,motionButton.AccessibleDescription);motionButton.Invalidate();card.Height=200+Math.Max(0,Font.Height-17)*2;
                ApplyTheme(card,ThemePalette.Resolve((string)_themes.SelectedItem!));var optionsLink=preview.Controls.OfType<LinkLabel>().Single();optionsLink.BackColor=CameraPreview.StatusBackground;_cameraTips.SetToolTip(optionsLink,"Update camera image. Drag the panel to change camera order.");
                if(_cameraCards.Controls.GetChildIndex(card)!=index)_cameraCards.Controls.SetChildIndex(card,index);
            }
        }finally{_cameraCards.ResumeLayout(true);}
        foreach(DataGridViewRow row in _cameras.Rows)if(row.DataBoundItem is CameraOverview camera&&_cameras.Columns.Contains("Preview")){row.Height=76;row.Cells["Preview"].Value=_cameraImages.GetValueOrDefault((camera.NetworkId,camera.Id));}
    }
    private void ApplyCameraView(bool grid)
    {
        // Visible reports effective visibility, including hidden parent tabs.
        // Derive both states from the selection, not another control's getter.
        _cameraHost.SuspendLayout();
        try
        {
            _cameraCards.Visible = grid; _cameras.Visible = !grid;
            if (grid) _cameraCards.BringToFront(); else _cameras.BringToFront();
        }
        finally { _cameraHost.ResumeLayout(); }
    }
    private async Task OpenLiveWindowAsync(CameraOverview camera)
    {
        if(!_clips.ApiAvailable)throw new BlinkSessionUnavailableException();
        if (_server == null) await StartServerAsync();
        await OpenPlayerWindowAsync(camera.Name, $"/live?network={camera.NetworkId}&camera={camera.Id}&record={_recordLive.Checked}");
    }
    private async Task OpenPlayerWindowAsync(string title, string route)
    {
        var server = _server!;
        foreach (var previous in _players.ToArray()) previous.Close();
        var window = new Form { Icon = Branding.WindowIcon(), Text = $"Weeping Angel · {title}", Size = new Size(960,680) };
        _players.Add(window);
        var browser = new WebView2 { Dock = DockStyle.Fill }; window.Controls.Add(browser); window.Show(this);
        window.FormClosed += (_, _) => { _players.Remove(window); browser.Dispose(); };
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DontBlink", "WebView2"));
            await browser.EnsureCoreWebView2Async(environment);
            browser.CoreWebView2.WebMessageReceived += (_, message) =>
            {
                if (message.Source.StartsWith(server.LocalAddress + "/live", StringComparison.Ordinal)
                    && message.TryGetWebMessageAsString() == "stop-live") window.Close();
            };
            var cookie = browser.CoreWebView2.CookieManager.CreateCookie("dontblink", server.DesktopToken(), "127.0.0.1", "/");
            cookie.IsHttpOnly = true; cookie.SameSite = CoreWebView2CookieSameSiteKind.Strict; browser.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
            browser.CoreWebView2.Navigate(server.LocalAddress + route + (route.Contains('?') ? "&" : "?") + "theme=" + Uri.EscapeDataString((string)_themes.SelectedItem!));
        }
        catch { window.Close(); throw; }
    }
    private async Task ChangeArchiveFolderAsync()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose archive folder. Existing recordings stay in their current folder.", SelectedPath = _clips.ArchiveRoot };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        string previous = _clips.ArchiveRoot;
        await _clips.ChangeArchiveRootAsync(picker.SelectedPath, Token);
        try { (AppSettings.Load() with { ArchiveFolder = _clips.ArchiveRoot }).Save(); }
        catch { await _clips.ChangeArchiveRootAsync(previous, CancellationToken.None); throw; }
        _archiveLocation.Text = _clips.ArchiveRoot; await ReloadArchiveAsync();
        Log($"Archive folder saved: {_clips.ArchiveRoot}. Existing recordings remain at {previous}.");
    }
    private async Task LoadModulesAsync()
    {
        var modules = await _clips.ModulesAsync(Token);
        _modules.Items.Clear();
        _modules.Items.AddRange(modules.Select(m => new ModuleChoice(m.Id, m.NetworkId)).ToArray());
        _items.Clear(); BindSource();
        if (modules.Length > 0) _modules.SelectedIndex = 0;
        else Log("No Sync Modules found.");
    }
    private async Task RefreshAsync()
    {
        int id = ModuleId;
        _items = await _clips.ListAsync(id, Token);
        BindSource();
        Log($"Module {id}: {_items.Count} clips returned. The USB manifest may not expose all retained clips.");
    }
    private bool _bindingSource;
    private void BindSource()
    {
        if(_bindingSource)return;_bindingSource=true;try{
        var names=_items.Select(x=>x.Camera).Distinct().Order().ToArray();var choices=names.Length==0?Array.Empty<string>():new[]{"All cameras"}.Concat(names).ToArray();if(!_filter.Items.Cast<string>().SequenceEqual(choices)){string selected=_filter.SelectedItem as string??"All cameras";_filter.BeginUpdate();_filter.Items.Clear();_filter.Items.AddRange(choices);_filter.SelectedItem=choices.Contains(selected)?selected:choices.FirstOrDefault();_filter.EndUpdate();}
        ((SortableGrid)_source).Bind(_items.Where(x => _filter.SelectedIndex<=0||x.Camera==_filter.Text)
            .Select(x => new ClipRow(x.Key, x.ModuleId, x.Camera, x.CreatedUtc.ToLocalTime(), x.Size)).ToList());
        if (_source.Columns["Key"] is { } column) column.Visible = false;
        }finally{_bindingSource=false;}
    }
    private static void FormatDates(object? sender, DataGridViewBindingCompleteEventArgs e)
    {
        if (sender is DataGridView grid) foreach (DataGridViewColumn column in grid.Columns)
            if (column.ValueType == typeof(DateTime)) column.DefaultCellStyle.Format = "yyyy.MM.dd HH:mm:ss";
    }
    private void BindArchive()
    {
        if (_bindingArchive) return;
        _bindingArchive = true;
        try
        {
        var all = _archiveItems;
        var names = new[] { "All cameras" }.Concat(all.Select(x => x.Camera).Distinct().Order()).ToArray();
        if (!_archiveCamera.Items.Cast<string>().SequenceEqual(names))
        {
            string previous = _archiveCamera.SelectedItem as string ?? "All cameras";
            _archiveCamera.Items.Clear(); _archiveCamera.Items.AddRange(names);
            _archiveCamera.SelectedItem = names.Contains(previous) ? previous : "All cameras";
        }
        List<ArchiveItem> filtered;
        try { filtered = ArchiveFilter.Apply(all, _archiveCamera.SelectedIndex > 0 ? _archiveCamera.SelectedItem as string : null,
            _archiveDay.Checked ? _archiveDay.Value : null, _archiveFrom.Checked ? _archiveFrom.Value : null, _archiveTo.Checked ? _archiveTo.Value : null); }
        catch (ArgumentException ex) { _archiveSummary.Text = ex.Message; filtered = []; }
        ((SortableGrid)_archive).Bind(filtered.Select(x => new ArchiveRow(x.Key, x.Camera, x.CreatedUtc.ToLocalTime(), x.FileName, x.Bytes)).ToList());
        if (_archive.Columns["Key"] is { } column) column.Visible = false;
        if (!_archive.Columns.Contains("Preview"))
            _archive.Columns.Insert(0, new DataGridViewImageColumn { Name = "Preview", HeaderText = "Preview", ImageLayout = DataGridViewImageCellLayout.Zoom, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 128, DefaultCellStyle = new DataGridViewCellStyle { NullValue = null } });
        foreach (DataGridViewRow row in _archive.Rows) row.Height = 76;
        if (!_archiveFrom.Checked || !_archiveTo.Checked || _archiveFrom.Value.Date <= _archiveTo.Value.Date)
            _archiveSummary.Text = $"{filtered.Count} of {all.Count} recordings · {filtered.Sum(x => x.Bytes) / 1048576d:F1} MB · Local time · Previews use the Windows video codec";
        }
        finally { _bindingArchive = false; }
    }
    private async Task ReloadArchiveAsync()
    {
        int version = ++_archiveLoadVersion;
        _archiveSummary.Text = "Loading network archive…";
        try
        {
            var items = await Task.Run(_clips.Archive);
            if (_closing || IsDisposed || version != _archiveLoadVersion) return;
            _archiveItems = items; BindArchive();
        }
        catch (Exception ex) { if (!_closing) { _archiveSummary.Text = "Archive unavailable. Use Reload archive to retry."; Log($"Archive: {ex.Message}"); } }
    }
    private void ArchiveCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _archive.Columns[e.ColumnIndex].Name != "Preview" || _archive.Rows[e.RowIndex].DataBoundItem is not ArchiveRow row) return;
        if (_thumbnailImages.TryGetValue(row.Key, out var image)) { e.Value = image; e.FormattingApplied = true; }
        else { _thumbnailImages[row.Key] = null; _ = LoadThumbnailAsync(row.Key, _thumbnailCancellation.Token); }
    }
    private async Task LoadThumbnailAsync(string key, CancellationToken ct)
    {
        try
        {
            var bytes = await Task.Run(() => _thumbnails.GetAsync(key, ct), ct);
            if (ct.IsCancellationRequested || IsDisposed) return;
            if (bytes != null) { using var stream = new MemoryStream(bytes); using var image = Image.FromStream(stream); _thumbnailImages[key] = new Bitmap(image); }
            _archive.Invalidate();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }
    private async Task BatchAsync(bool delete)
    {
        var rows = _source.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ClipRow>().ToArray();
        if (rows.Length == 0) throw new InvalidOperationException("Select clips first.");
        if (rows.Any(r => r.Module != ModuleId)) throw new InvalidOperationException("Refresh clips for the selected module first.");
        if (delete && MessageBox.Show(this, $"Permanently delete {rows.Length} selected clips from the Sync Module USB drive? Downloads are kept.",
            "Confirm USB deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var result = await _clips.BatchAsync(ModuleId, rows.Select(r => r.Key), delete, _archiveCancellation?.Token??Token, removeAfterArchive: !delete);
        Log($"Batch complete: {result.Count(r => r.Success)} succeeded, {result.Count(r => !r.Success)} failed.");
        await ReloadArchiveAsync();
        await RunAsync(RefreshAsync);
    }
    private async Task PlaySourceAsync()
    {
        if (_source.CurrentRow?.DataBoundItem is not ClipRow row) return;
        if (_clips.ArchivePath(row.Key) == null)
        {
            var result = await _clips.BatchAsync(row.Module, [row.Key], false, Token);
            if (!result.Single().Success) throw new IOException(result.Single().Message);
            await ReloadArchiveAsync();
        }
        await OpenArchiveWindowAsync(row.Key);
    }
    private async Task ImportAsync()
    {
        using var picker = new FolderBrowserDialog { Description = "Select a safely ejected USB drive or stable Pi export. Imported timestamps use file modification times." };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var results = await _clips.ImportAsync(picker.SelectedPath, Token);
        await ReloadArchiveAsync();
        Log($"Import complete: {results.Count(x => x.Success)} succeeded, {results.Count(x => !x.Success)} failed. Source files were kept. Imported dates use file modification times.");
    }
    private async Task PlayArchiveAsync()
    {
        if (_archive.CurrentRow?.DataBoundItem is ArchiveRow row) await OpenArchiveWindowAsync(row.Key);
    }
    private async Task OpenArchiveWindowAsync(string key)
    {
        if (_server == null) await StartServerAsync();
        await OpenPlayerWindowAsync("Archive recording", "/archive-player?key=" + Uri.EscapeDataString(key));
    }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private Task StartMonitorAsync()
    {
        if (_monitor != null) return Task.CompletedTask;
        if (!_auth.IsAuthenticated) { _autoArchiveState.Text = "Waiting for Blink login"; return Task.CompletedTask; }
        _monitorCancellation = new();
        _monitor = MonitorAsync(_monitorCancellation.Token);
        _autoArchiveState.Text = "Running · all modules · every 60s";
        Log("Auto archive started for all modules: notify clients → copy or skip existing → verify archive → delete USB original.");
        return Task.CompletedTask;
    }
    private async Task MonitorAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                try
                {
                    foreach (var module in await _clips.ModulesAsync(ct,background:true))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var items = await _clips.ListAsync(module.Id, ct,background:true);
                            await _clips.BatchAsync(module.Id, items.Select(x => x.Key), false, ct, removeAfterArchive: true);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { Log($"Auto archive module {module.Id}: {ex.Message}"); }
                    }
                    if (!_closing) await ReloadArchiveAsync();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log($"Auto archive: {ex.Message}"); }
                await Task.Delay(TimeSpan.FromSeconds(60), ct);
            }
        }
        catch (OperationCanceledException) { Log("Auto archive stopped."); }
    }
    private async Task StopMonitorAsync()
    {
        _monitorCancellation?.Cancel();
        if (_monitor != null) await _monitor;
        _monitor = null;
        _monitorCancellation?.Dispose(); _monitorCancellation = null;
        _autoArchiveState.Text = "Off";
    }
    private async Task StartServerAsync()
    {
        if (_server != null) return;
        var server = new MobileServer(_clips, clients: _clients, images: _cameraMedia, live: _live, devices: _lanDevices);
        server.Log += Log;
        try { await server.StartAsync(_lan.Checked, Token); }
        catch { await server.DisposeAsync(); throw; }
        _server = server;
        _lan.Enabled = true;
        _serverUrls.Items.Clear();
        _serverUrls.Items.AddRange(server.Addresses.Split("   ", StringSplitOptions.RemoveEmptyEntries)
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "http" && uri.Port != 5188).ToArray());
        if (_serverUrls.Items.Count > 0) _serverUrls.SelectedIndex = 0;
        _serverUrls.Enabled = _copyUrl.Enabled = _serverUrls.Items.Count > 0;
        _pairingCode.Text = server.PairingCode;
        _copyCode.Enabled = true;
        _phoneUiChanged?.Invoke();
        Log("Phone interface started. Open the displayed address on the S21 and enter the pairing code. Windows Firewall may require a private-network exception for TCP 5187.");
    }
    private async Task StopServerAsync()
    {
        foreach(var player in _players.ToArray()) player.Close();
        if (_server != null) await _server.DisposeAsync();
        _server = null;
        _serverUrls.Items.Clear();
        _serverUrls.Enabled = _copyUrl.Enabled = _copyCode.Enabled = false;
        _pairingCode.Clear();
        _lan.Enabled = true;
        _phoneUiChanged?.Invoke();
    }
    private Task<bool> ConfirmRelayCertificateAsync(RelayCertificate certificate, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_closing || IsDisposed || ct.IsCancellationRequested) return Task.FromResult(false);
        BeginInvoke(() =>
        {
            if (_closing || ct.IsCancellationRequested) { answer.TrySetResult(false); return; }
            string message = $"Blink's authorized API selected this live relay, but Windows could not validate its certificate.\n\nHost: {certificate.Host}\nSubject: {certificate.Subject}\nIssuer: {certificate.Issuer}\nErrors: {certificate.Errors}\nValid through: {certificate.ValidToUtc.ToLocalTime():yyyy.MM.dd HH:mm:ss}\nSHA-256: {certificate.Fingerprint}\n\nTrust this exact certificate for this host for up to 30 days? This is a certificate exception; the relay's identity could not be verified by Windows. Choosing No keeps the connection blocked. A changed certificate will require approval again.";
            answer.TrySetResult(MessageBox.Show(this, message, "Review live relay certificate", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        });
        return answer.Task.WaitAsync(ct);
    }
    private void CopyConnectionText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        try { Clipboard.SetText(value); }
        catch (System.Runtime.InteropServices.ExternalException)
        { Log("Clipboard is busy. Try copying again, or select the pairing code and press Ctrl+C."); }
    }
    private void BindClients()
    {
        var selected = _clientGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ClientRow>().Select(c => c.Id).ToHashSet();
        ((SortableGrid)_clientGrid).Bind(_clients.Clients().Select(c => new ClientRow(c.Id, c.Name, c.Address,
            c.LastSeenUtc > DateTime.UtcNow.AddMinutes(-5) && _server != null ? "Active" : "Idle", c.Trusted,
            c.LastSeenUtc == DateTime.MinValue ? "Not seen this run" : c.LastSeenUtc.ToLocalTime().ToString("yyyy.MM.dd HH:mm:ss"), c.ExpiresUtc.ToLocalTime())).ToList());
        if (_clientGrid.Columns["Id"] is { } id) id.Visible = false;
        if (selected.Count > 0) { _clientGrid.ClearSelection(); foreach (DataGridViewRow row in _clientGrid.Rows) if (row.DataBoundItem is ClientRow client && selected.Contains(client.Id)) row.Selected = true; }
    }
    private Task UpdateClientsAsync(bool trust, bool revoke)
    {
        var clients = _clientGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ClientRow>().ToArray();
        foreach (var client in clients) { if (revoke) _clients.Revoke(client.Id); else _clients.SetTrusted(client.Id, trust); }
        BindClients(); return Task.CompletedTask;
    }
    private void Log(string text)
    {
        if (IsDisposed || _closing) return;
        if (InvokeRequired) { BeginInvoke(() => Log(text)); return; }
        _diagnosticSummary.Text=text.StartsWith("Error:")?"Latest event: operation failed — expand for details":text.StartsWith("Sign-in cooldown ended.")?"Latest event: sign-in cooldown ended":text.Contains("cooldown",StringComparison.OrdinalIgnoreCase)?"Latest event: sign-in cooldown":text.Contains("Authenticated")||text.Contains("session restored")?"Latest event: Blink connected":text.Contains("PIN")||text.Contains("SMS")?"Latest event: authentication update":text.Contains("archive",StringComparison.OrdinalIgnoreCase)?"Latest event: archive update":"Latest event: diagnostics updated";
        LogAt(text,text.StartsWith("Error:")||text.Contains("failed",StringComparison.OrdinalIgnoreCase)?0:1);
    }
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            _closing = true;
            Enabled = false;
            _operation?.Cancel();_archiveCancellation?.Cancel();
            await _lanDevices.ActivityMonitor.StopAsync();await _lanDevices.ActivityMonitor.TestLog.StopAsync();
            await StopMonitorAsync();
            await StopServerAsync();
            while (_operation != null||_archiveCancellation!=null) await Task.Delay(100);
            Close();
            return;
        }
        base.OnFormClosing(e);
    }
    private record ModuleChoice(int Id, int Network) { public override string ToString() => $"Module {Id} / network {Network}"; }
    private record ClipRow(string Key, int Module, string Camera, DateTime Recorded, string Bytes);
    private record ArchiveRow(string Key, string Camera, DateTime Recorded, string FileName, long Bytes);
    private record ClientRow(string Id, string Name, string Address, string Activity, bool Trusted, string LastSeen, DateTime Expires);
}


