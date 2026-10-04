using System;
using System.Drawing;

enum PetEdge { None, Top, Right }

static class EdgeDock {
    public static PetEdge Detect(Rectangle pet, Rectangle area, int threshold) {
        int top=Math.Abs(pet.Top-area.Top),right=Math.Abs(area.Right-pet.Right);
        if(top<=threshold&&(right>threshold||top<=right))return PetEdge.Top;
        if(right<=threshold)return PetEdge.Right;
        return PetEdge.None;
    }
    public static Point Clamp(Rectangle pet, Rectangle area, PetEdge edge) {
        int x=Math.Max(area.Left,Math.Min(pet.Left,area.Right-pet.Width));
        int y=Math.Max(area.Top,Math.Min(pet.Top,area.Bottom-pet.Height));
        if(edge==PetEdge.Top)y=area.Top;
        if(edge==PetEdge.Right)x=Math.Max(area.Left,area.Right-pet.Width);
        return new Point(x,y);
    }
}
