using System.Text.Json;
using System.Timers;

using ArchiSteamFarm.Core;
using ArchiSteamFarm.NLog;

using ASFItemCollector.Data;
using ASFItemCollector.Data.Plugin;

using SteamKit2;
using SteamKit2.Internal;

namespace ASFItemCollector.Handlers;

public sealed class ItemDropHandler(PluginConfig config, ArchiLogger logger) : ClientMsgHandler, IDisposable
{
	private readonly PluginConfig _config = config ?? throw new ArgumentNullException(nameof(config));
	private readonly ArchiLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	private System.Timers.Timer? _dropCheckTimer;

	private Inventory? _inventoryService;
	private SteamUnifiedMessages? _steamUnifiedMessages;

	public bool IsRunning => _dropCheckTimer?.Enabled ?? false;

	public void Dispose()
	{
		if (_dropCheckTimer is not null)
		{
			_dropCheckTimer.Elapsed -= TimerCallback;
			_dropCheckTimer.Dispose();
		}
	}

	public override void HandleMsg(IPacketMsg packetMsg)
	{
		if (packetMsg is null)
			ASF.ArchiLogger.LogNullError(nameof(packetMsg));
	}

	private async void TimerCallback(object? state, ElapsedEventArgs e)
	{
		_logger.LogGenericDebug("Timer callback started execution");

		try
		{
			await SetPlayingStatus().ConfigureAwait(false);

			var uncheckedItems = new List<uint>();
			foreach (var app in _config.Apps)
				foreach (var item in app.Items)
				{
					var drop = await CheckItemDrop(app.AppId, item).ConfigureAwait(false);

					if (drop is not null)
					{
						uncheckedItems = [.. app.Items.SkipWhile(x => x != item).Skip(1)];

						if (uncheckedItems.Count != 0)
							_logger.LogGenericDebug($"Skipped itemdefids for appid {app.AppId}: {string.Join(", ", uncheckedItems)}");

						break;
					}

					_logger.LogGenericDebug("Wait 5 seconds before next request");
					await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
				}
		}
		catch (Exception ex)
		{
			_logger.LogGenericException(ex, "Failed to execute timer callback");
		}

		finally
		{
			_logger.LogGenericDebug("Timer callback finished execution");
		}
	}

	public async Task StartIdling()
	{
		if (IsRunning)
			throw new InvalidOperationException("Item drop collection is already active");

		if (_dropCheckTimer is null)
		{
			_dropCheckTimer = new System.Timers.Timer(_config.DropCheckInterval * 60000)
			{
				AutoReset = true
			};

			_dropCheckTimer.Elapsed += TimerCallback;
		}

		_dropCheckTimer.Start();

		await SetPlayingStatus().ConfigureAwait(false);
	}

	public async Task StopIdling()
	{
		if (!IsRunning)
			throw new InvalidOperationException("Item drop collection is not active");

		_dropCheckTimer?.Stop();

		await ClearPlayingStatus().ConfigureAwait(false);
	}

	public async Task SetPlayingStatus()
	{
		if (!Client.IsConnected)
		{
			_logger.LogGenericWarning("Cannot set playing status: client not connected");

			return;
		}

		var response = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

		foreach (var app in _config.Apps)
		{
			response.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
			{
				steam_id_gs = Client.SteamID!,
				game_id = new GameID(app.AppId),
			});
		}

		await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		Client.Send(response);
	}

	public async Task ClearPlayingStatus()
	{
		if (!Client.IsConnected)
		{
			_logger.LogGenericWarning("Cannot clear playing status: client not connected");

			return;
		}

		var response = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

		await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		Client.Send(response);
	}

	public async Task<DropResult?> CheckItemDrop(uint appId, uint itemDefId)
	{
		try
		{
			if (!Client.IsConnected)
			{
				_logger.LogGenericWarning("Cannot check item drop: client not connected");

				return null;
			}

			_steamUnifiedMessages ??= Client.GetHandler<SteamUnifiedMessages>();
			_inventoryService ??= _steamUnifiedMessages?.CreateService<Inventory>();

			var response = await _inventoryService!.ConsumePlaytime(new CInventory_ConsumePlaytime_Request
			{
				appid = appId,
				itemdefid = itemDefId,
			});

			if (response.Body?.item_json is not string json || string.IsNullOrWhiteSpace(json))
			{
				_logger.LogGenericDebug($"Received empty response for itemdefid {itemDefId} of appid {appId}");

				return null;
			}

			_logger.LogGenericDebug($"Response received for appid {appId}: {json}");

			var drops = JsonSerializer.Deserialize<DropResult[]>(json);
			var drop = drops?.FirstOrDefault(d => !string.IsNullOrEmpty(d.ItemDefId));

			if (drop is null)
			{
				_logger.LogGenericDebug($"Received empty drop for itemdefid {itemDefId} of appid {appId}");

				return null;
			}

			_logger.LogGenericInfo($"Received item for appid {appId}: {drop.ItemDefId}");

			return drop;
		}
		catch (Exception ex)
		{
			_logger.LogGenericError($"Failed to check item drop for itemdefid {itemDefId} of appid {appId}: {ex.Message}");

			return null;
		}
	}
}