namespace FODevManager.Operations;

/// <summary>A detached snapshot; Data is a redacted JsonElement when present</summary>
public sealed record OperationResult
{
    public required string OperationId { get; init; }
    public string? RequestId { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public object? Data { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public int DroppedDiagnosticCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public bool PartialChangesPossible { get; init; }
}
