using AstraGraph.Core;
using AstraGraph.Runtime.Network;
using Robust.Shared.Timing;

namespace AstraGraph.Robust.Shared;

/// <summary>
/// Drives <see cref="PredictionReconciler"/> from Robust's prediction tick.
/// </summary>
public sealed class RobustPredictionAdapter
{
    private readonly IGameTiming _timing;
    private readonly PredictionReconciler _reconciler;

    public RobustPredictionAdapter(IGameTiming timing, PredictionReconciler reconciler)
    {
        _timing = timing ?? throw new ArgumentNullException(nameof(timing));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
    }

    public int CurrentTick => (int)_timing.CurTick.Value;

    public bool InPrediction => _timing.InPrediction;

    public void Predict(int entityUid, SchemaId schemaId, AstraValue[] values)
    {
        PredictAt(CurrentTick, entityUid, schemaId, values);
    }

    public void PredictAt(int tick, int entityUid, SchemaId schemaId, AstraValue[] values)
    {
        if (_timing.InPrediction && !_timing.IsFirstTimePredicted)
        {
            return;
        }

        _reconciler.RecordPredictedState(tick, entityUid, schemaId, values);
    }

    public ReconciliationResult ApplyAuthoritative(int serverTick, int entityUid, SchemaId schemaId, AstraValue[] values)
    {
        return _reconciler.ReconcileServerState(serverTick, entityUid, schemaId, values);
    }

    public void Replay(int entityUid, SchemaId schemaId, Func<int, AstraValue[]> resimulate)
    {
        _reconciler.Replay(entityUid, schemaId, resimulate);
    }
}
