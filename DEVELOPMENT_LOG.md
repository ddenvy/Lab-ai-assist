# Lab AI Assistant — Дневник разработки

## О проекте

Lab AI Assistant — RAG-ассистент над лабораторными SOP и данными приборов для регулируемой среды
(GxP / 21 CFR Part 11). Отвечает **только** по найденным фрагментам документов, цитирует источники,
при отсутствии ответа говорит «Нет в источниках.» и пишет append-only журнал каждого AI-запроса:
кто спросил, вопрос, какие чанки использованы, модель, хеш промпта, обоснование, время.

Платформа: .NET 10, ASP.NET Core Minimal API + Blazor Server (один хост), SQLite.
Архитектура: Clean/Layered (Domain → Application → Infrastructure → Web),
Data-Oriented Design на hot path (brute-force cosine search).

**Стек:** C# 14, Semantic Kernel 1.80.1 + Google (Gemini) connector 1.80.1-alpha, EF Core 10 + SQLite,
PdfPig (парсинг PDF), DotNetEnv, Serilog, xUnit + NSubstitute + FluentAssertions.

**Связь с проектом A:** Mini-CDS (`C:\Develop\Mini-CDS`) — источник конвенций и готового паттерна
append-only аудита под Part 11. Формат `Mini-CDS\Reports\1.csv` поддерживается ингестом напрямую,
что делает связку A+C («покажи пики образца и объясни отклонение») загрузкой одного файла.

## Методика ведения

1. Запись **добавляется перед каждым `git commit`**. Исторические записи не правятся — только новые
   блоки в хронологическом порядке.
2. Коммит делается на закрытии каждой **части** milestone и обязательно на закрытии самого milestone.
3. Структура записи:
   ```
   ### [Дата] Milestone N, часть M: <короткий заголовок>

   **План:** что собрались сделать.

   **Сделано:** что реально написано / изменено.

   **Проблемы / ловушки:** ошибки проектирования, баги, спорные решения.

   **Итог:** тесты (N/N), статус, что дальше.
   ```
4. Раздел **«Проблемы / ловушки» обязателен**, даже когда всё прошло гладко — тогда пишется, что именно
   могло сломаться и почему не сломалось. Пустой раздел = запись не принята.
5. Конкретика вместо общих слов: не «были проблемы с эмбеддингами», а точное имя API, номер warning
   компилятора и что пришлось поменять.
6. Критерий завершения milestone: код собирается без предупреждений, все тесты зелёные,
   запись в дневнике есть, коммит сделан.

---

## Текущее состояние (2026-09-10)

| Milestone | Описание | Статус |
|-----------|----------|--------|
| 1 | Semantic Kernel + базовый чат (каркас, .env, Serilog, Gemini-адаптеры) | ✅ Закрыт (32/32 offline + 5/5 live, 0 падений) |
| 2 | Ingest: парсинг + чанки + метаданные | ✅ Закрыт (156/156 offline + 5/5 live skip, 0 падений) |
| 3 | Embeddings + vector store + поиск top-k | ✅ Закрыт (183/183 offline, 0 падений) |
| 4 | Grounded-генерация + citations + «не знаю» | ⬜ Не начат |
| 5 | AI Audit Log (append-only) + PII masking | ⬜ Не начат |
| 6 | UI: чат + панель источников + журнал аудита | ⬜ Не начат |
| 7 | Тесты (golden set) + README + GIF | ⬜ Не начат |

---

## Архитектурные решения, зафиксированные навсегда

| Решение | Обоснование |
|---------|-------------|
| `net10.0`, не `net8.0` из исходного ТЗ | .NET 8 SDK на машине отсутствует (установлены 9.0.315 и 10.0.301); Mini-CDS уже на net10.0 — расхождение версий разбило бы общий стек |
| ASP.NET Core Minimal API + Blazor Server в одном хосте | Проект A уже демонстрирует WPF; C должен закрывать трек AI Backend. Bonus: Mini-CDS может дёргать API для связки A+C |
| Свои порты `IGroundedChatClient` / `IEmbeddingService` вместо SK-типов наружу | (a) Domain/Application остаются без фреймворков (AGENT.md 2.1); (b) альфа-коннектор уже один раз поменял API — свой порт запирает нестабильность в двух файлах-адаптерах; (c) golden set в CI требует детерминированную заглушку того же контракта |
| Свой vector store на EF Core + SQLite, не `CommunityToolkit.VectorData` | Официального SQLite-коннектора в `CommunityToolkit.VectorData.*` не существует; биндинги `sqlite-vec` для .NET — сторонние пакеты сомнительного качества. `IVectorStore` остаётся seam'ом для Qdrant |
| Эмбеддинги как BLOB float32 LE без заголовка | `MemoryMarshal.Cast<byte, float>` даёт zero-copy; размерность и модель — в отдельных колонках, заголовок внутри BLOB только усложнил бы приведение |
| L2-нормализация при записи → поиск только dot product | Один `sqrt` на вектор при ингесте вместо одного `sqrt` на каждую пару при каждом запросе; в горячем цикле ноль нормализаций |
| `Temperature = 0` для генерации | Воспроизводимость — то, что делает `PromptHash` осмысленным доказательством: тот же хеш + та же модель должны давать тот же ответ |
| Маскирование PII на ингесте, а не только в момент промпта | Если маскировать только перед промптом, **немаскированный** текст всё равно уходит в Google при embedding-вызове на ингесте. Маскирование на границе доверия означает, что сырой текст не покидает её никогда |
| В аудите — маскированный текст + keyed HMAC оригинала, не обратимый шифртекст | Атрибутивность по Part 11 несут `ActorUserId` + `TimestampUtc` + `CorrelationId`. Голый SHA256 короткого вопроса брутфорсится, поэтому минимум — HMAC с ключом. Обратимый шифртекст превратил бы append-only журнал в хранилище PII |
| Fail-closed порядок: аудит пишется и awaited **до** возврата ответа | Если запись аудита не удалась — ответ не отдаётся. Обратный порядок означал бы, что существует ответ без записи в журнале, то есть журнал неполон по построению |
| Композиция промпта конкатенацией строк, не SK-шаблонами | Одна фиксированная композиция — движок шаблонов даёт нулевую отдачу (KISS/YAGNI). Плюс `PromptHash` обязан быть доказуемо хешем того, что увидела модель: своя сборка убирает неоднозначность рендерера |
| Cookie auth, не JWT | Blazor Server circuit — это WebSocket, который не может нести `Authorization: Bearer`. Cookie закрывает и HTTP-эндпоинты, и интерактивный circuit одним механизмом, без пакетов, и даёт `HttpContext.User` → `ActorUserId` для аудита бесплатно |
| Не стриминг в v1 | Детекция отказа, выделение «Обоснование» и запись аудита требуют полного текста — стриминг всё равно буферизуется в строку, удваивая поверхность альфа-коннектора |
| Append-only в 2 слоя (EF-интерцептор + SQL-триггеры) | Интерцептор ловит мутации через EF до генерации SQL; триггеры ловят сырой SQL в обход EF. Каждый слой закрывает свой вектор |
| Детерминированные токены маскирования | Один и тот же вход обязан давать один и тот же `PromptHash` навсегда; случайный или счётчиковый токен сломал бы воспроизводимость доказательства |
| Хост стартует без `GEMINI_API_KEY` — warning, а не fail-fast (осознанное расхождение с планом) | План требовал fail-fast, но golden set и E2E на `WebApplicationFactory` обязаны поднимать приложение **без ключа**, иначе CI не запускается вовсе. Компромисс: warning при старте, описательное исключение при первом обращении внутри адаптера, `geminiKeyPresent` в `/health`. Деградация видна, но не блокирует не-AI маршруты |
| Эмбеддинги через `AddGoogleAIEmbeddingGenerator` → `Microsoft.Extensions.AI.IEmbeddingGenerator`, не SK-нативный путь | `AddGoogleAIEmbeddingGeneration` и `GoogleAITextEmbeddingGenerationService` в 1.80.1-alpha помечены `[Obsolete]`. Коннектор сам мигрирует на `Microsoft.Extensions.AI`; встать на устаревшее имя означало бы гарантированный повторный churn |
| Чат остаётся на `AddGoogleAIGeminiChatCompletion`, хотя `AddGoogleAIChatClient` новее | Второй параметр `AddGoogleAIChatClient` — `Google.GenAI.Client`. Назвать этот тип в нашем коде = добавить прямую ссылку на `Google.GenAI`, а она запрещена (коннектор скомпилирован против 0.11.0, NuGet зарезолвил бы непроверенный мажор). Асимметрия «чат на SK, эмбеддинги на M.E.AI» принята осознанно и невидима выше Infrastructure |
| Модель чата — закреплённое имя (`gemini-3.5-flash`), никогда не rolling-алиас | В аудите `Model` — доказательство того, **какая** модель ответила. `gemini-flash-latest` молча переезжает на новую версию, и запись становится ложной, оставаясь внешне корректной. Это та же причина, что и `Temperature = 0` |
| Никакого фолбэка на вторую embedding-модель (план предлагал `text-embedding-004`) | `text-embedding-004` = 768 измерений против 3072 у `gemini-embedding-001`. Успешный фолбэк наполнил бы хранилище **другим векторным пространством**, чем то, о котором думает оператор. Громкий отказ + смена конфига + ре-ингест правильнее тихой подмены |
| Схема БД в конвенции Mini-CDS: snake_case таблицы, PascalCase-колонки, enum как TEXT(32), DateTime с Kind-конвертером | Оба проекта должны читаться как работа одного инженера, а enum-как-имя даёт forensic-читаемость: БД, открытую руками, не нужно дешифровать. Kind-конвертер обязателен: SQLite не имеет типа datetime и провайдер возвращает `Kind=Unspecified`, из-за чего любое сравнение или сериализация молча съезжает |
| Все FK — `DeleteBehavior.Restrict`, никогда `Cascade` | Каскад удалил бы историю версий и чанки, на которые ссылаются старые строки аудита, то есть уничтожил бы доказательство вместе с тем, что его цитирует. Проверено на двух уровнях: EF бросает `InvalidOperationException` («association … has been severed») уже на `Remove()`, до генерации SQL, а сырой `DELETE` в обход трекера останавливает сама SQLite (`FOREIGN KEY constraint failed`) |
| `Chunk.Text` без `HasMaxLength` — осознанно | В SQLite TEXT не имеет длины, поэтому ограничение ничего не защищает: оно лишь превратило бы легально длинную секцию в ошибку сохранения в тот день, когда `MaxChunkChars` в appsettings поднимут. Длина чанка — забота чанкера, не схемы |
| WAL включается одноразовым `PRAGMA journal_mode=WAL` на старте хоста, не в connection string и не в миграции | У `Microsoft.Data.Sqlite` ключа journal-mode не существует вовсе; в миграцию положить нельзя — миграции исполняются в транзакции, а PRAGMA внутри транзакции — no-op. Значение персистентно в файле БД, поэтому одного выполнения после `MigrateAsync` достаточно навсегда |
| Сидинг идемпотентен: существующая строка пользователя — запись, а не шаблон | Сравнение имён `OrdinalIgnoreCase`, существующие аккаунты никогда не перезаписываются и не перехешируются. Иначе каждый рестарт переписывал бы пароли и разрывал связь между `ActorUserId` в аудите и учётными данными, которые реально действовали в момент записи |
| Слияние секций в чанкере — только соседние секции **одного родителя**, путь слитого чанка = общий родитель | «4.1 Скорость» + «4.2 Температура» → один чанк с путём «4. Методика»: трёхстрочная секция сама по себе неретривабельна. Но секции из разных веток не сливаются: чанк, покрывающий несвязанные темы, был бы честной цитатой ни о чём. Путь «общий родитель» точнее описывает содержимое слитого чанка, чем путь первой секции |
| CSV-строки не оверлепятся (в отличие от текста) | Предложение, разрезанное границей бюджета, теряет смысл без повтора; строка измерения — независимый факт, и её дубль в соседнем окне дал бы двойную цитату одного и того же пика. Вместо оверлепа каждое окно повторяет преамбулу и шапку |

## Известные технические долги

- ~~`Marker.cs` в Application — временный якорь сборки, чтобы `LayeringTests` могли ссылаться на пустой
  проект. В Domain маркер **удалён** в M1 часть 3: там появились реальные порты
  (`Abstractions\IGroundedChatClient` и др.), и тест теперь ссылается на них. Удалить оставшийся в
  Milestone 2.~~ — закрыто в M2 часть 2: в Application появился `AuthService`, тест переведён на него.
- `/health` сейчас отвечает только про конфигурацию Gemini (`geminiKeyPresent`, модели). По плану он
  обязан также сообщать доступность БД и размер векторного индекса — обе проверки физически нечего
  вызывать до Milestone 2 (`LabAiDbContext`) и Milestone 3 (`EfVectorStore`). Расширить там же.
- Временный `POST /api/chat` — **без авторизации и без строки аудита**. Существует только как смоук
  обвязки Gemini и осознанно нарушает собственный принцип продукта («нет ответа без записи в
  журнале»). Удалить в Milestone 4 при переходе на `POST /api/ask`.
- В Gemini-адаптерах нет ретраев с backoff на 429/503 (план: 3 попытки, уважать `Retry-After`).
  Это не теория: живой прогон показал реальный `503 UNAVAILABLE` от `gemini-3.6-flash`. Сегодня 503
  прокидывается наружу сырым `HttpOperationException`.
- `Xunit.SkippableFact` в тестовом проекте — вынужденная зависимость: в xunit 2.9.3 нет динамического
  skip'а (`Assert.Skip` появился только в v3), а live-сьют обязан честно показывать «Skipped», а не
  «Passed». Уйдёт сама при миграции на xunit v3.
- ~~**WAL не включён.** План обещал «WAL в connection string», но у `Microsoft.Data.Sqlite` такого ключа
  нет вовсе. Включается одним `PRAGMA journal_mode=WAL` на старте после `MigrateAsync` (значение
  сохраняется в файле БД навсегда). В миграцию положить нельзя: миграции идут в транзакции, а внутри
  транзакции этот PRAGMA — no-op. Сделать в M2 часть 2, вместе с запуском миграций на старте хоста.~~ —
  закрыто в M2 часть 2: PRAGMA исполняется в `Program.cs` сразу после `MigrateAsync`, активность
  подтверждена файлами `lab.db-wal`/`lab.db-shm` на диске.
- **Инверсия зависимости в плане:** миграция append-only триггеров стояла в M2 часть 6, но таблицы
  `ai_audit_entries` не существует до M5. Перенесено целиком в M5 часть 3 — сущность `AiAuditEntry`,
  её миграция, `AppendOnlyInterceptor` и SQL-триггеры едут вместе, чтобы защита появлялась одновременно
  с таблицей. M2 часть 6 остаётся только про авторский корпус.
- `Demo:Password = "demo123"` закоммичен в `appsettings.json` — осознанный компромисс для демо
  (без него первый старт печатал бы три сгенерированных пароля в консоль, и повторяемый сценарий
  записи GIF стал бы зависеть от скролла консоли). README (M7) обязан явно помечать его как
  demo-only и запрещать к использованию вне локальной машины.
- Line-span'ы окон CSV-чанкера вычисляются арифметически (`LineStart секции + индекс строки`) и
  верны только для **подряд идущих** строк данных. Парсер пропускает пустые строки, и пустая
  строка между данными сдвинула бы маппинг на файл. Приборные экспорты пустых строк между
  записями не содержат (проверено на `1.csv`); если появится формат с пустыми строками — маппинг
  нужно будет переносить через парсер, а не восстанавливать в чанкере.
- `GenericTextChunker` не сливает короткие секции: корень-массив JSON дробится на чанк-на-элемент,
  даже если элемент из одной строки. Для корпуса (`balance-log` — один корневой объект) это
  неважно; если в golden set появится плоский массив мелких элементов, вернуть слияние придётся
  отдельно для JSON.
- `SourceDocument.SourcePath` не имеет индекса — `FindActiveBySourcePathAsync` делает полный скан
  таблицы. Для демо-корпуса (десятки документов) незаметно; если корпус вырастет до сотен файлов
  с частыми ре-ингестами, добавить `HasIndex(SourcePath)` + миграцию.
- E2E-тесты в M7 на `WebApplicationFactory` обязаны перекрывать `ConnectionStrings:LabDb` в тестовой
  конфигурации — иначе фабрика при старте натравит `MigrateAsync` + сидинг на настоящий
  `data/lab.db` рядом с исходниками. Подводный камень обнаружен при подключении миграций в хост
  (часть 2), задокументирован заранее.
- SHA256 hash chain журнала (`PrevHash`/`Hash` + `VerifyChainAsync`) — осознанно отложен: пользователь
  выбрал более простой вариант append-only. Дизайн оставляет место (детерминированный `max(Id)+1`
  в транзакции), реализация ~1 день, паттерн целиком есть в Mini-CDS.
- Qdrant-реализация `IVectorStore` — seam есть, кода нет (YAGNI). Brute-force точен, потолок — память:
  3072-dim float32 ≈ 12 КБ/вектор, ~80k векторов на ГБ.
- `Rag:MinScore = 0.35` — стартовое значение по умолчанию, не измеренное. Калибруется по golden set
  в Milestone 3/7; тесты должны утверждать итоговое число, а не оставлять его догадкой.
- Маскирование имён — best-effort эвристика (срабатывает только при роль-ключевике в той же строке).
  Без триггера `Agilent 1260` давал бы false positive.
- FluentAssertions под коммерческой лицензией — унаследовано от Mini-CDS; при переходе в прод заменить.
- Пути к БД и логам относительные (от CWD процесса) — унаследовано от Mini-CDS; для продакшена нужны
  абсолютные от `AppContext.BaseDirectory`.
- README Mini-CDS написан по-русски, хотя соглашение гласит «код и README — EN». Здесь осознанно
  расходимся в пользу EN (портфолио нацелено на Agilent, Shanghai) — стоит привести A к тому же виду.

---

## Хронологический лог

### 2026-09-10 — Планирование и разведка окружения

**План:** разобраться, что уже есть на машине, зафиксировать стек и снять технические риски до первой
строчки кода.

**Сделано:**
- Обследован `C:\Develop\Lab-ai-assist`: каталог пустой, только `AGENT.md` со стандартами кода
  (16 секций, включая DOD §2.4 для hot paths и маскирование чувствительных данных в логах §3.2).
- Найден и прочитан проект A `C:\Develop\Mini-CDS` (git `ddenvy/Mini-CDS`): Clean Architecture,
  net10.0, готовый append-only аудит под Part 11. Оттуда взяты как образцы:
  `AuditEntry.cs`, `IAuditTrail.cs`, `AuditTrail.cs` (`max(Id)+1` в транзакции),
  `AppendOnlyInterceptor.cs`, миграция `20260909030320_AddAppendOnlyTriggers.cs` (SQL-триггеры
  `RAISE(ABORT)`), `HashChain.cs` (length-prefix канонизация), `PasswordHasher.cs` (PBKDF2-SHA256,
  100k итераций, `FixedTimeEquals`), `DbSeeder.cs`, `ServiceCollectionExtension.cs`,
  `appsettings.json` (Serilog compact JSON).
- Проверены фактические версии пакетов через NuGet flat-container API, а не по памяти.
- Проверен формат `Mini-CDS\Reports\1.csv`: 28 строк, один образец `Sample_003`, строка на пик,
  **UTF-8 BOM**, колонка `Tailing` иногда пустая.
- Четыре архитектурные развилки согласованы с пользователем: net10.0, Blazor Server + Minimal API,
  Gemini, свой EF Core + SQLite vector store, реальный логин.

**Проблемы / ловушки:**
- **Исходное ТЗ требовало .NET 8, но .NET 8 SDK на машине не установлен** — `dotnet --list-sdks`
  показал только 9.0.315 и 10.0.301, рантаймы 9.0.17 и 10.0.9. Таргет на net8.0 потребовал бы
  ставить SDK и разошёлся бы с Mini-CDS. Решение: net10.0, подтверждено пользователем.
- **`Microsoft.SemanticKernel.Connectors.Google` не имеет стабильной версии** — весь ряд
  `1.76.0-alpha … 1.80.1-alpha`. При этом `Microsoft.SemanticKernel` сам стабильный (1.80.1).
  То есть альфа-статус — не случайность версии, а постоянное состояние коннектора. Отсюда решение
  закрыть его своими портами и зафиксировать exit ramp в ADR-0002.
- **API эмбеддингов в коннекторе уже сменилось.** Проверил рефлексирующим probe-проектом в `/tmp`
  (потом удалён), а не по документации: `AddGoogleAIEmbeddingGeneration` несёт атрибут
  `[Obsolete("Use AddGoogleAIEmbeddingGenerator instead.")]`. Актуальная поверхность —
  `AddGoogleAIEmbeddingGenerator(modelId, apiKey, GoogleAIVersion.V1_Beta, serviceId, httpClient,
  dimensions)`, возвращающая `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>`,
  а не `ITextEmbeddingGenerationService`. Заодно выяснилось, что `GeminiPromptExecutionSettings`
  имеет `Temperature`/`TopP`/`MaxTokens` как nullable, а `GoogleAIEmbeddingGenerator` **не**
  отдаёт `ModelId` публичным свойством — значит фактически использованную модель адаптер должен
  отслеживать сам.
- **NuGet id пакета PdfPig — именно `PdfPig`, а не `UglyToad.PdfPig`** (последний тоже существует и
  отдаёт 200, но это другой id; namespace при этом `UglyToad.PdfPig`). Стабильная версия 0.1.16.
  iText7 не рассматривался — AGPL.
- Официального SQLite-коннектора для векторов в экосистеме .NET нет: в
  `CommunityToolkit.VectorData.*` есть Qdrant/InMemory/PgVector/Redis/Weaviate/Cosmos, SQLite отсутствует;
  пакеты вида `HiraokaHyperTools.sqlite-vec` — сторонние, без внятной поддержки. Отсюда свой store.
- `Serilog.AspNetCore` **10.0.0** транзитивно приносит ровно те версии, которые план фиксировал
  отдельно (Extensions.Hosting 10.0.0, Formatting.Compact 3.0.0, Settings.Configuration 10.0.0,
  Sinks.File 7.0.0) — четыре явных ссылки были бы дублированием с риском конфликта версий.
  Проверил через nuspec и оставил один пакет.
- Прямую ссылку на `Google.GenAI` добавлять нельзя: коннектор скомпилирован против 0.11.0,
  а NuGet резолвит минимум — явная ссылка на свежий 1.21.0 дала бы несовместимость.

**Итог:** план утверждён (7 milestone, разбиты на части), риски сняты до кода. Тестов ещё нет.
**Далее:** Milestone 1, часть 1 — каркас solution.

---

### 2026-09-10 — Milestone 1, часть 1: Skeleton solution

**План:** создать решение с пятью проектами, настроить зависимости, получить зелёную сборку без
предупреждений и работающий тестовый harness.

**Сделано:**
- `git init -b main` в `C:\Develop\Lab-ai-assist`.
- `Lab-ai-assist.slnx` — новый XML-формат solution, та же форма, что `Mini-CDS.slnx`
  (папки `/src/` и `/tests/`).
- `Directory.Build.props` — копия Mini-CDS: `Nullable=enable`, `ImplicitUsings=enable`,
  `LangVersion=latest`.
- `.gitignore` — на базе Mini-CDS, но секция секретов вынесена наверх и усилена: `.env`, `.env.local`,
  `*.pfx`, `*.snk`. Плюс runtime-артефакты `data/`, `logs/`, `*.db*`.
- `.env.example` — `GEMINI_API_KEY`, `GEMINI_MODEL=gemini-2.5-flash`,
  `GEMINI_EMBEDDING_MODEL=gemini-embedding-001`, `PII_HMAC_KEY`. Каждый ключ с комментарием, зачем он
  и как сгенерировать (`openssl rand -hex 32`).
- Пять проектов: `LabAi.Domain` (ноль зависимостей), `LabAi.Application` (→ Domain),
  `LabAi.Infrastructure` (→ Application + Domain; SemanticKernel 1.80.1,
  SemanticKernel.Connectors.Google 1.80.1-alpha, DotNetEnv 3.2.0),
  `LabAi.Web` (`Microsoft.NET.Sdk.Web`; → все три; Serilog.AspNetCore 10.0.0),
  `LabAi.Tests` (xunit 2.9.3, NSubstitute 6.2.0, FluentAssertions 8.10.0, Mvc.Testing 10.0.12,
  coverlet 6.0.4).
- `src/LabAi.Web/Program.cs` — минимальный хост-заглушка и `appsettings.json` с полным блоком
  конфигурации `Rag` (TopK 5, MinScore 0.35, MaxChunkChars 2000, OverlapChars 200,
  EmbeddingBatchSize 16, OperationTimeoutSeconds 60), чтобы конфиг не переизобретать в M2–M4.
- `tests/LabAi.Tests/Architecture/LayeringTests.cs` — guard-тест Clean Architecture: Domain не
  ссылается ни на один другой проект и не тянет EF Core / Semantic Kernel / ASP.NET / Serilog;
  Application не ссылается на Infrastructure и Web. Тест написан сейчас, но платит позже — любая
  случайная зависимость в Domain упадёт здесь, а не на code review.
- `Marker.cs` в Domain и Application — временный якорь сборки (см. технические долги).

**Проблемы / ловушки:**
- **`dotnet restore` прошёл с первого раза** — главный риск milestone (альфа-коннектор на net10.0)
  не выстрелил: у пакета есть отдельная TFM-группа `lib/net10.0`, так что asset resolution не
  откатывался на netstandard2.0. Могло быть иначе — у альфа-пакетов свежая TFM-группа появляется
  не всегда, и тогда SK-тины тянулись бы из урезанной сборки без `IEmbeddingGenerator`.
- **FluentAssertions не подхватился** — сборка дала три `CS1061: "string[]" не содержит определения
  "Should"`. Причина: в тестовом csproj был глобальный `<Using Include="Xunit" />` (унаследован от
  Mini-CDS), но не было `<Using Include="FluentAssertions" />`. Добавил глобально, чтобы не
  повторять `using` в каждом файле тестов.
- **Guard-тест initially упал, и это содержательная находка.** Я написал
  `ReferencedProjectNames(Application).Should().BeEquivalentTo(["LabAi.Domain"])` — и получил пустой
  фактический список. Причина: **компилятор C# не записывает в метаданные сборки ссылки, которые код
  фактически не использует**. Пока в Application лежит только `Marker`, ссылка на Domain существует
  в csproj, но отсутствует в `Assembly.GetReferencedAssemblies()`. Позитивное утверждение
  «должен ссылаться на X» в принципе ненадёжно как архитектурный guard — оно ломается и
  восстанавливается само по себе по мере того, как код начинает трогать типы соседнего слоя.
  Переписал на утверждение только запретного направления (`NotContain(["LabAi.Infrastructure",
  "LabAi.Web"])`), что и соответствует имени теста. Вывод записан в комментарий к тесту.
- Пустые проекты `LabAi.Domain` / `LabAi.Application` собираются в пустые сборки без ошибок —
  отдельной заглушки для этого не нужно, но `LabAi.Web` на `Microsoft.NET.Sdk.Web` без `Program.cs`
  не собрался бы (нет точки входа при `OutputType=Exe`). Поэтому минимальный хост добавлен сразу.

**Итог:** сборка чистая — **0 предупреждений, 0 ошибок**; тесты **3/3 зелёные**. Каркас solution
готов, пакеты зарезолвлены, альфа-коннектор на net10.0 работает. Milestone 1 открыт (часть 1/3).
**Далее:** часть 2 — загрузка `.env`, Serilog с compact JSON, correlation-id middleware, `/health`.

---

### 2026-09-10 — Milestone 1, часть 2: Конфигурация, Serilog, correlation id, /health

**План:** научить хост читать `.env`, писать структурированные логи, сквозить correlation id через
все события одного запроса и отдавать `/health` — так, чтобы ни одна строка лога и ни одно поле
ответа не содержали секрет.

**Сделано:**
- `src/LabAi.Infrastructure/Ai/GeminiOptions.cs` — чистые данные: `ApiKey`, `ChatModelId`,
  `EmbeddingModelId` + вычисляемое `IsConfigured`. Все свойства `init`, объект неизменяемый и
  регистрируется как singleton.
- `src/LabAi.Web/Infrastructure/GeminiOptionsFactory.cs` — разрешение опций: переменные окружения
  (`GEMINI_API_KEY`, `GEMINI_MODEL`, `GEMINI_EMBEDDING_MODEL`) побеждают секцию `Gemini` в
  `appsettings.json`, дальше — значения по умолчанию. Пустая строка и whitespace считаются
  «не задано».
- `src/LabAi.Web/Infrastructure/CorrelationIdMiddleware.cs` — принимает `X-Correlation-Id` извне,
  иначе берёт `Activity.Current?.Id`, иначе генерирует `Guid("N")`; кладёт в `HttpContext.Items`,
  эхом возвращает в заголовке ответа и пушит в `LogContext`.
- `src/LabAi.Web/Endpoints/HealthEndpoints.cs` + `HealthResponse` — `GET /health`: `status`
  (`healthy` / `degraded`), `geminiKeyPresent`, `chatModel`, `embeddingModel`, `correlationId`.
- `src/LabAi.Web/Program.cs` переписан: `DotNetEnv.Env.TraversePath().Load()` → bootstrap-логгер →
  `UseSerilog(ReadFrom.Configuration + Enrich.FromLogContext)` → регистрация `GeminiOptions` →
  `UseMiddleware<CorrelationIdMiddleware>` → `UseSerilogRequestLogging` → маршруты. Всё обёрнуто в
  `try/catch(Log.Fatal)/finally(Log.CloseAndFlush)` — паттерн из Mini-CDS.
- `appsettings.json`: блок `Gemini` с **пустым** `ApiKey` (секрет в конфиг не кладётся принципиально),
  блок `Serilog` — Console + File (`logs/labai-.log`, `rollingInterval: Day`,
  `rollOnFileSizeLimit`, 10 МБ, `CompactJsonFormatter`), `Properties:Application = LabAi`.
- `.gitattributes` — `* text=auto eol=lf` плюс явные правила по расширениям и `binary` для
  `.png/.gif/.pdf/.db`.
- Тесты: `CorrelationIdMiddlewareTests` (5) и `GeminiOptionsFactoryTests` (5).

**Проблемы / ловушки:**
- **План противоречил сам себе, и это выяснилось только сейчас.** В плане написано «fail-fast при
  отсутствии `GEMINI_API_KEY`», а в разделе тестирования — что golden set и E2E на
  `WebApplicationFactory` должны проходить **офлайн, без ключа**. Fail-fast на старте сделал бы
  невозможным сам хост в тестах. Разрешил в пользу деградации: warning при старте, исключение при
  первом реальном обращении к адаптеру (часть 3), `geminiKeyPresent` в `/health`. Решение занесено
  в таблицу «зафиксированных навсегда» как осознанное расхождение с планом.
- **`DotNetEnv` обязан вызываться до `WebApplication.CreateBuilder`.** `CreateBuilder` одноразово
  снимает окружение процесса в `IConfiguration`; всё, что DotNetEnv выставит позже, конфигурация уже
  не увидит. Проверил практически: с `.env` на диске `/health` вернул `healthy`, без него — `degraded`.
- **Одна строка лога упорно приходила без `CorrelationId`.** Это оказался `Request finished ...` от
  `Microsoft.AspNetCore.Hosting.Diagnostics`: hosting-слой пишет его **снаружи** middleware pipeline,
  поэтому `LogContext`, запушенный внутри конвейера, для него недостижим в принципе. Заодно строка
  дублировала Serilog-овский `HTTP {RequestMethod} {RequestPath} responded {StatusCode}`. Поднял
  override `Microsoft.AspNetCore.Hosting` до `Warning`. Аналогично приглушил
  `Microsoft.AspNetCore.Http.Result` — `OkObjectResult` на каждый ответ писал две строки
  «Writing value of type ... as Json». После правки: **ноль** request-scoped строк без `CorrelationId`
  (проверено `grep '"RequestPath"' | grep -vc CorrelationId` → `0`).
- **Порядок `UseSerilogRequestLogging` относительно correlation middleware критичен, и я сначала
  поставил его неправильно мысленно.** Middleware dispose'ит `LogContext.PushProperty`, когда
  downstream возвращается. Если зарегистрировать request logging **до** correlation middleware, его
  completion-событие окажется уже вне scope и придёт без id. Поставил после — и зафиксировал
  причину комментарием в `Program.cs`, чтобы при будущем рефакторинге порядок не «починили».
- **`GeminiOptionsFactory` чуть не уехал в Infrastructure.** `IConfiguration` там доступен только
  транзитивно — через `DotNetEnv` → `Microsoft.Extensions.Configuration.Abstractions` **1.1.2**.
  Опереться на транзитивную зависимость с таким полом — значит получить сюрприз при первом же
  обновлении DotNetEnv. Разнёс: данные (`GeminiOptions`) в Infrastructure, чтение env+config —
  в Web, это буквально работа composition root. Побочный выигрыш: `AddLabAiGemini` в части 3 примет
  готовые опции, и Infrastructure не понадобится `IConfiguration` вообще.
- **`Activity.Current?.Id` — это не GUID.** В реальном хосте ASP.NET Core создаёт Activity на запрос,
  и id приходит в W3C-формате `00-245e8ea0...-88b4af48...-00`, тогда как в unit-тесте `Activity.Current`
  равен `null` и срабатывает ветка `Guid("N")`. Наивная assertion на «32 hex-символа» прошла бы в
  тесте и упала в рантайме. Поэтому тест утверждает только непустоту и **совпадение** id в заголовке,
  в `HttpContext.Items` и в том, что видит downstream.
- **Проверку приоритета env нельзя делать через `Environment.SetEnvironmentVariable`.** Переменные
  окружения процесса глобальны, а xunit гоняет тестовые классы параллельно — получилась бы гонка между
  `GeminiOptionsFactoryTests` и будущими тестами, читающими тот же `GEMINI_API_KEY`. Сделал вторую
  перегрузку фабрики с `Func<string, string?>`, и тест стал полностью герметичным.
- **Гигиена секрета при ручном прогоне.** Чтобы проверить ветку `healthy`, мне понадобился ключ.
  Создал временный `.env` с заведомо фиктивным значением, предварительно убедившись
  `git check-ignore -v .env` → `.gitignore:2`, и удалил файл сразу после проверки (`ls .env` →
  «No such file»). Настоящий ключ из `C:\Develop\JobJoy\backend\.env` не читался и не копировался;
  в лог и в ответ `/health` попадает только булев `geminiKeyPresent`, никогда не значение.
- Могло сломаться, но не сломалось: каталог `logs/` Serilog File sink создаёт сам, отдельной
  инициализации не нужно; `DotNetEnv.Env.TraversePath().Load()` при отсутствии `.env` молча
  возвращает пустой результат, а не бросает — иначе хост без ключа не стартовал бы вовсе.

**Итог:** сборка **0 предупреждений, 0 ошибок**; тесты **13/13 зелёные** (3 архитектурных + 10 новых).
Ручной прогон хоста: `GET /` → 200; `GET /health` → 200 с `degraded` без ключа и `healthy` с ключом;
`X-Correlation-Id` отдаётся в ответе и пробрасывается из входящего заголовка; все request-scoped строки
compact-JSON лога несут `CorrelationId`. Milestone 1 — часть 2/3 закрыта.
**Далее:** часть 3 — адаптеры `GeminiChatClient` и `GeminiEmbeddingService`, временный `POST /api/chat`,
ADR-0002, live-смоук тесты.

---

### 2026-09-10 — Milestone 1, часть 3: Gemini-адаптеры, /api/chat, ADR-0002

**План:** закрыть альфа-коннектор Gemini двумя своими портами, проверить обвязку живьём на реальном
ключе, зафиксировать необратимые решения в ADR-0002 и закрыть Milestone 1.

**Сделано:**
- **Reflection-probe вместо документации.** Одноразовый проект `C:\tmp\apiprobe` (после использования
  удалён) выгрузил реальную поверхность `Microsoft.SemanticKernel.Connectors.Google 1.80.1-alpha`:
  сигнатуры всех `AddGoogleAI*`, помеченные `[Obsolete]`, свойства `GeminiPromptExecutionSettings`,
  контракт `IEmbeddingGenerator<string, Embedding<float>>` и — отдельно — поведение при пустом и при
  `null` ключе. Всё, что дальше написано в адаптерах, опирается на этот дамп, а не на память.
  Ловушка внутри самой разведки: `AppDomain.CurrentDomain.GetAssemblies()` **не** содержит сборок,
  типы которых ни разу не трогали, — первый прогон нашёл 0 google-типов, пока не добавил явный
  `Assembly.Load(...)`.
- **Порты в Domain** (`Abstractions\`): `IGroundedChatClient` + `GroundedChatResult`,
  `IEmbeddingService`. `Marker.cs` из Domain удалён, `LayeringTests` переведён на
  `typeof(IGroundedChatClient).Assembly`.
- **Infrastructure\Ai:** `GeminiNotConfiguredException`, `GeminiChatClient`
  (`Temperature = 0.0`, `TopP = 0.95`, `MaxTokens = 2048`, `CandidateCount = 1`),
  `GeminiEmbeddingService` (проверка «сколько текстов — столько векторов, в том же порядке»),
  `GeminiServiceCollectionExtensions.AddLabAiGemini(GeminiOptions)`.
- **Web:** `Endpoints\ChatEndpoints.cs` — временный `POST /api/chat` с потолком 4000 символов
  (AGENT.md 3.3: недоверенный ввод ограничивается до похода в платный внешний API) и маппингом
  «ключа нет» → **503**, а не 500: ничего не сломано, ретрай без смены конфига не поможет.
- **Перестановка пакетов.** `DotNetEnv` переехал из Infrastructure в Web — единственный потребитель
  `Program.cs`. В Infrastructure явно прописаны `Microsoft.Extensions.DependencyInjection.Abstractions`
  и `Microsoft.Extensions.Logging.Abstractions` **10.0.6**: оба используются напрямую, а транзитивно
  приехали бы с полом `1.x` от альфа-коннектора.
- **ADR-0002** (EN): 7 решений, exit ramp на REST `generateContent`/`embedContent` (<100 строк, ноль
  изменений выше Infrastructure), таблица альтернатив, обязательства.
- **Тесты:** 24 новых — 7 на чат-адаптер, 8 на embedding-адаптер, 4 на DI-граф, 5 live.
- **Живой прогон:** `dotnet run` → `/health` = `healthy` с `chatModel: gemini-3.5-flash`;
  `POST /api/chat` → 200 с настоящим ответом Gemini, включая запрос на русском.

**Проблемы / ловушки:**
- **`gemini-2.5-flash` — модель из плана — снята с обслуживания для новых ключей.** Live-тест упал с
  `HttpOperationException: 404`. Тело ответа Google: *«This model models/gemini-2.5-flash is no longer
  available to new users. Please update your code to use models/gemini-3.6-flash»*, одинаково на `/v1/`
  и `/v1beta/`. При этом модель **присутствует** в `GET /v1beta/models` — значит список моделей не
  является проверкой доступности, только реальный вызов `generateContent`. Прогнал кандидатов с нашим
  точным `generationConfig`: `gemini-3.6-flash` → **503 UNAVAILABLE** (перегруз), `gemini-3.5-flash`,
  `gemini-flash-latest`, `gemini-3.1-flash-lite` → 200. Взял **`gemini-3.5-flash`**: `flash-latest`
  отвергнут, потому что это rolling-алиас, а `Model` в аудите — доказательство того, какая модель
  ответила; алиас сделал бы запись ложной, оставив её внешне корректной. Рекомендованный Google
  `3.6-flash` отвергнут из-за нестабильной доступности — демо, падающее на первом вопросе, хуже модели
  на поколение старше. Это ровно тот класс поломок, который offline-тесты поймать не могут **в
  принципе**, — лучший аргумент в пользу live-сьютa из всех возможных.
- **`Assert.Skip` в xunit 2.9.3 не существует, хотя план утверждал обратное.** Компилятор выдал
  CS0411, резолвя `Skip` в `AsyncEnumerable.Skip` — уже это подсказало, что члена `Assert.Skip` нет.
  Проверил не по памяти, а в самом пакете: в `xunit.assert.xml` есть `Xunit.Sdk.SkipException.ForSkip`
  с прямой оговоркой *«this only works in v3 and later of xUnit.net»*, а в #Strings-куче DLL имени
  `Skip` нет вовсе. Обходных путей в v2 тоже нет: `Skip` у `[Fact]` — compile-time константа, а
  `ReflectionAttributeInfo` читает `CustomAttributeData`, не создавая экземпляр, поэтому трюк
  «вычислить `Skip` в конструкторе своего атрибута» не работает. Взял `Xunit.SkippableFact` 1.5.85
  (MS-PL, netstandard2.0) → `[SkippableFact]` + `Skip.IfNot`. Ранний `return` отверг сознательно: он
  рисует «Passed» там, где тест не выполнялся. Результат без ключа: **32 passed, 5 skipped**.
- **`GeminiPromptExecutionSettings.Temperature` — `double?`, а не `float?`.** План писал
  `Temperature = 0.0f`; probe показал `double?`, и `0.0f` не скомпилировался бы. Мелочь, которая
  ловится только дампом сборки.
- **`CandidateCount = 1` обязателен рядом с `Temperature = 0`.** У Gemini `candidateCount > 1`
  требует `temperature = 1.0`, то есть «несколько вариантов ответа» и «воспроизводимость»
  взаимоисключающи по построению API. Зафиксировано отдельным тестом, чтобы будущая правка не
  разъехала молча.
- **Регистрация с пустым ключом проходит, с `null` — падает сразу.** Probe: `AddGoogleAI*(model, "")`
  → регистрация успешна, исключение только при резолве (`ArgumentException`: «The value cannot be an
  empty string…»). Это и есть техническая возможность регистрировать Gemini **безусловно** и держать
  guard внутри адаптера — деградированный режим без двух разных DI-графов. С `null` так не вышло бы.
- **`AddGoogleAIChatClient` новее, но брать нельзя.** Его второй параметр — `Google.GenAI.Client`:
  пришлось бы назвать в своём коде тип из транзитивно запертого `Google.GenAI 0.11.0`. Прямая ссылка
  на свежий мажор сломала бы коннектор. Асимметрия «чат на SK `IChatCompletionService`, эмбеддинги на
  `Microsoft.Extensions.AI.IEmbeddingGenerator`» принята осознанно и выше Infrastructure невидима.
- **`EmbedAsync` намеренно передаёт `options: null`.** У `EmbeddingGenerationOptions` есть `ModelId`,
  и override модели per call — это ровно тот путь, которым в хранилище появляются два несравнимых
  векторных пространства. Написал тест, утверждающий, что options действительно `null`, иначе
  намерение не отличить от забывчивости.
- **NSubstitute: `Arg.Do`/`Arg.Any` нельзя ставить внутрь условных выражений.** Первая версия хелпера
  была `history is null ? Arg.Any<ChatHistory>() : Arg.Do<ChatHistory>(…)`. Эти методы работают
  побочным эффектом на внутренней очереди сопоставителей, поэтому ветвление нарушает порядок аргументов
  непредсказуемо. Заменил на безусловные `Arg.Do<T>(captured => cb?.Invoke(captured))`.
- **CS1503 в собственном тесте:** `Replying("the model answer")` при `params ChatMessageContent[]`.
  Второй ошибкой сборки был тот самый `Assert.Skip`. Обе — цена написания четырёх тестовых файлов
  подряд без промежуточной сборки; впредь компилировать после каждого.
- **Ложный 400 на первом ручном прогоне.** `curl -d '{"message":"…кириллица…"}'` из Git Bash вернул
  400 при полностью рабочем эндпоинте. То же тело, записанное в файл как UTF-8 и отправленное
  `--data-binary @file`, дало 200 и ответ «Да». Артефакт кодировки оболочки, а не кода — но проверять
  пришлось обязательно: продукт целиком русскоязычный, и «не работает по-русски» было бы фатально.
- **Гигиена ключа при живом прогоне.** Ключ подставлялся в переменную окружения конвейером
  `grep | cut | tr` и **ни разу не напечатан**; в отчётах фигурирует только `${#GEMINI_API_KEY}` = 53.
  Файл `.env` в репозитории на этот раз не создавался вовсе. Перед прогоном отдельно проверил, что на
  пути `DotNetEnv.Env.TraversePath()` (от CWD строго вверх) нет чужих `.env`, способных перезаписать
  переменную, — `C:\Develop\.env` и `C:\.env` отсутствуют.
- Могло сломаться, но не сломалось: `gemini-embedding-001` реально вернул **3072** измерения —
  совпало с планом, значит dimension guard в M3 будет считать от проверенного числа, а не от
  предположения; `services.AddLogging()` в тестовом проекте разрешается благодаря `<FrameworkReference
  Include="Microsoft.AspNetCore.App" />`, который приносит `Mvc.Testing`; четыре embedding-теста
  прошли с первого раза, то есть адаптер правильно разбирает `GeneratedEmbeddings<Embedding<float>>`
  и `ReadOnlyMemory<float> → float[]`.

**Итог:** сборка **0 предупреждений, 0 ошибок**; offline **32/32** (плюс 5 честно пропущенных live),
live **5/5** с реальным ключом. Ручной прогон: `/health` → `healthy`, `POST /api/chat` → 200 с живым
ответом `gemini-3.5-flash`, в том числе на русском. Строка лога адаптера содержит только
`ModelId`, `PromptChars`, `CandidateCount`, `AnswerChars` и `CorrelationId` — ни текста вопроса, ни
текста ответа (AGENT.md 3.2), проверено дампом полной JSON-строки. ADR-0002 подписан.
**Milestone 1 закрыт (3/3).**
**Далее:** Milestone 2, часть 1 — `LabAiDbContext` + конфигурации сущностей + миграция `InitialCreate`
+ `IDesignTimeDbContextFactory`.

---

### 2026-09-10 — Milestone 2, часть 1: Схема БД — DbContext, конфигурации, InitialCreate

**План:** описать доменную модель в Domain, настроить маппинг в конвенции Mini-CDS, сгенерировать и
**проверить прогоном** первую миграцию.

**Сделано:**
- **Разведка конвенций отдельным агентом.** Нужны были дословные `CdsDbContext`, `User`,
  `UserConfiguration`, `SampleConfiguration` (самая насыщенная), `CdsDbContextFactory`,
  `DbSeeder`, DI-расширение и список миграций Mini-CDS — то есть ~300 строк чужого кода, которые в
  основном контексте были бы мусором. Агент вернул выжимку с cheat-sheet'ом; дальше я писал по ней,
  не открывая файлы повторно.
- **Domain:** enum'ы `UserRole` (Operator/Analyst/Administrator — взят из Mini-CDS как есть, роли
  совпали с планом), `DocumentKind`, `DocumentStatus`; сущности `User` (форма Mini-CDS без изменений,
  `get; set;` — аккаунт мутируем), `SourceDocument` (`get; set;` — supersede меняет `Status` и
  `SupersededByDocumentId`), `Chunk` (`init`-only — чанк неизменяем, цитата в старом аудите обязана
  вести ровно на тот текст, который был использован).
- **Infrastructure\Persistence:** `LabAiDbContext` (primary constructor, `DbSet => Set<T>()`,
  `ApplyConfigurationsFromAssembly`), `LabAiDbContextFactory` (design-time, throwaway `design-time.db`),
  три `IEntityTypeConfiguration<T>`, `PersistenceServiceCollectionExtensions.AddLabAiPersistence`.
- **Миграция `20260910063859_InitialCreate`:** 3 таблицы (`users`, `source_documents`, `chunks`),
  3 FK (все `Restrict`), 7 индексов, enum'ы как `TEXT(32)`, `Embedding` как `BLOB`.
- **17 тестов против настоящей SQLite** (`DataSource=:memory:` + `Database.Migrate()`, не
  `EnsureCreated` — заодно проверяется, что сам скрипт миграции применяется): имена таблиц, enum
  именем по сырому SQL, `Kind=Utc` после перечитывания, байт-точный round-trip BLOB, уникальность
  `(DocumentId, ChunkIndex)`, цепочка supersede, обе пары тестов на запрет удаления и DI-регистрация.

**Проблемы / ловушки:**
- **`NU1605` не случился только потому, что я его предупредил.** EF Core 10.0.12 зависит от
  `Microsoft.Extensions.Logging 10.0.12`, а в Infrastructure обе абстракции были явно закреплены на
  **10.0.6**. Прямая ссылка на версию ниже транзитивно требуемой — это `NU1605 Detected package
  downgrade`, то есть ошибка восстановления, а не предупреждение. Проверил nuspec'и
  `microsoft.entityframeworkcore{,.sqlite}` через flat-container API **до** правки csproj и поднял обе
  до 10.0.12.
- **`dotnet ef` отказался генерировать миграцию:** «Your startup project 'LabAi.Web' doesn't reference
  Microsoft.EntityFrameworkCore.Design». Причина — ровно наше же решение: в Infrastructure пакет
  объявлен с `PrivateAssets=all`, значит наружу он не течёт, а тулинг требует его в startup-проекте.
  Проверил grep'ом Mini-CDS: там Design объявлен **в обоих** проектах, и в `MiniCds.Wpf` тоже с
  `PrivateAssets=all`. Зеркалировал — расхождение с планом (там Design был только в Infrastructure).
- **`Restrict` на обязательном FK срабатывает раньше, чем я предположил, — и мой тест это поймал на
  себе.** Написал «`SaveChanges()` бросает `DbUpdateException`» — упало. На самом деле EF Core
  контролирует severing обязательной связи **в change tracker**, и `InvalidOperationException`
  («The association … has been severed») прилетает уже из `Remove()`: удаление даже не попадает в
  очередь изменений. Это сильнее, чем я тестировал. Переписал ассерты — и **добавил вторую пару тестов
  на сырой SQL**, потому что трекерный контроль обходится одним `ExecuteSqlRaw`, и без такой проверки
  `Restrict` оставался бы декларацией, а не свойством базы.
- **Побочно закрылся вопрос, который я собирался отложить: `PRAGMA foreign_keys` у EF Core включён по
  умолчанию.** Никаких настроек соединения я не добавлял, а сырой `DELETE FROM users WHERE Id = 1`
  упал с `FOREIGN KEY constraint failed`. То есть защита держится на двух уровнях без единой строчки
  конфигурации. Mini-CDS тоже ничего не настраивает — теперь понятно, почему это у них работает.
- **Тест на BLOB упал из-за фикстуры, а не из-за BLOB.** Фабрика `NewChunk()` ставила по умолчанию
  `DocumentId = 1`, а фикстура сеяла только пользователя — FK не дал вставить чанк, и
  `DbUpdateException` выглядел как проблема с blob-колонкой. Починил двумя движениями: один актёр
  создаётся в конструкторе фикстуры (каждый документ обязан кем-то быть загружен), а документ
  создаётся явно в каждом тесте про чанки. Урок общий: **дефолтные значения в тестовых фабриках — это
  скрытая связь между тестами**, которая стреляет в третьем по счёту тесте.
- **`Database.GetConnectionString()` — extension-метод из `Microsoft.EntityFrameworkCore`**
  (`RelationalDatabaseFacadeExtensions`). CS1061 в новом файле без using; в соседнем тестовом файле
  тот же вызов компилировался, потому что using там уже был. Классическая ловушка «в одном файле
  работает, в другом нет».
- **Осознанно не добавлял уникальных индексов «на вырост».** Соблазн был сильный: unique на
  `ContentHash` (идемпотентность же!) или на `(SourcePath, Version)`. Оба упираются в семантику,
  которой ещё нет в коде: повторный ингест **старого** содержимого легален (откат к предыдущей версии),
  поэтому глобальный unique на хеш запретил бы законную операцию. Ограничения появятся в M2 часть 5,
  когда `IngestPipeline` определит, что именно считается дубликатом, — и тогда их можно будет добавить
  одной миграцией. Индексы сейчас только под реальные запросы: `ContentHash` (поиск дубликата),
  `Status` (снапшот только Active-документов), `(DocumentId, ChunkIndex)` unique (позиция чанка),
  `(EmbeddingModelId, EmbeddingDimension)` (стартовый dimension guard из M3).
- **`AiAuditEntry` в `InitialCreate` не попал — намеренно.** Сначала хотел описать всю схему сразу,
  чтобы append-only триггеры легли следом, как в Mini-CDS (у них `InitialCreate` и
  `AddAppendOnlyTriggers` разделены пятью секундами). Отказался: аудит не имеет отношения к ингесту, а
  таблица, которую три milestone никто не читает и не пишет, — мёртвый груз в миграции. Заодно в плане
  нашлась инверсия зависимости (триггеры стояли в M2 часть 6, а таблица создаётся в M5) — всё
  перенесено в M5 часть 3 единым блоком, см. технические долги.
- Могло сломаться, но не сломалось: `init`-only свойства `Chunk` EF Core материализует без
  конструктора с параметрами (тот же приём, что `AuditEntry` в Mini-CDS); `ApplyConfigurationsFromAssembly`
  подхватил все три конфигурации — иначе таблицы назывались бы `SourceDocuments` и `Chunks`, и тест на
  snake_case имена это бы поймал; `Sqlite:Autoincrement` проставился для `long` PK сам; файл
  `design-time.db` при генерации миграции не создался вовсе — EF нужна модель, а не подключение.

**Итог:** сборка **0 предупреждений, 0 ошибок**; offline **49/49** (плюс 5 skipped live), из них 17
новых. Миграция проверена реальным прогоном на SQLite, а не только фактом генерации. Milestone 2 —
часть 1/7.
**Далее:** часть 2 — перенос auth из Mini-CDS (`PasswordHasher`, `EfUserStore`, `AuthService`) +
`DbSeeder`, подключение `AddLabAiPersistence` и `MigrateAsync` в хосте, `PRAGMA journal_mode=WAL`.

---

### 2026-09-10 — Milestone 2, часть 2: Auth-стек из Mini-CDS, DbSeeder, запуск миграций на старте

**План:** перенести auth-стек из Mini-CDS (`PasswordHasher`, `EfUserStore`, `AuthService`), добавить
`DbSeeder`, подключить `AddLabAiPersistence` и `MigrateAsync` в хосте, включить WAL.

**Сделано:**
- **Порты в Domain** (`Abstractions\`): `IPasswordHasher`, `IUserStore`, `IAuthService`, record
  `AuthResult` со статиками `Success(User)` / `Failure(string)`. Все четыре файла — по интерфейсу,
  как требует AGENT.md; Infrastructure реализует, Application потребляет.
- **Infrastructure\Security\PasswordHasher.cs** — перенесён из Mini-CDS дословно: PBKDF2-SHA256,
  100 000 итераций, 16-байтная соль, 32-байтный ключ, `CryptographicOperations.FixedTimeEquals`,
  `Convert.TryFromBase64String` с проверкой `bytesWritten` (битый base64 в БД даёт честный «пароль не
  подошёл», а не `FormatException`).
- **Infrastructure\Persistence\EfUserStore.cs** — `AsNoTracking()` + `FirstOrDefaultAsync` по точному
  равенству имени (case-sensitive, паритет с Mini-CDS — покрыто тестом, а не надеждой).
- **Application\Auth\AuthService.cs** — все пути отказа возвращают одну и ту же generic-ошибку, а
  unknown-user / disabled-account заранее прожигают верификацию на **dummy-креденшалах** из `Lazy`:
  без этого неизвестное имя отвечало бы за микросекунды, реальное — за 100k итераций PBKDF2, и время
  ответа стало бы оракулом перечисления пользователей.
- **Infrastructure\Persistence\DbSeeder.cs** — три демо-аккаунта по одному на роль (`admin`/`analyst`/
  `operator`, чтобы демо показывало RBAC с обеих сторон), идемпотентность по `OrdinalIgnoreCase`,
  генерация пароля с алфавитом без `l 1 I O 0 o`, plaintext возвращается в результате и печатается в
  **Console**, а не в Serilog — файловый sink секрет не получает.
- **Program.cs** — fail-fast на отсутствие `ConnectionStrings:LabDb` (деградация, в отличие от
  Gemini), каталог БД создаётся из `SqliteConnectionStringBuilder.DataSource`, далее scope:
  `MigrateAsync()` → `PRAGMA journal_mode=WAL;` → `SeedAsync()`.
- **`Marker.cs` из Application удалён** — там появился `AuthService`; `LayeringTests` переведён на
  `typeof(AuthService).Assembly` и получил четвёртый тест `Application_ReferencesNoFrameworkPackages`
  (Domain-тесты на EF/SK/ASP.NET/Serilog распространены на Application).
- Тесты: `PasswordHasherTests` (7), `AuthServiceTests` (8), `DbSeederTests` (8), `EfUserStoreTests` (4),
  плюс обновлённые `LayeringTests`.
- **Ручной прогон хоста:** старт → `Database ready; seeded 3 account(s)`; `/health` → 200; в каталоге
  БД появились `lab.db-shm` и `lab.db-wal` (WAL активен); рестарт → `seeded 0 account(s)` — второй
  проход нашёл существующие аккаунты и не создал ни одного.

**Проблемы / ловушки:**
- **Тест поймал настоящий баг в коде, который я сам же и написал за час до этого.** Тест «алфавит
  генератора не содержит `l 1 I O 0 o`» упал: в пароле оказалась строчная `o`. Причина — строка
  `"abcdefghijk**mnop**qrstuvwxyz"`: я вычеркнул только `l`, а `o` оставил, хотя комментарий над
  строкой обещал исключение обоих. Классический разъезд комментария и кода на соседней строчке;
  тест именно на это и писался. Алфавит исправлен, тест теперь зелёный и навсегда охраняет обещание.
- **Второй упавший тест оказался моим, а не кода.** Хотел проверить «пре-существующий `Admin` (в любом
  регистре) подавляет сидинг `admin`», но сначала прогнал первый `SeedAsync` — тот создал `admin` —
  и только потом добавил пользователя `Admin`. Ассерт «один администратор в базе» законно нашёл два:
  первый был создан самим тестом секундой раньше. Тест переписан на чистую базу, где pre-existing
  `Admin` — единственный актёр, плюс добавлен ассерт, что `FullName` остался «Pre-existing»
  (существующие строки не переписываются).
- **Асимметрия деградации теперь полная и осознанная: Gemini без ключа — warning, БД без connection
  string — fail-fast.** Логика: без ключа приложение остаётся полезным (ингест, аудит-инфраструктура,
  `/health` живы, AI-эндпоинты честно отвечают 503 при обращении), а без БД не работает вообще ничего,
  включая сам `/health` — хосту физически нечем быть «приложением». Каждому отказу — свой уровень
  строгости, а не единый fail-fast «для порядка».
- **Два расхождения с Mini-CDS приняты сознательно и продокументированы в XML-doc.** (a) `IUserStore`
  содержит только `FindByUsernameAsync` — `GetSystemUserIdAsync` из Mini-CDS выброшен, потому что
  journal этого проекта не имеет системных событий: автор каждой строки — человек после логина.
  Отсюда же (b) в `DbSeeder` не сидируется неинтерактивный `system`-аккаунт (в Mini-CDS он существует
  именно как автор системных записей) и `AuthService` не зависит от `IAuditTrail`.
- **CS8122 в тесте:** `Should().OnlyContain(u => u.Password is null)` не компилируется — FluentAssertions
  принимает `Expression<Func<T, bool>>`, а деревья выражений не допускают pattern-matching `is`.
  Заменено на `== null`. Сюда же общий урок из части 1: компилировать после каждого файла, а не после
  пяти.
- **`Demo:Password = "demo123"` в committed appsettings — принятый компромисс,** но с ценой: README
  обязан помечать его demo-only (долг записан). Альтернатива «всегда генерировать» сделала бы
  повторяемый сценарий GIF зависимым от скролла консоли первого запуска.
- **Подводный камень на будущее поймал сразу:** теперь, когда хост сам делает `MigrateAsync` + сидинг
  при старте, E2E на `WebApplicationFactory` в M7 обязан перекрывать `ConnectionStrings:LabDb`, иначе
  тесты натравят миграции на настоящий `data/lab.db`. Записано в технические долги заранее, чтобы
  не наступить в M7.
- Могло сломаться, но не сломалось: NU1605 при поднятии закреплённых `Microsoft.Extensions.*` с 10.0.6
  до 10.0.12 не случился — nuspec'и проверены ещё в части 1, здесь только исполнение; `Lazy` в
  `AuthService` потокобезопасен по умолчанию (`LazyThreadSafetyMode.ExecutionAndPublication`), что
  важно, когда сервис заскоучен и резолвится параллельными запросами; проверка case-sensitive lookup
  («Analyst» → null) прошла сразу — LINQ-трансляция даёт обычное равенство, а не SQLite-овский
  регистронезависимый LIKE, чего я опасался.

**Итог:** сборка **0 предупреждений, 0 ошибок**; offline **77/77** (плюс 5 skipped live), из них 28
новых. Ручной прогон: миграции + сидинг при старте, WAL подтверждён файлами `lab.db-wal`/`lab.db-shm`,
рестарт дал `seeded 0 account(s)`. Milestone 2 — часть 2/7 закрыта.
**Далее:** часть 3 — парсеры (`MarkdownDocumentParser`, `InstrumentCsvParser`, `PdfPigPdfParser`,
`JsonDocumentParser`) + тесты.

---

### 2026-09-10 — Milestone 2, часть 3: Четыре парсера форматов

**План:** порты `IDocumentParser` + value objects (`ParsedDocument`, `DocumentSection`) в Domain;
четыре парсера в Infrastructure — Markdown (section-aware), CSV (группировка по `Sample Name`),
PDF (PdfPig), JSON (flatten в `path: value`); тесты на всех, включая фикстуру в форме реального
`1.csv`.

**Сделано:**
- **Контракт.** `IDocumentParser { DocumentKind Kind; ParsedDocument Parse(byte[] content); }` —
  синхронный и принимающий байты: PDF бинарный, а пайплайн всё равно держит файл в памяти для
  SHA256. Разбор — чистый CPU, в `CancellationToken` нет смысла. Выбор парсера — по `Kind`, пайплайн
  (часть 5) резолвит `IEnumerable<IDocumentParser>`.
- **`MarkdownDocumentParser`** — стек заголовков (`SectionPath = "H1 > H2 > H3"`), срез YAML
  front-matter (только если `---` стоит первой строкой и закрывается; иначе это hr и текст остаётся
  телом), отслеживание code-fence построчно (заголовок внутри ``` — это контент), инклюзивные
  1-based line spans по **непустым** строкам секции.
- **`InstrumentCsvParser`** — RFC4180-токенизатор (`CsvTokenizer`, internal): кавычки, `""`,
  запятые и переводы строк внутри кавычек, CRLF/LF, BOM снимается до токенизации. Строгая проверка
  выравнивания: строка с числом колонок ≠ шапке → `InvalidDataException` с номером строки —
  сдвинутые данные измерений не должны ингестироваться молча. Группировка строк по `Sample Name`
  в порядке первого появления; секция = шапка + сырые строки образца. Без колонки `Sample Name` —
  фолбэк в одну секцию `Rows`. RawText рекорда — точный срез исходной строки (кавычки сохраняются
  байт-в-байт, текст чанка не расходится с файлом на диске).
- **`JsonDocumentParser`** — flatten в `path: value` (`runs[2].owner: ivanov`); корень-массив —
  секция на элемент (`[0]`, `[1]`), иначе одна секция `Json`; null и пустые контейнеры не
  продуцируют строк; переводы строк внутри строковых значений схлопываются, чтобы не ломать формат
  «один факт — одна строка»; невалидный JSON → `InvalidDataException` с позицией.
- **`PdfPigPdfParser`** — секция на страницу (`Page N`, `PageStart/PageEnd`), текст собирается из
  слов, сгруппированных по baseline (page.Text сплющивает вёрстку в поток слов); пустые страницы
  пропускаются; стандартные Unicode-лигатуры раскрываются (`ﬁ` → `fi`).
- **DI:** `AddLabAiParsing` регистрирует все четыре как singleton (парсеры stateless).
- **Тесты (33 новых):** markdown 10, CSV 9 (фикстура повторяет `1.csv`: BOM + CRLF + пустой
  `Tailing`), JSON 8, PDF 5 (QuestPDF строит двухстраничный PDF в памяти — бинарная фикстура не
  нужна), регистрация DI 1.

**Проблемы / ловушки:**
- **Главный баг части — в моём же токенизаторе, и обычный unit-тест его поймал только в кавычках.**
  В ветке `inQuotes` у `switch` не было ветки `default`: обычные символы внутри кавычек **не
  дописывались в поле**. `"Sample, 3"` разбиралось в `["", ""]` — поле пустое, и группировка
  отправляла образец в «(no sample name)». Файл без кавычек (`1.csv`) работал идеально, поэтому
  простые тесты были зелёными. Вывод: покрывать не только «реальный формат», но и ветки парсера,
  которых в реальном формате не бывает.
- **Вторая версия токенизатора захватывала CRLF в RawText** — срез рекорда включал `\r\n`, и
  секционный текст содержал пустые строки между записями (тест увидел 6 строк вместо 3). Заменил
  накопление посимвольно на точный срез исходной строки по индексам — одновременно исчезла и
  неточность с кавычками внутри кавычек.
- **PDF-тесты вскрыли артефакт, о котором я не думал: лигатуры.** QuestPDF (шрифт Lato по
  умолчанию) заменил «ti» в `verification`/`analytical` на дискреционную лигатуру **без
  Unicode-отображения** — глиф при извлечении исчез бесследно (`analytical` → `analycal`), а `ﬁ`
  остался одиночным символом. Для RAG это яд: чанк с «veriﬁcaon» не совпадёт с запросом
  «verification». Стандартные лигатуры (ﬁ ﬂ ﬀ ﬃ ﬄ ﬅ) раскрываются теперь в парсере; потерю
  «ti» вернуть невозможно в принципе — это свойство генератора PDF. Фикстуру перевёл на русский
  (корпус проекта русскоязычный, в кириллице лигатур нет) и задокументировал причину в тесте.
- **`Restrict`-подобная строгость для CSV — спорное решение, но осознанное:** строку с меньшим
  числом колонок можно было бы дополнить пустыми. Для приборных данных это хуже падения: молча
  сдвинутая колонка `Area` выглядела бы как легитимное измерение. Падение с номером строки
  диагностичнее.
- **CS1002 в тесте:** смешал экранирование обычной строки (`\"`) с verbatim-стилем (`""`) в одном
  литерале — переписал на raw string literal. CS9176: spread в `[.. a, .. b]` без явного целевого
  типа (`var` + byte[]-спред) не компилируется — указал `byte[] content = [.. ]`.
- **Di-проба через отдельный проект в `C:\tmp`** (удалён после): internal-`CsvTokenizer` не виден
  снаружи, поэтому поля рекорда печатал reflection'ом. Холостой круг с трассировкой на бумаге стоил
  дольше, чем сразу написать probe — в следующий раз при «необъяснимом» поведении парсера сначала
  probe, потом рассуждения.
- Могло сломаться, но не сломалось: PdfPig открыл `byte[]` напрямую (`PdfDocument.Open(content)`)
  без временного stream; `QuestPDF.Settings.License = Community` в статическом конструкторе
  тест-класса сработал до первого `GeneratePdf`; кириллица прошла полный цикл QuestPDF → PdfPig без
  потерь (проверено тестом на двух языках — латиница в PDF с Lato коварнее кириллицы).

**Итог:** сборка **0 предупреждений, 0 ошибок**; offline **110/110** (плюс 5 skipped live), из них 33
новых. Все четыре формата парсятся в единый `ParsedDocument`, фикстура CSV повторяет реальный
экспорт Mini-CDS. Milestone 2 — часть 3/7 закрыта.
**Далее:** часть 4 — чанкеры (`MarkdownChunker`, `CsvRowGroupChunker`, `GenericTextChunker`) + тесты.

### 2026-09-10 — Milestone 2, часть 4: Три чанкера — слияние секций, оверлеп, преамбулы CSV

**План:** порт `IChunkingStrategy` + `ChunkDraft` в Domain; чанкеры в **Application** (чистый BCL,
как и обещал план): `MarkdownChunker` (слияние коротких соседних секций, бюджет 2000, оверлеп 200,
разрез по границам предложений), `CsvRowGroupChunker` (преамбула «Результаты измерений, образец …»
+ повторённая шапка, окна строк), `GenericTextChunker` (построчные окна для JSON и PDF); тесты.

**Сделано:**
- **Контракт.** `IChunkingStrategy` c `SupportedKinds` (множеством, а не одиночным `Kind`) —
  GenericTextChunker честно обслуживает и JSON, и PDF-страницы, и заводить под PDF класс-пустышку
  ради симметрии с парсерами не имело смысла. Инвариант регистрации сменился с «ровно один на
  kind» на «покрытие всех kinds без пересечений» — тест это и утверждает.
- **`MarkdownChunker`.** Соседние секции **одного родителя** сливаются, пока сумма влезает в
  бюджет; путь слитого чанка = общий родитель («SOP > 4»), одиночной секции — её собственный
  путь. Секция крупнее бюджета режется по предложениям с окном и оверлепом. Сплитер предложений
  детерминированный: `.`/`!`/`?` завершают предложение только если дальше whitespace + не-цифра —
  «2.0%» и «п. 4.2» не режутся; слияние «не порезал» только удлиняет юнит, текст потерять
  невозможно в принципе.
- **`TextWindowPacker`** (общий для markdown и generic): жадная упаковка юнитов; хвост прошлого
  окна длиной ≥ `OverlapChars` открывает следующее; юнит длиннее бюджета жёстко режется по
  символам; хвост никогда не покрывает всё окно, а если даже усечённый хвост не оставляет места
  новому юниту — он сбрасывается с фронта, вместо того чтобы породить окно-дубликат.
- **`CsvRowGroupChunker`.** Каждое окно = преамбула («Результаты измерений, образец Sample_003:
  строк данных 4.») + шапка + строки; именно преамбула делает семантический запрос «результаты
  Sample_003» попадающим. Строки не оверлепятся (независимые факты, дубль = двойная цитата).
  Line-span окна — арифметически от `LineStart` секции (парсер кладёт каждую строку ровно в одну
  линию). Фолбэк-секция без `Sample Name` получает преамбулу без слова «образец»; сентинел
  «Rows» вынесен в `DocumentSection.UngroupedRowsPath` — единственное место истины для парсера и
  чанкера.
- **DI:** `AddLabAiChunking(maxChunkChars, overlapChars)` в Infrastructure — значения читаются
  там, где есть конфиг; Application остаётся без DI-типов.
- **Тесты (24 новых):** markdown 10 (слияние под общим родителем, стоп по бюджету, разные
  родители не сливаются, оверлеп, hard-split без потерь, «2.0%» и «п. 4.2»), CSV 8 (преамбула,
  окна 2+2 без дублей строк, span'ы окон 2–3 / 4–5, header-only, null-координаты), generic 5,
  регистрация 1.

**Проблемы / ловушки:**
- **Дефект в пакере поймал сам, на чтении, до первого запуска тестов.** После флаша внутри ветки
  hard-split юнита флаг «в current нет нового контента» оставался `false` — при следующем
  переполнении окно, состоящее из одного хвоста, было бы **выпущено как чанк**, продублировав
  уже покрытый текст (тихая категория бага: тест на «скольжение» бы прошёл, а дубль заметил бы
  только retrieval-тест на near-duplicates). Перенёс установку флага внутрь самого `Flush` —
  инвариант «после флаша current — только оверлеп» теперь невозможно нарушить ни в одной ветке.
- **Guard конструктора (`overlapChars < maxChunkChars`) столкнулся с моими же тестовыми
  бюджетами:** дефолтный оверлеп 200 при `maxChunkChars: 20` бросил бы `ArgumentOutOfRangeException`
  ещё до первого assert'а. Тесты теперь всегда задают оверлеп явно — заодно документируя, что
  для мелких бюджетов он осмысленно мал.
- **Моё ожидание в тесте на сокращения было неверным, и это вскрыло неточность в собственном
  правиле:** я писал тест со строкой «См. п. 4.2.», считая, что «См.» защищено. Нет: правило
  защищает только точку, за которой следует **цифра** («п. 4.2»), а «См.» режется как обычный
  конец предложения. Последствие — юнит лишь длиннее, потерь нет. Тест переписан на
  «Раздел п. 4.2 закона.» с бюджетом 12: одиночный 21-символьный юнит режется посимвольно
  («Раздел п. 4.»), что и доказывает отсутствие разреза по «п.».
- **Арифметика счёта тестов съехала на 1 ещё в части 3:** дневник утверждал «110/110», фактический
  базовый офлайн-счёт — 109 (проверено прогоном с фильтром `-Chunking`: 109 + 24 новых = 133).
  Переучёт, а не потеря теста. С этого момента счёт в «Итог» беру из вывода `dotnet test`, а не
  из памяти.
- **PDF-контракт чуть diverge'нул от плана:** план перечислял три чанкера и молчал про Pdf, но
  регистрация «по kind» обязана покрыть и его. Решение — `SupportedKinds` у generic-чанкера —
  дешевле, чем четвёртый класс-пустышка; если когда-нибудь понадобится PDF-специфичная логика
  (например, разбивка по смысловым блокам страницы), появится отдельный класс, а контракт менять
  не придётся.
- Могло сломаться, но не сломалось: жадная упаковка не зациклилась (хвост строго меньше окна —
  прогресс гарантирован конструкцией); `null + int` на `int?` LineStart в CSV-чанкере дал `null`,
  как и ожидалось от lifted-оператора; слившиеся pathless-секции («Вводный абзац» + «Второй») не
  породили лишнего чанка.

**Итог:** сборка **0 предупреждений, 0 ошибок**; offline **133/133** (плюс 5 skipped live), из них
24 новых. Milestone 2 — часть 4/7 закрыта.
**Далее:** часть 5 — `IngestPipeline` (SHA256, идемпотентность по `ContentHash`, supersede в
транзакции) + тесты на NSubstitute.

### 2026-09-10 — Milestone 2, часть 5: IngestPipeline — идемпотентность, supersede, одна транзакция

**План:** `IngestPipeline`: SHA256 → парсер по kind → чанкер → эмбеддинги батчами → L2-нормализация
→ BLOB → одна транзакция (новый документ + чанки + supersede предыдущего). Идемпотентность: тот же
`ContentHash` → `Duplicate` и **ноль** вызовов эмбеддингов. Тесты: NSubstitute на пайплайне, реальный
SQLite на репозитории.

**Сделано:**
- **Домен:** `IngestRequest` (байты файла + провенанс: путь, заголовок, версия, kind, `IngestedByUserId`),
  `IngestResult` (`DocumentId, Outcome, ChunksCreated, SupersededDocumentId`), enum `IngestOutcome`
  (`Created | Duplicate`), порты `IDocumentIngestService` и `IDocumentRepository`.
- **`IngestPipeline` (Application):** дубликат по хешу ловится **до** парсинга — самый дешёвый gate,
  дубликат не платит ни за парсинг, ни за модель. Эмбеддинг-батчи по `embeddingBatchSize` (тест
  ловит разрез 5 чанков батчем 2 → вызовы 2+2+1); вход модели — `"{Title} (v{Version}) — {SectionPath}\n{text}"`,
  а `Chunk.Text` хранится голым: заголовок детерминированно восстанавливается в M4. Проверка
  единой размерности по всему документу (`InvalidOperationException` **до** обращения к
  репозиторию — половинчатых записей не бывает). Supersede-lookup сознательно **после** эмбеддингов:
  решение принимается по состоянию, актуальному на момент коммита.
- **`VectorMath` (Application, DOD):** `NormalizeInPlace` (double-аккумулятор против дрейфа точности
  на 3072 измерениях; нулевой вектор не трогается — деление на ноль дало бы NaN, отравляющий все
  dot product'ы) и `ToFloat32Blob` (`MemoryMarshal.AsBytes`, IEEE-754 float32 LE без заголовка).
  План помещал его в M3 часть 1, но пайплайн физически не может сохранить чанки без кодирования и
  нормализации — функции появились сейчас, `DotProduct`/`TopK` и паритет-тесты остаются в M3.
- **`EfDocumentRepository`:** `AddAsync(document, buildChunks, supersededDocumentId)` — единственное
  место, знающее, что вставка новой версии и supersede предыдущего живут в **одной** транзакции.
  `buildChunks` — deferred-фабрика: `Chunk.DocumentId` init-only (сущность иммутабельна), а id
  документа генерирует БД внутри транзакции.
- **DI:** репозиторий — в `AddLabAiPersistence`, пайплайн — новый `AddLabAiIngest(embeddingBatchSize)`.
- **Тесты (18 новых):** pipeline 9 (дубликат не трогает ни парсер, ни модель; батчи; контекстный
  заголовок; разнобой размерностей → ничего не записано; supersede = 42; пустой документ без
  эмбеддингов; отсутствующие парсер/стратегия; валидация запроса), VectorMath 4, EfDocumentRepository
  5 (фиксап id, байт-в-байт round-trip BLOB, supersede, откат при сбое фабрики, Active-only поиски).

**Проблемы / ловушки:**
- **`Chunk.DocumentId` init-only против id, который генерирует БД** — главный дизайн-узел части.
  Варианты: сделать FK settable (ломает иммутабельность сущности), надеяться на EF-фиксап по FK
  без навигаций (не гарантирован), дать пайплайну доступ к DbContext (ломает слои). Выбрал
  deferred-фабрику в порте репозитория: она вызывается внутри транзакции, когда id уже присвоен.
  Цена — тесту надо захватить фабрику через `Arg.Do` и вызвать с фейковым id; выгода — пайплайн
  остаётся в Application без EF-типов, транзакция не протекает наружу.
- **Три FK-ошибки из моих же тестов — и первая гипотеза была наполовину верной.** Сначала все
  падало с «FOREIGN KEY constraint failed»: добавил сид пользователя в фикстуру (FK
  `IngestedByUserId → users` — реальное требование «кто загрузил», зашитое в схему), два теста из
  пяти починились, но два продолжали падать. Настоящий виновник: **тестовые** фабрики игнорировали
  переданный `documentId` и строили чанки с `DocumentId = 0`. Урок: починка, объясняющая часть
  сбоев, не объясняет всех — проверять, что оставшиеся падения действительно покрыты гипотезой.
  Заодно тест доказал ценность deferred-фабрики: не передашь id — FK ловит сразу.
- **Слои: план помещал `IngestPipeline` в Application, а `LabAiDbContext` — в Infrastructure.**
  Пайплайну нужен persistence — добавлен порт `IDocumentRepository` (в списке портов плана его не
  было). Тот же приём в M4: `RagQueryPipeline` возьмёт `IVectorStore` и `IAiAuditTrail`, а не
  DbContext.
- Могло сломаться, но не сломалось: `Convert.ToHexStringLower` (появился в .NET 9) совпал с
  ожиданиями хеша во всех тестах; батч «хвостом» из 1 текста не потерялся; пустой документ уходит
  в `AddAsync` с пустой фабрикой и не вызывает модель; supersede-lookup для дубликата не выполнялся
  вовсе (дубликат выходит раньше, и это правильно — supersede честно пропускается).

**Итог:** сборка **0 предупреждений, 0 ошибок**; offline **151/151** (плюс 5 skipped live), из них
18 новых. Milestone 2 — часть 5/7 закрыта.
**Далее:** часть 6 — авторский корпус `docs\corpus` (пять SOP, приборный CSV, JSON-лог весов);
миграция append-only триггеров уже перенесена в M5 часть 3.

### 2026-09-10 — Milestone 2, часть 6: авторский корпус docs\corpus и тест стабильности чанков

**План:** написать корпус, на котором будет работать весь остальной проект: пять SOP с численными
пределами (под golden set M7), приборный CSV в точной форме `Mini-CDS\Reports\1.csv` (BOM, CRLF,
11 колонок, пустой `Tailing`), JSON-журнал весов с полем `Owner` с синтетическим PII (под M5).
Закрыть критерий готовности M2 «ингест корпуса даёт стабильное число чанков (утверждено тестами)».

**Сделано:**
- `docs\corpus\`: SOP-QC-001 (системная пригодность ВЭЖХ: RSD ≤ 2,0 %, тарелки ≥ 2000, tailing
  ≤ 2,0, Rs ≥ 1,5), SOP-QC-002 (весы: допуск ±0,5 мг, размах ≤ 0,3 мг), SOP-QC-003 (pH-метр:
  наклон 95–102 %, контрольный буфер ±0,05 pH), SOP-QC-004 (дозаторы: пределы по объёмам
  ±0,8/1,0/2,5 %), SOP-QA-005 (OOS: уведомление ≤ 1 часа, первичная оценка ≤ 1 рабочий день,
  план ≤ 5 дней, отчёт ≤ 30 дней). Фронматтер `doc_id/title/version/effective_date/owner`,
  парсером он снимается.
- Числа согласованы между артефактами: в `hplc-run-001.csv` главный пик `Sample_2026-002` имеет
  Plates = 1650 (ниже предела ≥ 2000 из SOP-QC-001) — готовый сценарий «CSV + SOP → OOS» для
  golden set; в `balance-log-2026-09.json` проверка 2026-09-03 с отклонением −0,6 мг против
  допуска ±0,5 мг из SOP-QC-002.
- CSV написан через Write, затем нормализован до BOM+CRLF одной командой sed — сверено `cat -A`
  с эталоном `1.csv`: `M-oM-;M-?` в начале и `^M$` в конце каждой строки.
- `CorpusIngestTests` (3 теста): полностью реальный пайплайн (парсеры, чанкеры 2000/200,
  `EfDocumentRepository` на мигрированной in-memory SQLite) с константной заглушкой эмбеддингов;
  чанк-каунты запинены per-файл (6/7/6/6/6/2/1 = 34), повторный ингест всего корпуса — только
  `Duplicate` без новых строк, у markdown-чанков `SectionPath` непустой и есть line-span, у CSV
  текст начинается с преамбулы «Результаты измерений, образец Sample_2026-…».

**Проблемы / ловушки:**
- **Русская типографика против сплиттера предложений.** `TextUnitSplitter` считает конец
  предложения «.» + пробел + нецифра. Привычное для SOP «не более 2,0 %» с пробелом перед
  процентом не страдает (запятая — не терминатор), но написанное в плане «2.0 %» разрезалось бы
  на «2.» и «0 %» (пробел + `%` — нецифра). Решение: в корпусе десятичная запятая — это и есть
  норма русского лабораторного документа; в CSV точки (формат CDS), там сплиттер не применяется
  (CSV чанкуется по строкам).
- **Плоские заголовки дали бы пустой `SectionPath`.** При структуре «`##`-раздел с телом» подряд
  все секции имеют общего родителя `""` и сливаются в один чанк с пустым breadcrumb. Правило
  корпуса: тело только под `###` внутри `##`-главы → одна глава = один чанк с путём-хлебной
  крошкой («4. Критерии приемлемости»), соседние главы не сливаются, потому что родители разные.
- **Числам чанков нужна проверка на прочность, а не вера.** Каунты вычислены из устройства
  корпуса (по чанку на главу; CSV — по окну на образец при rowBudget ≈ 1836 против строк ~105
  символов) и подтверждены тестом с первого прогона; вчерашний дефект «запиненное число, угаданное
  на глаз» здесь исключён конструкцией: любое изменение чанкеров или корпуса уронит тест.
- **Перепроверка счётчика: в части 5 записано 151/151 offline, фактическая база перед частью 6 —
  152** (155 сейчас минус 3 новых корпуса-теста; всего 160 = 155 + 5 skipped live). Историческую
  запись не правлю — это ровно та же ошибка, что уже ловилась в части 3 (110 вместо 109). Правило
  «счёт только из вывода dotnet test» подтверждается второй раз: между «проверил» и «записал»
  вкрался ручной перенос числа.

**Итог:** сборка **0 предупреждений, 0 ошибок**; offline **155/155** (всего 160, из них 5 skipped
live), из них 3 новых. Milestone 2 — часть 6/7 закрыта, корпус и тест стабильности готовы.
**Далее:** часть 7 — `POST /api/ingest` (multipart, Roles="Administrator,Analyst") и страница
`/documents`, после чего Milestone 2 закрывается.

### 2026-09-10 — Milestone 2, часть 7: POST /api/ingest, страница /documents и вход через браузер

**План:** закрыть последнюю часть M2 — multipart-эндпоинт ингеста с ролевым ограничением, страницу
`/documents` с таблицей и формой загрузки. В план части входили и pull-in'ы из M6(1): cookie auth
и каркас Blazor пришлось поднять раньше, потому что ингест требует настоящего `IngestedByUserId`
из аутентифицированного пользователя — вымышленный ActorUserId сделал бы GxP-нарратив декларативным
(то же правило, по которому в M2 auth шёл перед ingest).

**Сделано:**
- `POST /api/ingest` ([Authorize Roles="Administrator,Analyst"]): валидация (файл null/пустой/
  >10 МБ, пустой title, version < 1, неподдерживаемое расширение → 400 со словарём ошибок),
  `GeminiNotConfiguredException` → 503 (зеркало контракта ChatEndpoints), ответ
  `{ documentId, chunksCreated, outcome, supersededDocumentId }`. `.DisableAntiforgery()` —
  осознанно: эндпоинт для внешних API-клиентов (связка A+C), CSRF закрыт SameSite=Strict cookie.
- `GET /api/documents` → активные документы, упорядоченные по названию (`ListActiveAsync`
  добавлен в `IDocumentRepository`/`EfDocumentRepository` + тест).
- `AuthClaims.ToClaimsPrincipal` + `POST /api/auth/login|logout`; cookie: HttpOnly,
  SameSite=Strict, 8 ч; `OnRedirectToLogin/AccessDenied` возвращают 401/403 вместо HTML-редиректа,
  чтобы JSON-клиент не получал страницу логина в теле ответа.
- Каркас Blazor: `App.razor` (lang="ru"), `Routes.razor` с `AuthorizeRouteView` +
  `RedirectToLogin`, `MainLayout`, страницы `/` (описание) и `/login`.
- `/documents` (InteractiveServer, [Authorize]): таблица активных документов, загрузка через
  `InputFile`, имя пользователя берётся из `AuthenticationState` → `IUserStore` (claims в circuit
  ненадёжны), ошибки парсера/пайплайна → баннер, после успеха — перезагрузка таблицы.
- Форма входа — обычная HTML-форма с `<AntiforgeryToken />`, постящаяся на новый `POST /login`
  (minimal API, редиректы `/login?error=1` → `/`). GET /login — маршрут компонента, POST /login —
  эндпоинт; конфликтов нет.
- Live-проверка curl'ом: 401 без cookie, 200 логин, 403 у `operator` на ингест, 400 на все
  варианты валидации, Created/Duplicate идемпотентность, чанк-каунты совпали с закреплёнными
  (6/2/1/7/6/6 по живым эмбеддингам), GET /api/documents упорядочен.
- Браузерная проверка: логин analyst/demo123 → редирект на `/`, `/documents` показывает все
  7 документов с версиями, аплоад SOP-QC-003 через форму → баннер «Документ #7: чанков 6,
  исход создан», повторный аплоад того же файла → «чанков 0, исход дубликат».

**Проблемы / ловушки:**
- **Падение хоста на старте: «An action cannot use both form and JSON body parameters».**
  Minimal API выводит источники привязки: параметры, чьи типы не зарегистрированы в DI,
  считаются JSON Body. `IDocumentIngestService ingest` рядом с `[FromForm]`-параметрами был
  выведен как Body → конфликт. Корень: `Program.cs` не вызывал `AddLabAiParsing/AddLabAiChunking/
  AddLabAiIngest` — сервисы вообще не были зарегистрированы. Урок: эта ошибка компиляции не
  ловится, она ловится только запуском хоста — smoke-запуск обязателен после любого эндпоинта.
- **`[FromForm]` живёт в `Microsoft.AspNetCore.Mvc`, а не в Http.** В .NET 10 атрибут переехал
  по пакетам; поиск по ref-пакетам (`grep -rl FromFormAttribute .../Microsoft.AspNetCore.App.Ref/
  10.0*`) нашёл его в Mvc.Core.
- **EditForm + `[SupplyParameterFromForm]` не забайндил поля, поля после сабмита invalid=true.**
  Имена полей, генерируемые из выражения `Model="FormModel"`, не совпали с префиксом биндера
  («Model.Username»), модель пришла пустой, серверная Required-валидация валила форму. Вместо
  отладки генерации имён — смена подхода: обычная HTML-форма + антифорджери-токен + minimal API
  `POST /login` с редиректами. Проще, детерминированнее, работает до подъёма circuit.
- **Тихая смерть circuit: аплоад не срабатывал без единой ошибки нигде.** Страница рендерилась
  (пререндеринг), но `InputFile.OnChange` не доходил до сервера. Причина двухслойная: (1) в
  `App.razor` не было `<script src="_framework/blazor.web.js">`; (2) `dotnet run` без
  launchSettings.json работает в Production, где static web assets отключены — лог прямо писал
  «Static Web Assets are not enabled». Исправления: скрипт в `App.razor` +
  `builder.WebHost.UseStaticWebAssets()` (no-op для published). Урок: интерактивный Blazor
  ломается **молча** — признак «нет ни ошибки, ни результата» означает «событие не долетело»,
  а не «обработчик упал».
- **`v@document.Version` отрендерился буквально.** Razor считает `@` после буквенного символа
  частью email-подобного текста (`v@document`), выражение не парсится. Фикс — явная скобочная
  форма `v@(document.Version)`.
- **Во время live-прогона curl ингест одного файла упал с 500** (`GeminiNotConfiguredException`
  без ключа после рестарта), и SOP-QC-003 пропал из корпуса, пока я не догрузил его через UI.
  Побочно это подтвердило fail-closed: без ключа документ не сохраняется наполовину.
- **CWD между Bash-вызовами сохраняется** — curl с относительными путями в третий раз за проект
  натыкается на это; правило: абсолютные пути или явный `cd` в той же команде.

**Итог:** сборка **0 предупреждений, 0 ошибок**; **156/156 offline** (всего 161, из них 5 skipped
live) — счётчик взят из вывода `dotnet test` немедленно после прогона. Live-матрица curl и
браузерный сценарий пройдены полностью. **Milestone 2 закрыт целиком (7/7).**
**Далее:** M3 — BLOB-кодирование + `VectorMath` (DOD, parity-тесты), затем `SearchIndex`/
`BruteForceSearch`, `EfVectorStore` с snapshot swap и dimension guard.

### 2026-09-10 — Milestone 3, часть 1: BLOB-декодирование и DotProduct в VectorMath, parity-тесты

**План:** довести `VectorMath` до полного контракта горячей петли поиска: декодирование
float32-LE BLOB, скалярное произведение на `ReadOnlySpan<float>` (обе стороны нормализованы при
записи → поиск это только dot product), parity-тесты против наивной эталонной реализации.

**Сделано:**
- `VectorMath.FromFloat32Blob(ReadOnlySpan<byte>)` → `float[]`: проверка кратности четырём,
  `MemoryMarshal.Cast<byte, float>` внутри. Копия (`ToArray`) — осознанно: снапшот индекса
  (часть 3) всё равно хочет владеть памятью, а zero-copy путь для запроса остаётся доступным
  напрямую через `MemoryMarshal.Cast` — ровно ради него формат BLOB без заголовка и выбран.
- `VectorMath.DotProduct(ReadOnlySpan<float>, ReadOnlySpan<float>)`: плоский `for` без LINQ и
  аллокаций (DOD, AGENT.md 2.4), guard на несовпадение длин.
- `VectorMathTests` +9 тестов (13 всего): round-trip encode→decode на 3072 измерениях, отказ
  на некратной длине, parity против наивной свёртки на измерениях 1/7/256/3072 (детерминированные
  сиды, не случайность), тождество «dot product нормализованных = cosine similarity» — семантика,
  на которую опирается retrieval, ортогональность, отказ на несовпадении длин.

**Проблемы / ловушки:**
- **Толерантность parity-теста — это решение о семантике, а не число.** Слишком тесный допуск
  (`1e-6`) упал бы в тот день, когда JIT начнёт векторизовать свёртку с реассоциацией суммы
  (SIMD меняет порядок сложения → другой результат округления). Слишком широкий (`1e-3` от
  масштаба) замаскировал бы реальный баг. Выбран относительный `1e-4` от `max(1, |expected|)`:
  наивный эталон и реализации с реассоциацией проходят, систематические ошибки (неверный stride,
  неверный порядок байт) — нет.
- **`Random(seed)` в тестах, а не «случайные» данные.** Падение parity-теста с плавающими
  сидами невоспроизводимо; сиды делают упавшее утверждение читаемым.
- **Фоновый хост снова заблокировал сборку** (MSB3026/MSB3027 на exe и DLL — уже третий раз за
  две части). Рабочее правило: хост поднимается только на время live-проверок и останавливается
  сразу после; между ними сборки и тесты гоняются на свободном дереве.
- **Decode отдаёт копию — пометка для будущих читателей.** Могло показаться, что zero-copy
  «утеряно»; на деле `SearchIndex` (часть 2) строится из owned `float[]`, а единственный по-настоящему
  zero-copy момент — это чтение BLOB из SQLite перед раскладкой в плоский массив снапшота.

**Итог:** сборка **0 предупреждений, 0 ошибок**; VectorMath-тесты **13/13**, полный offline-набор
**165/165** (161 + 9 новых − 5 live skipped вне фильтра; счёт из `dotnet test --filter
"Category!=Live"`: 165 пройдено, 0 упало). Milestone 3 — часть 1/4 закрыта.
**Далее:** часть 2 — `SearchIndex` (параллельные массивы SoA) + `BruteForceSearch.TopK`
(insertion-sorted буфер, детерминированный tie-break) + тесты.

### 2026-09-10 — Milestone 3, часть 2: SearchIndex (SoA-снапшот) и BruteForceSearch.TopK

**План:** неизменяемый снапшот векторного хранилища как параллельные массивы (SoA) и полный
перебор top-K с insertion-сортировкой фиксированного буфера: без полной сортировки, без кучи,
с детерминированным tie-break и отсечкой по порогу.

**Сделано:**
- `Domain/ValueObjects/SearchHit` — `readonly record struct (ChunkId, DocumentId, Score)`;
  `DocumentId` едет рядом, чтобы панель источников разрешила название/версию без второго
  запроса в горячем пути.
- `Application/Vectors/SearchIndex` — `ChunkIds[]`, `DocumentIds[]`, плоский `Vectors[]`
  (строка `i` начинается с `i * Dimension`), `Dimension`, `Count`; конструктор валидирует длины
  (несовпадение id-массивов и недозаполненный буфер — ошибка, а не тихое чтение мусора);
  `SearchIndex.Empty` — синглтон пустого хранилища.
- `Application/Vectors/BruteForceSearch.TopK(index, query, k, minScore, Span<SearchHit>)`:
  скан по всем строкам, insertion-поддержка отсортированного префикса прямо в destination-спане
  вызывающего (ноль аллокаций), отсечение `score < minScore`, tie-break по возрастанию ChunkId,
  возврат числа записанных хитов.
- Тесты (13 новых): раскладка SoA, обе валидации конструктора, Empty; порядок убывания score,
  полное исключение по порогу (не понижение в рейтинге), k > n, детерминизм tie-break (два
  прогона — один ответ), вытеснение худшего поздней строкой (путь замены в полном буфере),
  пустой индекс, k = 0, отказ на чужой размерности запроса, отказ на коротком destination.

**Проблемы / ловушки:**
- **k = 0 — это отдельный путь, а не «частный случай k > 0».** Первая версия на `taken == k`
  обращалась к `destination[taken - 1]` ещё до проверки k, и при k = 0 это был бы выход за
  левую границу спана. Поймано на ревью собственной логики до запуска; тест `ZeroKWritesNothing`
  теперь фиксирует поведение.
- **Тестовые векторы — тоже код.** Первый вариант данных для top-K теста дал 7 float на 3 строки
  размерности 3 — конструктор SearchIndex честно отказался (валидация сработала раньше теста).
  Переписал на целые косинусы (0.96/0.86/0.1) с единичными строками, чтобы ожидания были
  проверяемы руками.
- **Tie-break обязан быть до Match, а не после.** Скан идёт по возрастанию индекса строки; без
  правила «равный score → меньший ChunkId первый» порядок равных хитов зависел бы от порядка
  строк в БД — то есть от истории ингестов. Для журнала аудита, который цитирует `[S1]..[Sn]`,
  это была бы недетерминированная нумерация цитат.
- **Score — не nullable, FluentAssertions `BeGreaterThan(float)`**: `hits[1].Score!.Value`
  не компилируется (CS1061) — мелочь, но съедает цикл сборки, если писать по памяти.

**Итог:** сборка **0 предупреждений, 0 ошибок**; полный offline-набор **178/178** (счёт из
`dotnet test --filter "Category!=Live"` немедленно после прогона). Milestone 3 — часть 2/4
закрыта.
**Далее:** часть 3 — `EfVectorStore`: snapshot swap через `Volatile.Write`, сборка только из
Active-документов, стартовый dimension guard («смешанное хранилище — отказ обслуживать»),
подключение `RebuildAsync()` в конец ингеста.

### 2026-09-10 — Milestone 3, часть 3: EfVectorStore с rebuild и Active-only фильтрацией

**План:** EF-backed векторное хранилище как singleton с `IDbContextFactory<T>` для избежания captive
dependency; неизменяемый снапшот публикуется через `Volatile.Write`; сборка только из Active-документов
(устаревшие версии исключены на уровне SQL); стартовый dimension guard отказывается искать по смешанному
хранилищу; подключение `RebuildAsync()` в конец ingest pipeline и DI-регистрация.

**Сделано:**
- `Domain/Abstractions/IVectorStore` — порт с двумя методами: `Snapshot { get; }` и `RebuildAsync()`;
  возвращает `SearchIndex` из Domain.ValueObjects (не Application — иначе слой нарушил бы AGENT.md 2.1).
- `Infrastructure/Persistence/EfVectorStore` — реализация: конструктор принимает
  `IDbContextFactory<LabAiDbContext>` (а не scoped DbContext — singleton не может держать scoped зависимость);
  `RebuildAsync()` создаёт временный контекст через фабрику, проверяет dimension guard (`SELECT DISTINCT
  EmbeddingModelId, EmbeddingDimension` → больше одной пары = `InvalidOperationException`), собирает список
  Active document IDs явным подзапросом (у Chunk нет навигации `Document` — FK есть, но navigation property
  отсутствует), фильтрует чанки через `Contains(activeDocIds)`, декодирует BLOB через `MemoryMarshal.Cast`,
  заполняет SoA-массивы и публикует снапшот через `Volatile.Write`.
- `IngestPipeline` — добавлен параметр `IVectorStore vectorStore` в конструктор; после успешного
  `repository.AddAsync` вызывается `await vectorStore.RebuildAsync(cancellationToken)` — индекс перестраивается
  синхронно с транзакцией, fail-closed порядок: если rebuild упал, ingest падает вместе с ним.
- `IngestServiceCollectionExtensions` — обновлена фабрика `IngestPipeline`: добавлен
  `sp.GetRequiredService<IVectorStore>()` между `IDocumentRepository` и `embeddingBatchSize`.
- `PersistenceServiceCollectionExtensions` — зарегистрированы `AddDbContextFactory<LabAiDbContext>` (для
  EfVectorStore) и `services.AddSingleton<IVectorStore, EfVectorStore>()`.
- Тесты: `CorpusIngestTests` и `IngestPipelineTests` обновлены — везде добавлена заглушка
  `Substitute.For<IVectorStore>()` в конструкторы `IngestPipeline`.

**Проблемы / ловушки:**
- **SearchIndex оказался в Application, а IVectorStore — в Domain.** Порт не может ссылаться на тип из
  более низкого слоя без нарушения Clean Architecture. Решение: переместил `SearchIndex.cs` из
  `Application/Vectors/` в `Domain/ValueObjects/`. Это Value Object (не entity, не aggregate root), поэтому
  его место в Domain допустимо. В `IVectorStore` используется fully qualified `ValueObjects.SearchIndex` —
  оба типа в одном assembly, но разных namespace'ах.
- **Chunk не имеет навигации Document.** Конфигурация `ChunkConfiguration` определяет FK через
  `HasOne<SourceDocument>().WithMany().HasForeignKey(c => c.DocumentId)` — это shadow navigation, EF Core
  знает о связи, но C#-код не видит свойства `c.Document`. Попытка написать `Where(c => c.Document.Status == ...)`
  даёт CS1061. Пришлось переписать на явный подзапрос: сначала `SELECT Id FROM SourceDocuments WHERE Status =
  Active`, потом `WHERE activeDocIds.Contains(c.DocumentId)`. Генерируемый SQL идентичен (INNER JOIN), но
  компиляция проходит.
- **DI-регистрация IngestPipeline сломалась после добавления параметра.** Factory lambda в
  `IngestServiceCollectionExtensions` передавала 5 аргументов, а конструктор теперь требует 6. Компилятор
  указал на строку 20 — добавил `sp.GetRequiredService<IVectorStore>()` в правильную позицию (между
  repository и batchSize).
- **Тесты тоже требуют обновления.** `CorpusIngestTests` использует реальный SQLite и EfDocumentRepository —
  туда достаточно добавить `Substitute.For<IVectorStore>()` (RebuildAsync не вызывается в тестах, потому что
  тесты мокают embeddings, но сам pipeline создаётся полностью). `IngestPipelineTests` — чисто unit-тесты с
  NSubstitute, там добавил поле `private readonly IVectorStore vectorStore = Substitute.For<IVectorStore>();`
  и передал во все три места создания IngestPipeline.

**Итог:** сборка **0 предупреждений, 0 ошибок**; полный offline-набор **178/178** (все тесты зелёные, ни
один не сломался). Milestone 3 — часть 3/4 закрыта.
**Далее:** часть 4 — подключить `RebuildAsync()` на старте приложения (Program.cs после DbSeeder), расширить
`/health` endpoint информацией о размере индекса и размерности, написать EfVectorStoreTests (BLOB round-trip,
исключение superseded документов, dimension guard, atomic snapshot swap).

### 2026-09-10 — Milestone 3, часть 4: Startup rebuild, health extension, EfVectorStoreTests

**План:** перестроить векторный индекс при старте приложения, добавить размер и размерность в `/health`,
покрыть EfVectorStore интеграционными тестами на temp SQLite.

**Сделано:**
- `Program.cs` — после сидинга добавлен блок rebuild: вызывает `vectorStore.RebuildAsync()`, логирует
  количество векторов и размерность; ловит `InvalidOperationException` от dimension guard и пишет ошибку
  в лог (приложение продолжает работать, но поиск будет отказывать до ре-ингеста с одной моделью).
- `HealthEndpoints` — добавлены поля `VectorIndexSize` и `VectorDimension` в ответ; эндпоинт теперь читает
  `IVectorStore.Snapshot` и отдаёт актуальные цифры без дополнительных запросов к БД.
- `EfVectorStoreTests` (5 новых тестов):
  1. `RebuildEncodesAndDecodesBlobsByteExactly` — round-trip BLOB → float[] → BLOB точен;
  2. `SupersededDocumentsAreExcludedFromSnapshot` — только Active документы попадают в снапшот;
  3. `MixedDimensionsCauseRebuildToFail` — две разные пары (model, dim) вызывают `InvalidOperationException`;
  4. `SnapshotSwapIsAtomic` — до rebuild пустой снапшот, после — populated; повторное чтение возвращает ту же
     ссылку (неизменяемый объект, Volatile.Read);
  5. `EmptyStoreProducesEmptySnapshot` — пустая БД даёт `Count = 0, Dimension = 0`.

**Проблемы / ловушки:**
- **Wildcard assertion в FluentAssertions чувствителен к порядку.** Первый вариант теста ожидал
  `"*multiple*model/dimension*"`, но реальное сообщение содержит "2 distinct model/dimension pairs" — слово
  "multiple" отсутствует. Исправил на `"*model/dimension*"`, что покрывает суть без привязки к конкретному
  числительному.
- **TestDbContextFactory требует типизированный DbContextOptions.** Конструктор `LabAiDbContext` принимает
  `DbContextOptions<LabAiDbContext>`, а не базовый `DbContextOptions`. Ошибка CS1503 поймана сразу, исправлена
  генериком.
- **VectorMath не виден из тестов.** Класс находится в `LabAi.Application.Vectors`, нужно явное `using`.
  Без него компилятор выдаёт CS0103 на все вызовы `VectorMath.ToFloat32Blob`.

**Итог:** сборка **0 предупреждений, 0 ошибок**; полный offline-набор **183/183** (+5 EfVectorStoreTests).
Milestone 3 — часть 4/4 закрыта. **Milestone 3 полностью завершён.**
**Далее:** M4 — Grounded generation: `PromptComposer` с RU system prompt, `PromptHasher`, `RefusalDetector`
(threshold gate + фраза отказа), `RagQueryPipeline` с fail-closed порядком аудита, замена `/api/chat` на
`POST /api/ask`.
