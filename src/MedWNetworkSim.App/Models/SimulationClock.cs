namespace MedWNetworkSim.App.Models;

public sealed class SimulationClock
{
    public double CurrentTime { get; private set; }

    public double DeltaTime { get; set; } = 1.0;

    public void Reset()
    {
        CurrentTime = 0d;
    }

    public void Advance()
    {
        Advance(DeltaTime);
    }

    public void Advance(double deltaTime)
    {
        if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime) || deltaTime <= 0d)
        {
            deltaTime = DeltaTime > 0d ? DeltaTime : 1d;
        }

        CurrentTime += deltaTime;
    }
}
