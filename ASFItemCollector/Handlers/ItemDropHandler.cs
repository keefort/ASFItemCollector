using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;

using ArchiSteamFarm.Core;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Steam;

using ASFItemCollector.Data;
using ASFItemCollector.Data.Plugin;

using SteamKit2;
using SteamKit2.Internal;

namespace ASFItemCollector.Handlers;

public sealed class ItemDropHandler(Bot bot, PluginConfig config) : ClientMsgHandler, IDisposable
{
#pragma warning disable CA2213
	private readonly Bot _bot = bot ?? throw new ArgumentNullException(nameof(bot));
#pragma warning restore CA2213
	private readonly PluginConfig _config = config ?? throw new ArgumentNullException(nameof(config));

	private readonly ArchiLogger _logger = bot.ArchiLogger;

	private System.Timers.Timer? _dropCheckTimer;

	private Inventory? _inventoryService;
	private SteamUnifiedMessages? _steamUnifiedMessages;

	public bool IsRunning => _dropCheckTimer?.Enabled ?? false;

	public void Dispose()
	{
		if (_dropCheckTimer is not null)
		{
			_dropCheckTimer.Elapsed -= CheckItemDrops;
			_dropCheckTimer.Dispose();
		}
	}

	public override void HandleMsg(IPacketMsg packetMsg)
	{
		if (packetMsg is null) return;
		switch (packetMsg.MsgType)
		{
			default:
				break;
		}
	}

	public void SetupCallbacks(CallbackManager manager)
	{
		ArgumentNullException.ThrowIfNull(manager);

		_ = manager.Subscribe<SteamUser.PlayingSessionStateCallback>(OnPlayingSessionState);
	}

	public void OnPlayingSessionState(SteamUser.PlayingSessionStateCallback callback)
	{
		if (_config!.Enabled && !IsRunning && _bot.CardsFarmer.Paused && _bot.IsPlayingPossible)
			Utilities.InBackground(StartIdling);
		else if (IsRunning && !_bot.IsPlayingPossible)
			Utilities.InBackground(StopIdling);
	}

	private async void CheckItemDrops(object? state, ElapsedEventArgs e)
	{
		_logger.LogGenericDebug("Item drop check started");

		try
		{
			await SetPlayingStatus().ConfigureAwait(false);

			var uncheckedItems = new List<uint>();
			foreach (var app in _config.Apps)
				foreach (var itemDefId in app.ItemDefIds)
				{
					var drop = await GetItemDrop(app.ID, itemDefId).ConfigureAwait(false);

					if (drop is not null)
					{
						_logger.LogGenericInfo($"Received item for appid {app.ID}: {drop.ItemDefId}");

						uncheckedItems = [.. app.ItemDefIds.SkipWhile(x => x != itemDefId).Skip(1)];
						if (uncheckedItems.Count != 0)
							_logger.LogGenericDebug($"Skipped itemdefids for appid {app.ID}: {string.Join(", ", uncheckedItems)}");

						break;
					}
					else
					{
						_logger.LogGenericDebug($"Received empty drop for itemdefid {itemDefId} of appid {app.ID}");
					}

					_logger.LogGenericDebug("Wait 5 seconds before next request");
					await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
				}
		}
		catch (Exception ex)
		{
			_logger.LogGenericException(ex, "Failed to check item drop");
		}
		finally
		{
			_logger.LogGenericDebug("Item drop check complete");
		}
	}

	public async Task StartIdling()
	{
		if (IsRunning)
			throw new InvalidOperationException("dropped item collection is already active");

		if (!_bot.IsPlayingPossible || _bot.CardsFarmer.NowFarming)
			throw new InvalidOperationException("unable to start collecting dropped items");

		if (_dropCheckTimer is null)
		{
			_dropCheckTimer = new System.Timers.Timer(_config.DropCheckInterval * 60000)
			{
				AutoReset = true
			};

			_dropCheckTimer.Elapsed += CheckItemDrops;
		}

		_dropCheckTimer.Start();

		await SetPlayingStatus().ConfigureAwait(false);

		_bot.ArchiLogger.LogGenericInfo("Dropped item collection started");
	}

	public async Task StopIdling()
	{
		if (!IsRunning)
			throw new InvalidOperationException("dropped item collection is not active");

		_dropCheckTimer?.Stop();

		if (_bot.IsConnectedAndLoggedOn)
			await ClearPlayingStatus().ConfigureAwait(false);

		_bot.ArchiLogger.LogGenericInfo("Dropped item collection stopped");
	}

	public async Task SetPlayingStatus()
	{
		if (!Client.IsConnected)
			throw new InvalidOperationException("client is not connected");

		var response = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

		foreach (var app in _config.Apps)
		{
			response.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
			{
				steam_id_gs = Client.SteamID!,
				game_id = new GameID(app.ID),
			});
		}

		await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		Client.Send(response);
	}

	public async Task ClearPlayingStatus()
	{
		if (!Client.IsConnected)
			throw new InvalidOperationException("client is not connected");

		var response = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);

		await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		Client.Send(response);
	}

	public async Task<DropResult?> GetItemDrop(uint appId, uint itemDefId)
	{
		if (!Client.IsConnected)
			throw new InvalidOperationException("client is not connected");

		try
		{
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
			return drops?.FirstOrDefault(d => !string.IsNullOrEmpty(d.ItemDefId));
		}
		catch (Exception ex)
		{
			_logger.LogGenericError($"Failed to check item drop for itemdefid {itemDefId} of appid {appId}: {ex.Message}");

			return null;
		}
	}
}
