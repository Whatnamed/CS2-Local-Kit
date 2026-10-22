using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CS2LocalKit.App;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.App.ViewModels;
using CS2LocalKit.Core.HumanPresets;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var output = Path.Combine(repo, "temp", "ui-probe");
            Directory.CreateDirectory(output);
            var root = Path.Combine(output, "sandbox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Environment.SetEnvironmentVariable("CS2LOCALKIT_CS2MOD_ROOT", root);
            var catalog = args.FirstOrDefault() ?? Path.Combine(repo,"tests/CS2LocalKit.Core.Tests/fixtures/catalog");
            var presets = Path.Combine(root,"presets"); Directory.CreateDirectory(presets);
            File.WriteAllText(Path.Combine(presets,"周末练习.v1.json"),HumanPresetJson.Write(HumanPresetTemplate.CreateMinimalValid()));
            var game = Path.Combine(root,"fake-game"); Directory.CreateDirectory(Path.Combine(game,"game/csgo"));
            using var services = new AppServices(cs2ModRoot:root,catalogCacheRoot:catalog,imageCacheRoot:Path.Combine(root,"images"),csgoDir:Path.Combine(game,"game/csgo"),cs2Root:game,activePresetPath:Path.Combine(root,"active.json"),presetsRoot:presets,backupsRoot:Path.Combine(root,"backups"),playerStatePath:Path.Combine(root,"player.json"),lockPath:Path.Combine(repo,"runtime/inventory-simulator.lock.json"),imageHandler:new OfflineImages(),cs2RunningProbe:()=>false);
            var app = new App(); app.InitializeComponent();
            Console.WriteLine("Theme brush="+app.TryFindResource("ComboBoxBackground")+" Style="+((Style)app.FindResource(typeof(System.Windows.Controls.ComboBox))).BasedOn);
            var vm = new MainViewModel(services,new SilentDialogs());
            var window = new MainWindow { ShowActivated=false,ShowInTaskbar=false };
            window.AttachViewModel(vm);
            Capture(window,output,"spike",1280,840,96);
            Console.WriteLine("PASS: actual WPF resources and Shell rendered without Window.Show, HWND or global input.");
            window.Close();
            app.Shutdown();
            return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    static void Capture(MainWindow window,string output,string name,int width,int height,int dpi)
    {
        window.ApplyTemplate();
        var content=(FrameworkElement)window.Content;
        content.Width=width;content.Height=height;
        for(int i=0;i<3;i++) {content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();Pump();}
        var bitmap=new RenderTargetBitmap(width*dpi/96,height*dpi/96,dpi,dpi,PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
    }
    sealed class OfflineImages:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
    sealed class SilentDialogs:IDialogService
    {
        public UnsavedChangesResolution ConfirmUnsavedChanges(string n)=>UnsavedChangesResolution.Cancel;
        public bool Confirm(string t,string m)=>false;
        public void ShowError(string t,string m)=>Console.WriteLine("DIALOG ERROR: "+t+" "+m);
        public void ShowInfo(string t,string m)=>Console.WriteLine("DIALOG INFO: "+t+" "+m);
        public string? ShowSaveFileDialog(string n,string f)=>null;
        public string? ShowOpenFileDialog(string f)=>null;
    }
}
