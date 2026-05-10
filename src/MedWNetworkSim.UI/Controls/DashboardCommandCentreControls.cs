using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using MedWNetworkSim.Presentation;

namespace MedWNetworkSim.UI.Controls;

internal sealed class KpiCard : Border
{
    public KpiCard(string label, string value, string detail, Color accent)
    {
        Background = new SolidColorBrush(AvaloniaDashboardTheme.PanelHeaderBackground);
        BorderBrush = new SolidColorBrush(accent, 0.45);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(12, 10);
        Child = new StackPanel { Spacing = 3, Children = { new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText) }, new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText) }, new TextBlock { Text = detail, FontSize = 11, Foreground = new SolidColorBrush(AvaloniaDashboardTheme.MutedText), TextWrapping = TextWrapping.Wrap } } };
    }
}

internal static class DashboardUiFormat
{
    public static string Compact(double value) => Math.Abs(value) >= 1000d ? value.ToString("0,.#K", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
    public static Border SeverityBadge(string text, Color color) => new()
    {
        Background = new SolidColorBrush(color, 0.18), BorderBrush = new SolidColorBrush(color, 0.65), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3),
        Child = new TextBlock { Text = text, Foreground = new SolidColorBrush(color), FontSize = 11, FontWeight = FontWeight.SemiBold }
    };
}

internal sealed class HealthScoreCard : Border
{
    private readonly WorkspaceViewModel _vm;
    private readonly PropertyChangedEventHandler _handler;

    public HealthScoreCard(WorkspaceViewModel vm)
    {
        _vm = vm;
        var content = new StackPanel { Spacing = 10 };
        Child = content; Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground); BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorderStrong); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(14); Padding = new Thickness(14);
        void Rebuild(){ content.Children.Clear(); var s=vm.NetworkHealthSummary; var c=s.HealthScore>=85?AvaloniaDashboardTheme.Success:s.HealthScore>=60?AvaloniaDashboardTheme.Warning:AvaloniaDashboardTheme.Danger; content.Children.Add(new TextBlock{Text="Network Health",FontSize=16,FontWeight=FontWeight.SemiBold,Foreground=new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)}); content.Children.Add(new TextBlock{Text=$"{s.HealthScore:0}%",FontSize=36,FontWeight=FontWeight.Bold,Foreground=new SolidColorBrush(c)}); content.Children.Add(new ProgressBar{Minimum=0,Maximum=100,Value=s.HealthScore,Height=7,Foreground=new SolidColorBrush(c)}); var strip=new UniformGrid{Columns=3,Rows=1}; strip.Children.Add(new KpiCard("Demand",DashboardUiFormat.Compact(s.TotalDemand),"Total",AvaloniaDashboardTheme.Accent)); strip.Children.Add(new KpiCard("Served",DashboardUiFormat.Compact(s.TotalServed),"Delivered",AvaloniaDashboardTheme.Success)); strip.Children.Add(new KpiCard("Unmet",DashboardUiFormat.Compact(s.TotalUnmet),"Outstanding",AvaloniaDashboardTheme.Danger)); content.Children.Add(strip); var wp=new WrapPanel(); wp.Children.Add(DashboardUiFormat.SeverityBadge($"{s.CriticalIssueCount} critical",AvaloniaDashboardTheme.Danger)); wp.Children.Add(DashboardUiFormat.SeverityBadge($"{s.WarningIssueCount} warnings",AvaloniaDashboardTheme.Warning)); content.Children.Add(wp); }
        _handler = (_,e)=>{ if(e.PropertyName==nameof(WorkspaceViewModel.NetworkHealthSummary)) Rebuild();};
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm.PropertyChanged += _handler;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm.PropertyChanged -= _handler;
    }
}

internal sealed class BottleneckLeaderboard : Border
{
    private readonly WorkspaceViewModel _vm;
    private readonly PropertyChangedEventHandler _handler;

    public BottleneckLeaderboard(WorkspaceViewModel vm)
    {
        _vm = vm;
        var stack=new StackPanel{Spacing=8}; Child=stack; Background=new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground); BorderBrush=new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder); BorderThickness=new Thickness(1); CornerRadius=new CornerRadius(14); Padding=new Thickness(12);
        void Rebuild(){ stack.Children.Clear(); stack.Children.Add(new TextBlock{Text="Bottleneck Leaderboard",Foreground=new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),FontWeight=FontWeight.SemiBold}); if(vm.Bottlenecks.Count==0){ stack.Children.Add(new TextBlock{Text="No bottlenecks detected for current filters.",Foreground=new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)}); return;} foreach(var b in vm.Bottlenecks.Take(8)){ var tint=b.SeverityScore>=0.8?AvaloniaDashboardTheme.Danger:b.SeverityScore>=0.55?AvaloniaDashboardTheme.Warning:AvaloniaDashboardTheme.Accent; var badge=DashboardUiFormat.SeverityBadge(string.IsNullOrWhiteSpace(b.Badge)?"Watch":b.Badge,tint); var row=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Children={new StackPanel{Children={new TextBlock{Text=b.Label,Foreground=new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),FontWeight=FontWeight.SemiBold},new TextBlock{Text=$"{b.Kind} • Severity {b.SeverityScore:0.00}",Foreground=new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),FontSize=11}}},badge}}; Grid.SetColumn(badge,1); stack.Children.Add(new Border{Background=new SolidColorBrush(tint,0.14),BorderBrush=new SolidColorBrush(tint,0.45),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10,8),Child=row}); }}
        _handler = (_,e)=>{ if(e.PropertyName==nameof(WorkspaceViewModel.Bottlenecks)) Rebuild();};
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm.PropertyChanged += _handler;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm.PropertyChanged -= _handler;
    }
}

internal sealed class InsightRail : Border
{
    private readonly WorkspaceViewModel _vm;
    private readonly PropertyChangedEventHandler _handler;

    public InsightRail(WorkspaceViewModel vm)
    {
        _vm = vm;
        var items = new StackPanel { Spacing = 8 };
        Child = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground); BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(14); Padding = new Thickness(12);
        void Rebuild(){ items.Children.Clear(); items.Children.Add(new TextBlock{Text="Insight Rail",Foreground=new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),FontWeight=FontWeight.SemiBold}); if(vm.InsightCards.Count==0){ items.Children.Add(new TextBlock{Text="No insights available yet.",Foreground=new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)}); return;} foreach(var i in vm.InsightCards.Take(6)){ var c=i.Severity.Contains("critical",StringComparison.OrdinalIgnoreCase)?AvaloniaDashboardTheme.Danger:i.Severity.Contains("warn",StringComparison.OrdinalIgnoreCase)?AvaloniaDashboardTheme.Warning:AvaloniaDashboardTheme.Accent; items.Children.Add(new Border{Background=new SolidColorBrush(c,0.1),BorderBrush=new SolidColorBrush(c,0.5),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10),Child=new StackPanel{Spacing=4,Children={new TextBlock{Text=i.Title,FontWeight=FontWeight.SemiBold,Foreground=new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText)},DashboardUiFormat.SeverityBadge(i.Severity,c),new TextBlock{Text=i.Summary,Foreground=new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText),TextWrapping=TextWrapping.Wrap},new TextBlock{Text=i.Evidence,FontSize=11,Foreground=new SolidColorBrush(AvaloniaDashboardTheme.MutedText),TextWrapping=TextWrapping.Wrap}}}}); }}
        _handler = (_,e)=>{ if(e.PropertyName==nameof(WorkspaceViewModel.InsightCards)) Rebuild();};
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm.PropertyChanged += _handler;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm.PropertyChanged -= _handler;
    }
}

internal sealed class TimelineStrip : Border
{
    private readonly WorkspaceViewModel _vm;
    private readonly PropertyChangedEventHandler _handler;

    public TimelineStrip(WorkspaceViewModel vm)
    {
        _vm = vm;
        var rows = new StackPanel { Spacing = 8 };
        Child = rows; Background = new SolidColorBrush(AvaloniaDashboardTheme.ChromeBackground); BorderBrush = new SolidColorBrush(AvaloniaDashboardTheme.PanelBorder); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(14); Padding = new Thickness(12);
        void Rebuild(){ rows.Children.Clear(); rows.Children.Add(new TextBlock{Text="Timeline Strip",Foreground=new SolidColorBrush(AvaloniaDashboardTheme.PrimaryText),FontWeight=FontWeight.SemiBold}); var points=vm.TimelineMetrics.TakeLast(30).ToList(); if(points.Count==0){ rows.Children.Add(new TextBlock{Text="No timeline metrics for this selection.",Foreground=new SolidColorBrush(AvaloniaDashboardTheme.SecondaryText)}); return;} var max=Math.Max(1d,points.Max(p=>Math.Max(p.ServedDemand,p.UnmetDemand))); foreach(var p in points){ var bar=new Border{Height=8,Background=new SolidColorBrush(AvaloniaDashboardTheme.Success,0.2+0.7*(p.ServedDemand/max)),CornerRadius=new CornerRadius(4),HorizontalAlignment=HorizontalAlignment.Stretch}; var unmet=p.UnmetDemand/max; var txt=new TextBlock{Text=$"{p.UnmetDemand:0.#} unmet",Foreground=new SolidColorBrush(unmet>0.5?AvaloniaDashboardTheme.Danger:AvaloniaDashboardTheme.SecondaryText),FontSize=11,HorizontalAlignment=HorizontalAlignment.Right}; var g=new Grid{ColumnDefinitions=new ColumnDefinitions("52,*,Auto"),Children={new TextBlock{Text=$"T{p.Period}",Foreground=new SolidColorBrush(AvaloniaDashboardTheme.MutedText),FontSize=11},bar,txt}}; Grid.SetColumn(bar,1); Grid.SetColumn(txt,2); rows.Children.Add(g);} }
        _handler = (_,e)=>{ if(e.PropertyName==nameof(WorkspaceViewModel.TimelineMetrics)) Rebuild();};
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm.PropertyChanged += _handler;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm.PropertyChanged -= _handler;
    }
}
