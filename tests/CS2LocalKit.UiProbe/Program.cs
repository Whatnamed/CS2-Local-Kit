using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CS2LocalKit.App;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.App.ViewModels;
using CS2LocalKit.App.Views;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using CS2LocalKit.Core.MatchSetup;

/// <summary>
/// Actual WPF visual/automation probe. Never calls Show, Activate, SendInput, Clipboard, or a real
/// install/restore command. All services receive explicit sandbox paths and an offline HTTP handler.
/// Optional arguments are read-only catalog/art sources; no personal preset is read.
/// </summary>
internal static class Program
{
    static readonly List<string> Checks=[];
    static string Output="";
    static MainWindow Window=null!;
    static MainViewModel Vm=null!;
    static string Root="";
    static string Repo="";
    static readonly List<object> Captures=[];
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint handle,out uint processId);
    static uint WindowProcessId(nint handle) {GetWindowThreadProcessId(handle,out var id);return id;}

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Repo=Path.GetFullPath(args.FirstOrDefault(a=>a.StartsWith("--repo=",StringComparison.Ordinal))?[7..] ?? Path.Combine(AppContext.BaseDirectory,"../../../../../"));
            Output=Path.Combine(Repo,"temp/ui-probe");Directory.CreateDirectory(Output);
            Root=Path.Combine(Output,"sandbox-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable("CS2LOCALKIT_CS2MOD_ROOT",Root);
            var source=args.FirstOrDefault(a=>!a.StartsWith("--",StringComparison.Ordinal))??Path.Combine(Repo,"tests/CS2LocalKit.Core.Tests/fixtures/catalog");
            var catalog=Path.Combine(Root,"catalog");CopyDirectory(source,catalog);
            var presets=Path.Combine(Root,"presets");Directory.CreateDirectory(presets);
            var example=File.ReadAllText(Path.Combine(Repo,"tests/CS2LocalKit.Core.Tests/fixtures/presets/example.v1.json"));
            foreach(var name in new[]{"周末练习","CT 蓝色收藏","T 进攻配置","长名称测试 · 中文 English — personal practice preset"}) File.WriteAllText(Path.Combine(presets,name+".v1.json"),example);
            var game=Path.Combine(Root,"fake-game");Directory.CreateDirectory(Path.Combine(game,"game/csgo"));
            using var services=new AppServices(cs2ModRoot:Root,catalogCacheRoot:catalog,imageCacheRoot:Path.Combine(Root,"images"),csgoDir:Path.Combine(game,"game/csgo"),cs2Root:game,activePresetPath:Path.Combine(Root,"active.json"),presetsRoot:presets,backupsRoot:Path.Combine(Root,"backups"),playerStatePath:Path.Combine(Root,"player.json"),lockPath:Path.Combine(Repo,"runtime/inventory-simulator.lock.json"),imageHandler:new OfflineImages(args.Where(a=>!a.StartsWith("--",StringComparison.Ordinal)).Skip(1).FirstOrDefault()),cs2RunningProbe:()=>false);
            var beforeForeground=GetForegroundWindow();var foregroundProcessBefore=WindowProcessId(beforeForeground);
            var app=new App();app.InitializeComponent();SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var dialogs=new SilentDialogs();Vm=new MainViewModel(services,dialogs);
            Window=new MainWindow {ShowActivated=false,ShowInTaskbar=false};Window.AttachViewModel(Vm);
            using var sourceWindow=new HwndSource(new HwndSourceParameters("LocalKit isolated UI probe") { Width=1,Height=1,PositionX=-30000,PositionY=-30000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080 });
            sourceWindow.RootVisual=(Visual)Window.Content;Pump();
            var nativeDpi=sourceWindow.CompositionTarget.TransformToDevice.M11*96;
            Check("owned HWND remains hidden and non-activating",!IsWindowVisible(sourceWindow.Handle)&&GetForegroundWindow()!=sourceWindow.Handle);
            Check("40 built-in teams and thousands of persona identities",RosterCatalog.LoadBuiltIn().Teams.Length==40&&RosterCatalog.LoadBuiltIn().Players.Length>1000);
            Vm.Bot.Setup.Friendly.TeamId="team-13";Vm.Bot.Setup.Enemy.TeamId="team-2";
            Vm.Bot.Setup.ReplacedSlot=2;
            Vm.Bot.Setup.Name="周末对局 · 历史阵容";
            Capture("match-1280",1280,840,96);
            Check("both roster sides retained; human replacement is visible",Vm.Bot.Setup.Friendly.Slots[2].Participation=="我 · 真人"&&Vm.Bot.Setup.Enemy.Slots.All(s=>s.Participation=="Bot"));
            if(!args.Contains("--quick")) {
                Capture("match-1024",1024,660,96);Capture("match-min-client",1000,610,96);Capture("match-1440-125dpi",1440,900,120);
                Capture("match-1280-150dpi",1280,840,144);Capture("match-1600",1600,940,96);
            }
            Layout(1280,840,96);
            var match=Desc<MatchSetupView>(Window).Single();
            var rosterTabs=Desc<TabControl>(match).Where(t=>AutomationProperties.GetName(t)=="阵容编辑方式").ToArray();
            SelectTab(rosterTabs[0],2);Layout(1280,840,96);
            var search=Desc<TextBox>(match).First(t=>AutomationProperties.GetName(t)=="搜索选手");
            SetText(search,"ropz");Check("UIA ValuePattern filters real persona library",Vm.Bot.Setup.Friendly.Personas.Any(p=>p.Player.Name.Contains("ropz",StringComparison.OrdinalIgnoreCase)));
            var players=Desc<ListBox>(match).First(t=>AutomationProperties.GetName(t)=="选手搜索结果");
            players.SelectedItem=players.Items.Cast<PersonaChoice>().First(p=>p.Player.Enabled);Pump();

            Vm.Bot.Setup.Friendly.SelectedSeat=1;Invoke(Desc<Button>(match).First(t=>AutomationProperties.GetName(t)=="替换席位"));
            Check("UIA InvokePattern replaces the selected seat with explicit persona",Vm.Bot.Setup.Friendly.Slots[1].Snapshot().PersonaId is not null);
            Capture("players-search",1280,840,96);
            SetText(search,"no-such-player-中文-xyz");Check("empty search clears old persona and blocks replace",!Vm.Bot.Setup.Friendly.Personas.Any()&&!Vm.Bot.Setup.Friendly.ReplaceCommand.CanExecute(null));
            Capture("players-empty-1024",1024,660,96);
            var disabled=RosterCatalog.LoadBuiltIn().Players.First(p=>!p.Enabled);
            SetText(search,disabled.PersonaId);players.SelectedItem=players.Items[0];Pump();
            Check("disabled persona is shown but never bindable",!Vm.Bot.Setup.Friendly.ReplaceCommand.CanExecute(null));
            Capture("players-disabled",1280,840,96);
            SelectTab(rosterTabs[0],3);Capture("custom-roster",1280,840,96);
            Vm.Bot.Setup.Friendly.CustomName="混编练习";Vm.Bot.Setup.Friendly.NewCustomCommand.Execute(null);
            Check("custom roster writes sandbox library",new MatchSetupStore(Path.Combine(Root,"app-data/match-setup"),RosterCatalog.LoadBuiltIn()).Load().CustomRosters.Any());
            SelectTab(rosterTabs[0],0);
            Invoke(ById<Button>("SaveMatchPreset"));Check("save persists without creating a queue",File.Exists(Path.Combine(Root,"app-data/match-setup/library.json"))&&!File.Exists(Path.Combine(Root,"app-data/match-setup/request.json")));
            Invoke(ById<Button>("QueueMatchSetup"));Check("queue action creates an independent pending request",Vm.Bot.Setup.DraftState.Contains("一致")&&Vm.Bot.Setup.ApplyStatus.Contains("待应用"));
            SelectTab(ById<TabControl>("MatchWorkspaceTabs"),1);Capture("match-status-pending",1280,840,96);
            Vm.Bot.Setup.Aim=AimSetting.Body;Check("dirty draft is distinguished from queued snapshot",Vm.Bot.Setup.DraftState.Contains("不同"));Capture("match-dirty-1024",1024,660,96);
            SelectTab(ById<TabControl>("MatchWorkspaceTabs"),2);Capture("match-presets",1280,840,96);
            Navigate("Cosmetics");
            Vm.Cosmetics.SelectedItem=Vm.Cosmetics.Items.FirstOrDefault(i=>i.DefIndex==7)??Vm.Cosmetics.Items.FirstOrDefault();
            Layout(1280,840,96);WaitForImages();
            if(Vm.Cosmetics.Skins.Count>0)Vm.Cosmetics.SelectSkinCommand.Execute(Vm.Cosmetics.Skins.First());
            WaitForImages();Capture("cosmetics-1280",1280,840,96);Capture("cosmetics-1024",1024,660,96);Capture("cosmetics-min-client",1000,610,96);Capture("cosmetics-150dpi",1280,840,144);
            var cosmetics=Desc<CosmeticsView>(Window).Single();
            var tSide=Desc<RadioButton>(cosmetics).Single(r=>Equals(r.Content,"T"));Select(tSide);Check("UIA SelectionItemPattern switches CT/T",Vm.Cosmetics.IsT);
            var knife=Desc<RadioButton>(cosmetics).Single(r=>Equals(r.Content,"刀具"));Select(knife);Check("UIA section selection changes browser",Vm.Cosmetics.IsKnivesSection);
            WaitForImages();Capture("knives",1280,840,96);
            Vm.Cosmetics.Section=CosmeticsSection.Gloves;Vm.Cosmetics.GlovesEnabled=true;WaitForImages();Capture("gloves",1280,840,96);
            Vm.Cosmetics.Section=CosmeticsSection.MusicKit;WaitForImages();Capture("music",1280,840,96);
            Vm.Cosmetics.Section=CosmeticsSection.Weapons;Vm.Cosmetics.IsCt=true;
            Vm.Cosmetics.SelectedItem=Vm.Cosmetics.Items.FirstOrDefault(i=>i.DefIndex==7)??Vm.Cosmetics.Items.First();
            if(Vm.Cosmetics.Skins.Count>0)Vm.Cosmetics.SelectSkinCommand.Execute(Vm.Cosmetics.Skins.First());
            Layout(1280,840,96);
            if(Vm.Cosmetics.DetailEntry is not null)
            {
                var slider=Desc<Slider>(cosmetics).First();((IRangeValueProvider)Peer(slider).GetPattern(PatternInterface.RangeValue)).SetValue(0.123);
                Check("UIA range editing changes draft wear",Math.Abs(Vm.Cosmetics.DetailEntry.Wear-0.123)<0.00001);
            }
            Invoke(ById<Button>("SaveHumanPreset"));Check("save Human draft persists only sandbox preset",!Vm.Manager.IsDirty);
            Invoke(ById<Button>("ApplyHumanPreset"));Check("Human apply requires explicit confirmation and cancellation writes no game file",dialogs.Confirmations>0&&!Directory.EnumerateFiles(game,"*",SearchOption.AllDirectories).Any());
            Navigate("Presets");Capture("presets",1280,840,96);Capture("presets-1024",1024,660,96);
            var presetSearch=Desc<TextBox>(Window).Single(t=>AutomationProperties.GetName(t)=="搜索饰品预设");SetText(presetSearch,"zz-no-preset");Check("preset empty filter",Vm.Presets.EmptyLibrary);Capture("presets-empty",1280,840,96);SetText(presetSearch,"");
            var humanLibrary=ById<ListBox>("HumanPresetLibrary");humanLibrary.SelectedItem=humanLibrary.Items[0];Pump();Invoke(ById<Button>("ActivateHumanPreset"));Check("activation writes a sandbox pointer",File.Exists(Path.Combine(Root,"active.json")));
            Navigate("Bot");Check("install gate preserved",!ById<Button>("ApplyLocalMode").IsEnabled);Capture("bot-install",1280,840,96);Capture("bot-install-1024",1024,660,96);
            var recovery=Desc<Expander>(Window).First(e=>Equals(e.Header,"精确恢复与备份"));((IExpandCollapseProvider)Peer(recovery).GetPattern(PatternInterface.ExpandCollapse)).Expand();Layout(1280,840,96);Check("exact restore gate preserved",!ById<Button>("RestoreBotTransaction").IsEnabled);Capture("bot-restore-details",1280,840,96);
            Navigate("RuntimeStatus");Check("health page does not expose Human save/apply bar",!Vm.Navigation.IsHumanPage);Capture("health",1280,840,96);Capture("health-1024",1024,660,96);
            SelectTab(ById<TabControl>("HealthWorkspaceTabs"),1);Capture("health-evidence",1280,840,96);
            SelectTab(ById<TabControl>("HealthWorkspaceTabs"),2);Capture("recovery",1280,840,96);
            Check("recovery commands remain blocked for a fake game",!ById<Button>("RestoreHumanApplication").IsEnabled&&!ById<Button>("RepairMetaModStartup").IsEnabled);
            var decision=new DecisionWindow("恢复普通人机 + 我的饰品","install ID: synthetic-transaction-001\n备份: sandbox/backups\n\n精确恢复本事务记录的 framework 与 gameinfo，保留 Human 和预设；文件漂移将拒绝恢复。\n\n此图仅展示确认界面，未执行安装或恢复。"){ShowActivated=false,ShowInTaskbar=false};
            CaptureWindow(decision,"confirmation",580,440,96);decision.Close();
            var unsaved=new DecisionWindow("未保存的更改","预设「周末练习」有未保存的修改。\n\n保存并切换：保留修改。\n放弃并切换：放弃当前草稿。\n取消：继续编辑。",unsaved:true){ShowActivated=false,ShowInTaskbar=false};
            CaptureWindow(unsaved,"unsaved-confirmation",580,440,96);unsaved.Close();
            Check("all game-like writes remain inside synthetic game; no real install",!Directory.EnumerateFiles(game,"*",SearchOption.AllDirectories).Any());
            var loading=new MainWindow {ShowActivated=false,ShowInTaskbar=false};
            CaptureWindow(loading,"loading",1280,840,96);loading.Close();
            var missingRoot=Path.Combine(Root,"missing-catalog");
            using(var missing=new AppServices(cs2ModRoot:missingRoot,catalogCacheRoot:Path.Combine(missingRoot,"catalog"),imageCacheRoot:Path.Combine(missingRoot,"images"),csgoDir:Path.Combine(missingRoot,"fake-game/game/csgo"),cs2Root:Path.Combine(missingRoot,"fake-game"),activePresetPath:Path.Combine(missingRoot,"active.json"),presetsRoot:Path.Combine(missingRoot,"presets"),backupsRoot:Path.Combine(missingRoot,"backups"),playerStatePath:Path.Combine(missingRoot,"player.json"),lockPath:Path.Combine(Repo,"runtime/inventory-simulator.lock.json"),imageHandler:new OfflineImages(null),cs2RunningProbe:()=>false))
            {
                var missingVm=new MainViewModel(missing,dialogs);missingVm.Navigation.NavigateCosmeticsCommand.Execute(null);
                var missingWindow=new MainWindow {ShowActivated=false,ShowInTaskbar=false};missingWindow.AttachViewModel(missingVm);
                Check("catalog-unavailable state preserves validation gate",missingVm.CatalogUnavailable&&!missingVm.Manager.Save().Success);
                CaptureWindow(missingWindow,"catalog-unavailable",1024,660,96);missingWindow.Close();
            }
            Check("probe never made its HWND visible",!IsWindowVisible(sourceWindow.Handle));
            Window.Close();app.Shutdown();
            var foregroundAfter=GetForegroundWindow();
            File.WriteAllText(Path.Combine(Output,"report.json"),JsonSerializer.Serialize(new {sandbox=Root,nativeDpi,probeProcessId=Environment.ProcessId,appAssemblyPath=typeof(MainWindow).Assembly.Location,appAssemblySha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))).ToLowerInvariant(),foregroundBefore=beforeForeground.ToInt64(),foregroundProcessBefore,foregroundAfter=foregroundAfter.ToInt64(),foregroundProcessAfter=WindowProcessId(foregroundAfter),windowShowCalled=false,hiddenHwndVisible=IsWindowVisible(sourceWindow.Handle),inputInjectionUsed=false,checks=Checks,captures=Captures,limitations=new[]{"RenderTargetBitmap captures WPF visuals; native popups/compositor/titlebar/physical DPI/input routing need manual acceptance.","UIA providers exercised in process; no claim of cross-process desktop UIA coverage.","A hidden WS_POPUP HWND with WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW hosts the actual visual tree and Loaded lifecycle.","Read-only copied catalog and optional cached official item art; all data mutations are synthetic."}},new JsonSerializerOptions {WriteIndented=true}));
            File.Delete(Path.Combine(Output,"failure.txt"));
            Console.WriteLine($"PASS: {Checks.Count} checks; {Captures.Count} real WPF captures; no visible window or injected input.");return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Console.Error.WriteLine(e);return 1;}
    }
    static void Check(string label,bool valid){if(!valid)throw new InvalidOperationException(label);Checks.Add(label);Console.WriteLine("PASS "+label);}
    static void Navigate(string name)
    {
        Select(ById<RadioButton>("Navigate"+name));Layout(1280,840,96);
        Check("navigation "+name+" has one active page",new FrameworkElement[]{Desc<MatchSetupView>(Window).Single(),Desc<CosmeticsView>(Window).Single(),Desc<PresetsView>(Window).Single(),Desc<BotView>(Window).Single(),Desc<RuntimeStatusView>(Window).Single()}.Count(v=>v.Visibility==Visibility.Visible)==1);
    }
    static AutomationPeer Peer(UIElement e)=>UIElementAutomationPeer.CreatePeerForElement(e)??throw new InvalidOperationException("No UIA peer: "+e.GetType().Name);
    static void Select(UIElement e){((ISelectionItemProvider)Peer(e).GetPattern(PatternInterface.SelectionItem)).Select();Pump();}
    static void Invoke(Button b){Check("enabled action "+(AutomationProperties.GetAutomationId(b) is {Length:>0} id?id:b.Content?.ToString()),b.IsEnabled);((IInvokeProvider)Peer(b).GetPattern(PatternInterface.Invoke)).Invoke();Pump();}
    static void SetText(TextBox t,string text){((IValueProvider)Peer(t).GetPattern(PatternInterface.Value)).SetValue(text);Pump();}
    static void SelectTab(TabControl t,int i){t.UpdateLayout();var peer=Peer(t).GetChildren()[i];var provider=(ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem);provider.Select();Pump();}
    static T ById<T>(string id) where T:DependencyObject=>Desc<T>(Window).Single(e=>AutomationProperties.GetAutomationId(e)==id);
    static IEnumerable<T> Desc<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T t)yield return t;
        if(root is System.Windows.Window w) { foreach(var child in Desc<T>((DependencyObject)w.Content)) yield return child; yield break; }
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Desc<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    static void Pump(){var f=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>f.Continue=false));Dispatcher.PushFrame(f);}
    static void WaitForImages(){var f=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(600)};timer.Tick+=(_,_)=>{timer.Stop();f.Continue=false;};timer.Start();Dispatcher.PushFrame(f);}
    static void Layout(int width,int height,int dpi)=>Arrange(Window,width,height,dpi);
    static FrameworkElement Arrange(System.Windows.Window window,int width,int height,int dpi)
    {
        window.ApplyTemplate();var c=(FrameworkElement)window.Content;c.Width=width;c.Height=height;VisualTreeHelper.SetRootDpi(c,new DpiScale(dpi/96d,dpi/96d));
        for(int i=0;i<3;i++){c.Measure(new Size(width,height));c.Arrange(new Rect(0,0,width,height));c.UpdateLayout();Pump();}
        return c;
    }
    static void Capture(string name,int width,int height,int dpi)=>CaptureWindow(Window,name,width,height,dpi);
    static void CaptureWindow(System.Windows.Window window,string name,int width,int height,int dpi)
    {
        var c=Arrange(window,width,height,dpi);WaitForImages();var bitmap=new RenderTargetBitmap(width*dpi/96,height*dpi/96,dpi,dpi,PixelFormats.Pbgra32);bitmap.Render(c);
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);

        Check("nonblank capture "+name,pixels.Where((_,i)=>i%4==3).Count(a=>a>=250)>bitmap.PixelWidth*bitmap.PixelHeight*0.99&&pixels.Where((_,i)=>i%4!=3).Distinct().Count()>25);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(Output,name+".png"));png.Save(file);
        Captures.Add(new{name,width,height,dpi,pixelsWidth=bitmap.PixelWidth,pixelsHeight=bitmap.PixelHeight});
    }
    static void CopyDirectory(string source,string destination){Directory.CreateDirectory(destination);foreach(var f in Directory.EnumerateFiles(source))File.Copy(f,Path.Combine(destination,Path.GetFileName(f)));foreach(var d in Directory.EnumerateDirectories(source))CopyDirectory(d,Path.Combine(destination,Path.GetFileName(d)));}
    sealed class OfflineImages:HttpMessageHandler
    {
        readonly Dictionary<string,string> _images=new(StringComparer.Ordinal);
        public OfflineImages(string? source)
        {
            if(source is null)return;
            var prefix=Path.GetFullPath(source)+Path.DirectorySeparatorChar;
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"index.json")));
            foreach(var entry in doc.RootElement.GetProperty("images").EnumerateArray())
            {
                var path=Path.GetFullPath(entry.GetProperty("localPath").GetString()!);
                if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))continue;
                _images[entry.GetProperty("sourceUrl").GetString()!]=path;
            }
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)
        {
            var response=_images.TryGetValue(r.RequestUri!.ToString(),out var p)?new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(p))}:new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
    sealed class SilentDialogs:IDialogService
    {
        public UnsavedChangesResolution ConfirmUnsavedChanges(string n)=>UnsavedChangesResolution.Cancel;
        public int Confirmations { get; private set; }
        public bool Confirm(string t,string m) { Confirmations++;return false; }
        public void ShowError(string t,string m)=>Console.WriteLine("DIALOG ERROR: "+t+" "+m);
        public void ShowInfo(string t,string m)=>Console.WriteLine("DIALOG INFO: "+t+" "+m);
        public string? ShowSaveFileDialog(string n,string f)=>null;
        public string? ShowOpenFileDialog(string f)=>null;
    }
}
