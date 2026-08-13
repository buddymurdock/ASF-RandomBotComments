# ASF-RandomBotComments

Плагин для **[ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm)**, который через случайные интервалы оставляет короткий комментарий на стене профиля от одного бота другому — имитация того, что живые друзья изредка пишут друг другу на стене.

Комментарии отправляются **только между ботами, которые уже в друзьях друг у друга** (например, через [RandomBotFriends](https://github.com/buddymurdock/ASF-RandomBotFriends)) — плагин никогда не пишет посторонним/реальным людям. Раз в случайный интервал `[MinDelayBetweenComments; MaxDelayBetweenComments]` секунд выбирается случайный залогиненный бот и случайный из его бот-друзей, на чью стену оставляется случайный комментарий из пула — тем же публичным HTTP-эндпоинтом (`steamcommunity.com/comment/Profile/post/...`), которым Steam-клиент сам публикует комментарии.

## Источник текста

По умолчанию используется бандлированный список из ~38 нейтральных коротких фраз/эмодзи (`gg`, `nice profile!`, `👍` и т.п.) плюс ~200 вариаций на тему Steam-культуры `+rep`/`-rep` (`+rep fast trader`, `+rep, thanks for the trade`, шутливые `-rep too generous lol` между уже доверяющими друг другу друзьями) — все написаны нами самими, **не** скрапленные с чьих-то реальных стен. Ранняя идея взять реальные комментарии была отклонена дважды: сначала они оказались личной перепиской, адресованной конкретным людям (иногда подписанной их именами); затем, при повторной попытке через ручную модерацию, в отобранном материале обнаружился хейт-спич и харассмент (комментарии из "репутации" после матчей Dota/CS) — использовать это отказались.

## Установка

1. Скачайте архив плагина из [Releases](../../releases) и распакуйте в папку `plugins` рядом с ASF (создайте подпапку с именем плагина).
2. Перезапустите ASF.

## Конфигурация

Настройки задаются **глобально**, в `ASF.json`, как дополнительные (нераспознанные ASF) свойства верхнего уровня:

```json
{
	"RandomBotCommentsEnabled": true,
	"RandomBotCommentsMinDelayBetweenComments": 1800,
	"RandomBotCommentsMaxDelayBetweenComments": 7200,
	"RandomBotCommentsUseBundledComments": true,
	"RandomBotCommentsComments": []
}
```

| Свойство | Тип | По умолчанию | Описание |
| --- | --- | --- | --- |
| `RandomBotCommentsEnabled` | `bool` | `false` | Включает/выключает плагин. |
| `RandomBotCommentsMinDelayBetweenComments` | `uint`, секунды | `1800` | Нижняя граница случайной паузы между комментариями. |
| `RandomBotCommentsMaxDelayBetweenComments` | `uint`, секунды | `7200` | Верхняя граница случайной паузы между комментариями. |
| `RandomBotCommentsUseBundledComments` | `bool` | `false` | Добавлять ли в пул кандидатов фразы из встроенного списка (см. выше). |
| `RandomBotCommentsComments` | `string[]` | `[]` | Свой список фраз (до 255 символов, лимит Steam), добавляется к бандлу (если он включён) или используется как единственный источник (если бандл выключен). |

Если `MinDelayBetweenComments` больше `MaxDelayBetweenComments`, значения меняются местами автоматически. Если итоговый пул пуст, или ни у одного залогиненного бота нет бот-друзей, плагин просто ничего не делает на этом тике.

## Сборка

Проект использует **[ASF-PluginTemplate](https://github.com/JustArchiNET/ASF-PluginTemplate)** и собирается вместе с исходниками ASF, подключёнными как git submodule:

```sh
git clone --recurse-submodules https://github.com/buddymurdock/ASF-RandomBotComments.git
cd ASF-RandomBotComments
dotnet build -c Release
```

Если репозиторий уже склонирован без `--recurse-submodules`, подтяните submodule отдельно:

```sh
git submodule update --init --recursive
```

## Лицензия

Apache-2.0, см. [LICENSE.txt](LICENSE.txt).
