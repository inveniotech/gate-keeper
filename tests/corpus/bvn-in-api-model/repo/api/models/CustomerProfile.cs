namespace LedgerOps.Api.Models;

public sealed record CustomerProfile(
    string Id,
    string FullName,
    string Bvn)
{
    public static CustomerProfile Demo() =>
        new("demo-0001", "Demo Customer", "59217260258");
}
