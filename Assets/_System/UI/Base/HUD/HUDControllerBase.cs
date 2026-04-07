public abstract class HUDControllerBase<TView> : UIControllerBase where TView : UIViewBase
{
    public abstract void Show();
    public abstract void Hide();
}