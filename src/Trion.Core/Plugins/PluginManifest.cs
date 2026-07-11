using System.Reflection;

namespace Trion.Core.Plugins;

public sealed record PluginManifest
{
    public required string   Name         { get; init; }
    public required string   Version      { get; init; }
    public required string   Description  { get; init; }
    public required string   Author       { get; init; }
    public required string   AssemblyPath { get; init; }
    public required Assembly Assembly     { get; init; }
    public required string   Hash         { get; init; }
    public          bool     IsApproved   { get; init; }
    public          string?  LoadError    { get; init; }
}
