using System;
using System.Drawing;

// Local movement only. The form decides when user interaction requires a pause.
sealed class PetRoaming {
    readonly Random random=new Random();
    DateTime nextWalk=DateTime.UtcNow.AddSeconds(5),walkUntil,lastTick;
    Point target;double x,y;
    public bool Walking {get;private set;}
    public bool FacingLeft {get;private set;}
    public void Pause(DateTime now,int seconds){Walking=false;nextWalk=now.AddSeconds(seconds);lastTick=now;}
    public Point Tick(Point position,Size size,Rectangle area,DateTime now,bool paused){
        if(paused){Pause(now,2);return position;}
        if(!Walking){
            if(now<nextWalk)return position;
            int margin=Math.Max(12,size.Width/14);
            int left=area.Left+(area.Width>size.Width+2*margin?margin:0);
            int top=area.Top+(area.Height>size.Height+2*margin?margin:0);
            int right=Math.Max(left,area.Right-size.Width-margin);
            int bottom=Math.Max(top,area.Bottom-size.Height-margin);
            target=new Point(random.Next(left,right+1),random.Next(top,bottom+1));
            x=position.X;y=position.Y;
            if(Math.Abs(target.X-x)+Math.Abs(target.Y-y)<12){Pause(now,4);return position;}
            FacingLeft=target.X<x;Walking=true;walkUntil=now.AddSeconds(random.Next(8,19));lastTick=now;
        }
        double elapsed=Math.Max(0,Math.Min(0.1,(now-lastTick).TotalSeconds));lastTick=now;
        double dx=target.X-x,dy=target.Y-y,distance=Math.Sqrt(dx*dx+dy*dy);
        double step=Math.Max(18,size.Width*0.20)*elapsed;
        if(distance<=step){x=target.X;y=target.Y;Pause(now,random.Next(3,9));}
        else if(now>=walkUntil){Pause(now,random.Next(3,9));}
        else{x+=dx/distance*step;y+=dy/distance*step;}
        return EdgeDock.Clamp(new Rectangle((int)Math.Round(x),(int)Math.Round(y),size.Width,size.Height),area,PetEdge.None);
    }
}
