using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Threading.Tasks;

class QuotaCard : Form {
    readonly AccountPipe pipe;
    readonly bool demo;
    FlowLayoutPanel list=new FlowLayoutPanel(); Panel scroll=new Panel();
    Label status=new Label(),heading=new Label(),subtitle=new Label();
    SoftButton settings=new SoftButton();
    public Action SettingsRequested;
    public int RefreshMinutes=5;
    string RefreshDescription {get{return RefreshMinutes==0?"仅手动刷新":"每 "+RefreshMinutes+" 分钟自动更新";}}
    public void SetRefreshMode(int minutes){RefreshMinutes=minutes;subtitle.Text="LMService  /  "+RefreshDescription;}
    public void WithSettings(Action show){if(busy||modal)return;modal=true;try{show();}finally{modal=false;}}
    SoftButton refresh=new SoftButton(),home=new SoftButton(),plansTab=new SoftButton(),accountTab=new SoftButton();
    Dictionary<string,object> snapshot; List<Dictionary<string,object>> plans=new List<Dictionary<string,object>>();
    int selectedPlan; ComboBox planChoice; string view="home",rememberedUsername="",draftUsername; bool busy,modal,verification,loggedIn,changingAccount; TextBox name,password,code;
    public Func<bool> OverPet;
    public Action<string,string> QuotaChanged;
    public Action<string> SpendingDetected;
    public Action RefreshStarted;
    public Action<bool,bool> RefreshFinished;
    void DetectSpending(object previous,object current){
        if(SpendingDetected==null||!Style.Yes(previous,"logged_in")||!Style.Yes(current,"logged_in"))return;
        var before=Style.Get(previous,"personal");var after=Style.Get(current,"personal");
        if(Style.Yes(before,"stale")||Style.Yes(after,"stale"))return;
        var bp=Style.Get(before,"data");var ap=Style.Get(after,"data");
        var bd=Style.Get(bp,"daily_usage");var ad=Style.Get(ap,"daily_usage");
        if(Style.Yes(bd,"stale")||Style.Yes(ad,"stale"))return;
        string today=DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
        if(Style.Text(bd,"date")!=today||Style.Text(ad,"date")!=today)return;
        var b=Style.Number(bd,"quota");var a=Style.Number(ad,"quota");var u=Style.Number(ap,"unit");
        if(!a.HasValue||!b.HasValue||!u.HasValue||u.Value<=0||Style.Number(bp,"unit")!=u||a.Value<=b.Value)return;
        double dollars=(a.Value-b.Value)/u.Value;
        if(Double.IsInfinity(dollars)||Double.IsNaN(dollars))return;
        SpendingDetected(dollars<0.01?"−<$0.01":"−$"+dollars.ToString("N2"));
    }

    readonly System.Windows.Forms.Timer countdownTimer=new System.Windows.Forms.Timer();
    Label resetCountdown,todayUsage;bool lastReadFailed;
    string TodayUsageText(){
        var pd=ActiveSubscriptionData();var daily=Style.Get(pd,"daily_usage");
        string today=DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
        if(Style.Text(daily,"date")!=today)return "今日已用  — · 待刷新（北京时间）";
        var quota=Style.Number(daily,"quota");
        if(!quota.HasValue)return "今日已用  — · 暂不可用";
        bool old=lastReadFailed||Style.Yes(daily,"stale")||Style.Yes(Style.Get(snapshot,"personal"),"stale");
        return "今日已用  "+Style.Money(quota,Style.Number(pd,"unit"))+(old?" · 旧数据 "+Style.Date(Style.Get(daily,"fetched_at")):" · 北京时间");
    }

    string ResetCountdown(){
        var pd=ActiveSubscriptionData();if(pd==null)return "";
        if(Style.Text(pd,"subscription_error")!="")return "";
        double now=(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;
        double? nearest=null;int active=0;bool unknown=false;
        foreach(var sub in Style.Items(Style.Get(pd,"subscriptions"))){
            string state=Style.Text(sub,"status");var end=Style.Number(sub,"ends");
            if((state!=""&&state!="active")||(end.HasValue&&end.Value>0&&end.Value<=now))continue;
            active++;var reset=Style.Number(sub,"next_reset");
            if(Style.Text(sub,"reset_period")=="none"){continue;}
            if(!reset.HasValue||reset.Value<=0){unknown=true;continue;}
            if(end.HasValue&&end.Value>0&&reset.Value>=end.Value){continue;}
            if(!nearest.HasValue||reset.Value<nearest.Value)nearest=reset;
        }
        if(active==0)return "";
        if(!nearest.HasValue)return "";
        double seconds=nearest.Value-now;
        if(seconds<=0)return "已到重置时间 · 待刷新确认";
        if(seconds>315360000)return "";
        var span=TimeSpan.FromSeconds(Math.Ceiling(seconds));
        string left=(span.Days>0?span.Days+"天 ":"")+span.Hours.ToString("00")+":"+span.Minutes.ToString("00")+":"+span.Seconds.ToString("00");
        return (active>1?"最近重置 ":"重置剩余 ")+left+(unknown?" *":"");
    }
    void UpdateCountdown(){if(todayUsage!=null&&!todayUsage.IsDisposed)todayUsage.Text=TodayUsageText();if(resetCountdown!=null&&!resetCountdown.IsDisposed){resetCountdown.Text=ResetCountdown();resetCountdown.Visible=resetCountdown.Text!="";}NotifyQuota();}

    public bool CanAutoRefresh {get{return !busy&&!modal&&view=="home";}}
    public void OpenAccount(){Switch("account");}
    object ActiveSubscriptionData(){return Style.Get(Style.Get(snapshot,"personal"),"data");}
    string SubscriptionAmount(){
        var pd=ActiveSubscriptionData();if(pd==null||Style.Text(pd,"subscription_error")!="")return "—";
        double total=0;bool any=false;double now=(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;
        foreach(var sub in Style.Items(Style.Get(pd,"subscriptions"))){
            string state=Style.Text(sub,"status");var end=Style.Number(sub,"ends");
            if((state!=""&&state!="active")||(end.HasValue&&end.Value>0&&end.Value<=now))continue;
            var amount=Style.Number(sub,"total");var used=Style.Number(sub,"used");
            if(!amount.HasValue||!used.HasValue)return "—";
            total+=Math.Max(0,amount.Value-used.Value);any=true;
        }
        return Style.Money(any?total:0,Style.Number(pd,"unit"));
    }
    void NotifyQuota(){if(QuotaChanged==null)return;var personal=Style.Get(snapshot,"personal");
        bool old=lastReadFailed||Style.Yes(personal,"stale")||Style.Text(ActiveSubscriptionData(),"subscription_error")!="";
        string note=ResetCountdown();if(note=="")note=!loggedIn?"请登录":"更新 "+Style.Date(Style.Get(personal,"fetched_at"));QuotaChanged(SubscriptionAmount(),(old?"旧数据 · ":"")+note);}

    public bool Busy {get{return busy;}}
    public QuotaCard(AccountPipe client,bool preview=false){
        pipe=client;demo=preview;Text="LMService · 猫咪额度卡片";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(360,548);BackColor=Style.Cream;ForeColor=Style.Ink;Font=Style.Font(9);Padding=new Padding(16);
        var header=new Panel{Dock=DockStyle.Top,Height=92};heading.Text="我的额度";heading.Font=Style.Font(15,true);heading.AutoSize=true;heading.Location=new Point(0,1);header.Controls.Add(heading);
        var close=new SoftButton{Text="×",Size=new Size(30,28),Anchor=AnchorStyles.Top|AnchorStyles.Right,Location=new Point(294,0)};close.Click+=(s,e)=>Hide();header.Controls.Add(close);header.Resize+=(s,e)=>close.Location=new Point(header.ClientSize.Width-30,0);
        subtitle.Text="LMService  /  "+RefreshDescription;subtitle.AutoSize=true;subtitle.ForeColor=Style.Muted;subtitle.Location=new Point(1,32);header.Controls.Add(subtitle);
        var tabs=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=36,WrapContents=false};home.Text="返回额度";plansTab.Text="套餐";accountTab.Text="账户";home.Width=72;plansTab.Width=72;accountTab.Width=72;refresh.Width=72;settings.Width=72;settings.Text="设置";settings.Click+=(s,e)=>{if(SettingsRequested!=null)SettingsRequested();};refresh.Text="刷新";refresh.Accent=true;tabs.Controls.AddRange(new Control[]{home,accountTab,settings,refresh});header.Controls.Add(tabs);
        var footer=new Panel{Dock=DockStyle.Bottom,Height=48};status.Dock=DockStyle.Fill;status.Font=Style.Font(8);status.ForeColor=Style.Muted;status.Text="点击刷新获取最新额度";footer.Controls.Add(status);
        scroll.Dock=DockStyle.Fill;scroll.AutoScroll=true;list.FlowDirection=FlowDirection.TopDown;list.WrapContents=false;list.AutoSize=true;list.AutoSizeMode=AutoSizeMode.GrowAndShrink;list.Dock=DockStyle.Top;scroll.Controls.Add(list);Controls.Add(scroll);Controls.Add(footer);Controls.Add(header);
        home.Click+=(s,e)=>Switch("home");accountTab.Click+=(s,e)=>Switch("account");plansTab.Click+=async(s,e)=>{Switch("plans");await LoadPlans();};refresh.Click+=async(s,e)=>{if(view=="plans")await LoadPlans();else await RefreshData();};
        Deactivate+=(s,e)=>{if(!IsDisposed)BeginInvoke((Action)(()=>{if(!modal&&view!="account"&&!ContainsFocus&&(OverPet==null||!OverPet()))Hide();}));};
        Resize+=(s,e)=>{using(var shape=Style.Round(ClientRectangle,18)){var old=Region;Region=new Region(shape);if(old!=null)old.Dispose();}};
        using(var shape=Style.Round(ClientRectangle,18))Region=new Region(shape);
        Build();countdownTimer.Interval=1000;countdownTimer.Tick+=(s,e)=>UpdateCountdown();countdownTimer.Start();
    }
    public void ShowNear(Rectangle pet){var area=Screen.FromRectangle(pet).WorkingArea;int x=Math.Max(area.Left,Math.Min(pet.Right-Width,area.Right-Width));int y=pet.Top-Height-8;if(y<area.Top)y=Math.Min(pet.Bottom+8,area.Bottom-Height);Location=new Point(x,Math.Max(area.Top,y));Show();area=Screen.FromRectangle(pet).WorkingArea;Location=new Point(Math.Max(area.Left,Math.Min(pet.Right-Width,area.Right-Width)),Math.Max(area.Top,Math.Min(pet.Top-Height-8>=area.Top?pet.Top-Height-8:pet.Bottom+8,area.Bottom-Height)));Activate();}
    public async Task OnOpen(){if(RefreshMinutes>0&&!busy)await RefreshData();}
    void Switch(string target){if(busy)return;view=target;Build();}
    Label Label(string text,int x,int y,int width,float size=9,bool bold=false,Color? color=null){return new Label{Text=text,Location=new Point(x,y),Size=new Size(width,(int)Math.Ceiling(size*2.4)),Font=Style.Font(size,bold),ForeColor=color??Style.Ink,AutoEllipsis=true};}
    Panel Section(string title,int height){var p=new Panel{Width=306,Height=height,BackColor=Color.White,Margin=new Padding(0,7,0,3)};p.Controls.Add(Label(title,14,12,278,10,true));p.Paint+=(s,e)=>{e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var pen=new Pen(Style.Line))using(var path=Style.Round(new Rectangle(0,0,p.Width-1,p.Height-1),12))e.Graphics.DrawPath(pen,path);};return p;}
    void AddNote(string text){var l=Label(text,0,0,302,9,false,Style.Muted);l.Height=36;l.Margin=new Padding(4,9,0,4);list.Controls.Add(l);}
    void Build(){
        resetCountdown=null;todayUsage=null;list.SuspendLayout();while(list.Controls.Count>0){var c=list.Controls[0];list.Controls.RemoveAt(0);c.Dispose();}
        home.Visible=view=="account";home.Accent=view=="home";plansTab.Accent=view=="plans";accountTab.Accent=view=="account";home.Invalidate();plansTab.Invalidate();accountTab.Invalidate();
        if(view=="account")BuildAccount();else BuildHome();
        float scale=ClientSize.Width/360f; if(Math.Abs(scale-1)>0.01f)foreach(Control child in list.Controls)child.Scale(new SizeF(scale,scale)); list.ResumeLayout();
    }
    void ErrorNote(object s){var error=Style.Text(s,"error");if(error!="")AddNote(error+(Style.Get(s,"data")!=null?" · 下方保留上次成功结果":""));}
    void BuildHome(){
        var personal=Style.Get(snapshot,"personal");var pd=Style.Get(personal,"data");
        var p=Section("个人订阅剩余额度",ResetCountdown()==""?128:153);var amountLabel=Label(SubscriptionAmount(),14,35,278,25,true);amountLabel.Height=40;p.Controls.Add(amountLabel);
        string personalNote=pd==null?"登录后显示订阅额度":Style.Text(pd,"subscription_error");
        if(personalNote=="")personalNote=Style.Yes(personal,"stale")?"更新失败 · 显示上次结果":"有效订阅剩余额度";
        todayUsage=Label(TodayUsageText(),15,78,276,8,true,Style.Ink);p.Controls.Add(todayUsage);p.Controls.Add(Label(personalNote,15,101,276,8,false,Style.Muted));resetCountdown=Label(ResetCountdown(),15,125,276,9,true,Style.Orange);resetCountdown.Visible=resetCountdown.Text!="";p.Controls.Add(resetCountdown);list.Controls.Add(p);
        var site=Style.Get(snapshot,"site");var sd=Style.Get(site,"data");bool any=false;
        foreach(var w in Style.Items(Style.Get(sd,"windows"))){any=true;var used=Style.Number(w,"used_percent");double? left=used.HasValue?Math.Max(0,100-used.Value):(double?)null;
            var q=Section("站点总额度 · "+Style.Text(w,"period"),121);
            q.Controls.Add(Label(left.HasValue?left.Value.ToString("0.0")+"%":"—",14,34,278,25,true));
            q.Controls.Add(new Meter{Location=new Point(15,81),Width=274,Value=left??0});
            q.Controls.Add(Label(Style.Yes(site,"stale")||Style.Yes(sd,"upstream_stale")?"旧数据 · 刷新暂不可用":"剩余比例 · 重置 "+Style.Date(Style.Get(w,"resets")),15,96,275,8,false,Style.Muted));list.Controls.Add(q);}
        if(!any){var q=Section("站点总额度",100);q.Controls.Add(Label("—",14,36,270,25,true));list.Controls.Add(q);}
        if(plans.Count>0)BuildPlans();
        var renew=new SoftButton{Text="再次订阅",Width=306,Height=38,Accent=true,Margin=new Padding(0,12,0,4)};
        renew.Click+=async(s,e)=>{if(busy||modal)return;if(!loggedIn){Switch("account");return;}if(plans.Count==0)await LoadPlans();else await Purchase(plans[Math.Min(selectedPlan,plans.Count-1)]);};list.Controls.Add(renew);
    }
    void BuildAccount(){
        name=password=code=null;
        if(loggedIn&&!changingAccount&&!verification){
            AddNote("已恢复登录状态，无需重新输入账号密码。\n刷新额度时会自动续期。");
            var current=Section("当前账户",144);current.Controls.Add(Label(rememberedUsername==""?"账户已登录":rememberedUsername,15,45,274,12,true));
            var change=new SoftButton{Text="更换账户",Location=new Point(15,91),Width=274};change.Click+=(s,e)=>{if(busy)return;changingAccount=true;draftUsername=rememberedUsername;Build();};current.Controls.Add(change);list.Controls.Add(current);AddLogoutButton();return;
        }
        AddNote(verification?"账号密码已通过，请完成验证码登录。":rememberedUsername!=""?"已记住上次账号，重新登录只需输入密码。":"账号密码只发往 LMService 网站。\nWindows 加密保存账号和会话，不保存密码。");
        var p=Section("个人账户",verification?315:266);name=new TextBox{Text=draftUsername??rememberedUsername,ReadOnly=verification,Location=new Point(15,66),Width=274,Font=Style.Font(10)};var usernameInput=name;name.TextChanged+=(s,e)=>draftUsername=usernameInput.Text;password=new TextBox{Location=new Point(15,130),Width=274,Enabled=!verification,UseSystemPasswordChar=true,Font=Style.Font(10)};
        p.Controls.Add(Label("账号",15,43,260,9));p.Controls.Add(name);p.Controls.Add(Label("密码",15,106,260,9));p.Controls.Add(password);
        int by=186;if(verification){p.Controls.Add(Label("验证器验证码",15,169,265,9));code=new TextBox{Location=new Point(15,192),Width=274,Font=Style.Font(10)};p.Controls.Add(code);by=237;}
        var login=new SoftButton{Text=verification?"验证并登录":"登录并记住状态",Location=new Point(15,by),Width=274,Accent=true};p.Controls.Add(login);
        login.Click+=async(s,e)=>{if(busy)return;draftUsername=name.Text;var a=AccountPipe.Command(verification?"verify":"login");if(verification)a["code"]=code.Text;else{a["username"]=name.Text;a["password"]=password.Text;}password.Clear();if(code!=null)code.Clear();login.Enabled=false;
            var result=await Send(a);a.Clear();if(IsDisposed)return;if(!login.IsDisposed)login.Enabled=true;if(result==null)return;if(Style.Yes(result,"needs_2fa")){verification=true;Build();}else if(Style.Yes(result,"logged_in")){loggedIn=true;verification=false;changingAccount=false;draftUsername=null;snapshot=null;view="home";await RefreshData();}};
        list.Controls.Add(p);
        if(loggedIn){var cancel=new SoftButton{Text="取消更换账户",Width=306,Margin=new Padding(0,8,0,0)};cancel.Click+=(s,e)=>{if(busy)return;changingAccount=false;verification=false;draftUsername=null;Build();};list.Controls.Add(cancel);}
        if(loggedIn||verification)AddLogoutButton();
        else if(rememberedUsername!=""){var forget=new SoftButton{Text="清除记住的账号",Width=306,Margin=new Padding(0,8,0,0)};forget.Click+=async(s,e)=>{if(busy)return;var r=await Send(AccountPipe.Command("forget_username"));if(r!=null){draftUsername=null;Build();}};list.Controls.Add(forget);}
    }
    void AddLogoutButton(){
        var logout=new SoftButton{Text="退出登录（保留账号）",Width=306,Margin=new Padding(0,8,0,0)};logout.Click+=async(s,e)=>{if(busy)return;var r=await Send(AccountPipe.Command("logout"));if(r!=null){loggedIn=false;verification=false;changingAccount=false;draftUsername=null;plans.Clear();if(snapshot!=null)snapshot["personal"]=null;NotifyQuota();Build();}};list.Controls.Add(logout);
    }
    void BuildPlans(){
        planChoice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=306,Font=Style.Font(9),Margin=new Padding(0,9,0,0)};
        foreach(var plan in plans)planChoice.Items.Add(Style.Text(plan,"title")+" · "+Style.Text(plan,"price"));
        if(plans.Count>0)planChoice.SelectedIndex=Math.Min(selectedPlan,plans.Count-1);
        planChoice.SelectedIndexChanged+=(s,e)=>selectedPlan=Math.Max(0,planChoice.SelectedIndex);
        list.Controls.Add(planChoice);
    }
    async Task Purchase(Dictionary<string,object> selected){
        if(busy||modal)return;modal=true;bool approved=false;
        try{using(var dialog=new PurchaseDialog(Style.Text(selected,"title"),Style.Text(selected,"price")))approved=dialog.ShowDialog(this)==DialogResult.OK;}finally{modal=false;}
        if(!approved)return;var args=AccountPipe.Command("buy");args["quote"]=Style.Text(selected,"quote");args["confirmed"]=true;
        var result=await Send(args);if(IsDisposed)return;plans.Clear();Build();
        if(result!=null){view="home";await RefreshData();status.Text="订阅请求成功，已重新读取额度。";}
    }
    async Task<Dictionary<string,object>> Send(Dictionary<string,object> args){if(busy||demo)return null;busy=true;home.Enabled=plansTab.Enabled=accountTab.Enabled=refresh.Enabled=settings.Enabled=false;status.Text="正在读取，请稍候…";
        try{var response=await pipe.Call(args);if(IsDisposed)return null;if(!Style.Yes(response,"ok")){if(response.ContainsKey("logged_in"))loggedIn=Style.Yes(response,"logged_in");status.Text=Style.Text(response,"error","操作失败，请稍后重试。");return null;}var data=Style.Obj(Style.Get(response,"data"));if(data.ContainsKey("username")&&!Style.Yes(data,"needs_2fa"))rememberedUsername=Style.Text(data,"username");status.Text=Style.Text(data,"message");return data;}
        catch(Exception e){if(!IsDisposed)status.Text=e.Message;return null;}
        finally{busy=false;if(!IsDisposed)home.Enabled=plansTab.Enabled=accountTab.Enabled=refresh.Enabled=settings.Enabled=true;}}
    public async Task RestoreState(){var d=await Send(AccountPipe.Command("state"));if(d!=null){loggedIn=Style.Yes(d,"logged_in");if(view=="account"&&!changingAccount&&!verification)Build();}}
    public async Task RefreshData(){if(busy||modal)return;bool succeeded=false;if(RefreshStarted!=null)RefreshStarted();try{var d=await Send(AccountPipe.Command("summary"));if(d==null){lastReadFailed=true;NotifyQuota();return;}succeeded=true;lastReadFailed=false;var previous=snapshot;snapshot=d;DetectSpending(previous,d);loggedIn=Style.Yes(d,"logged_in");NotifyQuota();if(view!="account"||!changingAccount&&!verification)Build();if(Style.Text(d,"message")=="")status.Text="更新于 "+DateTime.Now.ToString("HH:mm:ss")+" · "+RefreshDescription;}finally{if(RefreshFinished!=null)RefreshFinished(succeeded,loggedIn);}}
    async Task LoadPlans(){var d=await Send(AccountPipe.Command("plans"));if(d==null)return;plans.Clear();foreach(var p in Style.Items(Style.Get(d,"plans")))plans.Add(Style.Obj(p));Build();status.Text=plans.Count==0?"当前没有可订阅套餐。":"选择套餐，再点“再次订阅”确认价格。";}
    public void Preview(Dictionary<string,object> sample){snapshot=sample;loggedIn=true;Build();status.Text="演示数据 · 用于检查界面排版";}
    public void PreviewAccount(){view="account";Build();status.Text="演示界面 · 未连接网站";}
    protected override void Dispose(bool disposing){if(disposing){countdownTimer.Stop();countdownTimer.Dispose();}base.Dispose(disposing);}
    protected override void OnFormClosing(FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}base.OnFormClosing(e);}
}

class PurchaseDialog : Form {
    public PurchaseDialog(string title,string price){Text="确认订阅";ClientSize=new Size(340,238);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;BackColor=Style.Cream;Font=Style.Font(10);ForeColor=Style.Ink;
        Controls.Add(new Label{Text=title,Location=new Point(20,20),Size=new Size(300,45),Font=Style.Font(12,true)});Controls.Add(new Label{Text=price,Location=new Point(20,70),Size=new Size(300,37),Font=Style.Font(21,true)});
        Controls.Add(new Label{Text="将提交真实订阅请求。非零价格可能扣款。\n请确认套餐和价格。",Location=new Point(20,118),Size=new Size(300,49),ForeColor=Style.Muted});
        var cancel=new SoftButton{Text="取消",Location=new Point(120,186),DialogResult=DialogResult.Cancel};var ok=new SoftButton{Text="确认订阅",Location=new Point(215,186),Accent=true,DialogResult=DialogResult.OK};Controls.Add(cancel);Controls.Add(ok);CancelButton=cancel;AcceptButton=ok;}
}
