namespace Trion.Core.Services.Infrastructure;

public enum InfraServiceState { Online, Offline }

public sealed record InfraStatus(
    string           Name,
    string           Detail,
    InfraServiceState State);
