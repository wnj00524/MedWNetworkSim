using System.Collections.ObjectModel;
using System.Collections.Specialized;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeViewModel : ObservableObject
{
    public const double DefaultWidth = 176d;
    public const double DefaultHeight = 118d;

    private string id;
    private string name;
    private double x;
    private double y;
    private double? transhipmentCapacity;

    public NodeViewModel(NodeModel model)
    {
        id = model.Id;
        name = model.Name;
        x = model.X ?? 0d;
        y = model.Y ?? 0d;
        transhipmentCapacity = model.TranshipmentCapacity;
        TrafficProfiles = new ObservableCollection<NodeTrafficProfileViewModel>(
            model.TrafficProfiles.Select(profile => new NodeTrafficProfileViewModel(profile)));
        TrafficProfiles.CollectionChanged += HandleTrafficProfilesChanged;

        // Bubble traffic-profile edits up as node-definition changes so the rest of the UI can refresh once.
        foreach (var profile in TrafficProfiles)
        {
            profile.PropertyChanged += HandleTrafficProfilePropertyChanged;
        }
    }

    public event EventHandler? PositionChanged;

    public event EventHandler? DefinitionChanged;

    public event EventHandler<ValueChangedEventArgs<string>>? IdChanged;

    public string Id
    {
        get => id;
        set
        {
            var oldValue = id;
            if (!SetProperty(ref id, value))
            {
                return;
            }

            IdChanged?.Invoke(this, new ValueChangedEventArgs<string>(oldValue, value));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (!SetProperty(ref name, value))
            {
                return;
            }

            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Width => DefaultWidth;

    public double Height => DefaultHeight;

    public double X
    {
        get => x;
        set
        {
            if (!SetProperty(ref x, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(CenterX));
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Y
    {
        get => y;
        set
        {
            if (!SetProperty(ref y, value))
            {
                return;
            }

            OnPropertyChanged(nameof(Top));
            OnPropertyChanged(nameof(CenterY));
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double Left => X - (Width / 2d);

    public double Top => Y - (Height / 2d);

    public double CenterX => X;

    public double CenterY => Y;

    public double? TranshipmentCapacity
    {
        get => transhipmentCapacity;
        set
        {
            if (!SetProperty(ref transhipmentCapacity, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TranshipmentCapacityLabel));
            OnPropertyChanged(nameof(FullTrafficSummary));
            DefinitionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObservableCollection<NodeTrafficProfileViewModel> TrafficProfiles { get; }

    public string TrafficProfileCountLabel => TrafficProfiles.Count switch
    {
        1 => "1 traffic type",
        _ => $"{TrafficProfiles.Count} traffic types"
    };

    public string TranshipmentCapacityLabel => TranshipmentCapacity.HasValue
        ? $"trans cap {TranshipmentCapacity.Value:0.##}"
        : "trans cap inf";

    public string FullTrafficSummary =>
        string.Join(
            Environment.NewLine,
            new[] { $"Transhipment Capacity: {(TranshipmentCapacity.HasValue ? TranshipmentCapacity.Value.ToString("0.##") : "Unlimited")}" }
                .Concat(TrafficProfiles.Select(profile => $"{profile.TrafficType}: {profile.RoleSummary}")));

    public void AddTrafficProfile(NodeTrafficProfileViewModel profile)
    {
        TrafficProfiles.Add(profile);
    }

    public void RemoveTrafficProfile(NodeTrafficProfileViewModel profile)
    {
        TrafficProfiles.Remove(profile);
    }

    public void MoveBy(double deltaX, double deltaY)
    {
        // Keep the node on the positive canvas while preserving drag semantics from the node center.
        X = Math.Max(Width / 2d, X + deltaX);
        Y = Math.Max(Height / 2d, Y + deltaY);
    }

    public NodeModel ToModel()
    {
        return new NodeModel
        {
            Id = Id,
            Name = Name,
            X = X,
            Y = Y,
            TranshipmentCapacity = TranshipmentCapacity,
            TrafficProfiles = TrafficProfiles
                .Select(profile => new NodeTrafficProfile
                {
                    TrafficType = profile.TrafficType,
                    Production = profile.Production,
                    Consumption = profile.Consumption,
                    CanTransship = profile.CanTransship
                })
                .ToList()
        };
    }

    private void HandleTrafficProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var profile in e.NewItems?.OfType<NodeTrafficProfileViewModel>() ?? [])
        {
            profile.PropertyChanged += HandleTrafficProfilePropertyChanged;
        }

        foreach (var profile in e.OldItems?.OfType<NodeTrafficProfileViewModel>() ?? [])
        {
            profile.PropertyChanged -= HandleTrafficProfilePropertyChanged;
        }

        OnPropertyChanged(nameof(TrafficProfileCountLabel));
        OnPropertyChanged(nameof(FullTrafficSummary));
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleTrafficProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FullTrafficSummary));
        DefinitionChanged?.Invoke(this, EventArgs.Empty);
    }
}
