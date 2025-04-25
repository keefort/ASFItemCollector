using System.Text.Json.Serialization;

namespace ASFItemCollector.Data.Plugin;

[method: JsonConstructor]
public sealed class PluginConfig(IReadOnlyCollection<App> apps, bool enabled = false, uint dropCheckInterval = 10)
{
	[JsonInclude]
	[JsonPropertyName("Apps")]
	public IReadOnlyCollection<App> Apps { get; private set; } = apps ?? throw new ArgumentNullException(nameof(apps));

	[JsonInclude]
	[JsonPropertyName("Enabled")]
	public bool Enabled { get; private set; } = enabled;

	[JsonInclude]
	[JsonPropertyName("DropCheckInterval")]
	public uint DropCheckInterval { get; private set; } = dropCheckInterval;
}