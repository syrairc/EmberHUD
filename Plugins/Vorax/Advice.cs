using Vorax.Sim;

namespace Vorax;

public enum AdviceSource { Simulator, Strategy, SimulatorFallback }

// Expected is the engine simulator's mean final Activity for the pick shown, whoever made the pick
public sealed record Advice(Stage Stage, AdviceSource Source, string StrategyName, Pick Pick, double[]? Values, double? RerollValue, string? Error, double? Expected = null);
