using System.Collections.Concurrent;
using System.Composition;
using System.Globalization;
using System.Text;
using System.Text.Json;

using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Interaction;

using ASFItemCollector.Data.Plugin;
using ASFItemCollector.Handlers;

using SteamKit2;

namespace ASFItemCollector;

[Export(typeof(IPlugin))]
public sealed class ASFItemCollectorPlugin : IPlugin, IASF, IBotSteamClient, IBotConnection, IBotCommand2, IBotCardsFarmerInfo
{
	private PluginConfig? _config;

	private readonly ConcurrentDictionary<Bot, ItemDropHandler> _itemDropHandlers = new();

	public string Name => nameof(ASFItemCollectorPlugin);
	public Version Version => typeof(ASFItemCollectorPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public Task OnLoaded()
	{
		return Task.CompletedTask;
	}

	public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null)
	{
		if (additionalConfigProperties is not null && additionalConfigProperties.TryGetValue("ASFItemCollector", out JsonElement configElement))
		{
			try
			{
				_config = configElement.ToJsonObject<PluginConfig>();
			}
			catch (Exception ex)
			{
				ASF.ArchiLogger.LogGenericException(ex, $"{Name} failed to load configuration");
			}
		}

		_config ??= new([]);

		return Task.CompletedTask;
	}

	public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot)
	{
		ArgumentNullException.ThrowIfNull(bot);

		ItemDropHandler itemDropHandler = new(bot, _config!);
		_itemDropHandlers.TryAdd(bot, itemDropHandler);

		IReadOnlyCollection<ClientMsgHandler> result = [itemDropHandler];
		return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>(result);
	}

	public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager)
	{
		ArgumentNullException.ThrowIfNull(bot);

		bot.GetHandler<ItemDropHandler>()?.SetupCallbacks(callbackManager);

		return Task.CompletedTask;
	}

	public Task OnBotLoggedOn(Bot bot)
	{
		return Task.CompletedTask;
	}

	public async Task OnBotDisconnected(Bot bot, EResult reason)
	{
		ArgumentNullException.ThrowIfNull(bot);

		var itemDropHandler = bot.GetHandler<ItemDropHandler>();
		if (itemDropHandler?.IsRunning == true)
			await StopItemIdling(bot).ConfigureAwait(false);
	}

	public async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0)
	{
		if (args is null || args.Length == 0)
			throw new ArgumentNullException(nameof(args));

		return args[0].ToUpperInvariant() switch
		{
			"ISTART" when args.Length == 1 && access >= EAccess.Master => await StartItemIdling(bot).ConfigureAwait(false),
			"ISTART" when args.Length == 2 && access >= EAccess.Master => await StartItemIdling(args[1]).ConfigureAwait(false),
			"ISTOP" when args.Length == 1 && access >= EAccess.Master => await StopItemIdling(bot).ConfigureAwait(false),
			"ISTOP" when args.Length == 2 && access >= EAccess.Master => await StopItemIdling(args[1]).ConfigureAwait(false),
			_ => null,
		};
	}

	public Task OnBotFarmingFinished(Bot bot, bool farmedSomething)
	{
		return Task.CompletedTask;
	}

	public async Task OnBotFarmingStarted(Bot bot)
	{
		ArgumentNullException.ThrowIfNull(bot);

		var itemDropHandler = bot.GetHandler<ItemDropHandler>();
		if (itemDropHandler?.IsRunning == true)
			await StopItemIdling(bot).ConfigureAwait(false);
	}

	public async Task OnBotFarmingStopped(Bot bot)
	{
		ArgumentNullException.ThrowIfNull(bot);

		if (_config!.Enabled && bot.IsPlayingPossible)
			await StartItemIdling(bot).ConfigureAwait(false);
	}

	public async Task<string?> StartItemIdling(Bot bot)
	{
		ArgumentNullException.ThrowIfNull(bot);

		var itemDropHandler = bot.GetHandler<ItemDropHandler>();
		if (itemDropHandler is null)
		{
			string error = string.Format(
				CultureInfo.InvariantCulture,
				CompositeFormat.Parse(Strings.ErrorIsEmpty),
				nameof(_itemDropHandlers)
			);

			bot.ArchiLogger.LogGenericError(error);
			return bot.Commands.FormatBotResponse(error);
		}

		try
		{
			await itemDropHandler.StartIdling().ConfigureAwait(false);
			return bot.Commands.FormatBotResponse($"Successfully started item idling");
		}
		catch (Exception ex)
		{
			string error = string.Format(
				CultureInfo.InvariantCulture,
				$"Failed to start item idling: {ex.Message}"
			);

			bot.ArchiLogger.LogGenericError(error);
			return bot.Commands.FormatBotResponse(error);
		}
	}

	public async Task<string?> StartItemIdling(string botNames)
	{
		ArgumentNullException.ThrowIfNull(botNames);
		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots is null) || (bots.Count == 0))
		{
			string error = string.Format(
				CultureInfo.InvariantCulture,
				CompositeFormat.Parse(Strings.BotNotFound),
				botNames
			);

			ASF.ArchiLogger.LogGenericError(error);
			return Commands.FormatStaticResponse(error);
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(StartItemIdling)).ConfigureAwait(false);
		List<string?> responses = [.. results.Where(result => !string.IsNullOrEmpty(result))];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}

	public async Task<string?> StopItemIdling(Bot bot)
	{
		ArgumentNullException.ThrowIfNull(bot);

		var itemDropHandler = bot.GetHandler<ItemDropHandler>();
		if (itemDropHandler is null)
		{
			string error = string.Format(
				CultureInfo.InvariantCulture,
				CompositeFormat.Parse(Strings.ErrorIsEmpty),
				nameof(_itemDropHandlers)
			);

			bot.ArchiLogger.LogGenericError(error);
			return bot.Commands.FormatBotResponse(error);
		}

		try
		{
			await itemDropHandler.StopIdling().ConfigureAwait(false);
			return bot.Commands.FormatBotResponse($"Successfully stoped item idling");
		}
		catch (Exception ex)
		{
			string error = string.Format(
				CultureInfo.InvariantCulture,
				$"Failed to stop item idling: {ex.Message}"
			);

			bot.ArchiLogger.LogGenericError(error);
			return bot.Commands.FormatBotResponse(error);
		}
	}

	public async Task<string?> StopItemIdling(string botNames)
	{
		ArgumentNullException.ThrowIfNull(botNames);
		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots is null) || (bots.Count == 0))
		{
			string error = string.Format(
				CultureInfo.InvariantCulture,
				CompositeFormat.Parse(Strings.BotNotFound),
				botNames
			);

			ASF.ArchiLogger.LogGenericError(error);
			return Commands.FormatStaticResponse(error);
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(StopItemIdling)).ConfigureAwait(false);
		List<string?> responses = [.. results.Where(result => !string.IsNullOrEmpty(result))];

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}
}
