namespace ElrsTtlBatchFlasher.Models;

public sealed class ReceiverSegment
{
    public string Label { get; set; } = "";
    public string Offset { get; set; } = "";
    public string Size { get; set; } = "";
    public bool Required { get; set; } = true;

    public override string ToString() => $"{Label} @ {Offset} ({Size})";
}

public sealed class ReceiverProfile
{
    public string Name { get; set; } = "";
    public string Chip { get; set; } = "auto";
    public List<ReceiverSegment> Segments { get; set; } = new();

    public override string ToString() => Name;
}
