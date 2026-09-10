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
| 2 | Ingest: парсинг + чанки + метаданные | 🔨 В работе (части 1–2/7 закрыты, 77/77 offline) |
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
| Эмбеддинги через `AddGoogleAIEmbeddingGenerator` → `Microsoft.Extensions.AI.IEmbeddingGenerator`, не SK-нативный путь | `AddGoogleAIEmbeddingGeneration` и `GoogleAITextEmbeddingGenerationService` в 1.80.1-alpha помечены `[Obsolete]`. Коннектор сам мигрирует на `Microsoft.Extensions.AI`; встать на устаревшее имя означало бы гарантированный повторный churn |
| Чат остаётся на `AddGoogleAIGeminiChatCompletion`, хотя `AddGoogleAIChatClient` новее | Второй параметр `AddGoogleAIChatClient` — `Google.GenAI.Client`. Назвать этот тип в нашем коде = добавить прямую ссылку на `Google.GenAI`, а она запрещена (коннектор скомпилирован против 0.11.0, NuGet зарезолвил бы непроверенный мажор). Асимметрия «чат на SK, эмбеддинги на M.E.AI» принята осознанно и невидима выше Infrastructure |
| Модель чата — закреплённое имя (`gemini-3.5-flash`), никогда не rolling-алиас | В аудите `Model` — доказательство того, **какая** модель ответила. `gemini-flash-latest` молча переезжает на новую версию, и запись становится ложной, оставаясь внешне корректной. Это та же причина, что и `Temperature = 0` |
| Никакого фолбэка на вторую embedding-модель (план предлагал `text-embedding-004`) | `text-embedding-004` = 768 измерений против 3072 у `gemini-embedding-001`. Успешный фолбэк наполнил бы хранилище **другим векторным пространством**, чем то, о котором думает оператор. Громкий отказ + смена конфига + ре-ингест правильнее тихой подмены |
| Схема БД в конвенции Mini-CDS: snake_case таблицы, PascalCase-колонки, enum как TEXT(32), DateTime с Kind-конвертером | Оба проекта должны читаться как работа одного инженера, а enum-как-имя даёт forensic-читаемость: БД, открытую руками, не нужно дешифровать. Kind-конвертер обязателен: SQLite не имеет типа datetime и провайдер возвращает `Kind=Unspecified`, из-за чего любое сравнение или сериализация молча съезжает |
| Все FK — `DeleteBehavior.Restrict`, никогда `Cascade` | Каскад удалил бы историю версий и чанки, на которые ссылаются старые строки аудита, то есть уничтожил бы доказательство вместе с тем, что его цитирует. Проверено на двух уровнях: EF бросает `InvalidOperationException` («association … has been severed») уже на `Remove()`, до генерации SQL, а сырой `DELETE` в обход трекера останавливает сама SQLite (`FOREIGN KEY constraint failed`) |
| `Chunk.Text` без `HasMaxLength` — осознанно | В SQLite TEXT не имеет длины, поэтому ограничение ничего не защищает: оно лишь превратило бы легально длинную секцию в ошибку сохранения в тот день, когда `MaxChunkChars` в appsettings поднимут. Длина чанка — забота чанкера, не схемы |
| WAL включается одноразовым `PRAGMA journal_mode=WAL` на старте хоста, не в connection string и не в миграции | У `Microsoft.Data.Sqlite` ключа journal-mode не существует вовсе; в миграцию положить нельзя — миграции исполняются в транзакции, а PRAGMA внутри транзакции — no-op. Значение персистентно в файле БД, поэтому одного выполнения после `MigrateAsync` достаточно навсегда |
| Сидинг идемпотентен: существующая строка пользователя — запись, а не шаблон | Сравнение имён `OrdinalIgnoreCase`, существующие аккаунты никогда не перезаписываются и не перехешируются. Иначе каждый рестарт переписывал бы пароли и разрывал связь между `ActorUserId` в аудите и учётными данными, которые реально действовали в момент записи |

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
