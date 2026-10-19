using System.Diagnostics.CodeAnalysis;

namespace Gridline.Core.Commands;

/// <summary>
/// Bounded undo/redo stacks with dirty tracking (§9.3). A history position is the Id of the top undo step, or
/// <see cref="BaseId"/> when nothing can be undone. Step Ids must be unique and increasing.
/// </summary>
internal sealed class UndoHistory(int limit)
{
    private readonly List<EditStep> _undo = [];     // oldest first
    private readonly List<EditStep> _redo = [];     // top (next to redo) last
    private bool _savedPositionLost;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The position reached by undoing everything: 0, or the Id of the most recently dropped step.</summary>
    public long BaseId { get; private set; }

    public long CurrentPosition => _undo.Count > 0 ? _undo[^1].Id : BaseId;

    public long SavedPosition { get; private set; }

    public bool IsDirty => CurrentPosition != SavedPosition || _savedPositionLost;

    /// <summary>Records a new step: clears redo, then drops the oldest step past the limit.</summary>
    public void Push(EditStep s)
    {
        foreach (var r in _redo)
            if (r.Id == SavedPosition) _savedPositionLost = true;
        _redo.Clear();

        _undo.Add(s);
        if (_undo.Count > limit)
        {
            BaseId = _undo[0].Id;
            _undo.RemoveAt(0);
            if (SavedPosition != BaseId && !OnStacks(SavedPosition)) _savedPositionLost = true;
        }
    }

    public bool TryUndo([MaybeNullWhen(false)] out EditStep s)
    {
        if (_undo.Count == 0) { s = null; return false; }
        s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(s);
        return true;
    }

    public bool TryRedo([MaybeNullWhen(false)] out EditStep s)
    {
        if (_redo.Count == 0) { s = null; return false; }
        s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(s);
        return true;
    }

    public void MarkSaved()
    {
        SavedPosition = CurrentPosition;
        _savedPositionLost = false;
    }

    private bool OnStacks(long id)
    {
        foreach (var s in _undo) if (s.Id == id) return true;
        foreach (var s in _redo) if (s.Id == id) return true;
        return false;
    }
}
