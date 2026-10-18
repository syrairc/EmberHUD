namespace Tartarus.Sim;

public sealed partial class Run
{
    void EndDay()
    {
        Chaining = -1;
        if (Day >= MaxDay)
        {
            Phase = Phase.Ended;
            Queue.Clear();
            return;
        }
        Day++;
        Consumed = Replaced = Hunted = false;
        Morning();
        SpawnEvent(Rules.EventMorningChance);
        Phase = Phase.Patrol;
        RollDoors();
    }
}
