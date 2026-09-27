namespace CompanionDesktopPet.Services;

internal readonly record struct VoiceTurn(string Text, string? Tone, string? Trigger, bool Urgent = false);

internal readonly record struct VoiceClip(string Text, string Path);

/// <summary>
/// One synthesis at a time, one finished clip waiting, and a short queue behind them.
/// A newer click replaces lines that have not started. The line already on the GPU runs to the end.
/// </summary>
internal sealed class VoiceTurnQueue
{
    public const int Capacity = 8;

    private readonly Queue<VoiceTurn> _pending = new();
    private VoiceTurn? _synthesizing;
    private VoiceClip? _buffered;
    private bool _playing;

    public bool IsIdle =>
        _synthesizing is null && _buffered is null && !_playing && _pending.Count == 0;

    public bool IsSynthesizing => _synthesizing is not null;

    public string? SynthesizingText => _synthesizing?.Text;

    public void Enqueue(VoiceTurn turn)
    {
        if (turn.Urgent)
        {
            _pending.Clear();
            _buffered = null;
            _playing = false;
        }

        while (_pending.Count >= Capacity)
        {
            _pending.Dequeue();
        }

        _pending.Enqueue(turn);
    }

    public void EnqueueFront(VoiceTurn turn)
    {
        var pending = _pending.ToArray();
        var urgent = pending.Where(item => item.Urgent).ToArray();
        var rest = pending.Where(item => !item.Urgent).ToArray();
        _pending.Clear();
        if (turn.Urgent)
        {
            EnqueueLimited(turn);
        }

        foreach (var item in urgent)
        {
            EnqueueLimited(item);
        }

        if (!turn.Urgent)
        {
            EnqueueLimited(turn);
        }

        foreach (var item in rest)
        {
            EnqueueLimited(item);
        }
    }

    private void EnqueueLimited(VoiceTurn turn)
    {
        if (_pending.Count < Capacity)
        {
            _pending.Enqueue(turn);
        }
    }

    public VoiceTurn? TryStartSynthesis()
    {
        if (_synthesizing is not null || _buffered is not null || _pending.Count == 0)
        {
            return null;
        }

        _synthesizing = _pending.Dequeue();
        return _synthesizing;
    }

    public bool CompleteSynthesis(string path)
    {
        var text = _synthesizing?.Text ?? string.Empty;
        _synthesizing = null;
        if (_pending.Count > 0 && _pending.Peek().Urgent)
        {
            return false;
        }

        _buffered = new VoiceClip(text, path);
        return true;
    }

    public VoiceClip? TryStartPlayback()
    {
        if (_playing || _buffered is null)
        {
            return null;
        }

        _playing = true;
        var clip = _buffered.Value;
        _buffered = null;
        return clip;
    }

    public void CompletePlayback() => _playing = false;

    public VoiceTurn? FailSynthesis()
    {
        var turn = _synthesizing;
        _synthesizing = null;
        return turn;
    }

    public void Clear()
    {
        _pending.Clear();
        _synthesizing = null;
        _buffered = null;
        _playing = false;
    }
}
