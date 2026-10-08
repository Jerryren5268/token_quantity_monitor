using System;
using System.Drawing;
using System.Windows.Forms;

class RefreshSettings : Form {
    readonly RadioButton automatic,manual;
    readonly NumericUpDown minutes;
    readonly TrackBar petSize;
    readonly CheckBox roaming;
    public int RefreshMinutes {get{return manual.Checked?0:(int)minutes.Value;}}
    public int PetScalePercent {get{return petSize.Value;}}
    public bool RoamingEnabled {get{return roaming.Checked;}}
    public RefreshSettings(int current,int currentScale=100,bool currentRoaming=true){
        Text="桌宠设置";ClientSize=new Size(350,420);FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;BackColor=Style.Cream;Font=Style.Font(10);ForeColor=Style.Ink;
        automatic=new RadioButton{Text="自动刷新",Location=new Point(22,22),Size=new Size(125,28),Checked=current>0};
        manual=new RadioButton{Text="仅点击“刷新”时更新",Location=new Point(22,102),Size=new Size(300,28),Checked=current==0};
        minutes=new NumericUpDown{Minimum=1,Maximum=1440,Value=Math.Max(1,Math.Min(1440,current>0?current:5)),Location=new Point(68,61),Width=94,Enabled=current>0};
        Controls.AddRange(new Control[]{automatic,manual,minutes,new Label{Text="每",Location=new Point(28,63),AutoSize=true},new Label{Text="分钟（1–1440）",Location=new Point(171,63),AutoSize=true},new Label{Text="手动模式下，启动和展开卡片均不请求额度。\n保存后立即生效，下次启动沿用。",Location=new Point(24,140),Size=new Size(304,45),Font=Style.Font(9),ForeColor=Style.Muted}});
        automatic.CheckedChanged+=(s,e)=>minutes.Enabled=automatic.Checked;
        var sizeTitle=new Label{Text="桌宠大小",Location=new Point(24,194),AutoSize=true};
        var sizeValue=new Label{Location=new Point(244,194),Size=new Size(80,23),TextAlign=ContentAlignment.MiddleRight};
        petSize=new TrackBar{Minimum=50,Maximum=200,SmallChange=5,LargeChange=25,TickFrequency=25,Value=Math.Max(50,Math.Min(200,currentScale)),Location=new Point(18,221),Size=new Size(312,45)};
        sizeValue.Text=petSize.Value+"%";petSize.ValueChanged+=(s,e)=>sizeValue.Text=petSize.Value+"%";
        var resetSize=new SoftButton{Text="恢复 100%",Location=new Point(219,268),Width=104,Height=28};resetSize.Click+=(s,e)=>petSize.Value=100;
        Controls.AddRange(new Control[]{sizeTitle,sizeValue,petSize,resetSize,new Label{Text="50%–200% · 保存后应用",Location=new Point(24,274),AutoSize=true,Font=Style.Font(8),ForeColor=Style.Muted}});
        roaming=new CheckBox{Text="随机散步",Checked=currentRoaming,Location=new Point(24,310),AutoSize=true};
        Controls.Add(roaming);Controls.Add(new Label{Text="拖动、查看额度和鼠标靠近时暂停。",Location=new Point(24,341),AutoSize=true,Font=Style.Font(8),ForeColor=Style.Muted});
        var cancel=new SoftButton{Text="取消",Location=new Point(144,373),DialogResult=DialogResult.Cancel};
        var save=new SoftButton{Text="保存",Location=new Point(239,373),Accent=true,DialogResult=DialogResult.OK};
        Controls.Add(cancel);Controls.Add(save);AcceptButton=save;CancelButton=cancel;
    }
}
