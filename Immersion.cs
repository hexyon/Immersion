using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Windows.Shell;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Immersion")]
[assembly: System.Reflection.AssemblyProduct("Immersion")]
[assembly: System.Reflection.AssemblyDescription("Minimal photo viewer with Lava Flow and slideshow controls")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace Immersion {
public class Photo {
    public string Path;
    public string Name { get { return System.IO.Path.GetFileName(Path); } }
    DateTime? date;
    public DateTime Date { get { if(!date.HasValue)date=File.GetLastWriteTime(Path);return date.Value; } }
    public void RefreshDate() {date=null;}
}
// Photo-colored wax in a glass channel. Summed fields let blobs join and separate.
public class LavaStrip : FrameworkElement {
    readonly DispatcherTimer animation = new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(40)};
    readonly Stopwatch clock = Stopwatch.StartNew();
    Color[] palette = Enumerable.Repeat(Colors.Black,6).ToArray();
    Color[] previous = Enumerable.Repeat(Colors.Black,6).ToArray();
    double changed;
    WriteableBitmap frame;
    byte[] pixels;
    LavaFluid fluid;
    int photoSeed;
    double lastFrame,accumulator,fluidAspect;
    // A modest display speed-up keeps the soft thermal motion perceptible in the strip.
    const double FlowSpeed=1.65;
    double[] waxField,waxRed,waxGreen,waxBlue;
    readonly LinearGradientBrush glass = new LinearGradientBrush();
    public Color[] Palette {get {return (Color[])palette.Clone();}}
    int version=2;
    public int Version {
        get {return version;}
        set {
            if(value!=1&&value!=2)throw new ArgumentOutOfRangeException("value");
            if(version==value)return;
            version=value;Height=version==1?12:28;frame=null;accumulator=0;lastFrame=clock.Elapsed.TotalSeconds;
            InvalidateVisual();
        }
    }
    public LavaStrip() {
        Height=28;IsHitTestVisible=false;SnapsToDevicePixels=true;
        glass.StartPoint=new Point(0,0);glass.EndPoint=new Point(0,1);
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(34,255,255,255),0));
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(8,255,255,255),0.26));
        glass.GradientStops.Add(new GradientStop(Colors.Transparent,0.5));
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(38,0,0,0),1));glass.Freeze();
        animation.Tick+=(s,e)=>InvalidateVisual();Loaded+=(s,e)=>SyncAnimation();IsVisibleChanged+=(s,e)=>SyncAnimation();Unloaded+=(s,e)=>animation.Stop();
    }
    void SyncAnimation() {if(IsVisible&&IsLoaded)animation.Start();else animation.Stop();}
    public void SetPhoto(BitmapSource photo) {
        previous=BlendedPalette(clock.Elapsed.TotalSeconds);palette=Sample(photo);changed=clock.Elapsed.TotalSeconds;
        photoSeed=PhotoSeed(photo);fluid=null;accumulator=0;lastFrame=changed;
        ToolTip=string.Join(" · ",palette.Select(c=>"#"+c.R.ToString("X2")+c.G.ToString("X2")+c.B.ToString("X2")));InvalidateVisual();
    }
    public static int PhotoSeed(BitmapSource photo) {
        if(photo==null)return 17;
        double scale=32.0/Math.Max(photo.PixelWidth,photo.PixelHeight);
        var small=new TransformedBitmap(photo,new ScaleTransform(scale,scale));
        var bitmap=new FormatConvertedBitmap(small,PixelFormats.Bgra32,null,0);
        var data=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(data,bitmap.PixelWidth*4,0);
        unchecked {uint hash=2166136261;foreach(byte value in data){hash^=value;hash*=16777619;}hash^=(uint)photo.PixelWidth;hash*=16777619;hash^=(uint)photo.PixelHeight;return (int)hash;}
    }
    static Color[] Sample(BitmapSource photo) {
        if(photo==null)return Enumerable.Repeat(Colors.Black,6).ToArray();
        double ratio=Math.Min(1,64.0/Math.Max(photo.PixelWidth,photo.PixelHeight));
        var small=ratio<1?(BitmapSource)new TransformedBitmap(photo,new ScaleTransform(ratio,ratio)):photo;
        var bitmap=new FormatConvertedBitmap(small,PixelFormats.Bgra32,null,0);int stride=bitmap.PixelWidth*4;var data=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(data,stride,0);
        var colors=new List<Color>();for(int i=0;i<data.Length;i+=4)if(data[i+3]>32)colors.Add(Color.FromRgb(data[i+2],data[i+1],data[i]));
        if(colors.Count==0)return Enumerable.Repeat(Colors.Black,6).ToArray();
        var centers=new Color[6];centers[0]=colors[colors.Count/2];
        for(int c=1;c<6;c++)centers[c]=colors.OrderByDescending(p=>centers.Take(c).Min(q=>Distance(p,q))).First();
        for(int round=0;round<10;round++) {
            var sums=new double[6,4];foreach(Color color in colors) {int nearest=0;double distance=double.MaxValue;for(int c=0;c<6;c++){double d=Distance(color,centers[c]);if(d<distance){distance=d;nearest=c;}}sums[nearest,0]+=color.R;sums[nearest,1]+=color.G;sums[nearest,2]+=color.B;sums[nearest,3]++;}
            for(int c=0;c<6;c++)if(sums[c,3]>0)centers[c]=Color.FromRgb((byte)(sums[c,0]/sums[c,3]),(byte)(sums[c,1]/sums[c,3]),(byte)(sums[c,2]/sums[c,3]));
        }
        var sorted=centers.OrderBy(c=>c.R+c.G+c.B).ToArray();return new[]{sorted[0],sorted[3],sorted[1],sorted[4],sorted[2],sorted[5]};
    }
    static double Distance(Color a,Color b) {return (a.R-b.R)*(a.R-b.R)+(a.G-b.G)*(a.G-b.G)+(a.B-b.B)*(a.B-b.B);}
    Color[] BlendedPalette(double now) {double blend=Math.Max(0,Math.Min(1,(now-changed)/2.0));blend=blend*blend*(3-2*blend);return palette.Select((p,i)=>Color.FromRgb((byte)(previous[i].R+(p.R-previous[i].R)*blend),(byte)(previous[i].G+(p.G-previous[i].G)*blend),(byte)(previous[i].B+(p.B-previous[i].B)*blend))).ToArray();}
    static double Clamp(double value,double low,double high) {return Math.Max(low,Math.Min(high,value));}
    static Color LiquidTint(Color[] colors,double value) {
        value=Clamp(value,0,5);int first=Math.Min(4,(int)value);double blend=value-first;
        Color a=colors[first],b=colors[first+1];
        return Color.FromRgb((byte)(a.R+(b.R-a.R)*blend),(byte)(a.G+(b.G-a.G)*blend),(byte)(a.B+(b.B-a.B)*blend));
    }
    void RenderLegacy(DrawingContext dc) {
        if(ActualWidth<1)return;int width=Math.Max(1,Math.Min(640,(int)ActualWidth)),height=12;
        if(frame==null||frame.PixelWidth!=width){frame=new WriteableBitmap(width,height,96,96,PixelFormats.Bgra32,null);pixels=new byte[width*height*4];}
        double time=clock.Elapsed.TotalSeconds;var colors=BlendedPalette(time);var cx=new double[6];var cy=new double[6];var sigma=new double[6];
        for(int i=0;i<6;i++){cx[i]=(i+0.5)/6.0+0.15*Math.Sin(time*(0.07+i*0.006)+i*1.7);cy[i]=0.5+0.4*Math.Sin(time*0.14+i*2.1);sigma[i]=0.11+0.025*(1+Math.Sin(time*0.11+i));}
        for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
            double total=0,r=0,g=0,b=0;for(int i=0;i<6;i++){double dx=(x+0.5)/width-cx[i];dx-=Math.Round(dx);double dy=(y+0.5)/height-cy[i];double weight=Math.Exp(-2.4*(dx*dx/(2*sigma[i]*sigma[i])+dy*dy*0.7));total+=weight;r+=colors[i].R*weight;g+=colors[i].G*weight;b+=colors[i].B*weight;}
            double vertical=(y+0.5)/height-0.5;double glow=0.12*Math.Exp(-vertical*vertical*9);double light=0.87+glow+0.035*Math.Sin(time*0.15+x*0.008);int at=(y*width+x)*4;pixels[at]=(byte)Math.Min(255,b/total*light);pixels[at+1]=(byte)Math.Min(255,g/total*light);pixels[at+2]=(byte)Math.Min(255,r/total*light);pixels[at+3]=255;
        }
        frame.WritePixels(new Int32Rect(0,0,width,height),pixels,width*4,0);dc.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight),ActualHeight/2,ActualHeight/2));dc.DrawImage(frame,new Rect(0,0,ActualWidth,ActualHeight));dc.DrawRectangle(glass,null,new Rect(0,0,ActualWidth,ActualHeight));dc.Pop();
    }
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc);if(Version==1){RenderLegacy(dc);return;}if(ActualWidth<1||ActualHeight<1)return;
        int width=Math.Max(2,Math.Min(960,(int)Math.Ceiling(ActualWidth*1.5))),height=42;
        if(frame==null||frame.PixelWidth!=width) {
            frame=new WriteableBitmap(width,height,96,96,PixelFormats.Bgra32,null);pixels=new byte[width*height*4];
            waxField=new double[width*height];waxRed=new double[width*height];waxGreen=new double[width*height];waxBlue=new double[width*height];
        }
        double now=clock.Elapsed.TotalSeconds,aspect=ActualWidth/ActualHeight;
        if(fluid==null) {fluid=new LavaFluid(aspect,photoSeed);fluidAspect=aspect;lastFrame=now;accumulator=0;}
        else if(Math.Abs(aspect-fluidAspect)>fluidAspect*0.12) {
            // Resizing preserves the current fluid instead of restarting its composition.
            var resized=new LavaFluid(aspect,photoSeed);int count=Math.Min(resized.Particles.Length,fluid.Particles.Length);
            for(int i=0;i<count;i++) {
                var a=fluid.Particles[i];var b=resized.Particles[i];b.X=a.X/fluid.Width*resized.Width;b.Y=a.Y;b.VX=a.VX;b.VY=a.VY;b.Heat=a.Heat;b.Color=a.Color;
            }
            fluid=resized;fluidAspect=aspect;
        }
        // Fixed substeps and a bounded catch-up prevent jumps after hiding or pausing.
        accumulator=Math.Min(8.0/60,accumulator+Math.Min(0.08,Math.Max(0,now-lastFrame))*FlowSpeed);lastFrame=now;
        int steps=0;while(accumulator>=1.0/60&&steps++<8){fluid.Step(1.0/60);accumulator-=1.0/60;}
        var colors=BlendedPalette(now);
        Array.Clear(waxField,0,waxField.Length);Array.Clear(waxRed,0,waxRed.Length);Array.Clear(waxGreen,0,waxGreen.Length);Array.Clear(waxBlue,0,waxBlue.Length);
        // Compact poly6 splats reconstruct a continuous surface from conserved wax mass.
        const double support=0.38;double pixelX=fluid.Width/width,pixelY=1.0/height;
        foreach(var p in fluid.Particles) {
            Color color=LiquidTint(colors,p.Color);
            int x0=Math.Max(0,(int)((p.X-support)/pixelX)),x1=Math.Min(width-1,(int)((p.X+support)/pixelX));
            int y0=Math.Max(0,(int)((p.Y-support)/pixelY)),y1=Math.Min(height-1,(int)((p.Y+support)/pixelY));
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++) {
                double rx=(x+0.5)*pixelX-p.X,ry=(y+0.5)*pixelY-p.Y;
                double q=1-(rx*rx+ry*ry)/(support*support);if(q<=0)continue;
                double weight=q*q*q*0.5625;int at=y*width+x;waxField[at]+=weight;
                waxRed[at]+=color.R*weight;waxGreen[at]+=color.G*weight;waxBlue[at]+=color.B*weight;
            }
        }
        for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
            int at=y*width+x,index=at*4;double density=waxField[at];
            Color liquid=LiquidTint(colors,fluid.LiquidColor((x+0.5)*pixelX,(y+0.5)*pixelY));
            double depth=1-(x+0.5)/width,backlight=0.43+0.24*depth;
            double edge=Clamp((density-1.15)/0.65,0,1);edge=edge*edge*(3-2*edge);
            // Wide gradient stencil (3 px each way): a 1 px stencil picks up the lumpy per-particle density and
            // draws a faint ring inside big blobs; averaging over a wider baseline keeps only the blob's shape.
            int xl=Math.Max(0,x-3),xr=Math.Min(width-1,x+3),yu=Math.Max(0,y-3),yd=Math.Min(height-1,y+3);
            double gx=(waxField[y*width+xr]-waxField[y*width+xl])/((xr-xl)*pixelX);
            double gy=(waxField[yd*width+x]-waxField[yu*width+x])/((yd-yu)*pixelY);
            double length=Math.Sqrt(gx*gx+gy*gy+90),lighting=0.88+0.18*(0.3*gx-gy)/length;
            double rim=Math.Exp(-Math.Abs(density-1.5)*2.2),glow=0.07*Math.Min(1,density);
            double waxLight=16+12*depth+rim*4,divisor=Math.Max(0.00001,density);
            double red=waxRed[at]/divisor,green=waxGreen[at]/divisor,blue=waxBlue[at]/divisor;
            pixels[index]=(byte)Clamp((8+liquid.B*(backlight+glow))*(1-edge)+(blue*lighting+waxLight)*edge,0,255);
            pixels[index+1]=(byte)Clamp((8+liquid.G*(backlight+glow))*(1-edge)+(green*lighting+waxLight)*edge,0,255);
            pixels[index+2]=(byte)Clamp((8+liquid.R*(backlight+glow))*(1-edge)+(red*lighting+waxLight)*edge,0,255);pixels[index+3]=255;
        }
        var bounds=new Rect(0,0,ActualWidth,ActualHeight);
        frame.WritePixels(new Int32Rect(0,0,width,height),pixels,width*4,0);
        dc.PushClip(new RectangleGeometry(bounds,ActualHeight/2,ActualHeight/2));
        dc.DrawImage(frame,bounds);dc.DrawRectangle(glass,null,bounds);dc.Pop();
        dc.DrawRoundedRectangle(null,new Pen(new SolidColorBrush(Color.FromArgb(28,255,255,255)),1),new Rect(0.5,0.5,Math.Max(0,ActualWidth-1),Math.Max(0,ActualHeight-1)),ActualHeight/2,ActualHeight/2);
    }
}
public class App : Application {
    [STAThread] public static int Main(string[] args) {
        try {
            if (args.Contains("--register")) { Registration.Register(); return 0; }
            if (args.Contains("--unregister")) { Registration.Unregister(); return 0; }
            App app = new App();
            app.DispatcherUnhandledException += (s,e) => { MessageBox.Show(e.Exception.Message, "Immersion"); e.Handled=true; };
            bool fastOpen=args.Any(a=>File.Exists(a)&&Registration.Extensions.Contains(System.IO.Path.GetExtension(a).ToLowerInvariant()));
            Viewer window = new Viewer(args.Contains("--self-test"),fastOpen);
            if (args.Contains("--self-test")) return window.SelfTest();
            window.LoadArguments(args);
            app.Run(window); return 0;
        } catch(Exception ex) { File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),ex.ToString()); return 1; }
    }
}
public static class Registration {
    public static readonly string[] Extensions = {".jpg",".jpeg",".jfif",".png",".bmp",".gif",".tif",".tiff",".ico",".wdp"};
    public static void Register() {
        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        string iconReference="\""+System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"icons","Immersion.ico")+"\",0";
        string command = "\""+exe+"\" \"%1\"";
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Immersion.Image")) {
            key.SetValue("", "Immersion image");
            using(var icon=key.CreateSubKey("DefaultIcon")) icon.SetValue("",iconReference);
            using(var open=key.CreateSubKey(@"shell\open\command")) open.SetValue("",command);
        }
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Immersion\Capabilities")) {
            key.SetValue("ApplicationName","Immersion");
            key.SetValue("ApplicationDescription","Photo library, viewer and Slide Pro slideshow controls");
            key.SetValue("ApplicationIcon",iconReference);
            using(var types=key.CreateSubKey("FileAssociations")) foreach(string ext in Extensions) types.SetValue(ext,"Immersion.Image");
        }
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications")) key.SetValue("Immersion",@"Software\Immersion\Capabilities");
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\Immersion.exe")) {
            key.SetValue("FriendlyAppName","Immersion");
            using(var icon=key.CreateSubKey("DefaultIcon")) icon.SetValue("",iconReference);
            using(var open=key.CreateSubKey(@"shell\open\command")) open.SetValue("",command);
            using(var types=key.CreateSubKey("SupportedTypes")) foreach(string ext in Extensions) types.SetValue(ext,"");
        }
        foreach(string ext in Extensions) using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Classes\"+ext+@"\OpenWithProgids")) key.SetValue("Immersion.Image",new byte[0],RegistryValueKind.None);
        // Keep existing user-selected handlers working after the rename.
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\PhotosPlus.Image",true))if(key!=null){key.SetValue("","Immersion image");using(var icon=key.CreateSubKey("DefaultIcon"))icon.SetValue("",iconReference);using(var open=key.CreateSubKey(@"shell\open\command"))open.SetValue("",command);}
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications\PhotosPlus.exe",true))if(key!=null){key.SetValue("FriendlyAppName","Immersion");using(var icon=key.CreateSubKey("DefaultIcon"))icon.SetValue("",iconReference);using(var open=key.CreateSubKey(@"shell\open\command"))open.SetValue("",command);}
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications",true))if(key!=null)key.DeleteValue("Photos Plus",false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\PhotosPlus\Capabilities",false);
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
    [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern void SHChangeNotify(uint a,uint b,IntPtr c,IntPtr d);
    public static void Unregister() {
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications",true)) if(key!=null) key.DeleteValue("Immersion",false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Immersion\Capabilities",false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Immersion.Image",false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\Immersion.exe",false);
        foreach(string ext in Extensions) using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Classes\"+ext+@"\OpenWithProgids",true)) if(key!=null) key.DeleteValue("Immersion.Image",false);
        SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
    }
}
static class DesktopMenuBounds {
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct NativeRect {public int Left,Top,Right,Bottom;}
    delegate bool WindowVisitor(IntPtr window,IntPtr parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visitor,IntPtr parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
    public static double SafeTop(IntPtr ownWindow,System.Windows.Forms.Screen display) {
        var monitor=display.Bounds;double top=display.WorkingArea.Top;
        // Desktop menu replacements sometimes draw a topmost strip without registering
        // an appbar. Respect its actual occupied pixels, never a hard-coded top gap.
        EnumWindows(delegate(IntPtr window,IntPtr parameter) {
            if(window==ownWindow||!IsWindowVisible(window)||(GetWindowLong(window,-20)&8)==0)return true;
            NativeRect rect;if(!GetWindowRect(window,out rect))return true;
            int overlap=Math.Min(rect.Right,monitor.Right)-Math.Max(rect.Left,monitor.Left);
            if(rect.Top<=monitor.Top+1&&rect.Bottom>monitor.Top&&rect.Bottom<=monitor.Top+96&&
                rect.Bottom-rect.Top>=8&&rect.Bottom-rect.Top<=96&&overlap>=monitor.Width*0.9)
                top=Math.Max(top,rect.Bottom);
            return true;
        },IntPtr.Zero);
        return top;
    }
}
public class Viewer : Window {
    readonly List<Photo> photos = new List<Photo>();
    readonly List<Photo> visible = new List<Photo>();
    readonly HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly List<string> folders = new List<string>();
    readonly Random rng = new Random();
    readonly DispatcherTimer timer = new DispatcherTimer();
    readonly SemaphoreSlim thumbnailSlots=new SemaphoreSlim(3);
    readonly Dictionary<string,BitmapSource> thumbnailCache=new Dictionary<string,BitmapSource>();
    int libraryGeneration, thumbnailRequests;
    bool stateLoaded;
    readonly string stateDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Immersion");
    Grid root, library, viewer;
    WrapPanel tiles;
    StackPanel sidebar;
    WrapPanel slideControls;
    Border slidePanel;
    Border infoPanel;
    Border morePanel;
    StackPanel infoFields;
    ToggleButton infoButton;
    ToggleButton slideButton;
    FrameworkElement libraryHeader;
    Grid viewerToolbar;
    TextBlock zoomLabel;
    int checks;
    TextBox search;
    TextBlock count, title, details, position, message;
    Image image;
    ScrollViewer imageScroll;
    LavaStrip lava;
    ToggleButton shuffleButton, loopButton, lavaButton, lavaV1Button;
    Button startButton, stopButton;
    Button previousArrow, nextArrow;
    ComboBox delay, sort;
    Slider zoom;
    BitmapSource original;
    Photo current;
    List<Photo> sequence = new List<Photo>();
    int index, rotation;
    bool favoriteOnly, playing, fullscreen, testing;
    WindowState previousState;
    WindowStyle previousStyle;
    Rect previousBounds;
    ResizeMode previousResizeMode;
    WindowChrome previousChrome;
    bool previousTopmost;
    Thickness previousRootMargin;
    bool insetUpdateQueued;
    bool panning,panMoved,panZoomed;
    readonly DispatcherTimer sideClickTimer = new DispatcherTimer();
    int pendingSide;
    Point panStart;
    double panHorizontal, panVertical;
    readonly Brush bg = Brush("#000000"), panel = Brush("#0A0A0A"), muted = Brush("#808080"), accent = Brush("#EDEDED");
    public static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
    public Viewer(bool test,bool fastOpen=false) {
        testing=test;
        if(test)stateDir=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","state");
        Title="Immersion"; Width=1250; Height=820; MinWidth=760; MinHeight=480;
        string iconPath=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"icons","Immersion.ico");if(File.Exists(iconPath))using(var iconStream=File.OpenRead(iconPath)){var decoder=BitmapDecoder.Create(iconStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);Icon=decoder.Frames.OrderByDescending(f=>f.PixelWidth).First();Icon.Freeze();}
        WindowStyle=WindowStyle.None;
        WindowChrome.SetWindowChrome(this,new WindowChrome {CaptionHeight=0,ResizeBorderThickness=new Thickness(5),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false,CornerRadius=new CornerRadius(8)});
        Background=bg; Foreground=Brush("#EDEDED"); FontFamily=new FontFamily("Segoe UI"); FontSize=12;
        UseLayoutRounding=true; SnapsToDevicePixels=true;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var style=new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#12FFFFFF"))); style.Setters.Add(new Setter(Control.ForegroundProperty,accent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,Brush("#1AFFFFFF"))); style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,5,10,5)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(2))); style.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty,ButtonTemplate(false)));
        Resources.Add(typeof(Button),style);
        var toggle=new Style(typeof(ToggleButton));
        toggle.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#12FFFFFF"))); toggle.Setters.Add(new Setter(Control.ForegroundProperty,accent));
        toggle.Setters.Add(new Setter(Control.BorderBrushProperty,Brush("#1AFFFFFF"))); toggle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,5,10,5)));
        toggle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(2)));
        toggle.Setters.Add(new Setter(Control.TemplateProperty,ButtonTemplate(true)));
        var trigger=new Trigger {Property=ToggleButton.IsCheckedProperty,Value=true};
        trigger.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#2EFFFFFF"))); toggle.Triggers.Add(trigger);
        Resources.Add(typeof(ToggleButton),toggle);
        var tooltipStyle=new Style(typeof(ToolTip));tooltipStyle.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'><Border Background='#1A1A1D' BorderBrush='#3B3B40' BorderThickness='1' Padding='8,5'><ContentPresenter TextElement.Foreground='#EDEDED'/></Border></ControlTemplate>")));Resources.Add(typeof(ToolTip),tooltipStyle);
        var comboStyle=new Style(typeof(ComboBox));comboStyle.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#EDEDED")));comboStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#111111")));
        comboStyle.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'><Grid><ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border CornerRadius='5' Background='#111111' Padding='7,4'><TextBlock Text='⌄' Foreground='#808080' HorizontalAlignment='Right'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' Margin='9,4,20,4' VerticalAlignment='Center'/><Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'><Border Background='#0A0A0A' BorderBrush='#2E2E2E' BorderThickness='1' CornerRadius='5' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' Padding='4'><ScrollViewer MaxHeight='260'><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate>")));
        Resources.Add(typeof(ComboBox),comboStyle);
        var sliderStyle=new Style(typeof(Slider));sliderStyle.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'><Grid Height='16' Background='Transparent'><Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate><Border Height='2' Background='#EDEDED' CornerRadius='1'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='12' Height='12'><Thumb.Template><ControlTemplate><Ellipse Width='10' Height='10' Fill='#EDEDED'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate><Border Height='2' Background='#2E2E2E' CornerRadius='1'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>")));Resources.Add(typeof(Slider),sliderStyle);
        BuildUI();
        sideClickTimer.Interval=TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime);
        sideClickTimer.Tick+=(s,e)=>{int direction=pendingSide;CancelSideNavigation();if(direction!=0&&viewer.IsVisible)Advance(direction,false);};
        timer.Tick+=(s,e)=>Advance(1,true);
        AllowDrop=true; DragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop)) ImportPaths((string[])e.Data.GetData(DataFormats.FileDrop));};
        PreviewKeyDown+=Keys;
        Loaded+=(s,e)=>{if(current!=null&&!fullscreen)ToggleFullscreen();QueueToolbarInset();};
        Activated+=(s,e)=>QueueToolbarInset();
        StateChanged+=(s,e)=>QueueToolbarInset();LocationChanged+=(s,e)=>QueueToolbarInset();SizeChanged+=(s,e)=>QueueToolbarInset();
        Closing+=(s,e)=>{Stop();CancelSideNavigation();if(!testing) SaveState();};
        if(testing)RefreshLibrary();
        else if(fastOpen)ReadState(false);
        else {RefreshLibrary();Loaded+=(s,e)=>Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{ReadState(true);RefreshLibrary();}));}
    }
    Button Btn(string label, Action action) { var b=new Button {Content=label};b.Click+=(s,e)=>action();return b; }
    void ToggleLava(int version,bool enabled) {
        lavaV1Button.IsChecked=enabled&&version==1;
        lavaButton.IsChecked=enabled&&version==2;
        if(enabled)lava.Version=version;
        lava.Visibility=enabled?Visibility.Visible:Visibility.Hidden;
    }
    ToggleButton Toggle(string label,Action action) {var b=new ToggleButton {Content=label};b.Click+=(s,e)=>action();return b;}
    TextBlock Txt(string value,double size,Brush color) {return new TextBlock {Text=value,FontSize=size,Foreground=color,Margin=new Thickness(4),VerticalAlignment=VerticalAlignment.Center};}
    ControlTemplate ButtonTemplate(bool toggle) {
        string checkedTrigger=toggle?"<Trigger Property='IsChecked' Value='True'><Setter TargetName='surface' Property='Background' Value='#2EFFFFFF'/><Setter TargetName='surface' Property='BorderBrush' Value='#66FFFFFF'/></Trigger>":"";
        return (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='"+(toggle?"ToggleButton":"Button")+"'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers>"+checkedTrigger+"<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#1FFFFFFF'/><Setter TargetName='surface' Property='BorderBrush' Value='#2EFFFFFF'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='#2EFFFFFF'/></Trigger><Trigger Property='Tag' Value='primary'><Setter TargetName='surface' Property='Background' Value='#EDEDED'/><Setter TargetName='surface' Property='BorderBrush' Value='#66FFFFFF'/><Setter Property='Foreground' Value='#000000'/></Trigger><MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsMouseOver' Value='True'/></MultiTrigger.Conditions><Setter TargetName='surface' Property='Background' Value='#FFFFFF'/></MultiTrigger><MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='primary'/><Condition Property='IsPressed' Value='True'/></MultiTrigger.Conditions><Setter TargetName='surface' Property='Background' Value='#DDDDDD'/></MultiTrigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='0.3'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
    }
    FrameworkElement MakeIcon(string name) {
        string data;
        switch(name) {
            case "back":data="M10,3 L5,8 10,13";break;
            case "next":data="M6,3 L11,8 6,13";break;
            case "heart":data="M8,13 C6,11 1,8 1,5 C1,1 6,1 8,5 C10,1 15,1 15,5 C15,8 10,11 8,13 Z";break;
            case "rotate":data="M3,6 A5.5,5.5 0 1 1 3,11 M3,2 L3,6 7,6";break;
            case "save":data="M2,2 L12,2 14,4 14,14 2,14 Z M5,2 L5,6 11,6 11,2 M5,14 L5,10 11,10 11,14";break;
            case "info":data="M8,1 A7,7 0 1 1 7.99,1 M8,7 L8,12 M8,4 L8,4.5";break;
            case "folder":data="M1,4 L6,4 8,6 15,6 15,13 1,13 Z";break;
            case "slide":data="M1,2 L15,2 15,12 1,12 Z M6,5 L10,7 6,9 Z M8,12 L8,15 M5,15 L11,15";break;
            case "play":data="M4,2 L14,8 4,14 Z";break;
            case "full":data="M1,6 L1,1 6,1 M10,1 L15,1 15,6 M15,10 L15,15 10,15 M6,15 L1,15 1,10";break;
            case "minus":data="M3,8 L13,8";break;
            case "plus":data="M3,8 L13,8 M8,3 L8,13";break;
            case "maximize":data="M3,3 L13,3 13,13 3,13 Z";break;
            case "close":data="M4,4 L12,12 M12,4 L4,12";break;
            case "more":data="M2,8 L3,8 M7.5,8 L8.5,8 M13,8 L14,8";break;
            default:data="M2,2 L14,2 14,14 2,14 Z";break;
        }
        var icon=new System.Windows.Shapes.Path {Data=Geometry.Parse(data),StrokeThickness=name=="play"?0:1.25,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=name=="play"?11:14,Height=14,Stretch=Stretch.Uniform};
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new Binding("Foreground") {RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ButtonBase),1)});
        if(name=="play")icon.SetBinding(System.Windows.Shapes.Shape.FillProperty,new Binding("Foreground") {RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ButtonBase),1)});return icon;
    }
    Button IconBtn(string icon,string name,Action action) {var b=Btn("",action);b.Content=MakeIcon(icon);b.ToolTip=name;b.Width=26;b.Height=24;b.Padding=new Thickness(4);AutomationProperties.SetName(b,name);return b;}
    ToggleButton IconToggle(string icon,string name,Action action) {var b=Toggle("",action);b.Content=MakeIcon(icon);b.ToolTip=name;b.Width=26;b.Height=24;b.Padding=new Thickness(4);AutomationProperties.SetName(b,name);return b;}
    Border Surface(UIElement content,Thickness margin) {return new Border {Child=content,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(3),Margin=margin,VerticalAlignment=VerticalAlignment.Center};}
    void QueueToolbarInset() {
        if(insetUpdateQueued||!IsLoaded)return;insetUpdateQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{insetUpdateQueued=false;RefreshToolbarInset();}));
    }
    void RefreshToolbarInset() {
        if(!IsLoaded||WindowState==WindowState.Minimized)return;
        // Fullscreen uses the complete monitor, including space reserved by desktop
        // toolbars. An inset here creates a visible empty band above our own toolbar.
        if(fullscreen) {root.Margin=new Thickness(0);return;}
        var source=PresentationSource.FromVisual(this);if(source==null)return;
        double scaleX=source.CompositionTarget.TransformToDevice.M11,scaleY=source.CompositionTarget.TransformToDevice.M22;
        var display=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        Point origin=root.PointToScreen(new Point(0,0));
        Point corner=root.PointToScreen(new Point(root.ActualWidth,root.ActualHeight));
        double clientLeft=origin.X-root.Margin.Left*scaleX,clientTop=origin.Y-root.Margin.Top*scaleY;
        double clientRight=corner.X+root.Margin.Right*scaleX,clientBottom=corner.Y+root.Margin.Bottom*scaleY;
        double safeTop=DesktopMenuBounds.SafeTop(new WindowInteropHelper(this).Handle,display);
        // Borderless maximized windows can extend their resize frame beyond all four
        // work-area edges. Keep the entire client layout, including the bottom bar,
        // inside the visible area rather than compensating for the top alone.
        bool maximized=WindowState==WindowState.Maximized;
        var inset=new Thickness(
            maximized?Math.Max(0,(display.WorkingArea.Left-clientLeft)/scaleX):0,
            Math.Max(0,(safeTop-clientTop)/scaleY),
            maximized?Math.Max(0,(clientRight-display.WorkingArea.Right)/scaleX):0,
            maximized?Math.Max(0,(clientBottom-display.WorkingArea.Bottom)/scaleY):0);
        if(Math.Abs(root.Margin.Left-inset.Left)>0.5||Math.Abs(root.Margin.Top-inset.Top)>0.5||
            Math.Abs(root.Margin.Right-inset.Right)>0.5||Math.Abs(root.Margin.Bottom-inset.Bottom)>0.5)root.Margin=inset;
    }
    void StyleBarButton(ButtonBase button,bool close=false,double width=46,double iconSize=12) {
        button.Width=width;button.MinWidth=46;button.Height=double.NaN;button.Margin=new Thickness(0);
        button.Padding=new Thickness(12,0,12,0);button.VerticalAlignment=VerticalAlignment.Stretch;
        button.Background=Brushes.Transparent;button.BorderThickness=new Thickness(0);
        bool toggle=button is ToggleButton;
        string checkedTrigger=toggle?"<Trigger Property='IsChecked' Value='True'><Setter TargetName='surface' Property='Background' Value='#353535'/></Trigger>":"";
        button.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='"+(toggle?"ToggleButton":"Button")+"'><Border x:Name='surface' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers>"+checkedTrigger+"<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='Background' Value='#353535'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='"+(close?"#C42B1C":"#303030")+"'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='"+(close?"#A32317":"#404040")+"'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        var icon=button.Content as System.Windows.Shapes.Path;
        if(icon!=null){icon.Width=iconSize;icon.Height=iconSize;}
    }
    StackPanel WindowButtons() {
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0),VerticalAlignment=VerticalAlignment.Stretch};
        buttons.Children.Add(IconBtn("minus","Minimize",()=>WindowState=WindowState.Minimized));
        buttons.Children.Add(IconBtn("maximize","Maximize / restore",()=>{if(fullscreen)ToggleFullscreen();else WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}));
        buttons.Children.Add(IconBtn("close","Close",Close));
        foreach(Button button in buttons.Children) {
            bool close=AutomationProperties.GetName(button)=="Close";
            StyleBarButton(button,close,46,10);
            WindowChrome.SetIsHitTestVisibleInChrome(button,true);
        }
        return buttons;
    }
    void GlassArrow(Button button) {
        button.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderThickness='0' CornerRadius='8'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
        button.Foreground=Brushes.White;button.Effect=null;button.FocusVisualStyle=null;
        var chevron=(System.Windows.Shapes.Path)button.Content;chevron.Width=8;chevron.Height=10;chevron.StrokeThickness=1.1;
    }
    void UpdateGlassArrows() {
        Color tint=lava.Palette[3];
        var fill=new SolidColorBrush(Color.FromRgb((byte)(16+tint.R*0.07),(byte)(16+tint.G*0.07),(byte)(16+tint.B*0.07)));fill.Freeze();
        foreach(Button arrow in new[]{previousArrow,nextArrow}){arrow.Background=fill;arrow.BorderBrush=Brushes.Transparent;arrow.BorderThickness=new Thickness(0);arrow.Foreground=Brushes.White;}
    }
    void RevealArrows(double x,double width,bool inside) {
        int side=inside?ImageSide(x,width):0;
        bool left=side<0,right=side>0;
        previousArrow.Opacity=left?1:0;previousArrow.IsHitTestVisible=left;nextArrow.Opacity=right?1:0;nextArrow.IsHitTestVisible=right;
    }
    static int ImageSide(double x,double width) {
        if(width<=0||x<0||x>width)return 0;
        double zone=Math.Min(width*0.22,Math.Max(90,Math.Min(180,width*0.12)));
        return x<=zone?-1:(x>=width-zone?1:0);
    }
    void CancelSideNavigation() {sideClickTimer.Stop();pendingSide=0;}
    void EndImageDrag() {
        panning=false;
        if(imageScroll.IsMouseCaptured)imageScroll.ReleaseMouseCapture();
        imageScroll.Cursor=Cursors.Arrow;
    }
    void ResetZoom() {CancelSideNavigation();EndImageDrag();zoom.Value=100;UpdateZoom();}
    bool BeginImageGesture(Point point,int clickCount) {
        if(original==null)return false;
        CancelSideNavigation();
        if(clickCount==2&&zoom.Value>100){ResetZoom();return true;}
        if(clickCount>1)return true;
        panning=false;panMoved=false;panZoomed=zoom.Value>100;panStart=point;
        panHorizontal=imageScroll.HorizontalOffset;panVertical=imageScroll.VerticalOffset;
        // Capture can synchronously raise a mouse-move event. Start the gesture only
        // after capture, so that event cannot be mistaken for the user's drag.
        panning=imageScroll.CaptureMouse();return panning;
    }
    void MoveImageGesture(Point point) {
        if(!panning)return;
        if(Math.Abs(point.X-panStart.X)>=SystemParameters.MinimumHorizontalDragDistance||
            Math.Abs(point.Y-panStart.Y)>=SystemParameters.MinimumVerticalDragDistance)panMoved=true;
        if(!panMoved||!panZoomed)return;
        imageScroll.Cursor=Cursors.Hand;
        imageScroll.ScrollToHorizontalOffset(panHorizontal+panStart.X-point.X);
        imageScroll.ScrollToVerticalOffset(panVertical+panStart.Y-point.Y);
    }
    bool EndImageGesture(Point point) {
        if(!panning)return false;
        MoveImageGesture(point);
        bool dragged=panMoved,wasZoomed=panZoomed;EndImageDrag();
        if(dragged||point.Y<0||point.Y>imageScroll.ActualHeight)return true;
        int direction=ImageSide(point.X,imageScroll.ActualWidth);
        if(direction==0)return true;
        // At enlarged zoom, wait for the system double-click window so a double-click
        // resets the current photo before a single side click can navigate away.
        if(wasZoomed){pendingSide=direction;sideClickTimer.Start();}
        else Advance(direction,false);
        return true;
    }
    void DragTitle(object sender,MouseButtonEventArgs e) {if(fullscreen)return;if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else if(e.LeftButton==MouseButtonState.Pressed)DragMove();}
    void ToggleSlidePanel() {slidePanel.Visibility=slidePanel.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;slideButton.IsChecked=slidePanel.Visibility==Visibility.Visible;}
    void CloseSlidePanel() {slidePanel.Visibility=Visibility.Collapsed;slideButton.IsChecked=false;}
    void CloseInfo() {infoPanel.Visibility=Visibility.Collapsed;infoButton.IsChecked=false;}
    void CloseMore() {morePanel.Visibility=Visibility.Collapsed;}
    void ViewerChrome(bool viewing) {libraryHeader.Visibility=viewing?Visibility.Collapsed:Visibility.Visible;message.Visibility=viewing?Visibility.Collapsed:Visibility.Visible;if(!viewing)root.Margin=new Thickness(0);if(viewerToolbar!=null){viewerToolbar.Visibility=Visibility.Visible;viewerToolbar.Opacity=1;Panel.SetZIndex(viewerToolbar,20);}}
    void BuildUI() {
        root=new Grid {Background=bg};Content=root;
        root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var header=new DockPanel {Background=Brush("#171717"),LastChildFill=true,Height=46};libraryHeader=header;
        var branding=Txt("Immersion",14,Brushes.White);branding.FontWeight=FontWeights.SemiBold;branding.Margin=new Thickness(18,0,24,0);branding.MouseLeftButtonDown+=DragTitle;DockPanel.SetDock(branding,Dock.Left);header.Children.Add(branding);
        var windowButtons=WindowButtons();DockPanel.SetDock(windowButtons,Dock.Right);header.Children.Add(windowButtons);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0),VerticalAlignment=VerticalAlignment.Stretch};DockPanel.SetDock(actions,Dock.Right);
        actions.Children.Add(Btn("Open photos",OpenPhotos));actions.Children.Add(Btn("Add folder",AddFolder));actions.Children.Add(Btn("Set as default",SetDefault));header.Children.Add(actions);
        foreach(ButtonBase action in actions.Children)StyleBarButton(action,false,double.NaN);
        search=new TextBox {MaxWidth=350,Margin=new Thickness(10,8,20,8),Padding=new Thickness(10,3,10,3),Background=bg,Foreground=Brushes.White,BorderBrush=Brush("#292929"),ToolTip="Search filenames",VerticalContentAlignment=VerticalAlignment.Center};
        search.TextChanged+=(s,e)=>RefreshLibrary();header.Children.Add(search);root.Children.Add(header);
        library=new Grid();library.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(168)});library.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(library,1);root.Children.Add(library);
        sidebar=new StackPanel {Background=panel,Margin=new Thickness(0,1,0,0)};
        var section=Txt("LIBRARY",10,muted);section.Margin=new Thickness(14,22,10,10);sidebar.Children.Add(section);sidebar.Children.Add(Btn("All photos",()=>{favoriteOnly=false;RefreshLibrary();}));sidebar.Children.Add(Btn("Favorites",()=>{favoriteOnly=true;RefreshLibrary();}));
        sidebar.Children.Add(Btn("Slideshow",()=>{if(visible.Count>0){OpenPhoto(visible[0]);ToggleSlidePanel();}}));sidebar.Children.Add(Btn("Refresh folders",RefreshFolders));
        sidebar.Children.Add(Btn("Manage folders",ManageFolders));
        var tip=Txt("Drop photos or folders\nto add to your library.\n\nDouble-click to view.",11,muted);tip.Margin=new Thickness(14,28,10,12);sidebar.Children.Add(tip);library.Children.Add(sidebar);
        var area=new DockPanel {Margin=new Thickness(24,20,24,16)};Grid.SetColumn(area,1);library.Children.Add(area);
        var libraryTop=new DockPanel();DockPanel.SetDock(libraryTop,Dock.Top);area.Children.Add(libraryTop);
        sort=new ComboBox {Width=126,Margin=new Thickness(5),Padding=new Thickness(6),FontSize=11};
        sort.Items.Add("Newest first");sort.Items.Add("Oldest first");sort.Items.Add("Name A–Z");sort.Items.Add("Name Z–A");sort.SelectedIndex=0;sort.SelectionChanged+=(s,e)=>RefreshLibrary();DockPanel.SetDock(sort,Dock.Right);libraryTop.Children.Add(sort);
        count=Txt("All photos",22,Brushes.White);count.FontWeight=FontWeights.SemiBold;libraryTop.Children.Add(count);
        var galleryScroll=new ScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        tiles=new WrapPanel {Margin=new Thickness(0,16,0,0)};galleryScroll.Content=tiles;area.Children.Add(galleryScroll);
        viewer=new Grid {Visibility=Visibility.Collapsed,Background=bg};Grid.SetRow(viewer,1);root.Children.Add(viewer);
        viewer.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});viewer.RowDefinitions.Add(new RowDefinition());viewer.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var toolbar=new Grid {Background=Brush("#171717"),Height=48};viewerToolbar=toolbar;
        toolbar.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(160)});toolbar.ColumnDefinitions.Add(new ColumnDefinition());toolbar.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(160)});
        var tools=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Stretch};
        tools.Children.Add(IconBtn("save","Save a copy",SaveCopy));infoButton=IconToggle("info","Photo information",ShowInfo);tools.Children.Add(infoButton);
        var more=IconBtn("more","More options",()=>{if(morePanel.Visibility==Visibility.Visible)CloseMore();else {CloseInfo();CloseSlidePanel();morePanel.Visibility=Visibility.Visible;}});
        tools.Children.Add(more);foreach(ButtonBase tool in tools.Children)StyleBarButton(tool);
        var toolsShell=Surface(tools,new Thickness(0));toolsShell.Padding=new Thickness(0);toolsShell.VerticalAlignment=VerticalAlignment.Stretch;toolsShell.HorizontalAlignment=HorizontalAlignment.Left;toolbar.Children.Add(toolsShell);
        title=Txt("",11,accent);title.TextAlignment=TextAlignment.Center;title.TextTrimming=TextTrimming.CharacterEllipsis;title.Margin=new Thickness(12,0,12,0);title.MouseLeftButtonDown+=DragTitle;var titleShell=Surface(title,new Thickness(14,6,14,6));titleShell.MaxWidth=450;Grid.SetColumn(titleShell,1);toolbar.Children.Add(titleShell);
        var captionButtons=WindowButtons();captionButtons.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(captionButtons,2);toolbar.Children.Add(captionButtons);viewer.Children.Add(toolbar);
        var stage=new Grid {ClipToBounds=true,Background=bg};Grid.SetRow(stage,1);viewer.Children.Add(stage);
        var menuItems=new StackPanel();morePanel=new Border {Child=menuItems,Width=210,Background=Brush("#1A1A1D"),BorderBrush=Brush("#35353A"),BorderThickness=new Thickness(1),Padding=new Thickness(5),Margin=new Thickness(8,5,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Visibility=Visibility.Collapsed};
        Action<string,Action> menuItem=(label,action)=>{var item=Btn("",()=>{CloseMore();action();});item.Content=new TextBlock {Text=label,Width=170,TextAlignment=TextAlignment.Left};item.Padding=new Thickness(8,7,8,7);item.Background=Brushes.Transparent;menuItems.Children.Add(item);};
        menuItem("Back to library",Back);menuItem("Open photos…",OpenPhotos);menuItem("Add folder…",AddFolder);menuItem("Add / remove favorite",ToggleFavorite);menuItem("Rotate clockwise",()=>{rotation=(rotation+90)%360;UpdateImage();});menuItem("Show in folder",()=>{if(current!=null)Process.Start("explorer.exe","/select,\""+current.Path+"\"");});menuItem("Set as default",SetDefault);
        stage.Children.Add(morePanel);Panel.SetZIndex(morePanel,20);
        image=new Image {Stretch=Stretch.Uniform,Margin=new Thickness(0)};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
        imageScroll=new ScrollViewer {Content=image,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};imageScroll.SizeChanged+=(s,e)=>UpdateZoom();stage.Children.Add(imageScroll);
        imageScroll.PreviewMouseWheel+=(s,e)=>{if(original!=null)ZoomAt(e.GetPosition(imageScroll),zoom.Value+(e.Delta>0?25:-25));e.Handled=true;};
        imageScroll.PreviewMouseLeftButtonDown+=(s,e)=>{if(BeginImageGesture(e.GetPosition(imageScroll),e.ClickCount))e.Handled=true;};
        imageScroll.PreviewMouseMove+=(s,e)=>{if(panning){MoveImageGesture(e.GetPosition(imageScroll));e.Handled=true;}};
        imageScroll.PreviewMouseLeftButtonUp+=(s,e)=>{if(EndImageGesture(e.GetPosition(imageScroll)))e.Handled=true;};
        imageScroll.LostMouseCapture+=(s,e)=>{panning=false;imageScroll.Cursor=Cursors.Arrow;};
        var prev=IconBtn("back","Previous photo",()=>Advance(-1,false));previousArrow=prev;prev.HorizontalAlignment=HorizontalAlignment.Left;prev.VerticalAlignment=VerticalAlignment.Center;prev.Margin=new Thickness(8);prev.Width=32;prev.Height=32;GlassArrow(prev);stage.Children.Add(prev);
        var next=IconBtn("next","Next photo",()=>Advance(1,false));nextArrow=next;next.HorizontalAlignment=HorizontalAlignment.Right;next.VerticalAlignment=VerticalAlignment.Center;next.Margin=new Thickness(8);next.Width=32;next.Height=32;GlassArrow(next);stage.Children.Add(next);
        RevealArrows(0,1,false);
        stage.PreviewMouseMove+=(s,e)=>{Point point=e.GetPosition(stage);RevealArrows(point.X,stage.ActualWidth,point.Y>=0&&point.Y<=stage.ActualHeight);};
        stage.MouseEnter+=(s,e)=>RevealArrows(e.GetPosition(stage).X,stage.ActualWidth,true);
        stage.MouseLeave+=(s,e)=>RevealArrows(0,stage.ActualWidth,false);
        var bottom=new DockPanel {Background=Brush("#080808"),Height=40,Margin=new Thickness(0)};Grid.SetRow(bottom,2);viewer.Children.Add(bottom);
        var bottomLeft=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0),VerticalAlignment=VerticalAlignment.Stretch};DockPanel.SetDock(bottomLeft,Dock.Left);
        slideButton=IconToggle("play","Open slideshow controls",ToggleSlidePanel);StyleBarButton(slideButton,false,46,10);bottomLeft.Children.Add(slideButton);position=Txt("",10,muted);position.Margin=new Thickness(10,0,14,0);bottomLeft.Children.Add(position);bottom.Children.Add(bottomLeft);
        var zoomPanel=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0),VerticalAlignment=VerticalAlignment.Stretch};DockPanel.SetDock(zoomPanel,Dock.Right);
        var fit=Btn("Fit",ResetZoom);fit.FontSize=10;fit.Padding=new Thickness(8,3,8,3);fit.ToolTip="Fit image to window";zoomPanel.Children.Add(fit);
        zoomLabel=Txt("100%",10,muted);zoomLabel.Width=38;zoomLabel.TextAlignment=TextAlignment.Center;zoomPanel.Children.Add(zoomLabel);
        zoomPanel.Children.Add(IconBtn("minus","Zoom out",()=>zoom.Value=Math.Max(100,zoom.Value-25)));zoom=new Slider {Minimum=100,Maximum=400,Value=100,Width=105,VerticalAlignment=VerticalAlignment.Center,ToolTip="Zoom",Foreground=accent};zoom.ValueChanged+=(s,e)=>UpdateZoom();zoomPanel.Children.Add(zoom);zoomPanel.Children.Add(IconBtn("plus","Zoom in",()=>zoom.Value=Math.Min(400,zoom.Value+25)));zoomPanel.Children.Add(IconBtn("full","Fullscreen (F11)",ToggleFullscreen));foreach(var control in zoomPanel.Children.OfType<ButtonBase>())StyleBarButton(control);DockPanel.SetDock(zoomPanel,Dock.Right);bottom.Children.Add(zoomPanel);
        details=Txt("",10,muted);bottomLeft.Children.Add(details);lava=new LavaStrip {Margin=new Thickness(16,0,16,0),VerticalAlignment=VerticalAlignment.Center};bottom.Children.Add(lava);
        slidePanel=new Border {Background=Brush("#F51A1A1D"),BorderBrush=Brush("#35353A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(0),Padding=new Thickness(12),Width=286,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(10,0,14,12),Visibility=Visibility.Collapsed,Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=24,ShadowDepth=4,Opacity=0.55}};
        var slideArea=new StackPanel();slidePanel.Child=slideArea;stage.Children.Add(slidePanel);
        var slideHeading=new DockPanel {Margin=new Thickness(2,0,0,10)};var closeSlide=IconBtn("close","Close slideshow controls",CloseSlidePanel);DockPanel.SetDock(closeSlide,Dock.Right);slideHeading.Children.Add(closeSlide);var heading=Txt("Slideshow",13,Brushes.White);heading.FontWeight=FontWeights.SemiBold;slideHeading.Children.Add(heading);slideArea.Children.Add(slideHeading);
        slideControls=new WrapPanel();
        startButton=Btn("▶ Start Slide",Start);stopButton=Btn("■ Stop Slide",Stop);startButton.Width=124;stopButton.Width=124;startButton.Tag="primary";slideControls.Children.Add(startButton);slideControls.Children.Add(stopButton);
        shuffleButton=Toggle("Shuffle",()=>{BuildSequence();if(playing) RestartTimer();});loopButton=Toggle("Loop",()=>{});
        shuffleButton.Width=124;loopButton.Width=124;slideControls.Children.Add(shuffleButton);slideControls.Children.Add(loopButton);
        lavaV1Button=Toggle("Lava Flow v1",()=>ToggleLava(1,lavaV1Button.IsChecked==true));lavaV1Button.Width=124;lavaV1Button.ToolTip="Toggle the original flowing photo colors";slideControls.Children.Add(lavaV1Button);
        lavaButton=Toggle("Lava Flow v2",()=>ToggleLava(2,lavaButton.IsChecked==true));lavaButton.IsChecked=true;lavaButton.Width=124;lavaButton.ToolTip="Toggle the photo-colored lava blobs";slideControls.Children.Add(lavaButton);
        var fullSlide=Btn("Full Screen",ToggleFullscreen);fullSlide.Width=124;slideControls.Children.Add(fullSlide);slideArea.Children.Add(slideControls);
        var timing=new DockPanel {Margin=new Thickness(4,12,4,3)};delay=new ComboBox {Width=80,Padding=new Thickness(5),VerticalAlignment=VerticalAlignment.Center,FontSize=11};for(int i=1;i<=30;i++)delay.Items.Add(i+" s");delay.SelectedIndex=2;delay.SelectionChanged+=(s,e)=>{if(playing)RestartTimer();};DockPanel.SetDock(delay,Dock.Right);timing.Children.Add(delay);timing.Children.Add(Txt("Time per photo",11,muted));slideArea.Children.Add(timing);
        var shortcuts=Txt("Space to play / pause · ← → to navigate",10,muted);shortcuts.Margin=new Thickness(5,10,0,2);slideArea.Children.Add(shortcuts);
        infoPanel=new Border {Background=Brush("#F51A1A1D"),BorderBrush=Brush("#35353A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(0),Padding=new Thickness(14),Width=300,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(12),Visibility=Visibility.Collapsed,Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=24,ShadowDepth=4,Opacity=0.55}};
        var infoLayout=new DockPanel();infoPanel.Child=infoLayout;
        var infoHeading=new DockPanel {Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(infoHeading,Dock.Top);infoLayout.Children.Add(infoHeading);var closeInfo=IconBtn("close","Close photo information",CloseInfo);DockPanel.SetDock(closeInfo,Dock.Right);infoHeading.Children.Add(closeInfo);infoHeading.Children.Add(Txt("Photo information",13,accent));
        infoFields=new StackPanel();var infoScroll=new ScrollViewer {Content=infoFields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var infoScrollbarStyle=new Style(typeof(ScrollBar));infoScrollbarStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty,8.0));infoScrollbarStyle.Setters.Add(new Setter(Control.TemplateProperty,(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'><Grid Background='#0A0A0A'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='Vertical' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}' Value='{TemplateBinding Value}'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb MinHeight='20'><Thumb.Template><ControlTemplate><Border Background='#414141' CornerRadius='3' Margin='2,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>")));infoScroll.Resources.Add(typeof(ScrollBar),infoScrollbarStyle);
        infoLayout.Children.Add(infoScroll);stage.Children.Add(infoPanel);Panel.SetZIndex(infoPanel,10);
        stage.SizeChanged+=(s,e)=>infoPanel.MaxHeight=Math.Max(1,stage.ActualHeight-24);
        message=Txt("Ready  •  Local photos stay on your computer",10,muted);message.Margin=new Thickness(16,4,16,4);Grid.SetRow(message,2);root.Children.Add(message);
    }
    void Status(string value) {message.Text=value;}
    void OpenPhotos() { var dialog=new OpenFileDialog {Multiselect=true,Filter="Images|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.wdp|All files|*.*"};if(dialog.ShowDialog()==true)ImportPaths(dialog.FileNames); }
    void AddFolder() {using(var dialog=new System.Windows.Forms.FolderBrowserDialog {Description="Choose a photo folder (includes subfolders)"}) if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK) ImportPaths(new[]{dialog.SelectedPath});}
    IEnumerable<string> Scan(string folder) {
        var pending=new Stack<string>();pending.Push(folder);
        while(pending.Count>0) {string next=pending.Pop();string[] files=null,dirs=null;try{files=Directory.GetFiles(next);dirs=Directory.GetDirectories(next);}catch(IOException){}catch(UnauthorizedAccessException){}
            if(files!=null)foreach(string f in files)if(IsImage(f))yield return f;
            if(dirs!=null)foreach(string d in dirs) {try{if((File.GetAttributes(d)&FileAttributes.ReparsePoint)==0)pending.Push(d);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
    }
    static bool IsImage(string path) {return Registration.Extensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());}
    public void ImportPaths(IEnumerable<string> paths) {
        Stop();int before=photos.Count;var known=new HashSet<string>(photos.Select(p=>p.Path),StringComparer.OrdinalIgnoreCase);
        foreach(string raw in paths) {string path=System.IO.Path.GetFullPath(raw);
            if(Directory.Exists(path)) {if(!folders.Contains(path,StringComparer.OrdinalIgnoreCase))folders.Add(path);foreach(string f in Scan(path))if(known.Add(f))photos.Add(new Photo {Path=f});}
            else if(File.Exists(path)&&IsImage(path)&&known.Add(path)) photos.Add(new Photo {Path=path});
        }
        RefreshLibrary();if(current!=null)BuildSequence();Status((photos.Count-before)+" photos added • "+photos.Count+" in library");if(!testing)SaveState();
    }
    void RefreshFolders() {photos.RemoveAll(p=>!File.Exists(p.Path));foreach(Photo photo in photos)photo.RefreshDate();ImportPaths(folders.ToArray());}
    void ManageFolders() {
        var dialog=new Window {Title="Library folders",Owner=this,Width=520,Height=360,Background=panel,Foreground=Brushes.White,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new DockPanel {Margin=new Thickness(15)};dialog.Content=layout;
        var list=new ListBox {ItemsSource=folders.ToArray(),Background=bg,Foreground=Brushes.White};
        var buttons=new StackPanel {Orientation=Orientation.Horizontal};DockPanel.SetDock(buttons,Dock.Bottom);layout.Children.Add(buttons);
        buttons.Children.Add(Btn("Remove from library",()=>{string selected=list.SelectedItem as string;if(selected==null)return;folders.Remove(selected);photos.RemoveAll(p=>p.Path.StartsWith(selected.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase));RefreshLibrary();SaveState();list.ItemsSource=folders.ToArray();}));buttons.Children.Add(Btn("Close",()=>dialog.Close()));layout.Children.Add(list);dialog.ShowDialog();
    }
    void RefreshLibrary() {
        if(tiles==null)return;int generation=++libraryGeneration; visible.Clear();IEnumerable<Photo> query=photos.Where(p=>File.Exists(p.Path)&&(!favoriteOnly||favorites.Contains(p.Path))&&(search==null||p.Name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0));
        int order=sort==null?0:sort.SelectedIndex;
        query=order==0?query.OrderByDescending(p=>p.Date):order==1?query.OrderBy(p=>p.Date):order==2?query.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase):query.OrderByDescending(p=>p.Name,StringComparer.CurrentCultureIgnoreCase);
        visible.AddRange(query);count.Text=(favoriteOnly?"Favorites":"All photos")+"  ·  "+visible.Count;tiles.Children.Clear();
        if(visible.Count==0) {var empty=new StackPanel {Margin=new Thickness(40,65,40,40)};empty.Children.Add(Txt("Your photos, together.",32,Brushes.White));empty.Children.Add(Txt(favoriteOnly?"Mark photos as favorites to find them here.":"Open photos or add a folder to get started.\nBrowse, zoom, and play a slideshow with Slide Pro controls.",16,muted));tiles.Children.Add(empty);return;}
        foreach(Photo photo in visible) {
            var card=new Border {Width=182,Margin=new Thickness(0,0,12,12),CornerRadius=new CornerRadius(8),Background=panel,Cursor=Cursors.Hand};
            var content=new StackPanel();var thumb=new Image {Height=130,Width=182,Stretch=Stretch.UniformToFill};
            QueueThumbnail(thumb,photo,generation);
            content.Children.Add(thumb);var label=Txt((favorites.Contains(photo.Path)?"♥  ":"")+photo.Name,11,Brushes.White);label.Margin=new Thickness(9,7,9,7);label.TextTrimming=TextTrimming.CharacterEllipsis;content.Children.Add(label);card.Child=content;card.ToolTip=photo.Path;
            card.MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2)OpenPhoto(photo);};tiles.Children.Add(card);
        }
    }
    void QueueThumbnail(Image thumb,Photo photo,int generation) {
        string key=photo.Path+"|"+photo.Date.Ticks;BitmapSource cached;
        lock(thumbnailCache)if(thumbnailCache.TryGetValue(key,out cached)){thumb.Source=cached;return;}
        thumbnailRequests++;
        Task.Run(()=>{
            thumbnailSlots.Wait();try {
                if(generation!=Volatile.Read(ref libraryGeneration))return (BitmapSource)null;
                BitmapSource bitmap=Decode(photo.Path,260);lock(thumbnailCache){if(thumbnailCache.Count>=256)thumbnailCache.Clear();thumbnailCache[key]=bitmap;}return bitmap;
            }catch(Exception){return (BitmapSource)null;}finally{thumbnailSlots.Release();}
        }).ContinueWith(task=>{
            if(Dispatcher.HasShutdownStarted)return;
            Dispatcher.BeginInvoke(new Action(()=>{if(generation!=libraryGeneration)return;if(task.Result!=null)thumb.Source=task.Result;else thumb.ToolTip="This image could not be decoded";}));
        });
    }
    static BitmapSource Decode(string path,int width) {
        var bitmap=new BitmapImage();using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;if(width>0)bitmap.DecodePixelWidth=width;bitmap.EndInit();bitmap.Freeze();}return bitmap;
    }
    void OpenPhoto(Photo photo) {
        Stop();CloseSlidePanel();CloseInfo();CloseMore();current=photo;BuildSequence();library.Visibility=Visibility.Collapsed;viewer.Visibility=Visibility.Visible;ViewerChrome(true);ShowCurrent();if(IsLoaded&&!fullscreen)ToggleFullscreen();
    }
    void BuildSequence() {
        sequence=new List<Photo>(visible);if(current!=null&&!sequence.Contains(current))sequence.Insert(0,current);
        if(shuffleButton.IsChecked==true) {for(int i=sequence.Count-1;i>0;i--){int j=rng.Next(i+1);Photo tmp=sequence[i];sequence[i]=sequence[j];sequence[j]=tmp;}if(current!=null&&sequence.Remove(current))sequence.Insert(0,current);}
        index=current==null?0:Math.Max(0,sequence.IndexOf(current));UpdateProgress();
    }
    void ShowCurrent() {
        if(sequence.Count==0)return;CancelSideNavigation();EndImageDrag();current=sequence[index];rotation=0;zoom.Value=100;title.Text=current.Name;
        try {original=Decode(current.Path,0);UpdateImage();lava.SetPhoto(original);details.Text=original.PixelWidth+" × "+original.PixelHeight+"  •  "+(new FileInfo(current.Path).Length/1024.0/1024).ToString("0.##")+" MB";Status("← → Navigate   •   Space Play / stop   •   F11 Fullscreen   •   Esc Library");}
        catch(Exception ex) {original=null;image.Source=null;lava.SetPhoto(null);details.Text="Cannot display this image";Status("Could not open "+current.Name+": "+ex.Message);}
        UpdateGlassArrows();UpdateProgress();if(infoPanel.Visibility==Visibility.Visible)UpdateInfo();
    }
    void UpdateImage() {if(original==null)return;image.Source=rotation==0?original:new TransformedBitmap(original,new RotateTransform(rotation));UpdateZoom();}
    void ZoomAt(Point cursor,double value) {
        value=Math.Max(100,Math.Min(400,value));CancelSideNavigation();
        if(original==null||Math.Abs(value-zoom.Value)<0.01)return;
        UpdateLayout();Point anchor=imageScroll.TranslatePoint(cursor,image);
        zoom.Value=value;UpdateLayout();
        if(value<=100)return;
        Point moved=image.TranslatePoint(anchor,imageScroll);
        imageScroll.ScrollToHorizontalOffset(imageScroll.HorizontalOffset+moved.X-cursor.X);
        imageScroll.ScrollToVerticalOffset(imageScroll.VerticalOffset+moved.Y-cursor.Y);
        UpdateLayout();
    }
    void UpdateZoom() {
        if(image==null||zoom==null||imageScroll==null)return;
        bool enlarged=zoom.Value>100;imageScroll.HorizontalScrollBarVisibility=enlarged?ScrollBarVisibility.Hidden:ScrollBarVisibility.Disabled;imageScroll.VerticalScrollBarVisibility=enlarged?ScrollBarVisibility.Hidden:ScrollBarVisibility.Disabled;
        image.Width=Math.Max(1,imageScroll.ActualWidth);image.Height=Math.Max(1,imageScroll.ActualHeight);image.LayoutTransform=new ScaleTransform(zoom.Value/100,zoom.Value/100);
        if(!enlarged){imageScroll.ScrollToHorizontalOffset(0);imageScroll.ScrollToVerticalOffset(0);}if(zoomLabel!=null)zoomLabel.Text=Math.Round(zoom.Value)+"%";
    }
    void UpdateProgress() {if(position!=null)position.Text=sequence.Count==0?"":(index+1)+" / "+sequence.Count;}
    void Start() {if(current==null){if(visible.Count==0){Status("Add photos before starting a slideshow.");return;}OpenPhoto(visible[0]);}if(sequence.Count==0)return;if(index==sequence.Count-1&&sequence.Count>1){index=0;ShowCurrent();}playing=true;startButton.IsEnabled=false;stopButton.IsEnabled=true;RestartTimer();}
    void RestartTimer() {timer.Stop();timer.Interval=TimeSpan.FromSeconds(delay.SelectedIndex+1);if(playing)timer.Start();}
    void Stop() {playing=false;timer.Stop();if(startButton!=null)startButton.IsEnabled=true;if(stopButton!=null)stopButton.IsEnabled=false;}
    void Advance(int direction,bool automatic) {
        if(sequence.Count==0)return;int next=index+direction;
        if(next<0||next>=sequence.Count) {if(loopButton.IsChecked==true)next=(next+sequence.Count)%sequence.Count;else {if(automatic)Stop();return;}}
        index=next;ShowCurrent();if(playing&&!automatic)RestartTimer();
    }
    void Back() {Stop();CancelSideNavigation();EndImageDrag();CloseSlidePanel();CloseInfo();CloseMore();if(fullscreen)ToggleFullscreen();viewer.Visibility=Visibility.Collapsed;library.Visibility=Visibility.Visible;ViewerChrome(false);current=null;original=null;image.Source=null;RefreshLibrary();RefreshToolbarInset();Status("Ready • "+photos.Count+" photos in library");}
    void ToggleFullscreen() {
        if(viewer.Visibility!=Visibility.Visible){if(visible.Count==0)return;OpenPhoto(visible[0]);if(fullscreen)return;}
        if(!fullscreen) {
            previousState=WindowState;previousStyle=WindowStyle;previousBounds=WindowState==WindowState.Normal?new Rect(Left,Top,ActualWidth,ActualHeight):RestoreBounds;previousResizeMode=ResizeMode;previousTopmost=Topmost;previousChrome=WindowChrome.GetWindowChrome(this);previousRootMargin=root.Margin;
            var handle=new WindowInteropHelper(this).EnsureHandle();var display=System.Windows.Forms.Screen.FromHandle(handle);var monitor=display.Bounds;var source=PresentationSource.FromVisual(this);var transform=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;var origin=transform.Transform(new Point(monitor.Left,monitor.Top));var size=transform.Transform(new Vector(monitor.Width,monitor.Height));
            // Fill monitor bounds, not its working area; the toolbar starts at the top edge.
            root.Margin=new Thickness(0);
            fullscreen=true;WindowState=WindowState.Normal;WindowChrome.SetWindowChrome(this,null);WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Left=origin.X;Top=origin.Y;Width=size.X;Height=size.Y;Topmost=true;
        } else {
            fullscreen=false;root.Margin=previousRootMargin;Topmost=previousTopmost;WindowState=WindowState.Normal;ResizeMode=previousResizeMode;WindowStyle=previousStyle;WindowChrome.SetWindowChrome(this,previousChrome);Left=previousBounds.Left;Top=previousBounds.Top;Width=previousBounds.Width;Height=previousBounds.Height;WindowState=previousState;
        }
        ViewerChrome(true);UpdateLayout();RefreshToolbarInset();UpdateLayout();UpdateZoom();QueueToolbarInset();
    }
    void ToggleFavorite() {if(current==null)return;if(!favorites.Add(current.Path))favorites.Remove(current.Path);if(!testing)SaveState();}
    void ShowInfo() {if(current==null)return;if(infoPanel.Visibility==Visibility.Visible){CloseInfo();return;}UpdateInfo();infoPanel.Visibility=Visibility.Visible;infoButton.IsChecked=true;}
    void InfoField(string label,string value,bool selectable) {
        var block=new StackPanel {Margin=new Thickness(2,0,2,15)};block.Children.Add(Txt(label,10,muted));
        if(selectable)block.Children.Add(new TextBox {Text=value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Background=Brushes.Transparent,Foreground=accent,BorderThickness=new Thickness(0),Padding=new Thickness(4),FontSize=11,Cursor=Cursors.IBeam});
        else {var text=Txt(value,12,accent);text.TextWrapping=TextWrapping.Wrap;block.Children.Add(text);}infoFields.Children.Add(block);
    }
    void UpdateInfo() {
        if(current==null)return;infoFields.Children.Clear();InfoField("Filename",current.Name,true);InfoField("File type",System.IO.Path.GetExtension(current.Path).TrimStart('.').ToUpperInvariant(),false);
        if(original!=null)InfoField("Dimensions",original.PixelWidth+" × "+original.PixelHeight+" pixels",false);
        try {var file=new FileInfo(current.Path);InfoField("File size",(file.Length/1024.0/1024).ToString("0.##")+" MB  ·  "+file.Length.ToString("N0")+" bytes",false);InfoField("Modified",file.LastWriteTime.ToString("f"),false);InfoField("Created",file.CreationTime.ToString("f"),false);}catch(IOException){InfoField("File status","File is no longer available.",false);}catch(UnauthorizedAccessException){InfoField("File status","File details are unavailable.",false);}
        InfoField("Location",current.Path,true);string ext=System.IO.Path.GetExtension(current.Path).ToLowerInvariant();if(ext==".gif"||ext==".tif"||ext==".tiff")InfoField("Display","Showing the first frame.",false);
    }
    void SaveCopy() {
        if(original==null)return;var dialog=new SaveFileDialog {Filter="PNG image|*.png|JPEG image|*.jpg",FileName=System.IO.Path.GetFileNameWithoutExtension(current.Path)+"-edited.png",AddExtension=true,OverwritePrompt=true};
        if(dialog.ShowDialog()!=true)return;
        if(string.Equals(System.IO.Path.GetFullPath(dialog.FileName),current.Path,StringComparison.OrdinalIgnoreCase)){MessageBox.Show("Choose a different filename to keep your original photo.","Save copy");return;}
        try{SaveBitmap((BitmapSource)image.Source,dialog.FileName,dialog.FilterIndex==2);Status("Copy saved: "+dialog.FileName);}catch(Exception ex){MessageBox.Show(ex.Message,"Could not save copy");}
    }
    static void SaveBitmap(BitmapSource bitmap,string path,bool jpeg) {BitmapEncoder encoder=jpeg?(BitmapEncoder)new JpegBitmapEncoder {QualityLevel=95}:new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
    void SetDefault() {try{Registration.Register();Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser="+Uri.EscapeDataString("Immersion")) {UseShellExecute=true});Status("In Settings, choose Immersion for the image types you want to open.");}catch(Exception ex){MessageBox.Show(ex.Message,"Default app setup");}}
    void Keys(object sender,KeyEventArgs e) {
        if(e.Key==Key.F11){ToggleFullscreen();e.Handled=true;return;}
        if(e.Key==Key.Escape){if(morePanel.Visibility==Visibility.Visible)CloseMore();else if(infoPanel.Visibility==Visibility.Visible)CloseInfo();else if(slidePanel.Visibility==Visibility.Visible)CloseSlidePanel();else if(fullscreen)ToggleFullscreen();else if(viewer.Visibility==Visibility.Visible)Back();e.Handled=true;return;}
        if(e.OriginalSource is TextBox || e.OriginalSource is ComboBox)return;
        if(viewer.Visibility!=Visibility.Visible)return;
        if(e.Key==Key.Left){Advance(-1,false);e.Handled=true;}else if(e.Key==Key.Right){Advance(1,false);e.Handled=true;}else if(e.Key==Key.Space){if(playing)Stop();else Start();e.Handled=true;}
    }
    public void LoadArguments(string[] args) {
        var paths=args.Where(a=>!a.StartsWith("--")&&(File.Exists(a)||Directory.Exists(a))).ToArray();if(paths.Length==0)return;
        if(!File.Exists(paths[0])||!IsImage(paths[0])){ImportPaths(paths);return;}
        string first=System.IO.Path.GetFullPath(paths[0]);var requested=new Photo {Path=first};var known=photos.ToDictionary(p=>p.Path,p=>p,StringComparer.OrdinalIgnoreCase);
        if(known.ContainsKey(first))requested=known[first];else {photos.Add(requested);known[first]=requested;}
        visible.Clear();visible.Add(requested);OpenPhoto(requested);
        // Directory enumeration is also deferred, so the window can show the requested photo immediately.
        Func<string[]> neighborPaths=()=>{try{return Directory.GetFiles(System.IO.Path.GetDirectoryName(first)).Where(IsImage).OrderBy(p=>p,StringComparer.CurrentCultureIgnoreCase).ToArray();}catch(IOException){return new string[0];}catch(UnauthorizedAccessException){return new string[0];}};
        Action<string[]> applyNeighbors=items=>{
            if(items.Length==0||current==null||!string.Equals(current.Path,first,StringComparison.OrdinalIgnoreCase)||viewer.Visibility!=Visibility.Visible)return;
            var lookup=photos.ToDictionary(p=>p.Path,p=>p,StringComparer.OrdinalIgnoreCase);var neighbors=new List<Photo>();
            foreach(string path in items){Photo photo;if(!lookup.TryGetValue(path,out photo)){photo=new Photo {Path=path};photos.Add(photo);lookup[path]=photo;}neighbors.Add(photo);}
            visible.Clear();visible.AddRange(neighbors);BuildSequence();
        };
        if(testing)applyNeighbors(neighborPaths());else Task.Run(neighborPaths).ContinueWith(task=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(()=>applyNeighbors(task.Result)));});
    }
    void ReadState(bool scanFolders) {
        try {
            var known=new HashSet<string>(photos.Select(p=>p.Path),StringComparer.OrdinalIgnoreCase);string file=System.IO.Path.Combine(stateDir,"library.txt");
            if(!File.Exists(file))file=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PhotosPlus","library.txt");
            if(File.Exists(file))foreach(string line in File.ReadAllLines(file)){if(line.Length<3)continue;string path=line.Substring(2);if(line.StartsWith("F|"))favorites.Add(path);else if(line.StartsWith("D|")&&(!scanFolders||Directory.Exists(path))&&!folders.Contains(path,StringComparer.OrdinalIgnoreCase))folders.Add(path);else if(line.StartsWith("P|")&&(!scanFolders||File.Exists(path))&&known.Add(path))photos.Add(new Photo {Path=path});}
            if(scanFolders)foreach(string folder in folders.ToArray())foreach(string path in Scan(folder))if(known.Add(path))photos.Add(new Photo {Path=path});
        }catch(Exception ex){Status("Could not restore library: "+ex.Message);}finally{stateLoaded=true;}
    }
    void SaveState() {if(!testing&&!stateLoaded)return;try {Directory.CreateDirectory(stateDir);var lines=folders.Select(p=>"D|"+p).Concat(photos.Select(p=>"P|"+p.Path)).Concat(favorites.Select(p=>"F|"+p)).ToArray();string target=System.IO.Path.Combine(stateDir,"library.txt"), temp=target+".tmp";File.WriteAllLines(temp,lines);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}catch(Exception ex){Status("Could not save library: "+ex.Message);}}
    void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
    void Pump(int milliseconds) {var frame=new DispatcherFrame();var pause=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(milliseconds)};pause.Tick+=(s,e)=>{pause.Stop();frame.Continue=false;};pause.Start();Dispatcher.PushFrame(frame);}
    byte[] LavaPixels() {lava.InvalidateVisual();UpdateLayout();var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(lava),null,new Rect(0,0,lava.ActualWidth,lava.ActualHeight));var bitmap=new RenderTargetBitmap((int)lava.ActualWidth,(int)lava.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var data=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(data,bitmap.PixelWidth*4,0);return data;}
    void CheckImageInteractions() {
        int savedIndex=index;
        var center=new Point(imageScroll.ActualWidth*0.5,imageScroll.ActualHeight*0.5);
        var right=new Point(imageScroll.ActualWidth-25,imageScroll.ActualHeight*0.2);
        var left=new Point(25,imageScroll.ActualHeight*0.2);
        zoom.Value=150;ZoomAt(center,75);UpdateLayout();
        Check(zoom.Value==100&&zoomLabel.Text=="100%","zooming out stops at 100 percent");
        zoom.Value=25;UpdateLayout();Check(zoom.Value==100,"slider cannot shrink the photo below fit");
        BeginImageGesture(right,1);bool capturedForTest=panning;EndImageGesture(right);
        Check(index==(savedIndex+1)%sequence.Count,"clicking the right side away from its arrow advances (index "+index+", start "+savedIndex+", width "+imageScroll.ActualWidth+", x "+right.X+", active "+capturedForTest+", moved "+panMoved+", zoomed "+panZoomed+")");
        BeginImageGesture(left,1);EndImageGesture(left);
        Check(index==savedIndex,"clicking the left side away from its arrow goes back");
        BeginImageGesture(center,1);EndImageGesture(center);
        Check(index==savedIndex,"a single center click keeps the current photo");
        zoom.Value=200;UpdateLayout();imageScroll.ScrollToHorizontalOffset(100);imageScroll.ScrollToVerticalOffset(80);UpdateLayout();
        double beforePan=imageScroll.HorizontalOffset;
        BeginImageGesture(right,1);var dragged=new Point(right.X-40,right.Y-30);MoveImageGesture(dragged);UpdateLayout();EndImageGesture(dragged);
        Check(index==savedIndex&&imageScroll.HorizontalOffset>beforePan&&!sideClickTimer.IsEnabled,
            "dragging from a side zone pans without navigating");
        BeginImageGesture(center,2);UpdateLayout();
        Check(zoom.Value==100&&imageScroll.HorizontalOffset==0&&imageScroll.VerticalOffset==0&&!imageScroll.IsMouseCaptured,
            "double-click resets zoom, pan offsets and mouse capture");
        zoom.Value=200;UpdateLayout();BeginImageGesture(right,1);EndImageGesture(right);
        Check(sideClickTimer.IsEnabled,"zoomed side clicks allow time for a double-click");
        BeginImageGesture(right,2);Pump(System.Windows.Forms.SystemInformation.DoubleClickTime+60);UpdateLayout();
        Check(index==savedIndex&&zoom.Value==100&&!sideClickTimer.IsEnabled,
            "double-clicking a zoomed side resets fit without changing photos");
        zoom.Value=150;UpdateLayout();BeginImageGesture(left,1);EndImageGesture(left);
        Pump(System.Windows.Forms.SystemInformation.DoubleClickTime+60);
        Check(index==(savedIndex+sequence.Count-1)%sequence.Count&&zoom.Value==100,
            "a single zoomed side click navigates after the double-click window");
        index=savedIndex;ShowCurrent();UpdateLayout();
    }
    void CheckDesktopMenuLayout() {
        var display=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var transform=PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
        var origin=transform.Transform(new Point(display.Bounds.Left,display.Bounds.Top));
        double barPixels=Math.Min(90,Math.Max(34,DesktopMenuBounds.SafeTop(new WindowInteropHelper(this).Handle,display)-display.Bounds.Top+8));
        var size=transform.Transform(new Vector(display.Bounds.Width,barPixels));
        var menuBar=new Window {WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,
            ShowActivated=false,Topmost=true,Left=origin.X,Top=origin.Y,Width=size.X,Height=size.Y,Background=Brushes.Black};
        var savedPhoto=current;
        try {
            Back();WindowState=WindowState.Maximized;UpdateLayout();menuBar.Show();menuBar.UpdateLayout();
            RefreshToolbarInset();UpdateLayout();
            double menuBottom=menuBar.PointToScreen(new Point(0,menuBar.ActualHeight)).Y;
            Check(libraryHeader.IsVisible&&viewer.Visibility==Visibility.Collapsed&&
                libraryHeader.PointToScreen(new Point(0,0)).Y>=menuBottom-1,
                "maximized library header stays below an unregistered desktop menu bar");
            Snapshot(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","library-desktop-menu.png"));
            OpenPhoto(savedPhoto);UpdateLayout();CheckFullscreenBounds("desktop menu detection does not add a fullscreen gap");
            ToggleFullscreen();UpdateLayout();
            var bottomBar=(FrameworkElement)lava.Parent;
            Point bottomOrigin=bottomBar.PointToScreen(new Point(0,0));
            Point bottomCorner=bottomBar.PointToScreen(new Point(bottomBar.ActualWidth,bottomBar.ActualHeight));
            Point lavaOrigin=lava.TranslatePoint(new Point(0,0),bottomBar);
            Check(bottomBar.ActualHeight==40&&bottomOrigin.X>=display.WorkingArea.Left-1&&
                bottomCorner.X<=display.WorkingArea.Right+1&&bottomCorner.Y<=display.WorkingArea.Bottom+1&&
                lavaOrigin.Y>=0&&lavaOrigin.Y+lava.ActualHeight<=bottomBar.ActualHeight,
                "maximized bottom bar and lava remain fully inside the visible work area with a desktop menu");
            Snapshot(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","viewer-desktop-menu.png"));
        } finally {menuBar.Close();RefreshToolbarInset();UpdateLayout();}
        Check(Math.Abs(viewerToolbar.PointToScreen(new Point(0,0)).Y-
            DesktopMenuBounds.SafeTop(new WindowInteropHelper(this).Handle,display))<=1,
            "removing a desktop menu releases its reserved header space");
    }
    void CheckFullscreenBounds(string name) {
        var screen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;
        Point origin=root.PointToScreen(new Point(0,0));
        Point corner=root.PointToScreen(new Point(root.ActualWidth,root.ActualHeight));
        Point toolbarOrigin=viewerToolbar.PointToScreen(new Point(0,0));
        bool fits=fullscreen&&root.Margin==new Thickness(0)&&viewerToolbar.IsVisible&&viewerToolbar.ActualHeight==48&&
            Math.Abs(origin.X-screen.Left)<=1&&Math.Abs(origin.Y-screen.Top)<=1&&
            Math.Abs(corner.X-screen.Right)<=1&&Math.Abs(corner.Y-screen.Bottom)<=1&&
            Math.Abs(toolbarOrigin.Y-screen.Top)<=1;
        Check(fits,name+(fits?"":" (origin "+origin+", corner "+corner+", screen "+screen+", margin "+root.Margin+")"));
    }
    public int SelfTest() {
        string output=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(output);string dir=System.IO.Path.Combine(output,"input");Directory.CreateDirectory(dir);
        for(int i=0;i<3;i++) {var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawRectangle(Brush(i==0?"#4089B5":i==1?"#AC775D":"#708A59"),null,new Rect(0,0,960,640));dc.DrawEllipse(Brush("#E2CDA2"),null,new Point(620,190),90,90);dc.DrawGeometry(Brush("#233F45"),null,Geometry.Parse("M0,640 L0,480 240,250 510,540 760,330 960,520 960,640 Z"));dc.DrawText(new FormattedText("Sample landscape "+(i+1),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),28,Brushes.White),new Point(35,580));}var bmp=new RenderTargetBitmap(960,640,96,96,PixelFormats.Pbgra32);bmp.Render(drawing);SaveBitmap(bmp,System.IO.Path.Combine(dir,"sample-"+i+".png"),false);}
        File.WriteAllText(System.IO.Path.Combine(dir,"broken.png"),"invalid image");
        ImportPaths(new[]{dir});Check(photos.Count==4,"folder import and corrupt-file tolerance");ImportPaths(new[]{dir});Check(photos.Count==4,"duplicate import");
        search.Text="sample";Check(visible.Count==3,"filename search");OpenPhoto(visible[0]);Check(original!=null,"image decode");
        Start();Check(playing&&timer.IsEnabled,"start slideshow");delay.SelectedIndex=6;Check(timer.Interval==TimeSpan.FromSeconds(7),"live delay change");Stop();Check(!timer.IsEnabled&&!playing,"stop slideshow");
        index=sequence.Count-1;loopButton.IsChecked=false;Start();index=sequence.Count-1;Advance(1,true);Check(!playing&&index==sequence.Count-1,"non-loop end stops");
        loopButton.IsChecked=true;Advance(1,false);Check(index==0,"loop wraps");Advance(-1,false);Check(index==sequence.Count-1,"previous wraps");
        shuffleButton.IsChecked=true;BuildSequence();Check(sequence.Distinct().Count()==3&&sequence[index]==current,"shuffle retains current and all photos");
        ToggleFavorite();Check(favorites.Contains(current.Path),"favorite toggles");rotation=90;UpdateImage();Check(((BitmapSource)image.Source).PixelWidth==640,"rotation");SaveBitmap((BitmapSource)image.Source,System.IO.Path.Combine(output,"rotated.png"),false);Check(Decode(System.IO.Path.Combine(output,"rotated.png"),0).PixelHeight==960,"save rotated copy");
        OpenPhoto(photos.First(p=>p.Name=="broken.png"));Check(original==null,"corrupt image fails gracefully");OpenPhoto(visible[0]);
        Show();UpdateLayout();Check(fullscreen&&root.Margin==new Thickness(0),"photo opens in fullscreen automatically");Snapshot(System.IO.Path.Combine(output,"startup-fullscreen.png"));ToggleFullscreen();UpdateLayout();Check(slidePanel.Visibility==Visibility.Collapsed&&libraryHeader.Visibility==Visibility.Collapsed,"compact viewer hides library header and slide controls");Check(imageScroll.ActualHeight>=ActualHeight-92,"viewer reserves nearly all height for image");
        Check(lava.Palette.Length==6&&lava.Palette.Distinct().Count()>=3,"six colors sampled from photo");Check(lava.ActualHeight==28&&lava.ActualWidth>100,"lava lamp fits within the bottom controls");Pump(2400);var firstLava=LavaPixels();Pump(240);Check(!firstLava.SequenceEqual(LavaPixels()),"lava animation changes while slideshow is stopped");Snapshot(System.IO.Path.Combine(output,"viewer.png"));
        Check((string)lavaV1Button.Content=="Lava Flow v1"&&(string)lavaButton.Content=="Lava Flow v2"&&lavaButton.IsChecked==true&&lavaV1Button.IsChecked!=true,"versioned lava buttons default to v2");
        lavaV1Button.IsChecked=true;lavaV1Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();
        Check(lava.Version==1&&lava.IsVisible&&lava.ActualHeight==12&&lavaButton.IsChecked==false,"v1 selects the original thin strip and deselects v2");
        Pump(240);var firstLegacy=LavaPixels();Pump(240);Check(!firstLegacy.SequenceEqual(LavaPixels()),"original v1 animation changes while slideshow is stopped");Snapshot(System.IO.Path.Combine(output,"lava-v1.png"));
        lavaV1Button.IsChecked=false;lavaV1Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();Check(!lava.IsVisible&&lavaV1Button.IsChecked==false&&lavaButton.IsChecked==false,"clicking active v1 stops lava");
        lavaButton.IsChecked=true;lavaButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();Check(lava.Version==2&&lava.IsVisible&&lava.ActualHeight==28,"v2 restarts the blob effect after v1 stops");
        lavaButton.IsChecked=false;lavaButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();Check(!lava.IsVisible&&lavaV1Button.IsChecked==false&&lavaButton.IsChecked==false,"clicking active v2 stops lava");
        lavaV1Button.IsChecked=true;lavaV1Button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var beforeLegacyPalette=lava.Palette;Advance(1,false);Check(!beforeLegacyPalette.SequenceEqual(lava.Palette)&&lava.Version==1,"v1 colors follow photo navigation");Advance(-1,false);
        lavaButton.IsChecked=true;lavaButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();Check(lava.Version==2&&lava.IsVisible&&lavaV1Button.IsChecked==false&&lava.ActualHeight==28,"v2 replaces active v1 and restores blob height");
        RevealArrows(90,1000,true);Check(previousArrow.Opacity==1&&nextArrow.Opacity==0,"broad left zone reveals previous arrow");RevealArrows(910,1000,true);Check(nextArrow.Opacity==1&&previousArrow.Opacity==0,"broad right zone reveals next arrow");RevealArrows(500,1000,true);Check(previousArrow.Opacity==0&&nextArrow.Opacity==0&&!previousArrow.IsHitTestVisible,"center hides arrows and invisible buttons do not capture clicks");RevealArrows(0,1000,false);Check(previousArrow.Opacity==0&&nextArrow.Opacity==0,"leaving image hides arrows");
        var leftShell=(Border)viewerToolbar.Children[0];var leftButtons=(StackPanel)leftShell.Child;Check(leftButtons.Children.Count==3&&leftShell.BorderThickness==new Thickness(0)&&leftShell.Background==Brushes.Transparent,"top left keeps only save info and more without outer frame");
        var saveControl=(Button)leftButtons.Children[0];double topGap=saveControl.TranslatePoint(new Point(0,0),leftShell).Y;double bottomGap=leftShell.ActualHeight-topGap-saveControl.ActualHeight;Check(Math.Abs(topGap)<=1&&Math.Abs(bottomGap)<=1&&saveControl.ActualWidth==46,"toolbar controls fill the bar height and meet the top edge");
        ((Button)leftButtons.Children[2]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();Check(morePanel.IsVisible&&((SolidColorBrush)morePanel.Background).Color==Color.FromRgb(26,26,29),"more opens an in-app dark menu without native white gutter");Snapshot(System.IO.Path.Combine(output,"more.png"));CloseMore();
        double imageHeight=imageScroll.ActualHeight;slideButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();Check(slidePanel.Visibility==Visibility.Visible&&imageScroll.ActualHeight==imageHeight,"slide button opens overlay without reducing image area");Check(((SolidColorBrush)slideButton.Foreground).Color==((SolidColorBrush)infoButton.Foreground).Color&&((System.Windows.Shapes.Path)slideButton.Content).Fill==slideButton.Foreground,"slideshow uses the same light icon as other dark buttons");Snapshot(System.IO.Path.Combine(output,"slideshow.png"));CloseSlidePanel();Check(slidePanel.Visibility==Visibility.Collapsed&&slideButton.IsChecked==false,"slide controls close");
        zoom.Value=200;UpdateLayout();Check(imageScroll.ExtentWidth>imageScroll.ViewportWidth&&original.PixelWidth==960,"zoom scrolls full resolution source");zoom.Value=100;UpdateLayout();Check(imageScroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled&&imageScroll.VerticalScrollBarVisibility==ScrollBarVisibility.Disabled,"fit removes scrollbars after zoom");
        var cursor=new Point(imageScroll.ViewportWidth*0.72,imageScroll.ViewportHeight*0.65);var anchor=imageScroll.TranslatePoint(cursor,image);ZoomAt(cursor,150);var anchored=image.TranslatePoint(anchor,imageScroll);Check(Math.Abs(anchored.X-cursor.X)<2&&Math.Abs(anchored.Y-cursor.Y)<2,"wheel zoom preserves photo point beneath off-center cursor");
        anchor=imageScroll.TranslatePoint(cursor,image);ZoomAt(cursor,225);anchored=image.TranslatePoint(anchor,imageScroll);Check(Math.Abs(anchored.X-cursor.X)<2&&Math.Abs(anchored.Y-cursor.Y)<2,"repeated wheel zoom keeps cursor anchor");
        anchor=imageScroll.TranslatePoint(cursor,image);ZoomAt(cursor,175);anchored=image.TranslatePoint(anchor,imageScroll);Check(Math.Abs(anchored.X-cursor.X)<2&&Math.Abs(anchored.Y-cursor.Y)<2,"wheel zoom out keeps cursor anchor");zoom.Value=100;UpdateLayout();
        zoom.Value=225;UpdateLayout();Check(imageScroll.ComputedHorizontalScrollBarVisibility!=Visibility.Visible&&imageScroll.ComputedVerticalScrollBarVisibility!=Visibility.Visible,"zoom never displays native white scrollbars");imageScroll.ScrollToHorizontalOffset(100);imageScroll.ScrollToVerticalOffset(80);UpdateLayout();Check(imageScroll.HorizontalOffset>0&&imageScroll.VerticalOffset>0,"hidden scrollbars preserve panning");Snapshot(System.IO.Path.Combine(output,"zoom-without-scrollbars.png"));zoom.Value=100;UpdateLayout();
        CheckImageInteractions();
        ShowInfo();UpdateLayout();Check(infoPanel.IsVisible&&OwnedWindows.Count==0&&imageScroll.ActualHeight==imageHeight,"info opens inside viewer without a modal dialog or shrinking photo");
        Check(infoFields.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<TextBox>()).Any(t=>t.Text==current.Path),"info displays current file path");Snapshot(System.IO.Path.Combine(output,"info.png"));
        var beforePalette=lava.Palette;Advance(1,false);Check(!beforePalette.SequenceEqual(lava.Palette),"lava palette follows photo navigation");Check(infoFields.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<TextBox>()).Any(t=>t.Text==current.Path),"info follows photo navigation");ShowInfo();Check(!infoPanel.IsVisible&&infoButton.IsChecked==false,"info button closes panel");
        Rect normalBounds=new Rect(Left,Top,ActualWidth,ActualHeight);ToggleFullscreen();UpdateLayout();
        Check(fullscreen&&ResizeMode==ResizeMode.NoResize&&WindowChrome.GetWindowChrome(this)==null,"fullscreen removes nonclient clipping");
        CheckFullscreenBounds("fullscreen fills all monitor edges with the toolbar at the top");Snapshot(System.IO.Path.Combine(output,"fullscreen.png"));
        ShowInfo();UpdateLayout();Check(infoPanel.IsVisible&&viewerToolbar.IsVisible,"fullscreen info remains in app");Snapshot(System.IO.Path.Combine(output,"fullscreen-info.png"));CloseInfo();
        ToggleSlidePanel();UpdateLayout();Check(slidePanel.IsVisible&&viewerToolbar.IsVisible,"fullscreen keeps slideshow and toolbar available");CloseSlidePanel();ToggleFullscreen();UpdateLayout();
        Check(!fullscreen&&root.Margin.Top==0&&WindowState==WindowState.Normal&&libraryHeader.Visibility==Visibility.Collapsed&&Math.Abs(Left-normalBounds.Left)<1&&Math.Abs(Top-normalBounds.Top)<1&&Math.Abs(ActualWidth-normalBounds.Width)<1,"fullscreen restores compact viewer and bounds");
        WindowState=WindowState.Maximized;UpdateLayout();ToggleFullscreen();ToggleFullscreen();Check(WindowState==WindowState.Maximized&&viewerToolbar.IsVisible,"fullscreen restores maximized window");
        for(int cycle=0;cycle<8;cycle++) {
            ToggleFullscreen();Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));UpdateLayout();CheckFullscreenBounds("repeated fullscreen entry fills monitor "+cycle);
            ToggleFullscreen();Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));UpdateLayout();var activeScreen=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);Check(!fullscreen&&WindowState==WindowState.Maximized&&Math.Abs(viewerToolbar.PointToScreen(new Point(0,0)).Y-DesktopMenuBounds.SafeTop(new WindowInteropHelper(this).Handle,activeScreen))<=1,"repeated maximized restore aligns toolbar to work area "+cycle);
        }
        CheckDesktopMenuLayout();
        Snapshot(System.IO.Path.Combine(output,"maximized-after-fullscreen.png"));WindowState=WindowState.Normal;UpdateLayout();RefreshToolbarInset();UpdateLayout();
        var focusTarget=(Button) ((StackPanel)((Border)viewerToolbar.Children[0]).Child).Children[0];focusTarget.Focus();UpdateLayout();var buttonSurface=(Border)focusTarget.Template.FindName("surface",focusTarget);Check(buttonSurface.BorderThickness==new Thickness(0)&&focusTarget.ActualWidth==46,"focus keeps toolbar button size stable");
        Width=760;Height=480;UpdateLayout();Check(imageScroll.ActualHeight>=ActualHeight-92,"compact viewer at minimum size");ToggleSlidePanel();UpdateLayout();Snapshot(System.IO.Path.Combine(output,"compact-slideshow.png"));CloseSlidePanel();ShowInfo();UpdateLayout();Check(infoPanel.ActualHeight<=imageScroll.ActualHeight-23,"info panel scrolls within small viewer");Snapshot(System.IO.Path.Combine(output,"compact-info.png"));CloseInfo();Width=1250;Height=820;Back();Check(libraryHeader.Visibility==Visibility.Visible,"library header returns");OpenPhoto(visible[0]);Check(fullscreen&&root.Margin==new Thickness(0),"opening photo from loaded library also enters fullscreen");Back();favoriteOnly=true;RefreshLibrary();Check(visible.Count==1,"favorites filter");favoriteOnly=false;search.Text="";RefreshLibrary();UpdateLayout();Snapshot(System.IO.Path.Combine(output,"library.png"));var fastWatch=Stopwatch.StartNew();var fastViewer=new Viewer(true,true);fastViewer.LoadArguments(new[]{System.IO.Path.Combine(dir,"sample-0.png")});fastWatch.Stop();Check(fastViewer.current.Name=="sample-0.png"&&fastViewer.original!=null,"direct photo startup displays requested image");Check(fastViewer.thumbnailRequests==0,"direct startup does not queue library thumbnails");Check(fastViewer.sequence.Count==4,"direct startup retains neighboring image navigation");fastViewer.Close();File.WriteAllText(System.IO.Path.Combine(output,"startup-timing.txt"),"Direct photo preparation with initialized runtime: "+fastWatch.ElapsedMilliseconds+" ms. No gallery thumbnails queued.");Close();File.WriteAllText(System.IO.Path.Combine(output,"results.txt"),"PASS: "+checks+" functional checks; library, viewer, menu, slideshow and info snapshots generated.");return 0;
    }
    void Snapshot(string path) {var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);SaveBitmap(bmp,path,false);}
}
}
