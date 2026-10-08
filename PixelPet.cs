using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class PixelPet : Form {
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    readonly AccountPipe pipe=new AccountPipe();readonly QuotaCard card;readonly System.Windows.Forms.Timer animation=new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer refreshTimer=new System.Windows.Forms.Timer(); string headAmount="—",headNote="等待更新";
    SpendBubble spendBubble;
    readonly NotifyIcon tray=new NotifyIcon();readonly EventWaitHandle wake; RegisteredWaitHandle wakeRegistration;readonly string settingsFile;
    readonly Bitmap[] frames=new Bitmap[8];int frame=-1,tick,badgeTop;bool dragging,pressed,quitting,fetching;Point down,origin;int logicalSize=280,refreshMinutes=5,petScalePercent=75;
    PetEdge edge=PetEdge.None;
    readonly Bitmap[] renderedFrames=new Bitmap[8];
    readonly PetRoaming roaming=new PetRoaming();
    readonly Bitmap[] walkingSources=new Bitmap[8];
    readonly Bitmap[,] walkFrames=new Bitmap[2,8];readonly Bitmap[] walkMasks=new Bitmap[2];
    bool roamingEnabled=true,settingsOpen;ToolStripMenuItem roamingItem;
    DateTime lastInteraction=DateTime.UtcNow,poseUntil=DateTime.MinValue;
    public PixelPet(EventWaitHandle wakeEvent){wake=wakeEvent;settingsFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LMServiceQuota","settings.json");
        Text="LMService 龙娘";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(196,324);BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=true;Cursor=Cursors.Hand;
        for(int i=0;i<frames.Length;i++)frames[i]=DragonArt.Frame((DragonPose)i);
        for(int i=0;i<walkingSources.Length;i++)walkingSources[i]=DragonArt.WalkingFrame(i);
        var area=Screen.PrimaryScreen.WorkingArea;Location=new Point(area.Right-150,area.Bottom-145);LoadPosition();
        card=new QuotaCard(pipe);card.OverPet=()=>ActiveBounds.Contains(Cursor.Position);
        card.QuotaChanged=(amount,note)=>{headAmount=amount;headNote=note;Invalidate();};
        card.SpendingDetected=(amount)=>{if(!Visible||IsDisposed)return;if(spendBubble!=null&&!spendBubble.IsDisposed)spendBubble.Close();spendBubble=new SpendBubble(this,amount);spendBubble.Show(this);};
        card.RefreshStarted=()=>{if(IsDisposed)return;fetching=true;ResolvePose();};
        card.RefreshFinished=(success,loggedIn)=>{if(IsDisposed)return;fetching=false;if(success&&loggedIn&&Visible&&!dragging)ShowTemporary(DragonPose.Treat,1800);else ResolvePose();};
        card.SettingsRequested=ShowRefreshSettings;card.SetRefreshMode(refreshMinutes);refreshTimer.Interval=Math.Max(1,refreshMinutes)*60000;refreshTimer.Tick+=async(s,e)=>{if(card.CanAutoRefresh)await card.RefreshData();};
        var menu=new ContextMenuStrip{Font=Style.Font(9)};
        menu.Items.Add("查看额度",null,async(s,e)=>{ShowPet();ShowTemporary(DragonPose.Click,800);card.ShowNear(ActiveBounds);await card.OnOpen();});
        menu.Items.Add("桌宠设置",null,(s,e)=>ShowRefreshSettings());
        roamingItem=new ToolStripMenuItem("随机散步"){Checked=roamingEnabled,CheckOnClick=true};roamingItem.CheckedChanged+=(s,e)=>{if(roamingEnabled==roamingItem.Checked)return;bool previous=roamingEnabled;roamingEnabled=roamingItem.Checked;PauseWalking(3);if(!SavePosition()){roamingEnabled=previous;roamingItem.Checked=previous;MessageBox.Show("散步设置保存失败，请检查本地目录权限。","桌宠设置");}};menu.Items.Add(roamingItem);
        var pinned=new ToolStripMenuItem("始终置顶"){Checked=TopMost,CheckOnClick=true};pinned.CheckedChanged+=(s,e)=>{TopMost=pinned.Checked;card.TopMost=TopMost;SavePosition();};menu.Items.Add(pinned);
        menu.Items.Add("隐藏到托盘",null,(s,e)=>{PauseWalking(3);card.Hide();Hide();animation.Stop();});menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出桌宠",null,(s,e)=>Quit());ContextMenuStrip=menu;
        tray.Text="LMService 龙娘 · 双击显示";using(var ico=Icon.ExtractAssociatedIcon(Application.ExecutablePath))tray.Icon=(Icon)ico.Clone();tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>ShowPet();tray.Visible=true;
        wakeRegistration=ThreadPool.RegisterWaitForSingleObject(wake,(s,t)=>{try{if(!IsDisposed)BeginInvoke((Action)ShowPet);}catch{}},null,-1,false);animation.Interval=50;animation.Tick+=(s,e)=>{tick++;UpdateWalking();ResolvePose();Invalidate();};
        Shown+=async(s,e)=>{FitSize();ClampPosition();DockNearEdge();SetFrame((int)DragonPose.Stand);ResolvePose();animation.Start();await card.RestoreState();if(refreshMinutes>0)await card.RefreshData();if(!IsDisposed)ApplyRefreshMode();};
        MouseDown+=(s,e)=>{if(e.Button!=MouseButtons.Left)return;pressed=true;dragging=false;down=Cursor.Position;origin=Location;Capture=true;ShowTemporary(DragonPose.Click,800);};
        MouseMove+=(s,e)=>{if(!pressed)return;var p=Cursor.Position;int dx=p.X-down.X,dy=p.Y-down.Y;if(Math.Abs(dx)>4||Math.Abs(dy)>4){if(!dragging){dragging=true;edge=PetEdge.None;poseUntil=DateTime.MinValue;SetPose(DragonPose.Drag);}}if(dragging){Location=new Point(origin.X+dx,origin.Y+dy);if(card.Visible)card.Hide();}};
        MouseUp+=async(s,e)=>{if(e.Button!=MouseButtons.Left||!pressed)return;pressed=false;Capture=false;lastInteraction=DateTime.UtcNow;if(dragging){dragging=false;poseUntil=DateTime.MinValue;ClampPosition();DockNearEdge();ResolvePose();SavePosition();return;}if(card.Visible)card.Hide();else{card.TopMost=TopMost;card.ShowNear(ActiveBounds);await card.OnOpen();}};
        MouseCaptureChanged+=(s,e)=>{if(pressed&&!Capture){pressed=false;dragging=false;poseUntil=DateTime.MinValue;ClampPosition();DockNearEdge();ResolvePose();SavePosition();}};
        FormClosing+=(s,e)=>{if(!quitting){quitting=true;SavePosition();}if(spendBubble!=null&&!spendBubble.IsDisposed)spendBubble.Close();refreshTimer.Stop();animation.Stop();tray.Visible=false;pipe.Dispose();card.Dispose();};
        FormClosed+=(s,e)=>{if(wakeRegistration!=null)wakeRegistration.Unregister(null);tray.Dispose();animation.Dispose();refreshTimer.Dispose();foreach(var f in frames)f.Dispose();foreach(var f in renderedFrames)if(f!=null)f.Dispose();foreach(var f in walkingSources)f.Dispose();foreach(var f in walkFrames)if(f!=null)f.Dispose();foreach(var f in walkMasks)if(f!=null)f.Dispose();};
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    void ApplyRefreshMode(){refreshTimer.Stop();card.SetRefreshMode(refreshMinutes);if(refreshMinutes>0){refreshTimer.Interval=refreshMinutes*60000;refreshTimer.Start();}}
    void ShowRefreshSettings(){settingsOpen=true;PauseWalking(3);try{card.WithSettings(()=>{
        using(var dialog=new RefreshSettings(refreshMinutes,petScalePercent,roamingEnabled)){
            if(dialog.ShowDialog(card.Visible?(IWin32Window)card:this)!=DialogResult.OK)return;
            int previous=refreshMinutes,previousScale=petScalePercent;var previousPosition=Location;
            bool previousRoaming=roamingEnabled;
            int center=Left+Width/2,bottom=Bottom;
            refreshMinutes=dialog.RefreshMinutes;petScalePercent=dialog.PetScalePercent;roamingEnabled=dialog.RoamingEnabled;
            if(spendBubble!=null&&!spendBubble.IsDisposed)spendBubble.Close();
            FitSize();Location=new Point(center-Width/2,bottom-Height);ClampPosition();
            if(!SavePosition()){
                refreshMinutes=previous;petScalePercent=previousScale;roamingEnabled=previousRoaming;FitSize();Location=previousPosition;ClampPosition();
                MessageBox.Show("设置保存失败，请检查本地目录权限。","桌宠设置");
            }
            roamingItem.Checked=roamingEnabled;ApplyRefreshMode();if(card.Visible)card.ShowNear(ActiveBounds);
        }
    });}finally{settingsOpen=false;PauseWalking(3);}}
    void Quit(){quitting=true;SavePosition();Close();}
    bool Peeking {get{return frame==(int)DragonPose.PeekTop||frame==(int)DragonPose.PeekRight;}}
    Rectangle ActiveBounds {get{return Peeking?Bounds:new Rectangle(Left,Top+badgeTop,Width,Height-badgeTop);}}
    Rectangle SpriteBounds {get{return new Rectangle(0,Peeking?0:ClientSize.Height-logicalSize,logicalSize,logicalSize);}}
    void SetPose(DragonPose pose){if(frame!=(int)pose)SetFrame((int)pose);}
    void ShowTemporary(DragonPose pose,int milliseconds){PauseWalking(3);lastInteraction=DateTime.UtcNow;poseUntil=lastInteraction.AddMilliseconds(milliseconds);SetPose(edge==PetEdge.Top?DragonPose.PeekTop:edge==PetEdge.Right?DragonPose.PeekRight:pose);}
    void ResolvePose(){if(frame<0)return;var now=DateTime.UtcNow;if(dragging){SetPose(DragonPose.Drag);return;}if(edge!=PetEdge.None){SetPose(edge==PetEdge.Top?DragonPose.PeekTop:DragonPose.PeekRight);return;}if(fetching){SetPose(DragonPose.Magic);return;}if(now<poseUntil)return;SetPose(!roaming.Walking&&now-lastInteraction>TimeSpan.FromSeconds(90)?DragonPose.Sleep:DragonPose.Stand);}
    void ShowPet(){PauseWalking(3);lastInteraction=DateTime.UtcNow;poseUntil=DateTime.MinValue;Show();ResolvePose();ClampPosition();animation.Start();Activate();}
    void PauseWalking(int seconds){bool moving=roaming.Walking;roaming.Pause(DateTime.UtcNow,seconds);if(moving&&frame>=0){ResolvePose();SetFrame(frame);}}
    void UpdateWalking(){
        var now=DateTime.UtcNow;var hover=ActiveBounds;hover.Inflate(24,24);
        bool paused=!roamingEnabled||!Visible||pressed||dragging||fetching||settingsOpen||card.Visible||card.Busy||ContextMenuStrip.Visible||now<poseUntil||hover.Contains(Cursor.Position)||(spendBubble!=null&&!spendBubble.IsDisposed&&spendBubble.Visible);
        bool before=roaming.Walking;bool facing=roaming.FacingLeft;
        Location=roaming.Tick(Location,Size,Screen.FromRectangle(Bounds).WorkingArea,now,paused);
        if(roaming.Walking&&!before){edge=PetEdge.None;poseUntil=DateTime.MinValue;ResolvePose();SetFrame(frame);}
        else if(!roaming.Walking&&before){if(!paused){DockNearEdge();if(edge!=PetEdge.None)roaming.Pause(now,10);SavePosition();}ResolvePose();SetFrame(frame);}
        else if(roaming.Walking&&facing!=roaming.FacingLeft)SetFrame(frame);
    }
    void FitSize(){
        PauseWalking(3);
        double dpi=96;try{uint value=GetDpiForWindow(Handle);if(value>0)dpi=value;}catch{}
        logicalSize=Math.Max(100,(int)Math.Round(dpi*280.0/96.0*petScalePercent/100.0));
        int badgeHeight=Math.Max(24,(int)Math.Round(logicalSize*44.0/280.0));
        ClientSize=new Size(logicalSize,logicalSize+badgeHeight);
        if(frame>=0)SetFrame(frame);
    }
    void ClampPosition(){Location=EdgeDock.Clamp(Bounds,Screen.FromRectangle(Bounds).WorkingArea,edge);}
    void DockNearEdge(){var area=Screen.FromRectangle(Bounds).WorkingArea;edge=EdgeDock.Detect(Bounds,area,Math.Max(14,(int)Math.Round(logicalSize*0.09)));ClampPosition();}
    void LoadPosition(){try{if(!File.Exists(settingsFile))return;var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(settingsFile));Location=new Point(Convert.ToInt32(d["x"]),Convert.ToInt32(d["y"]));TopMost=Style.Yes(d,"topmost");if(d.ContainsKey("roamingEnabled"))roamingEnabled=Style.Yes(d,"roamingEnabled");string savedEdge=Style.Text(d,"edge");edge=savedEdge=="Top"?PetEdge.Top:savedEdge=="Right"?PetEdge.Right:PetEdge.None;var size=Style.Number(d,"petScalePercent");if(size.HasValue)petScalePercent=(int)Math.Max(50,Math.Min(200,size.Value));var frequency=Style.Number(d,"refreshMinutes");if(frequency.HasValue)refreshMinutes=(int)Math.Max(0,Math.Min(1440,frequency.Value));ClampPosition();}catch{}}
    bool SavePosition(){try{Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));var text=new JavaScriptSerializer().Serialize(new {x=Left,y=Top,topmost=TopMost,refreshMinutes=refreshMinutes,petScalePercent=petScalePercent,roamingEnabled=roamingEnabled,edge=edge.ToString()});var tmp=settingsFile+".tmp";File.WriteAllText(tmp,text);if(File.Exists(settingsFile))File.Replace(tmp,settingsFile,null);else File.Move(tmp,settingsFile);return true;}catch{return false;}}
    Bitmap RenderedFrame(int value){
        var cached=renderedFrames[value];if(cached!=null&&cached.Width==logicalSize)return cached;if(cached!=null)cached.Dispose();
        var bitmap=new Bitmap(logicalSize,logicalSize,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.DrawImage(frames[value],new Rectangle(0,0,logicalSize,logicalSize));}
        // WinForms color-key windows require an opaque mask to avoid a magenta halo.
        for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var p=bitmap.GetPixel(x,y);bitmap.SetPixel(x,y,p.A<128?Color.Transparent:Color.FromArgb(255,p.R,p.G,p.B));}
        renderedFrames[value]=bitmap;return bitmap;
    }
    bool WalkingPose {get{return roaming.Walking&&frame==(int)DragonPose.Stand;}}
    void PrepareWalkingFrames(){
        int direction=roaming.FacingLeft?1:0;
        if(walkFrames[direction,0]!=null&&walkFrames[direction,0].Width==logicalSize)return;
        if(walkMasks[direction]!=null)walkMasks[direction].Dispose();
        for(int phase=0;phase<8;phase++){
            if(walkFrames[direction,phase]!=null)walkFrames[direction,phase].Dispose();
            var bitmap=new Bitmap(logicalSize,logicalSize,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(bitmap)){
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                if(direction==1){g.TranslateTransform(logicalSize,0);g.ScaleTransform(-1,1);}
                g.DrawImage(walkingSources[phase],new Rectangle(0,0,logicalSize,logicalSize));
            }
            for(int y=0;y<logicalSize;y++)for(int x=0;x<logicalSize;x++){var p=bitmap.GetPixel(x,y);bitmap.SetPixel(x,y,p.A<128?Color.Transparent:Color.FromArgb(255,p.R,p.G,p.B));}
            walkFrames[direction,phase]=bitmap;
        }
        // One stable hit region covers every gait phase, so the feet remain clickable.
        var mask=new Bitmap(logicalSize,logicalSize,PixelFormat.Format32bppArgb);
        for(int y=0;y<logicalSize;y++)for(int x=0;x<logicalSize;x++)for(int phase=0;phase<8;phase++)if(walkFrames[direction,phase].GetPixel(x,y).A>=128){mask.SetPixel(x,y,Color.White);break;}
        walkMasks[direction]=mask;
    }
    Bitmap SpriteForPaint(){if(!WalkingPose)return RenderedFrame(frame);PrepareWalkingFrames();return walkFrames[roaming.FacingLeft?1:0,(tick/2)%8];}
    Bitmap SpriteForRegion(){if(!WalkingPose)return RenderedFrame(frame);PrepareWalkingFrames();return walkMasks[roaming.FacingLeft?1:0];}
    void SetFrame(int value){
        frame=value;var b=SpriteForRegion();var region=new Region();region.MakeEmpty();float scale=1;
        float offsetX=SpriteBounds.X,offsetY=SpriteBounds.Y;
        int firstVisible=b.Height,lastVisible=0;
        for(int y=0;y<b.Height&&firstVisible==b.Height;y++)for(int x=0;x<b.Width;x++)if(b.GetPixel(x,y).A>=128){firstVisible=y;break;}
        if(firstVisible==b.Height)firstVisible=0;
        int badgeHeight=ClientSize.Height-logicalSize,badgeWidth=Math.Min(ClientSize.Width-2,Math.Max(120,(int)Math.Round(logicalSize*0.72)));
        if(Peeking){for(int y=b.Height-1;y>=0&&lastVisible==0;y--)for(int x=0;x<b.Width;x++)if(b.GetPixel(x,y).A>=128){lastVisible=y+1;break;}}
        badgeTop=Peeking?Math.Min(ClientSize.Height-badgeHeight,(int)Math.Ceiling(lastVisible*scale)+2):Math.Max(1,(int)Math.Round(offsetY+firstVisible*scale-badgeHeight+2));
        using(var badge=Style.Round(new Rectangle((ClientSize.Width-badgeWidth)/2,badgeTop,badgeWidth,badgeHeight-2),8))region.Union(badge);
        for(int y=0;y<b.Height;y++){int x=0;while(x<b.Width){while(x<b.Width&&b.GetPixel(x,y).A<128)x++;int start=x;while(x<b.Width&&b.GetPixel(x,y).A>=128)x++;if(x>start)region.Union(new RectangleF(offsetX+start*scale,offsetY+y*scale-2,(x-start)*scale,scale+2));}}
        var old=Region;Region=region;if(old!=null)old.Dispose();Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e){
        if(frame<0)return;int bob=!WalkingPose&&(frame==(int)DragonPose.Stand||frame==(int)DragonPose.Magic)&&((tick/2)%30>=15)?2:0;
        e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
        var sprite=SpriteBounds;sprite.Y-=bob;e.Graphics.DrawImageUnscaled(SpriteForPaint(),sprite.X,sprite.Y);
        float factor=logicalSize/280f;int badgeHeight=ClientSize.Height-logicalSize;
        int badgeWidth=Math.Min(ClientSize.Width-2,Math.Max(120,(int)Math.Round(logicalSize*0.72))),badgeX=(ClientSize.Width-badgeWidth)/2;
        e.Graphics.SmoothingMode=SmoothingMode.None;using(var badge=Style.Round(new Rectangle(badgeX,badgeTop,badgeWidth,badgeHeight-2),8))using(var fill=new SolidBrush(Style.Cream))e.Graphics.FillPath(fill,badge);
        using(var font=Style.Font(13*factor,true))TextRenderer.DrawText(e.Graphics,headAmount,font,new Rectangle(badgeX,badgeTop+1,badgeWidth,(int)(24*factor)),Style.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        using(var font=Style.Font(6.8f*factor))TextRenderer.DrawText(e.Graphics,headNote,font,new Rectangle(badgeX,badgeTop+(int)(24*factor),badgeWidth,(int)(17*factor)),Style.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
    }
    protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0x02E0){FitSize();ClampPosition();}}

    static void Preview(string directory){Directory.CreateDirectory(directory);foreach(DragonPose pose in Enum.GetValues(typeof(DragonPose))){using(var sprite=DragonArt.Frame(pose)){sprite.Save(Path.Combine(directory,"pose-"+pose.ToString().ToLowerInvariant()+".png"),ImageFormat.Png);if(pose==DragonPose.Stand){sprite.Save(Path.Combine(directory,"dragon-girl.png"),ImageFormat.Png);using(var image=new Bitmap(384,448)){using(var g=Graphics.FromImage(image)){g.Clear(Color.FromArgb(227,231,242));g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(sprite,new Rectangle(55,0,274,426));}image.Save(Path.Combine(directory,"dragon-preview.png"),ImageFormat.Png);}}}}
        var sample=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>("{\"logged_in\":true,\"personal\":{\"data\":{\"balance\":40000000,\"used\":1200000,\"unit\":500000,\"subscriptions\":[{\"id\":59,\"total\":100000000,\"used\":8100000,\"ends\":1791046800,\"status\":\"active\"}]},\"fetched_at\":1790589600},\"site\":{\"data\":{\"windows\":[{\"name\":\"主额度\",\"period\":\"1 周\",\"used_percent\":52,\"resets\":1791047818}],\"collected_at\":\"2026-09-28T10:00:00Z\"},\"fetched_at\":1790589600}}");
        using(var c=new QuotaCard(null,true)){c.Preview(sample);c.StartPosition=FormStartPosition.Manual;c.Location=new Point(-20000,-20000);c.Show();Application.DoEvents();c.PerformLayout();using(var b=new Bitmap(c.Width,c.Height)){c.DrawToBitmap(b,c.ClientRectangle);b.Save(Path.Combine(directory,"quota-card.png"));}c.PreviewAccount();c.PerformLayout();using(var b=new Bitmap(c.Width,c.Height)){c.DrawToBitmap(b,c.ClientRectangle);b.Save(Path.Combine(directory,"account-card.png"));}}
    }
    [STAThread] public static void Main(string[] args){try{try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{SetProcessDPIAware();}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--preview"){Preview(args[1]);return;}
        string sid=WindowsIdentity.GetCurrent().User.Value;bool fresh;
        using(var mutex=new Mutex(true,"Local\\LMServicePixelPet_"+sid,out fresh))using(var wakeEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\LMServicePixelPetWake_"+sid)){if(!fresh){wakeEvent.Set();return;}Application.Run(new PixelPet(wakeEvent));}
        }catch(Exception ex){MessageBox.Show("桌宠启动失败："+ex.Message,"LMService 龙娘",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
}
