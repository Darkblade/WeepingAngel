using DontBlink.Services;
namespace DontBlink;
public partial class Form1
{
    private void BuildInlineOverlayControls(FlowLayoutPanel panel)
    {
        var saved=(AppSettings.Load().Overlay??new()).Validated();
        CheckBox Check(string text,bool value){var control=new ThemedCheckBox{Text=text,Checked=value,AutoSize=true};panel.Controls.Add(control);return control;}
        var enabled=Check("Enable recording overlays",saved.Enabled);var timestamp=Check("Timestamp",saved.Timestamp);var camera=Check("Camera name",saved.Camera);var utc=Check("Use UTC",saved.Utc);
        Control Field(string title,Control input){panel.Controls.Add(new Label{Text=title,AutoSize=true});panel.Controls.Add(input);return input;}
        var watermark=(TextBox)Field("Watermark",new TextBox{Text=saved.Watermark,Width=300,MaxLength=120});
        ComboBox Choice(string title,string[] items,string selected){var control=new ComboBox{Width=260,DropDownStyle=ComboBoxStyle.DropDownList};control.Items.AddRange(items);control.SelectedItem=selected;Field(title,control);return control;}
        var placement=Choice("Placement",RecordingOverlay.Placements,saved.Placement);var font=Choice("Font",RecordingOverlay.Fonts,saved.Font);var format=Choice("Timestamp format",RecordingOverlay.Formats,saved.Format);
        var size=(NumericUpDown)Field("Text size",new NumericUpDown{Minimum=12,Maximum=72,Value=saved.Size,Width=100});
        var save=new ThemedButton{Text="Save recording overlays",AutoSize=true};panel.Controls.Add(save);var status=new Label{AutoSize=true};panel.Controls.Add(status);
        save.Click+=(_,_)=>{try{var overlay=new RecordingOverlay(enabled.Checked,timestamp.Checked,camera.Checked,watermark.Text,(string)placement.SelectedItem!, (string)font.SelectedItem!,(int)size.Value,(string)format.SelectedItem!,utc.Checked).Validated();(AppSettings.Load() with{Overlay=overlay}).Save();status.Text="Saved. Applies to new recordings.";Log("Recording overlay settings saved.");}catch(Exception ex){status.Text=ex.Message;}};
    }
}