using Unity.Entities;

public partial class CustomUpdateGroup : ComponentSystemGroup
{
    public CustomUpdateGroup()
    {
        //RateManager = new RateUtils.VariableRateManager(500, true); // Tick every 500 ms (0.5s)
       RateManager = new RateUtils.VariableRateManager(10, true);
    }
}
