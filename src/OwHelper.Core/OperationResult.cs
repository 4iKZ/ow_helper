namespace OwHelper.Core;

public sealed record OperationResult(
    string Name,
    bool Success,
    int? NativeError,
    string? Message);
