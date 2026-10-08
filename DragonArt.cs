using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

enum DragonPose { Stand, Magic, Click, Treat, Drag, Sleep, PeekTop, PeekRight }

static class DragonArt {
    public static Bitmap Frame(DragonPose pose) {
        string resource = "DragonGirl_" + pose.ToString().ToLowerInvariant();
        return LoadSprite(resource);
    }
    public static Bitmap WalkingFrame(int index) {
        if(index<0||index>=8)throw new ArgumentOutOfRangeException("index");
        return LoadSprite("DragonGirl_walk"+index.ToString("00"));
    }
    static Bitmap LoadSprite(string resource) {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)) {
            if (stream == null) throw new FileNotFoundException("找不到龙娘形象资源。");
            using (var source = new Bitmap(stream)) {
                var frame = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(frame)) {
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImageUnscaled(source, 0, 0);
                }
                return frame;
            }
        }
    }
}
