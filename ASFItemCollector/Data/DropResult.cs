using System.Text.Json.Serialization;

namespace ASFItemCollector.Data;

public class DropResult
{
	[JsonPropertyName("accountid")]
	public required string AccountId { get; set; }

	[JsonPropertyName("appid")]
	public int AppId { get; set; }

	[JsonPropertyName("itemid")]
	public required string ItemId { get; set; }

	[JsonPropertyName("originalitemid")]
	public required string OriginalItemId { get; set; }

	[JsonPropertyName("quantity")]
	public int Quantity { get; set; }

	[JsonPropertyName("itemdefid")]
	public required string ItemDefId { get; set; }

	[JsonPropertyName("acquired")]
	public required string Acquired { get; set; }

	[JsonPropertyName("state")]
	public required string State { get; set; }

	[JsonPropertyName("origin")]
	public required string Origin { get; set; }

	[JsonPropertyName("state_changed_timestamp")]
	public required string StateChangedTimestamp { get; set; }

	[JsonIgnore]
	public DateTime AcquiredTime => DateTime.ParseExact(Acquired, "yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

	[JsonIgnore]
	public DateTime StateChangedTime => DateTime.ParseExact(StateChangedTimestamp, "yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
}