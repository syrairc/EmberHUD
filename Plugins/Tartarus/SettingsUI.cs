using EmberHUD.Ui;

namespace Tartarus;

static class SettingsUI
{
    public static void DrawPanel(Settings s)
    {
        Setting.Checkbox("Show panel", ref s.ShowPanel, "The advice card next to the screen's choice");
        Setting.Checkbox("Show assumed", ref s.ShowAssumed, "Lists what the advice had to guess, such as tool uses left or events behind unseen cells");
        Setting.Checkbox("Lock", ref s.Lock, "A locked panel ignores the mouse - hold the move key to drag it anyway");
        Setting.Hotkey("Move key", ref s.MoveKey, "Hold to move the panel while locked");
        Setting.Checkbox("Highlight cards", ref s.HighlightCards, "Glows the best card on the game's own decision cards and instrument pick instead of a row in the panel");
    }

    public static void DrawAdvice(Settings s)
    {
        Setting.Slider("Playouts", ref s.Playouts, 20, 2000, "Simulated runs per option, more is steadier but slower to appear");
    }

    public static void DrawRules(Settings s)
    {
        Setting.Slider("Chromatic cap", ref s.ChromaticCap, 1, 30, "Chromatic inmates allowed per run, past it nothing reaches Chromatic");
        Setting.Checkbox("Eater keeps its beast", ref s.EaterKeepsBeast, "Off: an inmate that eats and rises comes back as a random beast. On: it stays the same beast");
        Setting.Slider("Second meal from tier", ref s.SecondMealFromTier, 0, 5, "Mammoths and lions can eat two beasts down from this tier up");
        Setting.Checkbox("Replace is Execute", ref s.ReplaceIsExecute, "Calls the once-a-day swap Execute on the card, it acts the same either way");
    }
}
