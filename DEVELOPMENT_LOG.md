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
| 1 | Semantic Kernel + базовый чат (каркас, .env, Serilog, Gemini-адаптеры) | 🔨 В работе (часть 1/3 закрыта, 3/3 tests) |
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

## Известные технические долги

- `Marker.cs` в Domain и Application — временный якорь сборки, чтобы `LayeringTests` могли ссылаться
  на пустые проекты. Удалить в Milestone 2, когда появятся реальные типы.
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
