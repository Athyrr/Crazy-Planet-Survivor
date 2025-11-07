using Unity.Entities;

public partial class TestUpdateGroup : ComponentSystemGroup
{
    public TestUpdateGroup()
    {
        //RateManager = new RateUtils.VariableRateManager(500, true); // Tick every 500 ms (0.5s)
        RateManager = new RateUtils.VariableRateManager(15, true);
    }
}
