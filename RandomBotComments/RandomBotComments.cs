using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;
using SteamKit2;

namespace RandomBotComments;

#pragma warning disable CA1812 // ASF uses this class during runtime
#pragma warning disable CA1001 // Plugin instances live for the process' lifetime; ASF gives IPlugin implementations no disposal hook to call into
#pragma warning disable CA5394 // Randomness here only picks an arbitrary bot/comment/delay, it's not used for anything security-sensitive
[UsedImplicitly]
internal sealed class RandomBotComments : IASF, IGitHubPluginUpdates {
	private const uint DefaultMaxDelayBetweenCommentsInSeconds = 7200;
	private const uint DefaultMinDelayBetweenCommentsInSeconds = 1800;
	private const byte MaxCommentLength = 255; // Steam's profile comment length limit

	private static readonly Uri SteamCommunityURL = new("https://steamcommunity.com");

	// Self-authored, generic enough to fit on any friend's wall regardless of context - deliberately NOT
	// scraped from anyone's real wall (real comments turned out to be personal correspondence addressed
	// to a specific person, sometimes signed by name - not safe or sensible to repost verbatim elsewhere)
	private static readonly string[] BundledComments = [
		"gg", "nice profile!", "👍", "add me back sometime", "🔥", "cool avatar", "let's play sometime",
		"o7", "welcome back", "nice to see you online", ":)", "gl in your games", "have a good one",
		"👋", "solid taste in games", "🎮", "cheers", "stay awesome", "nice pfp", "o/", "keep it up",
		"epic", "based", "let's go", "nice collection", "respect", "peace ✌️", "take care", "see you around",
		"🙌", "keep grinding", "nice", "hey", "sup", "yo", "🎉", "congrats", "well played", "👌"
	];

	private CancellationTokenSource? BackgroundLoopCts;
	private HashSet<string> CommentPool = [];
	private bool Enabled;
	private uint MaxDelayBetweenCommentsInSeconds = DefaultMaxDelayBetweenCommentsInSeconds;
	private uint MinDelayBetweenCommentsInSeconds = DefaultMinDelayBetweenCommentsInSeconds;
	private bool UseBundledComments;

	public string Name => nameof(RandomBotComments);
	public string RepositoryName => "buddymurdock/ASF-RandomBotComments";
	public Version Version => typeof(RandomBotComments).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	// Reads RandomBotCommentsEnabled / RandomBotCommentsMinDelayBetweenComments / RandomBotCommentsMaxDelayBetweenComments /
	// RandomBotCommentsComments / RandomBotCommentsUseBundledComments from the global ASF.json config
	public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		HashSet<string> parsedComments = [];

		if (additionalConfigProperties != null) {
			foreach ((string configProperty, JsonElement configValue) in additionalConfigProperties) {
				switch (configProperty) {
					case $"{nameof(RandomBotComments)}Enabled" when configValue.ValueKind is JsonValueKind.True or JsonValueKind.False:
						Enabled = configValue.GetBoolean();

						break;
					case $"{nameof(RandomBotComments)}MinDelayBetweenComments" when (configValue.ValueKind == JsonValueKind.Number) && configValue.TryGetUInt32(out uint minDelay) && (minDelay > 0):
						MinDelayBetweenCommentsInSeconds = minDelay;

						break;
					case $"{nameof(RandomBotComments)}MaxDelayBetweenComments" when (configValue.ValueKind == JsonValueKind.Number) && configValue.TryGetUInt32(out uint maxDelay) && (maxDelay > 0):
						MaxDelayBetweenCommentsInSeconds = maxDelay;

						break;
					case $"{nameof(RandomBotComments)}UseBundledComments" when configValue.ValueKind is JsonValueKind.True or JsonValueKind.False:
						UseBundledComments = configValue.GetBoolean();

						break;
					case $"{nameof(RandomBotComments)}Comments" when configValue.ValueKind == JsonValueKind.Array:
						AddParsedComments(configValue, parsedComments);

						break;
				}
			}
		}

		if (UseBundledComments) {
			foreach (string comment in BundledComments) {
				parsedComments.Add(comment);
			}
		}

		CommentPool = parsedComments;

		if (MinDelayBetweenCommentsInSeconds > MaxDelayBetweenCommentsInSeconds) {
			(MinDelayBetweenCommentsInSeconds, MaxDelayBetweenCommentsInSeconds) = (MaxDelayBetweenCommentsInSeconds, MinDelayBetweenCommentsInSeconds);
		}

		if (!Enabled) {
			ASF.ArchiLogger.LogGenericInfo($"{Name} is disabled, set {nameof(RandomBotComments)}Enabled to true in ASF.json to turn it on.");

			return Task.CompletedTask;
		}

		ASF.ArchiLogger.LogGenericInfo($"{Name} is enabled, {MinDelayBetweenCommentsInSeconds}-{MaxDelayBetweenCommentsInSeconds}s between comments, picking from {CommentPool.Count} candidate(s), only between bots that are already friends with each other.");

		if (BackgroundLoopCts != null) {
			// OnASFInit() should only ever be called once per process, this is just a safety net against a possible double start
			return Task.CompletedTask;
		}

		BackgroundLoopCts = new CancellationTokenSource();

		Utilities.InBackground(() => BackgroundLoopAsync(BackgroundLoopCts.Token), true);

		return Task.CompletedTask;
	}

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{Name} has been loaded!");

		return Task.CompletedTask;
	}

	// Delay is re-rolled every tick within [MinDelayBetweenCommentsInSeconds; MaxDelayBetweenCommentsInSeconds] instead of a
	// fixed-period timer - a perfectly metronomic tick interval running around the clock is itself a machine-detectable pattern
	private async Task BackgroundLoopAsync(CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			uint delaySeconds = MinDelayBetweenCommentsInSeconds == MaxDelayBetweenCommentsInSeconds ? MinDelayBetweenCommentsInSeconds : (uint) Random.Shared.Next((int) MinDelayBetweenCommentsInSeconds, (int) MaxDelayBetweenCommentsInSeconds + 1);

			try {
				await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
			} catch (OperationCanceledException) {
				break;
			}

			try {
				await TryPostSingleCommentAsync().ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}
	}

	// Posts at most one comment per call, from a random bot onto the wall of a random other bot it's already Steam-friends with
	private async Task TryPostSingleCommentAsync() {
		if (CommentPool.Count == 0) {
			return;
		}

		IReadOnlyDictionary<string, Bot>? bots = Bot.BotsReadOnly;

		if ((bots == null) || (bots.Count < 2)) {
			return;
		}

		List<Bot> onlineBots = [.. bots.Values.Where(static bot => bot.IsConnectedAndLoggedOn).OrderBy(static _ => Random.Shared.Next())];

		foreach (Bot sender in onlineBots) {
			List<Bot> friendBots = [.. onlineBots.Where(otherBot => (otherBot != sender) && (otherBot.SteamID != 0) && (sender.SteamFriends.GetFriendRelationship(otherBot.SteamID) == EFriendRelationship.Friend))];

			if (friendBots.Count == 0) {
				continue;
			}

			Bot receiver = friendBots[Random.Shared.Next(friendBots.Count)];
			string comment = CommentPool.ElementAt(Random.Shared.Next(CommentPool.Count));

			bool success = await PostCommentAsync(sender, receiver.SteamID, comment).ConfigureAwait(false);

			if (success) {
				sender.ArchiLogger.LogGenericInfo($"Posted a comment on {receiver.BotName}'s wall: \"{comment}\".");
			} else {
				sender.ArchiLogger.LogGenericWarning($"Failed to post a comment on {receiver.BotName}'s wall.");
			}

			return;
		}
	}

	private static async Task<bool> PostCommentAsync(Bot bot, ulong receiverSteamID, string comment) {
		Uri request = new(SteamCommunityURL, $"/comment/Profile/post/{receiverSteamID}/-1");

		Dictionary<string, string> data = new(StringComparer.Ordinal) {
			{ "comment", comment },
			{ "count", "1" }
		};

		ArchiSteamFarm.Web.Responses.ObjectResponse<CommentPostResponse>? response = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<CommentPostResponse>(request, data: data, referer: SteamCommunityURL).ConfigureAwait(false);

		return response?.Content?.Success ?? false;
	}

	private static void AddParsedComments(JsonElement array, HashSet<string> target) {
		foreach (JsonElement commentElement in array.EnumerateArray()) {
			string? comment = commentElement.ValueKind == JsonValueKind.String ? commentElement.GetString() : null;

			if (!string.IsNullOrWhiteSpace(comment) && (comment.Length <= MaxCommentLength)) {
				target.Add(comment);
			} else {
				ASF.ArchiLogger.LogGenericWarning($"Ignoring invalid {nameof(RandomBotComments)}Comments entry: {commentElement}.");
			}
		}
	}

	private sealed record CommentPostResponse([property: JsonPropertyName("success")] bool Success, [property: JsonPropertyName("error")] string? Error);
}
#pragma warning restore CA5394
#pragma warning restore CA1001
#pragma warning restore CA1812
