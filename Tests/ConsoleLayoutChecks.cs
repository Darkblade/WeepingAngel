using DontBlink;
using DontBlink.Services;
using System.Reflection;

internal static class ConsoleLayoutChecks
{
    // Suppress startup session restoration: these are presentation checks only.
    private sealed class PreviewForm : Form1 { protected override void OnShown(EventArgs e) { } }
    public static int Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var form=new PreviewForm{StartPosition=FormStartPosition.Manual,Location=new Point(-4000,-4000),ShowInTaskbar=false};
        T Field<T>(string name)=>(T)typeof(Form1).GetField(name,flags)!.GetValue(form)!;
        void Invoke(string name)=>typeof(Form1).GetMethod(name,flags)!.Invoke(form,null);
        IEnumerable<Control> Desc(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in Desc(child))yield return nested;}}
        var all=Desc(form).ToArray();var tabs=all.OfType<TabControl>().ToArray();
        var settings=tabs.Single(t=>t.TabPages.Cast<TabPage>().Any(p=>p.Text=="Connection"));
        var main=tabs.Single(t=>t.TabPages.Cast<TabPage>().Any(p=>p.Text=="Settings"));main.SelectedTab=main.TabPages.Cast<TabPage>().Single(p=>p.Text=="Devices");settings.SelectedTab=settings.TabPages.Cast<TabPage>().Single(p=>p.Text=="Connection");
        form.Show();Application.DoEvents();Field<Action<bool>>("_setDebugLogCollapsed")(true);Application.DoEvents();
        var account=all.Single(c=>c.Name=="account-section");var phone=all.Single(c=>c.Name=="phone-section");
        var connection=all.OfType<FlowLayoutPanel>().Single(c=>c.Name=="connection-sections");
        Check(account.Parent!=phone.Parent,"account and phone access use separate navigation pages");
        Check(!Field<TextBox>("_pin").Visible,"verification input hidden while verification is not required");
        var auth=Field<BlinkAuthService>("_auth");typeof(BlinkAuthService).GetField("<RequiresPin>k__BackingField",flags)!.SetValue(auth,true);Invoke("UpdateAuthenticationPresentation");Application.DoEvents();
        Check(Field<TextBox>("_pin").Visible&&Field<List<Button>>("_loginButtons")[0].Text=="Verify code","verification state exposes the code field and action");
        Check(Field<List<Button>>("_loginButtons").Any(b=>b.Text.Contains("resend SMS")),"resend SMS remains available in verification state");
        typeof(BlinkAuthService).GetField("<RequiresPin>k__BackingField",flags)!.SetValue(auth,false);Invoke("UpdateAuthenticationPresentation");
        var authenticatedField=typeof(BlinkAuthService).GetField("<IsAuthenticated>k__BackingField",flags)!;authenticatedField.SetValue(auth,true);Invoke("UpdateAuthenticationPresentation");Check(form.Controls.Find("blink-account-help",true).Single().Text=="Connected to Blink. Your devices are available.","connected account replaces the sign-in invitation");authenticatedField.SetValue(auth,false);Invoke("UpdateAuthenticationPresentation");var reveal=all.OfType<CheckBox>().Single(c=>c.Text=="Show password");reveal.Checked=true;Check(!Field<TextBox>("_password").UseSystemPasswordChar,"password visibility action works");reveal.Checked=false;
        var lan=Field<CheckBox>("_lan");lan.Checked=true;Application.DoEvents();
        var start=all.OfType<Button>().Single(c=>c.Text=="Start phone interface");
        
        var phonePage=main.TabPages.Cast<TabPage>().Single(p=>p.Text=="Phone Access");main.SelectedTab=phonePage;Application.DoEvents();Check(start.Visible&&start.Width>100&&start.Height>=24&&start.Parent!.Height>=start.Height,"phone start action has usable visible bounds");lan.Checked=false;main.SelectedTab=main.TabPages.Cast<TabPage>().Single(p=>p.Text=="Devices");Application.DoEvents();
        Check(all.OfType<ConsoleInput>().Where(c=>c.Visible).All(c=>c.Height>=30&&c.Height>=c.Controls[0].Height+c.Padding.Vertical&&c.Bottom<=c.Parent!.ClientSize.Height),"credential inputs have sufficient visible height");var previews=Path.Combine(Directory.GetCurrentDirectory(),"docs","previews");Directory.CreateDirectory(previews);
        void Snapshot(string name){Application.DoEvents();using var shot=new Bitmap(form.Width,form.Height);form.DrawToBitmap(shot,new Rectangle(Point.Empty,shot.Size));shot.Save(Path.Combine(previews,name+".png"));}
        var theme=Field<ComboBox>("_themes");string oldTheme=(string)theme.SelectedItem!;
        try{
            // Apply palettes directly, without changing the user's saved selection.
            foreach(string name in ThemePalette.All.Keys){typeof(Form1).GetMethod("ApplyTheme",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[form,ThemePalette.All[name]]);Check(account.BackColor==ThemePalette.All[name].ColorOf(ThemePalette.All[name].Surface),name+" applies shared section colors");if(name is "Gold" or "Ocean" or "Contrast")Snapshot("console-connection-"+name.ToLowerInvariant());}
            typeof(Form1).GetMethod("ApplyTheme",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[form,ThemePalette.All["Gold"]]);form.Size=form.MinimumSize;Application.DoEvents();
            Check(account.Parent!=phone.Parent,"narrow layout keeps phone access separate");
            Check(!connection.HorizontalScroll.Visible,"narrow Connection needs no horizontal scrolling");
            Snapshot("console-connection-small");
            form.Size=new Size(1380,900);Field<Action<bool>>("_setDebugLogCollapsed")(false);Application.DoEvents();
            var split=all.OfType<SplitContainer>().Single(c=>c.Name=="workspace-log-split");Check(!split.IsSplitterFixed,"expanded Diagnostics can be resized");
            Snapshot("console-diagnostics-expanded");Field<Action<bool>>("_setDebugLogCollapsed")(true);
            foreach(float scale in new[]{1.25f,1.5f,2f}){
                form.Font=new Font("Segoe UI",10*scale);form.Size=new Size(1100,850);Application.DoEvents();
                Check(account.Parent!=phone.Parent,"large text "+scale+" keeps sections separate");
                Check(!connection.HorizontalScroll.Visible,"large text "+scale+" avoids horizontal clipping");
                Check(all.OfType<ThemedButton>().Where(b=>b.Presentation=="Sidebar").All(b=>b.Height>=b.Font.Height*2),"sidebar labels have room to wrap at "+scale);
                Snapshot("console-text-"+(int)(scale*100));
            }
        }finally{typeof(Form1).GetMethod("ApplyTheme",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[form,ThemePalette.All[oldTheme]]);}
        string cooldownPath=Path.Combine(Path.GetTempPath(),"WeepingAngel-cooldown-"+Guid.NewGuid()+".json");
        var now=DateTimeOffset.UtcNow;var cooldown=new LoginCooldown(cooldownPath,()=>now);
        var cooldownField=typeof(BlinkAuthService).GetField("<Cooldown>k__BackingField",flags)!;var originalCooldown=cooldownField.GetValue(auth);
        try{
            cooldownField.SetValue(auth,cooldown);cooldown.RateLimited(new Blink.Exceptions.BlinkLoginRateLimitException(now.AddMinutes(1),true));Invoke("UpdateLoginCooldown");
            var log=Field<ListBox>("_log");int CountEnded()=>log.Items.Cast<object>().Count(item=>item.ToString()!.Contains("Sign-in cooldown ended."));int before=CountEnded();
            Check(Field<List<Button>>("_loginButtons").All(b=>!b.Enabled),"cooldown disables sign-in actions");now=now.AddMinutes(2);Invoke("UpdateLoginCooldown");
            Check(CountEnded()==before+1&&Field<List<Button>>("_loginButtons").All(b=>b.Enabled),"cooldown expiry logs once and enables sign-in actions");Invoke("UpdateLoginCooldown");Invoke("UpdateLoginCooldown");Check(CountEnded()==before+1,"subsequent timer ticks do not repeat the expiry message");
            Check(Field<Label>("_diagnosticSummary").Text=="Latest event: sign-in cooldown ended","collapsed Diagnostics identifies cooldown expiry");
        }finally{cooldownField.SetValue(auth,originalCooldown);if(File.Exists(cooldownPath))File.Delete(cooldownPath);}
        Console.WriteLine("PASS console presentation checks; no Blink login or phone server started");return 0;
    }
    private static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
}
