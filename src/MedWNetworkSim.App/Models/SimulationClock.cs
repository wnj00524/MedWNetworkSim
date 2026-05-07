namespace MedWNetworkSim.App.Models;
/// <summary>
/// Represents the simulation clock component.
/// </summary>

public sealed class SimulationClock
{
    /// <summary>
    /// Gets or sets the current time.
    /// </summary>
    public double CurrentTime { get; private set; }
    /// <summary>
    /// Gets or sets the delta time.
    /// </summary>

    public double DeltaTime { get; set; } = 1.0;
    /// <summary>
    /// Executes the reset operation.
    /// </summary>

    public void Reset()
    {
        CurrentTime = 0d;
    }
    /// <summary>
    /// Executes the advance operation.
    /// </summary>

    public void Advance()
    {
        Advance(DeltaTime);
    }
    /// <summary>
    /// Executes the advance operation.
    /// </summary>

    public void Advance(double deltaTime)
    {
        if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime) || deltaTime <= 0d)
        {
            deltaTime = DeltaTime > 0d ? DeltaTime : 1d;
        }

        CurrentTime += deltaTime;
    }
}
