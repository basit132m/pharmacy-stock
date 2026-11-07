namespace PharmacyStock.Api;

public sealed record SetLevelRequest(decimal LevelPacks, bool IsManaged = true, string? PreferredSupplier = null, string? Notes = null);
public sealed record SetManagedRequest(bool IsManaged);
public sealed record SuggestRequest(string Scope = "missing");
public sealed record SaveOrderRequest(string ItemCode, decimal QtyPacks, string Status = "Pending", string? Supplier = null, string? Notes = null, DateTime? ExpectedAt = null, long? Id = null);
public sealed record SnoozeRequest(int Hours = 24);
public sealed record DemoQtyRequest(string ItemCode, decimal Packs);
public sealed record ActionResult(bool Ok, string Message, int Changed = 0);
