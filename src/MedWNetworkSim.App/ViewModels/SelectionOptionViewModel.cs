namespace MedWNetworkSim.App.ViewModels;

public sealed record SelectionOptionViewModel<T>(T Value, string Label) where T : struct, Enum;
