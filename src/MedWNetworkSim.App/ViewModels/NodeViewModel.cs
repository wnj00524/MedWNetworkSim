using System.Collections.ObjectModel;
using MedWNetworkSim.App.Models;

namespace MedWNetworkSim.App.ViewModels;

public sealed class NodeViewModel : ObservableObject
{
    public const double DefaultWidth = 176d;
    public const double DefaultHeight = 118d;

    private double x;
    private double y;

    public NodeViewModel(NodeModel model)
    {
        Id = model.Id;
        Name = model.Name;
        x = model.X ?? 0d;
        y = model.Y ?? 0d;
        TrafficProfiles = new ObservableCollection<NodeTrafficProfileViewModel>(
            model.TrafficProfiles.Select(profile => new NodeTrafficProfileViewModel(profile)));
    }

    public event EventHandler? PositionChanged;

    public string Id { get; }

    public string Name { get; }

    public double Width => DefaultWidth;

    public double Height => DefaultHeight;

    public double X
    {
        get => x;
        private set
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
        private set
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

    public ObservableCollection<NodeTrafficProfileViewModel> TrafficProfiles { get; }

    public string TrafficProfileCountLabel => TrafficProfiles.Count switch
    {
        1 => "1 traffic type",
        _ => $"{TrafficProfiles.Count} traffic types"
    };

    public string FullTrafficSummary => string.Join(
        Environment.NewLine,
        TrafficProfiles.Select(profile => $"{profile.TrafficType}: {profile.RoleSummary}"));

    public void MoveBy(double deltaX, double deltaY)
    {
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
}
