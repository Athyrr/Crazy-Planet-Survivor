using Unity.Entities;

public partial class CustomUpdateGroup : ComponentSystemGroup
{
    public CustomUpdateGroup()
    {
        // AI "decision" cadence (~30 Hz). Only expensive, staleness-tolerant systems that do NOT write
        // render transforms belong here (avoidance steering, flow-field grid, enemy targeting). Movement
        // is applied per-frame elsewhere using the latest decision output, so lowering this rate costs
        // behavioral responsiveness (repath/spacing lag), never visual smoothness.
        // 66 Hz (15 ms) was effectively no cap at 60 fps; ~30 Hz roughly halves the steering cost.
        // Note: Avoidance and FlowField share this single manager — split into separate groups if they
        // ever need different rates. Profile on target hardware before tuning further.
        RateManager = new RateUtils.VariableRateManager(33, true); // Tick every 33 ms (~30 Hz)
    }
}
