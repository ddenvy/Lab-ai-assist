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
| 1 | Semantic Kernel + базовый чат (каркас, .env, Serilog, Gemini-адаптеры) | 🔨 В работе (часть 2/3 закрыта, 13/13 tests) |
| 2 | Ingest: парсинг + чанки + метаданные | ⬜ Не начат |
| 3 | Embeddings + vector store + поиск top-k | ⬜ Не начат |
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

## Известные технические долги

- `Marker.cs` в Domain и Application — временный якорь сборки, чтобы `LayeringTests` могли ссылаться
  на пустые проекты. Удалить в Milestone 2, когда появятся реальные типы.
- `/health` сейчас отвечает только про конфигурацию Gemini (`geminiKeyPresent`, модели). По плану он
  обязан также сообщать доступность БД и размер векторного индекса — обе проверки физически нечего
  вызывать до Milestone 2 (`LabAiDbContext`) и Milestone 3 (`EfVectorStore`). Расширить там же.
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
