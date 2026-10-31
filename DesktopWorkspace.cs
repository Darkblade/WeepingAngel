using DontBlink.Services;

namespace DontBlink;

public partial class Form1
{
    private void BuildWorkspace(TableLayoutPanel root,TabControl tabs,FlowLayoutPanel login,FlowLayoutPanel usbTools,FlowLayoutPanel archiveTools,FlowLayoutPanel phone,TableLayoutPanel debug,TabPage usb,TabPage archive,TabPage cameras,TabPage clients)
    {
        SuspendLayout();
        root.Controls.Clear();root.RowStyles.Clear();root.RowCount=3;root.Padding=new Padding(0);
        root.RowStyles.Add(new(SizeType.AutoSize));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.AutoSize));
        var banner=new TableLayoutPanel{Name="console-header",Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Padding=new Padding(16,8,16,8),Margin=Padding.Empty};
        banner.ColumnStyles.Add(new(SizeType.Percent,100));banner.ColumnStyles.Add(new(SizeType.AutoSize));
        var identity=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty,Anchor=AnchorStyles.Left};
        var icon=new PictureBox{Image=Icon!.ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Size=new Size(28,28),Margin=new Padding(0,2,10,0)};
        icon.Disposed+=(_,_)=>icon.Image?.Dispose();
        var brand=new Label{Name="application-brand",Text="Weeping Angel",AutoSize=true,Margin=new Padding(0,2,0,0),Font=Branding.TitleFont(16)};
        identity.Controls.AddRange([icon,brand]);
        var themeTools=new FlowLayoutPanel{AutoSize=true,Anchor=AnchorStyles.Right,WrapContents=false,Margin=Padding.Empty};themeTools.Controls.Add(new Label{Text="Theme",AutoSize=true,Margin=new Padding(0,6,8,0)});_themes.Width=140;themeTools.Controls.Add(_themes);
        banner.Controls.Add(identity,0,0);banner.Controls.Add(themeTools,1,0);root.Controls.Add(banner,0,0);
        var contentSplit=new SplitContainer{Name="workspace-log-split",Size=new Size(900,600),Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,Panel1MinSize=180,Panel2MinSize=44,FixedPanel=FixedPanel.Panel2,IsSplitterFixed=false,SplitterWidth=6,Margin=Padding.Empty};
        var logContainer=new Panel{Name="debug-log-container",Dock=DockStyle.Fill,Padding=new Padding(12,2,12,8),Margin=Padding.Empty};
        contentSplit.Panel2.Controls.Add(logContainer);
        var workspace=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};
        workspace.ColumnStyles.Add(new(SizeType.Absolute,184));workspace.ColumnStyles.Add(new(SizeType.Percent,100));
        var navigation=new FlowLayoutPanel{Name="console-sidebar",Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8,16,8,12)};
        var connection=new TabPage("Connection");var appearance=new TabPage("Accessibility");var settings=new TabPage("Settings");var devicesPage=new TabPage("Devices");var phonePage=new TabPage("Phone Access");
        tabs.TabPages.Remove(clients);tabs.TabPages.AddRange([devicesPage,phonePage,settings]);
        tabs.Appearance=TabAppearance.FlatButtons;tabs.SizeMode=TabSizeMode.Fixed;tabs.ItemSize=new Size(1,1);tabs.Multiline=true;tabs.Padding=Point.Empty;
        tabs.DrawMode=TabDrawMode.Normal;tabs.Margin=Padding.Empty;
        archive.Text="Clips";usb.Text="USB storage";cameras.Text="Home";clients.Text="Paired devices";
        foreach(var page in tabs.TabPages.Cast<TabPage>())page.Padding=new Padding(24);
        var navButtons=new Dictionary<TabPage,SettingsTabLink>();
        foreach(var page in new[]{cameras,archive,usb,devicesPage,phonePage,settings}){
            var b=new SettingsTabLink{Text=page.Text,IconName=page.Text,Name="sidebar-page-link",AutoSize=false,Width=160,Height=48,Padding=new Padding(38,10,10,12),TextAlign=ContentAlignment.MiddleLeft,AccessibleName=page.Text};
            b.Click+=(_,_)=>tabs.SelectedTab=page;navButtons.Add(page,b);navigation.Controls.Add(b);
        }
        void Mark(){_pageStatus.Text=tabs.SelectedTab?.Text??"";foreach(var pair in navButtons){pair.Value.Text=pair.Key.Text;pair.Value.Selected=tabs.SelectedTab==pair.Key;pair.Value.AccessibleDescription=tabs.SelectedTab==pair.Key?"Current page":"";pair.Value.Invalidate();}}
        tabs.SelectedIndexChanged+=(_,_)=>Mark();tabs.VisibleChanged+=(_,_)=>Mark();tabs.HandleCreated+=(_,_)=>Mark();Mark();
        var guardLink=new SettingsTabLink{Text="Guard Post",IconName="Guard Post",Name="guard-post-navigation",AutoSize=false,Width=160,Height=48,Padding=new Padding(38,10,10,12),TextAlign=ContentAlignment.MiddleLeft};guardLink.Click+=async(_,_)=>await RunAsync(async()=>{if(_server==null)await StartServerAsync();Open(_server!.LocalAddress+"/guard-post?theme="+Uri.EscapeDataString((string)_themes.SelectedItem!));});navigation.Controls.Add(guardLink);navigation.Controls.SetChildIndex(guardLink,3);
        // Actions now live with the content they act on.
        var usbSecondRow=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Dock=DockStyle.Fill,Margin=Padding.Empty};var usbControls=usbTools.Controls.Cast<Control>().ToArray();int filterIndex=Array.IndexOf(usbControls,_filter);foreach(var control in usbControls.Skip(filterIndex))usbSecondRow.Controls.Add(control);usbTools.FlowDirection=FlowDirection.TopDown;usbTools.WrapContents=false;var usbFirstRow=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Margin=Padding.Empty};foreach(var control in usbControls.Take(filterIndex))usbFirstRow.Controls.Add(control);usbTools.Controls.Add(usbFirstRow);usbTools.Controls.Add(usbSecondRow);
        var usbLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};usbLayout.RowStyles.Add(new(SizeType.AutoSize));usbLayout.RowStyles.Add(new(SizeType.Percent,100));
        usbLayout.Controls.Add(usbTools,0,0);usbLayout.Controls.Add(_source,0,1);usb.Controls.Add(usbLayout);
        var archiveLayout=(TableLayoutPanel)_archive.Parent!;archiveLayout.RowCount=4;archiveLayout.RowStyles.Add(new(SizeType.AutoSize));archiveLayout.Controls.Add(archiveTools,0,3);
        archiveTools.Controls.OfType<Button>().FirstOrDefault(b=>b.Text=="Live view status")?.Hide();
        void PageHeading(TableLayoutPanel layout,string text){layout.SuspendLayout();foreach(Control child in layout.Controls.Cast<Control>().ToArray())layout.SetRow(child,layout.GetRow(child)+1);layout.RowCount++;layout.RowStyles.Insert(0,new RowStyle(SizeType.AutoSize));layout.Controls.Add(new Label{Name="page-heading",Text=text,AutoSize=true,Font=new Font("Segoe UI Semibold",18),Margin=new Padding(0,0,0,16)},0,0);layout.ResumeLayout(true);}
        PageHeading(usbLayout,"USB storage");PageHeading(archiveLayout,"Clips");PageHeading((TableLayoutPanel)cameras.Controls[0],"Home");
        var phoneAccess=new TabPage("Access");BuildConnectionPage(connection,login,phone,phoneAccess);
        var appearanceContent=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8)};
        var preferences=AppSettings.Load();
        appearanceContent.Controls.Add(new Label{Text="Accessibility",Font=new Font("Segoe UI Semibold",16),AutoSize=true,Margin=new Padding(8,24,8,12)});
        var size=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=200,AccessibleName="Text size"};size.Items.AddRange(["100%","125%","150%","200%"]);size.SelectedIndex=preferences.TextScale>=2?3:preferences.TextScale>=1.5?2:preferences.TextScale>=1.25?1:0;
        appearanceContent.Controls.Add(new Label{Text="Text size",AutoSize=true});appearanceContent.Controls.Add(size);
        size.SelectedIndexChanged+=(_,_)=>{double scale=new[]{1d,1.25,1.5,2}[size.SelectedIndex];(AppSettings.Load() with{TextScale=scale}).Save();Font=new Font("Segoe UI",(float)(10*scale));};
        void Preference(string text,bool value,Func<AppSettings,bool,AppSettings> update){var check=new ThemedCheckBox{Text=text,Checked=value,AutoSize=true,Margin=new Padding(8)};check.CheckedChanged+=(_,_)=>{update(AppSettings.Load(),check.Checked).Save();ApplyTheme(this,ThemePalette.Resolve((string)_themes.SelectedItem!));};appearanceContent.Controls.Add(check);}
        Preference("Comfortable controls",preferences.ComfortableControls,(a,b)=>a with{ComfortableControls=b});
        Preference("Increased text spacing",preferences.TextSpacing,(a,b)=>a with{TextSpacing=b});
        Preference("Reduce visual clutter",preferences.ReduceVisualClutter,(a,b)=>a with{ReduceVisualClutter=b});
        var motion=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=200,AccessibleName="Motion preference"};motion.Items.AddRange(["Follow system","Reduce motion"]);motion.SelectedIndex=preferences.MotionPreference=="Reduce"?1:0;motion.SelectedIndexChanged+=(_,_)=>{(AppSettings.Load() with{MotionPreference=motion.SelectedIndex==1?"Reduce":"System"}).Save();};appearanceContent.Controls.Add(motion);
        appearance.Controls.Add(appearanceContent);
        var overlayPage=new TabPage("Recording overlays");
        var overlayContent=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(12)};
        overlayContent.Controls.Add(new Label{Text="Recording overlays",AutoSize=true,Font=new Font("Segoe UI Semibold",17),Margin=new Padding(0,8,0,14)});
        overlayContent.Controls.Add(new Label{Text="Configure timestamps, camera names and watermarks for new recordings.",AutoSize=true,Margin=new Padding(0,0,0,14)});
        var overlayButton=archiveTools.Controls.OfType<Button>().Single(b=>b.Text=="Recording overlays");overlayContent.Controls.Add(overlayButton);overlayPage.Controls.Add(overlayContent);
        var servicesPage=new TabPage("Services & systems");var servicesContent=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};servicesContent.Controls.Add(new Label{Text="Services, systems and devices",Font=new Font("Segoe UI Semibold",17),AutoSize=true});servicesContent.Controls.Add(new Label{Text="Manage each provider and system separately. Blink, local archives, and future RTSP / ONVIF services share this workspace.",AutoSize=true,MaximumSize=new Size(700,0),Margin=new Padding(0,12,0,12)});Add(servicesContent,"Open services and device settings",()=>OpenManagementAsync());servicesContent.Controls.Add(new Label{Text="Blink: system arm/disarm, module status, camera details and supported settings.\nRTSP / ONVIF: placeholders until their provider adapters are implemented.",AutoSize=true,MaximumSize=new Size(700,0)});servicesPage.Controls.Add(servicesContent);
        void NestedPages(TabPage parent,params TabPage[] pages){var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Percent,100));var nav=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};var inner=new TabControl{Appearance=TabAppearance.FlatButtons,SizeMode=TabSizeMode.Fixed,ItemSize=new Size(1,1),Multiline=true};inner.TabPages.AddRange(pages);var links=new Dictionary<TabPage,SettingsTabLink>();foreach(var page in pages){var link=new SettingsTabLink{Text=page.Text,AutoSize=true,Selected=page==inner.SelectedTab};link.Click+=(_,_)=>inner.SelectedTab=page;links.Add(page,link);nav.Controls.Add(link);}inner.SelectedIndexChanged+=(_,_)=>{foreach(var pair in links)pair.Value.Selected=pair.Key==inner.SelectedTab;};var host=new Panel{Dock=DockStyle.Fill};host.Controls.Add(inner);host.SizeChanged+=(_,_)=>inner.Bounds=new Rectangle(-4,-8,host.Width+8,host.Height+8);layout.Controls.Add(nav,0,0);layout.Controls.Add(host,0,1);parent.Controls.Add(layout);}
        var lanPage=BuildDeviceSettings();lanPage.Text="LAN devices";NestedPages(devicesPage,lanPage,connection,servicesPage);NestedPages(phonePage,phoneAccess,clients);
        var settingsContent=new FlowLayoutPanel{Name="settings-sections",Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8)};settings.Controls.Add(settingsContent);settingsContent.Controls.Add(new Label{Name="page-heading",Text="Settings",AutoSize=true,Font=new Font("Segoe UI Semibold",18),Margin=new Padding(0,0,0,16)});
        var accessibilitySection=AddDeviceSection(settingsContent,"accessibility-section");foreach(Control control in appearanceContent.Controls.Cast<Control>().ToArray())accessibilitySection.Controls.Add(control);
        var recordingSection=AddDeviceSection(settingsContent,"recording-overlays-section");foreach(Control control in overlayContent.Controls.Cast<Control>().Where(c=>c is not Button).ToArray())recordingSection.Controls.Add(control);_actions.Remove(overlayButton);overlayButton.Dispose();BuildInlineOverlayControls(recordingSection);
        var debugTools=(FlowLayoutPanel)debug.Controls[0];
        debugTools.Padding=Padding.Empty;debugTools.Margin=Padding.Empty;debugTools.WrapContents=false;

        var toggleLog=new ThemedButton{Name="toggle-debug-log",AutoSize=true,MinimumSize=new Size(36,34),Size=new Size(36,34),AccessibleName="Collapse debug log",Margin=new Padding(0,4,0,4)};
        debugTools.Controls.Add(toggleLog);debugTools.Controls.Add(new Label{Text="Diagnostics",AutoSize=true,Margin=new Padding(8,10,12,0)});var detail=new ComboBox{Name="diagnostic-level",AccessibleName="Diagnostic verbosity",DropDownStyle=ComboBoxStyle.DropDownList,Width=120,Margin=new Padding(4,6,8,0)};detail.Items.AddRange(["Errors","Normal","Detailed","Trace"]);detail.SelectedItem=detail.Items.Contains(AppSettings.Load().DiagnosticVerbosity)?AppSettings.Load().DiagnosticVerbosity:"Normal";detail.SelectedIndexChanged+=(_,_)=>{try{SetDiagnosticLevel((string)detail.SelectedItem!);}catch(Exception ex){Log("Could not save diagnostic level: "+ex.Message);}};debugTools.Controls.Add(detail);_diagnosticSummary.Margin=new Padding(8,10,0,0);debugTools.Controls.Add(_diagnosticSummary);debugTools.AutoSize=false;debug.Padding=Padding.Empty;debug.Margin=Padding.Empty;
        logContainer.Controls.Add(debug);
        var logMenu=new ContextMenuStrip{ShowImageMargin=false};
        var copyLogItem=logMenu.Items.Add("Copy debug log");
        copyLogItem.Click+=(_,_)=>CopyConnectionText(DebugLogFormatter.Format(_log.Items.Cast<object>().Select(item=>item.ToString()??"")));
        logMenu.Opening+=(_,_)=>{var palette=ThemePalette.Resolve((string)_themes.SelectedItem!);logMenu.BackColor=palette.ColorOf(palette.Surface);logMenu.ForeColor=palette.ColorOf(palette.Text);};
        _log.ContextMenuStrip=logMenu;_log.Disposed+=(_,_)=>logMenu.Dispose();
        _log.AccessibleName="Debug log";
        bool logCollapsed=AppSettings.Load().DebugLogCollapsed;int logHeight=Math.Clamp(AppSettings.Load().DebugLogHeight,190,600);bool sizingLog=false;bool userSizingLog=false;
        void SizeLog(){
            if(sizingLog)return;sizingLog=true;int toolbarHeight=Math.Max(42,toggleLog.PreferredSize.Height+8);
            debug.RowStyles[0].SizeType=SizeType.Absolute;debug.RowStyles[0].Height=toolbarHeight;
            int available=contentSplit.ClientSize.Height-contentSplit.SplitterWidth;
            int desired=logCollapsed?toolbarHeight+logContainer.Padding.Vertical:Math.Max(toolbarHeight+150,logHeight);
            if(!logCollapsed){int mainMinimum=Math.Min(available-contentSplit.Panel2MinSize,Math.Max(contentSplit.Panel1MinSize,420));desired=Math.Min(desired,Math.Max(contentSplit.Panel2MinSize,Math.Min((int)(available*0.4),available-mainMinimum)));}
            if(available>=contentSplit.Panel1MinSize+contentSplit.Panel2MinSize)
                contentSplit.SplitterDistance=Math.Clamp(available-desired,contentSplit.Panel1MinSize,available-contentSplit.Panel2MinSize);sizingLog=false;
        }
        _setDebugLogCollapsed=collapsed=>{
            logCollapsed=collapsed;_log.Visible=!collapsed;
            toggleLog.Text=collapsed?"+":"−";
            toggleLog.AccessibleName=collapsed?"Expand debug log":"Collapse debug log";
            toggleLog.AccessibleDescription=collapsed?"Log collapsed":"Log expanded";
            contentSplit.IsSplitterFixed=collapsed;toggleLog.Selected=!collapsed;SizeLog();toggleLog.Invalidate();
        };
        toggleLog.Click+=(_,_)=>{
            _setDebugLogCollapsed(!logCollapsed);
            try{(AppSettings.Load() with{DebugLogCollapsed=logCollapsed}).Save();}
            catch(Exception ex){Log("Could not remember log visibility: "+ex.Message);}
        };
        contentSplit.SplitterMoving+=(_,e)=>{userSizingLog=true;int available=contentSplit.ClientSize.Height-contentSplit.SplitterWidth;e.SplitY=Math.Max(e.SplitY,Math.Min(available-contentSplit.Panel2MinSize,Math.Max(420,(int)(available*0.6))));};contentSplit.SplitterMoved+=(_,_)=>{if(userSizingLog&&!sizingLog&&!logCollapsed){userSizingLog=false;logHeight=contentSplit.Panel2.Height;try{(AppSettings.Load() with{DebugLogHeight=logHeight}).Save();}catch{}}};contentSplit.SizeChanged+=(_,_)=>SizeLog();toggleLog.FontChanged+=(_,_)=>SizeLog();
        _setDebugLogCollapsed(logCollapsed);
        // Crop the native TabControl frame; navigation is provided by the sidebar.
        var pageHost=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};tabs.Dock=DockStyle.None;
        void FitPages(){tabs.Bounds=new Rectangle(-4,-8,pageHost.ClientSize.Width+8,pageHost.ClientSize.Height+12);}
        pageHost.Controls.Add(tabs);pageHost.SizeChanged+=(_,_)=>FitPages();FitPages();
        workspace.Controls.Add(navigation,0,0);workspace.Controls.Add(pageHost,1,0);contentSplit.Panel1.Controls.Add(workspace);root.Controls.Add(contentSplit,0,1);var status=new TableLayoutPanel{Name="console-status",AutoSize=true,Dock=DockStyle.Fill,ColumnCount=3,Padding=new Padding(16,4,16,4),Margin=Padding.Empty};status.ColumnStyles.Add(new(SizeType.AutoSize));status.ColumnStyles.Add(new(SizeType.Percent,100));status.ColumnStyles.Add(new(SizeType.AutoSize));status.Controls.Add(_connectionStatus,0,0);_transferStatus.Dock=DockStyle.Fill;_transferStatus.AutoSize=false;status.Controls.Add(_transferStatus,1,0);status.Controls.Add(_pageStatus,2,0);root.Controls.Add(status,0,2);
        tabs.Selected+=async(_,e)=>{if(e.TabPage==archive)await RunAsync(ReloadArchiveAsync);};
        Microsoft.Win32.UserPreferenceChangedEventHandler systemChanged=(_,_)=>{if(!IsDisposed&&IsHandleCreated)BeginInvoke(()=>ApplyTheme(this,ThemePalette.Resolve((string)_themes.SelectedItem!)));};
        Microsoft.Win32.SystemEvents.UserPreferenceChanged+=systemChanged;Disposed+=(_,_)=>Microsoft.Win32.SystemEvents.UserPreferenceChanged-=systemChanged;
        FontChanged+=(_,_)=>{foreach(var label in Controls.Find("section-heading",true).Concat(Controls.Find("page-heading",true)).OfType<Label>()){var previous=label.Font;label.Font=new Font("Segoe UI Semibold",Font.Size*(label.Name=="page-heading"?1.8f:1.2f));previous.Dispose();}};
        ResumeLayout(true);
    }

    private void BuildConnectionPage(TabPage page,FlowLayoutPanel login,FlowLayoutPanel phone,TabPage phonePage)
    {
        var content=new FlowLayoutPanel{Name="connection-sections",Dock=DockStyle.Fill,WrapContents=true,AutoScroll=true,Padding=new Padding(0,16,0,0),Margin=Padding.Empty};
        var account=new ConsoleSection{Name="account-section"};var phoneSection=new ConsoleSection{Name="phone-section"};
        void Heading(ConsoleSection section,string title,string help){section.Controls.Add(new Label{Text=title,Name="section-heading",AutoSize=true,Font=new Font("Segoe UI Semibold",12),Margin=new Padding(0,0,0,8)});section.Controls.Add(new Label{Text=help,Name="supporting-text",AutoSize=true,Margin=new Padding(0,0,0,16)});}
        Heading(account,"Blink account","");var accountHelp=account.Controls.OfType<Label>().Single(l=>l.Name=="supporting-text");accountHelp.Name="blink-account-help";
        _authStatus.Margin=new Padding(0,0,0,16);account.Controls.Add(_authStatus);
        Control Field(string title,Control control){var field=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=2,Margin=new Padding(0,0,0,12)};field.RowStyles.Add(new(SizeType.AutoSize));field.RowStyles.Add(new(SizeType.AutoSize));field.Controls.Add(new Label{Text=title,AutoSize=true,Margin=new Padding(0,0,0,8)});control.Margin=Padding.Empty;control.AccessibleName=title;var input=new ConsoleInput(control){Width=360,Margin=Padding.Empty};field.Controls.Add(input);void MeasureField(){field.MinimumSize=new Size(0,field.Controls[0].PreferredSize.Height+8+input.GetPreferredSize(Size.Empty).Height);}input.SizeChanged+=(_,_)=>MeasureField();field.FontChanged+=(_,_)=>MeasureField();MeasureField();account.Controls.Add(field);return field;}
        _email.Width=_password.Width=360;Field("Email address",_email);Field("Password",_password);
        var reveal=new ThemedCheckBox{Text="Show password",AutoSize=true,Margin=new Padding(0,0,0,12)};reveal.CheckedChanged+=(_,_)=>_password.UseSystemPasswordChar=!reveal.Checked;account.Controls.Add(reveal);
        _verificationField=Field("Verification code",_pin);_verificationField.Visible=_auth.RequiresPin;
        _remember.Margin=new Padding(0,0,0,16);account.Controls.Add(_remember);
        _loginCooldownLabel.Margin=new Padding(0,0,0,12);account.Controls.Add(_loginCooldownLabel);
        var actions=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,12)};
        foreach(var button in login.Controls.OfType<Button>().ToArray()){button.AutoSize=true;button.MinimumSize=new Size(0,36);if(button==_loginButtons[0])((ThemedButton)button).Presentation="Primary";actions.Controls.Add(button);}account.Controls.Add(actions);
        account.Controls.Add(new Label{Text="A verification code will be requested when needed.",Name="supporting-text",AutoSize=true,Margin=Padding.Empty});
        Heading(phoneSection,"Phone access","Use the phone interface on your local network.");
        _lan.Text="Enable phone interface";_lan.AutoSize=true;_lan.Enabled=true;_lan.Margin=new Padding(0,0,0,12);phoneSection.Controls.Add(_lan);phoneSection.Controls.Add(_phoneStatus);
        var start=phone.Controls.OfType<Button>().Single(b=>b.Text=="Start phone interface");var stop=phone.Controls.OfType<Button>().Single(b=>b.Text=="Stop phone interface");var secure=phone.Controls.OfType<Button>().Single(b=>b.Text=="Secure phone setup");
        var phoneOptions=new FlowLayoutPanel{Name="phone-options",AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(0,12,0,0)};
        var sharedSlot=new Panel{MinimumSize=new Size(220,26),Width=220,Height=26,Margin=new Padding(0,0,0,12)};start.Dock=stop.Dock=DockStyle.Fill;start.AutoSize=stop.AutoSize=false;sharedSlot.Controls.Add(start);sharedSlot.Controls.Add(stop);phoneOptions.Controls.Add(sharedSlot);
        var addresses=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,12)};addresses.Controls.Add(_serverUrls);addresses.Controls.Add(_copyUrl);phoneOptions.Controls.Add(new Label{Text="Address",AutoSize=true});phoneOptions.Controls.Add(addresses);
        var pairing=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Margin=new Padding(0,0,0,12)};pairing.Controls.Add(_pairingCode);pairing.Controls.Add(_copyCode);phoneOptions.Controls.Add(new Label{Text="Pairing code",AutoSize=true});phoneOptions.Controls.Add(pairing);phoneOptions.Controls.Add(secure);phoneSection.Controls.Add(phoneOptions);
        phoneSection.Controls.Add(new Label{Text="Pairing",AutoSize=true,Font=new Font("Segoe UI Semibold",10),Margin=new Padding(0,20,0,8)});phoneSection.Controls.Add(new Label{Text="Manage connected phones in Paired devices.",Name="supporting-text",AutoSize=true,Margin=Padding.Empty});
        _phoneUiChanged=()=>{phoneOptions.Visible=_lan.Checked;start.Visible=_lan.Checked&&_server==null;stop.Visible=_lan.Checked&&_server!=null;secure.Enabled=_server==null;_phoneStatus.Text=!_lan.Checked?"Phone access is turned off.":_server==null?"Ready to start the phone interface.":"Phone interface is running.";};
        _lan.CheckedChanged+=async(_,_)=>{_phoneUiChanged();if(_operation==null&&_server!=null)await RunAsync(StopServerAsync);};_phoneUiChanged();
        content.Controls.Add(account);page.Controls.Add(content);var phoneContent=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(8)};phoneContent.Controls.Add(phoneSection);phonePage.Controls.Add(phoneContent);void FitPhone(){sharedSlot.Height=Math.Max(26,Font.Height+8);sharedSlot.MinimumSize=new Size(220,sharedSlot.Height);int width=Math.Clamp(phoneContent.ClientSize.Width-40,260,650);phoneSection.MinimumSize=new Size(width,0);phoneSection.MaximumSize=new Size(width,0);phoneSection.Width=width;int inner=width-phoneSection.Padding.Horizontal;foreach(Control control in phoneSection.Controls){control.MaximumSize=new Size(inner,0);if(control is FlowLayoutPanel flow){flow.Width=inner;flow.MaximumSize=new Size(inner,0);foreach(Control child in flow.Controls){child.MaximumSize=new Size(inner,0);if(child is FlowLayoutPanel line){line.MaximumSize=new Size(inner,0);line.Width=inner;}if(child is TextBox or ComboBox)child.Width=Math.Min(inner,400);}}}phoneSection.PerformLayout();}
        phoneContent.SizeChanged+=(_,_)=>FitPhone();phoneContent.VisibleChanged+=(_,_)=>FitPhone();FontChanged+=(_,_)=>FitPhone();_lan.CheckedChanged+=(_,_)=>FitPhone();FitPhone();        bool fitting=false;
        void Fit(){if(fitting)return;fitting=true;int gap=16;int w=Math.Max(240,content.ClientSize.Width-content.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);int min=(int)(410*DeviceDpi/96d*Math.Max(1,Font.Size/10));bool wide=false;int accountWidth=wide?(w-gap)*56/100:Math.Min(w,760);account.MinimumSize=new Size(accountWidth,0);account.MaximumSize=new Size(accountWidth,0);account.Width=accountWidth;int phoneWidth=wide?w-accountWidth-gap:accountWidth;account.Margin=new Padding(0,0,wide?gap:0,16);phoneSection.Margin=new Padding(0,0,0,16);
            foreach(var section in new[]{account}){int inner=section.Width-section.Padding.Horizontal;foreach(Control c in section.Controls){c.MaximumSize=new Size(inner,0);if(c is FlowLayoutPanel or TableLayoutPanel){c.Width=inner;foreach(Control child in c.Controls){child.MaximumSize=new Size(inner,0);if(child is TextBox or ComboBox or ConsoleInput)child.Width=Math.Min(inner,440);}}}section.PerformLayout();}fitting=false;}
        content.SizeChanged+=(_,_)=>Fit();FontChanged+=(_,_)=>Fit();Fit();UpdateAuthenticationPresentation();
    }

}

// Explicit theme states and additive keyboard focus for native action controls.
internal sealed class ThemedButton:Button
{
    public override Size GetPreferredSize(Size proposedSize){if(Presentation!="Compact")return base.GetPreferredSize(proposedSize);var text=TextRenderer.MeasureText(Text,Font,new Size(int.MaxValue,int.MaxValue),TextFormatFlags.SingleLine);return new Size(text.Width+18,Math.Max(24,text.Height+6));}
    public bool Selected;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ThemePalette? Palette { get; set; }
    public ThemedButton(){Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;}
    private bool hovered,pressed;
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);var previous=Region;Region=null;previous?.Dispose();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(Presentation=="Sidebar")Height=Math.Max(44,Font.Height*2+12);}
    protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode is Keys.Space or Keys.Enter){pressed=true;Invalidate();}base.OnKeyDown(e);}
    protected override void OnKeyUp(KeyEventArgs e){pressed=false;Invalidate();base.OnKeyUp(e);}
    protected override void OnEnabledChanged(EventArgs e){pressed=false;base.OnEnabledChanged(e);Invalidate();}
    protected override void OnMouseEnter(EventArgs e){hovered=true;base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;base.OnMouseLeave(e);Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e){pressed=true;base.OnMouseDown(e);Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;base.OnMouseUp(e);Invalidate();}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Presentation { get; set; }="Standard";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string IconName { get; set; }="";
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Palette is not {} p){base.OnPaint(e);return;}
        string foreground=!Enabled?p.Muted:hovered||pressed||Selected?p.Accent:p.Text;
                using(var background=new SolidBrush(Parent?.BackColor??p.ColorOf(p.Background)))e.Graphics.FillRectangle(background,ClientRectangle);
        if(Presentation=="Compact"){
            string state=!Enabled?"disabled":pressed?"pressed":hovered?"hover":"normal";string token="buttons.standard."+state+".";
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var shape=RoundedGeometry.Path(new RectangleF(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3)),4);using var fill=new SolidBrush(p.ColorOf(p.Token(token+"background",p.Surface)));e.Graphics.FillPath(fill,shape);using var border=new Pen(p.ColorOf(hovered||Focused?p.Accent:p.Token(token+"border",p.Raised)));e.Graphics.DrawPath(border,shape);
        }
        if(Selected){using var underline=new Pen(p.ColorOf(p.Accent),2);e.Graphics.DrawLine(underline,10,Height-2,Width-10,Height-2);}
        int left=Presentation=="Sidebar"?44:12;
        if(Presentation=="Sidebar")ConsoleGlyph.Draw(e.Graphics,IconName,new Rectangle(15,(Height-18)/2,18,18),p.ColorOf(foreground));
        var label=Text;if(AppSettings.Load().TextSpacing)label=string.Join("\u2009",label.ToCharArray());
        TextRenderer.DrawText(e.Graphics,label,Font,new Rectangle(left,4,Math.Max(1,Width-left-10),Height-8),p.ColorOf(foreground),TextFormatFlags.VerticalCenter|(Presentation=="Sidebar"||TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.WordBreak);
        if(Focused&&ShowFocusCues){using var focus=new Pen(p.ColorOf(p.Accent),2);using var focusPath=RoundedGeometry.Path(new RectangleF(4,4,Width-9,Height-9),4);e.Graphics.DrawPath(focus,focusPath);}
    }
}
