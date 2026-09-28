namespace EndfieldAicWeb.Domain.Validation;

/// <summary>検証違反 1 件。EntityKind は "Item"/"Facility"/"Environment"/"Recipe"/"GameEvent" 等。</summary>
public sealed record MasterValidationError(
    string EntityKind,
    string EntityId,
    string? Field,
    string Message);
