namespace AstraGraph.Core;

/// <summary>
/// What is allowed to invoke a compiled entry point.
/// Update is the only trigger the tick scheduler runs.
/// </summary>
public enum EntryPointTrigger
{
    Startup,
    Update,
    NativeEvent,
    AstraEvent,
    Function,
    UIAction
}
