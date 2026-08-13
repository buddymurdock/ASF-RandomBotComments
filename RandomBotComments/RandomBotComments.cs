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

	// Same self-authored principle as BundledComments above, but templated on Steam's own +rep/-rep
	// trading-reputation culture (leaving a short verdict on a trader's wall after a deal) - the -rep
	// entries are deliberately light, friendly ribbing between people who already trust each other
	// (e.g. "-rep too generous lol"), never a genuine accusation or insult.
	private static readonly string[] BundledTradeComments = [
		"+rep 5 stars", "+rep always a pleasure", "+rep always delivers", "+rep appreciate the deal",
		"+rep awesome buyer", "+rep awesome deal", "+rep awesome player", "+rep awesome seller", "+rep awesome trade",
		"+rep chill buyer", "+rep chill deal", "+rep chill partner", "+rep chill player", "+rep chill seller",
		"+rep chill trade", "+rep cool partner", "+rep cool player", "+rep cool trader", "+rep easy buyer",
		"+rep easy deal", "+rep easy partner", "+rep easy player", "+rep easy trade", "+rep easy trader",
		"+rep efficient buyer", "+rep efficient deal", "+rep efficient partner", "+rep efficient player",
		"+rep efficient seller", "+rep efficient trade", "+rep efficient trader", "+rep excellent buyer",
		"+rep excellent deal", "+rep excellent partner", "+rep excellent player", "+rep excellent seller",
		"+rep excellent trade", "+rep excellent trader", "+rep fair trade", "+rep fast and easy trade",
		"+rep fast buyer", "+rep fast deal", "+rep fast seller", "+rep fast trade", "+rep fast trader",
		"+rep friendly player", "+rep friendly seller", "+rep friendly trade", "+rep friendly trader",
		"+rep generous deal", "+rep generous partner", "+rep generous seller", "+rep generous trade",
		"+rep generous trader", "+rep genuine buyer", "+rep genuine deal", "+rep genuine partner",
		"+rep genuine player", "+rep genuine seller", "+rep genuine trade", "+rep genuine trader", "+rep great buyer",
		"+rep great experience overall", "+rep great partner", "+rep great player", "+rep great seller",
		"+rep great trade", "+rep great trader", "+rep great trading with you", "+rep helpful buyer",
		"+rep helpful deal", "+rep helpful partner", "+rep helpful player", "+rep helpful seller", "+rep honest player",
		"+rep honest seller", "+rep honest trade", "+rep legit buyer", "+rep legit deal", "+rep legit partner",
		"+rep legit seller", "+rep legit trade", "+rep legit trader", "+rep nice player", "+rep nice seller",
		"+rep nice trade", "+rep no issues at all", "+rep patient buyer", "+rep patient deal", "+rep patient partner",
		"+rep patient player", "+rep patient seller", "+rep patient trade", "+rep patient trader", "+rep polite buyer",
		"+rep polite deal", "+rep polite seller", "+rep polite trade", "+rep polite trader", "+rep professional buyer",
		"+rep professional deal", "+rep professional partner", "+rep professional player", "+rep professional seller",
		"+rep professional trade", "+rep professional trader", "+rep quick and painless", "+rep quick buyer",
		"+rep quick partner", "+rep quick player", "+rep quick response", "+rep quick seller", "+rep quick trade",
		"+rep quick trader", "+rep reliable buyer", "+rep reliable partner", "+rep reliable seller",
		"+rep reliable trade", "+rep reliable trader", "+rep respectful buyer", "+rep respectful player",
		"+rep respectful seller", "+rep respectful trade", "+rep respectful trader", "+rep smooth buyer",
		"+rep smooth deal", "+rep smooth partner", "+rep smooth player", "+rep smooth trade", "+rep smooth trader",
		"+rep smooth transaction", "+rep solid buyer", "+rep solid deal", "+rep solid player", "+rep solid trade",
		"+rep solid trader", "+rep thanks for the cards", "+rep thanks for the gift", "+rep top buyer", "+rep top deal",
		"+rep top partner", "+rep top seller", "+rep top trade", "+rep top trader", "+rep trusted trader",
		"+rep trustworthy deal", "+rep trustworthy player", "+rep trustworthy seller", "+rep trustworthy trade",
		"+rep trustworthy trader", "+rep very patient", "+rep would recommend", "+rep would trade again",
		"+rep, 5 stars", "+rep, always a pleasure", "+rep, always delivers", "+rep, fair trade",
		"+rep, fast and easy trade", "+rep, great communication", "+rep, great experience overall",
		"+rep, no complaints", "+rep, no issues at all", "+rep, quick and painless", "+rep, quick response",
		"+rep, smooth transaction", "+rep, super easy deal", "+rep, thanks for the cards", "+rep, thanks for the gift",
		"+rep, thanks for the trade", "+rep, top notch trader", "+rep, trusted trader", "+rep, very patient",
		"+rep, would recommend", "-rep for beating me at cards", "-rep for being better than me at everything",
		"-rep for having too much drip", "-rep for out-trading me", "-rep for the trade offer spam (jk love you)",
		"-rep for the unfair advantage (jk)", "-rep for winning the giveaway", "-rep gave me a great deal, suspicious",
		"-rep hoarding all the good cards", "-rep jk, love ya", "-rep sneaky good trader ngl",
		"-rep too chill, makes the rest of us look bad", "-rep too generous lol", "-rep too good at this game",
		"-rep too lucky with drops", "-rep, for having a better inventory than me", "-rep, for having too much drip",
		"-rep, for making me look bad", "-rep, for out-trading me", "-rep, for the sniped item (all in good fun)",
		"-rep, for the trade offer spam (jk love you)", "-rep, jk, love ya", "-rep, stole the good cards (jk)",
		"-rep, too chill, makes the rest of us look bad", "-rep, too generous lol", "-rep, too good at this game",
		"-rep, too lucky with drops"
	];

	// Same self-authored principle, in Russian (operator's own audience is Russian-speaking, see CLAUDE.md) -
	// generic wall greetings, no scraping involved.
	private static readonly string[] BundledCommentsRu = [
		"го катку", "красава", "круто!", "👍", "давно не виделись", "го в стим", "класс", "молодец", "успехов",
		"хорошего дня", "заходи в гости", "го дуо как-нибудь", "красивый профиль", "круто играешь", "респект",
		"го трейд как-нибудь", "рад видеть тебя онлайн", "мир", "го гамать", "все супер", "продолжай в том же духе",
		"😄", "🔥", "👋", "заходи", "привет", "как дела", "го играть", "удачи в игре", "🎮", "🙌", "хорошей игры",
		"до скорого", "го тусить", "класс аватарка", "😎", "хорошего вечера", "го дальше фармить", "красиво собрано",
		"🎉", "неплохая коллекция"
	];

	// Russian +рeп/-реп equivalent of BundledTradeComments above, same templated construction, same rule
	// that -реп entries stay light and clearly friendly, never a real accusation or insult.
	private static readonly string[] BundledTradeCommentsRu = [
		"+реп адекватный игрок", "+реп адекватный партнёр", "+реп адекватный покупатель", "+реп адекватный продавец",
		"+реп адекватный трейдер", "+реп адекватный человек", "+реп без нареканий", "+реп без проблем",
		"+реп быстрая сделка", "+реп быстрый игрок", "+реп быстрый ответ", "+реп быстрый партнёр",
		"+реп быстрый покупатель", "+реп быстрый продавец", "+реп быстрый трейдер", "+реп быстрый человек",
		"+реп вежливый игрок", "+реп вежливый партнёр", "+реп вежливый покупатель", "+реп вежливый продавец",
		"+реп вежливый трейдер", "+реп вежливый человек", "+реп всегда на связи", "+реп го ещё потрейдим",
		"+реп дружелюбный игрок", "+реп дружелюбный партнёр", "+реп дружелюбный покупатель",
		"+реп дружелюбный продавец", "+реп дружелюбный трейдер", "+реп дружелюбный человек", "+реп крутой игрок",
		"+реп крутой партнёр", "+реп крутой покупатель", "+реп крутой продавец", "+реп крутой трейдер",
		"+реп крутой человек", "+реп лёгкая сделка", "+реп лёгкий игрок", "+реп лёгкий партнёр",
		"+реп лёгкий покупатель", "+реп лёгкий продавец", "+реп лёгкий трейдер", "+реп лёгкий человек",
		"+реп надёжный игрок", "+реп надёжный партнёр", "+реп надёжный покупатель", "+реп надёжный продавец",
		"+реп надёжный трейдер", "+реп надёжный человек", "+реп нормальный игрок", "+реп нормальный партнёр",
		"+реп нормальный покупатель", "+реп нормальный продавец", "+реп нормальный трейдер", "+реп нормальный человек",
		"+реп отличное общение", "+реп отличный игрок", "+реп отличный партнёр", "+реп отличный покупатель",
		"+реп отличный продавец", "+реп отличный трейдер", "+реп отличный человек", "+реп приятно иметь дело",
		"+реп приятный игрок", "+реп приятный партнёр", "+реп приятный покупатель", "+реп приятный продавец",
		"+реп приятный трейдер", "+реп приятный человек", "+реп проверенный трейдер", "+реп профессиональный игрок",
		"+реп профессиональный партнёр", "+реп профессиональный покупатель", "+реп профессиональный продавец",
		"+реп профессиональный трейдер", "+реп профессиональный человек", "+реп рекомендую", "+реп спасибо за карточки",
		"+реп спасибо за подарок", "+реп спасибо за трейд", "+реп спокойный игрок", "+реп спокойный партнёр",
		"+реп спокойный покупатель", "+реп спокойный продавец", "+реп спокойный трейдер", "+реп спокойный человек",
		"+реп терпеливый игрок", "+реп терпеливый партнёр", "+реп терпеливый покупатель", "+реп терпеливый продавец",
		"+реп терпеливый трейдер", "+реп терпеливый человек", "+реп хороший игрок", "+реп хороший партнёр",
		"+реп хороший покупатель", "+реп хороший продавец", "+реп хороший трейдер", "+реп хороший человек",
		"+реп честная сделка", "+реп честный игрок", "+реп честный партнёр", "+реп честный покупатель",
		"+реп честный продавец", "+реп честный трейдер", "+реп честный человек", "+реп щедрый игрок",
		"+реп щедрый партнёр", "+реп щедрый покупатель", "+реп щедрый продавец", "+реп щедрый трейдер",
		"+реп щедрый человек", "+реп, без нареканий", "+реп, без проблем", "+реп, быстрая сделка",
		"+реп, быстрый ответ", "+реп, всегда на связи", "+реп, го ещё потрейдим", "+реп, лёгкая сделка",
		"+реп, отличное общение", "+реп, приятно иметь дело", "+реп, проверенный трейдер", "+реп, рекомендую",
		"+реп, спасибо за карточки", "+реп, спасибо за подарок", "+реп, спасибо за трейд", "+реп, честная сделка",
		"-реп выиграл розыгрыш", "-реп инвентарь лучше моего", "-реп обошёл меня в трейде",
		"-реп обыграл меня в карточки", "-реп повезло с дропом", "-реп слишком добрый для этого мира",
		"-реп слишком хорош в этом", "-реп слишком щедрый лол", "-реп спамит трейдами (шучу, люблю тебя)",
		"-реп стащил лучшие карты (шучу)", "-реп, выиграл розыгрыш", "-реп, инвентарь лучше моего",
		"-реп, обошёл меня в трейде", "-реп, обыграл меня в карточки", "-реп, повезло с дропом",
		"-реп, слишком добрый для этого мира", "-реп, слишком хорош в этом", "-реп, слишком щедрый лол",
		"-реп, спамит трейдами (шучу, люблю тебя)", "-реп, стащил лучшие карты (шучу)"
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

			foreach (string comment in BundledTradeComments) {
				parsedComments.Add(comment);
			}

			foreach (string comment in BundledCommentsRu) {
				parsedComments.Add(comment);
			}

			foreach (string comment in BundledTradeCommentsRu) {
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
