namespace Game.Unit.Activity
{
    public interface IActivity
    {
        bool IsFinished();
        // Called each frame (or tick) to update the activity.
        void Tick();
    }
}
