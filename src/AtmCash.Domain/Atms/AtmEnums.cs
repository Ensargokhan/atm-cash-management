namespace AtmCash.Domain.Atms;

/// <summary>
/// UNCONFIGURED → CONFIGURED → LOADED → IN_SERVICE ⇄ MAINTENANCE
/// </summary>
public enum AtmStatus
{
    Unconfigured = 1,
    Configured = 2,
    Loaded = 3,
    InService = 4,
    Maintenance = 5
}

public enum CassetteType
{
    /// <summary>Accepts any currency and denomination; cash is never dispensed from it.</summary>
    Deposit = 1,

    /// <summary>Accepts and dispenses a single denomination of a single currency.</summary>
    Recycling = 2
}
