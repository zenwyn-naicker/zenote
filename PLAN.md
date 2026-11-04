# Zenote — Project Plan (rev 7)

> **Zenote** (zen + note): a simpler, cleaner, fully local take on NotebookLM for macOS.
> Group PDFs into notebooks and chat with them using local retrieval-augmented generation,
> with answers that cite the source file and page. Built for learning and experimenting with AI.
> Not affiliated with Google or NotebookLM.
>
> Library versions and facts last checked **1 Nov 2026** (sources in §11). Re-check when you pin versions in M0.

---

## Status

M0 to M2 are merged and tagged. **M3 is in review.** Next: M4.

## What changed in rev 7 (learned in M3)

- **D2b settled: keep `nomic-embed-text`.** Eval results on the 26-question set:

  | Model | Hit@1 | Hit@3 | Hit@6 | MRR | Eval time | Loaded size |
  |---|---|---|---|---|---|---|
  | `nomic-embed-text` | 88% | 100% | 100% | 0.936 | 1.6 s | 335 MB |
  | `qwen3-embedding:0.6b` | 88% | 100% | 100% | 0.936 | 8.8 s | 1.2 GB |

- **Gate stays at 0.55:** keeps 96% of answerable questions and blocks 88% of the 8 unanswerable ones.
- The eval corpus is 4 documents I wrote (16 pages), generated into PDFs by `TestPdfs.Paragraphs`, so nothing needs licensing.
  It's small and synthetic: treat the numbers as a regression check, not a benchmark. Real two-column papers are still untested.
- Skipped the debug search box. Chat replaces it in M4, and the eval covers retrieval more thoroughly.

## What changed in rev 6 (learned in M2)

- `NomicTextEmbedder` became `OllamaTextEmbedder`: vector size and prefixes come from `ModelOptions`, so any Ollama embedding model works.
- Changing the embedding model **rebuilds automatically**. `DatabaseInitializer` drops the vectors, re-queues every document,
  and the app re-embeds on launch. That replaces the planned "Rebuild index" button.
- Dropped `InMemoryVectorStore`: sqlite-vec is proven, so the fallback would be unused code. Tests use the real store.
- First real numbers: relevant question 0.71 cosine, unrelated question 0.51. The 0.55 starting gate looks plausible; M3 calibrates it.

## What changed in rev 5 (learned in M1)

- Fixture PDFs are **built in code** by `TestPdfs` (PdfPig's writer), so the repo has no binary fixtures to license.
  Real papers still come in with the M3 eval corpus, which is where two-column reading order gets its real test.
- Extracted text isn't stored yet: pages are re-read from the PDF when a source is selected. M2 stores chunks.
- After import a document stays `Queued` until M2 adds indexing.
- Schema changes go through `DatabaseInitializer` migrations, tracked with SQLite's `user_version`, not the `meta` table.

## What changed in rev 4 (learned in M0)

- **Pinned versions** are in `Directory.Packages.props`: Avalonia 12.1.5, OllamaSharp 5.5.0, PdfPig 0.1.16, PDFtoImage 5.4.0,
  Microsoft.Data.Sqlite 10.0.14, LightStudio.sqlite-vec 0.1.9.6, xunit.v3 4.0.2, SDK 10.0.403.
- **D3 settled: `LightStudio.sqlite-vec`.** Loads from both normal and published builds; the notebook partition key works.
- The solution file is `Zenote.slnx` (the .NET 10 default). The NuGet package is `PdfPig`, not `UglyToad.PdfPig`.
- Tests run on Microsoft Testing Platform (`global.json`), so use `dotnet test --solution Zenote.slnx`.
  `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` aren't needed.
- **PDFtoImage gotcha:** a `string` argument means Base64 PDF data, not a file path. Pass a `FileStream`.
- Added `Zenote --self-check`, which prints the status-bar checks without a window, for testing published builds.
- Measured: both models use about 3.4 GB together, 100% on GPU. The published app folder is 130 MB (self-contained).
- `nomic-embed-text` ran with a 2048-token context in Ollama, which is ample for ~400-token chunks.

## What changed in rev 3

- **Name chosen: Zenote.** Namespaces `Zenote.*`, bundle ID `com.zenwyn.zenote`, repo `zenote` (private).
- **Lightweight notebooks added to v1**, the one NotebookLM idea worth keeping. Each notebook has its own sources and chats,
  and questions search only that notebook. They're one level deep, with no nesting and no sharing. NotebookLM's audio overviews,
  study guides and mind maps are out of scope.

## What changed in rev 2

**Updated because they were out of date**
- **.NET 9 → .NET 10 (LTS).** .NET 9 reaches end of support on **10 Nov 2026**. .NET 10 is supported until Nov 2028.
- **Avalonia 11 → Avalonia 12.** v12 went stable in April 2026, and the current line is 12.1.x.
- **sqlite-vec packaging.** The current stable release is 0.1.9. The official `sqlite-vec` NuGet package is still a 0.1.7 *prerelease*.
  `LightStudio.sqlite-vec` (0.1.9.x) ships an osx-arm64 `vec0.dylib`. Loading needs the resolved file path plus the
  entry point `sqlite3_vec_init`.
- **Vector-store alternative.** `CommunityToolkit.VectorData.SqliteVec` (Microsoft.Extensions.VectorData, net10) replaces the
  older Semantic Kernel connector as the "less code" option.
- **Ollama default context** is now 4096 in recent versions, but it has changed between releases. The fix stays the same: set `num_ctx` explicitly.

**Simplified to keep the app smooth, light and useful.** Each cut is listed in §6 with how to add it back.
- Vector-only retrieval in v1. Hybrid FTS5 + RRF becomes a stretch goal, added only if the eval shows exact-term misses.
- Multi-turn without an extra LLM call: no query-rewriting step. Retrieval embeds the previous question together with the new one.
- Citations are parsed **once, when the answer finishes**, not mid-stream.
- PDFs are **copied into app storage**, so there's no "file moved" handling.
- Extraction uses PdfPig's built-in content-order extractor. Header/footer stripping is deferred.
- Chunks belong to **one page** (`Page`, not `PageStart/PageEnd`).
- No Generic Host and no Markdown renderer: plain `ServiceCollection` and selectable plain text.
- 11 milestones become **8**.

---

## 0. Decisions captured

| Topic | Decision |
|---|---|
| Name | **Zenote**, a simpler, cleaner, local NotebookLM-style app (§10) |
| Scope | **Notebooks → sources (PDFs) → chats.** Questions search only the current notebook. |
| Hardware | Apple Silicon, **16 GB** unified memory |
| Runtime | C# / **.NET 10 LTS** (was .NET 9; see above) |
| UI | **Avalonia 12** + FluentTheme, **light mode only**, MVVM via CommunityToolkit.Mvvm (source generators) |
| AI | Ollama locally: `nomic-embed-text` (768-dim) + `llama3.2` (3B), behind Microsoft.Extensions.AI via OllamaSharp |
| PDF | PdfPig for text; PDFtoImage (PDFium) for page rendering |
| Storage | SQLite (Microsoft.Data.Sqlite) + sqlite-vec |
| Citation click | Side panel with the cited passage **and an in-app render of the page** |
| Chat | **Multi-turn, persisted** conversations |
| Scanned PDFs | **Detect and warn** (no OCR) |
| Repo | **Private** until v1.0 is polished and signed off (§5) |
| Cost | $0: everything runs locally |

---

## 1. Solution structure

```
Zenote/
├── Zenote.slnx
├── Directory.Build.props          # net10.0, Nullable, TreatWarningsAsErrors, AvaloniaUseCompiledBindingsByDefault
├── Directory.Packages.props       # Central package management, pinned versions
├── .editorconfig
├── .github/workflows/ci.yml       # macos-latest (arm64): build + unit + integration tests
├── src/
│   ├── Zenote.Core/               # Domain + use cases. No UI, SQLite or PdfPig.
│   │   ├── Domain/                # Notebook, Document, Chunk, Conversation, Message, Citation …
│   │   ├── Abstractions/          # Interfaces implemented by Infrastructure
│   │   ├── Chunking/              # PageChunker (pure, heavily unit-tested)
│   │   ├── Retrieval/             # Retriever (embed → KNN → gate)
│   │   ├── Answering/             # RagAnswerService, PromptBuilder, CitationParser
│   │   ├── Indexing/              # IndexingQueue (one background worker)
│   │   └── Options/               # ChunkingOptions, RetrievalOptions, ModelOptions
│   ├── Zenote.Infrastructure/
│   │   ├── Pdf/                   # PdfPigTextExtractor, PdfiumPageRenderer
│   │   ├── Persistence/           # SqliteConnectionFactory (WAL), migrations, repositories
│   │   ├── Vectors/               # SqliteVecVectorStore, InMemoryVectorStore (tests/fallback)
│   │   ├── Ai/                    # Ollama wiring, NomicTextEmbedder, OllamaHealthCheck, warm-up
│   │   ├── Storage/               # AppPaths (~/Library/Application Support/Zenote), PDF copy-in
│   │   └── ServiceCollectionExtensions.cs
│   └── Zenote.App/                # Avalonia
│       ├── App.axaml(.cs)         # ServiceCollection + JSON config, RequestedThemeVariant=Light
│       ├── Views/                 # MainWindow, NotebooksView (home), SourcesView, ChatView, SourcePanelView, SettingsSheet
│       ├── ViewModels/
│       ├── Controls/              # CitationChip
│       └── appsettings.json       # Model names, Ollama URL, retrieval defaults
└── tests/
    └── Zenote.Tests/
        ├── Unit/                  # Chunker, CitationParser, PromptBuilder, gate, ViewModels
        ├── Integration/           # SQLite + sqlite-vec, PdfPig on fixture PDFs   [Trait Category=Integration]
        ├── Eval/                  # Retrieval eval (needs Ollama, local only)      [Trait Category=Eval]
        └── Fixtures/              # Small redistributable PDFs + eval.json
```

**Dependency rule:** `App → Infrastructure → Core` and `App → Core`. Core references only
`Microsoft.Extensions.AI.Abstractions`, `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions`.
Adding Azure OpenAI later is an Infrastructure-only change.

**Packages** (pin exact versions in M0)

| Project | Packages |
|---|---|
| Core | Microsoft.Extensions.AI.Abstractions, Microsoft.Extensions.Options, Microsoft.Extensions.Logging.Abstractions |
| Infrastructure | **OllamaSharp** 5.x (`OllamaApiClient` implements `IChatClient` and `IEmbeddingGenerator`), PdfPig, Microsoft.Data.Sqlite 10.x, **LightStudio.sqlite-vec**, PDFtoImage |
| App | Avalonia 12, Avalonia.Desktop, Avalonia.Themes.Fluent, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Configuration.Json |
| Tests | xunit.v3, NSubstitute, Shouldly (FluentAssertions v8+ is commercial) |

> The old `Microsoft.Extensions.AI.Ollama` preview package is **deprecated**. Use OllamaSharp.

---

## 2. Data model

### 2.1 Domain types (Core)

```csharp
public sealed record Notebook(Guid Id, string Title, DateTimeOffset UpdatedAt);

public enum DocumentStatus { Queued, Extracting, Embedding, Ready, Failed, Cancelled }

public sealed record Document(
    Guid Id, Guid NotebookId, string FileName, // display name
    string StoredPath,                        // copy inside app storage
    string ContentHash,                       // SHA-256 → skip duplicate imports within a notebook
    int PageCount, int PagesWithoutText,      // scanned-page warning
    DocumentStatus Status, string? Error,
    DateTimeOffset ImportedAt);

public sealed record ExtractedPage(int PageNumber, string Text, bool HasText);
public sealed record ExtractedDocument(IReadOnlyList<ExtractedPage> Pages);

public sealed record ChunkDraft(int Page, int Ordinal, string Text);
public sealed record Chunk(long Id, Guid DocumentId, string FileName, int Page, string Text);
public sealed record ScoredChunk(Chunk Chunk, double Similarity);

public sealed record RetrievalResult(IReadOnlyList<ScoredChunk> Chunks, bool Relevant);

public sealed record Conversation(Guid Id, Guid NotebookId, string Title, DateTimeOffset UpdatedAt);
public enum MessageRole { User, Assistant }
public sealed record Message(
    Guid Id, Guid ConversationId, MessageRole Role, string Content,
    bool NotFound, IReadOnlyList<Citation> Citations, DateTimeOffset CreatedAt);
public sealed record Citation(int Marker, Guid DocumentId, string FileName, int Page, string Snippet);

public sealed record IndexingProgress(Guid DocumentId, DocumentStatus Stage, int Completed, int Total);

// Streamed to the ChatViewModel
public abstract record AnswerEvent;
public sealed record TokenReceived(string Text) : AnswerEvent;
public sealed record AnswerCompleted(Message Message) : AnswerEvent;   // final text + validated citations
public sealed record AnswerNotFound(Message Message, IReadOnlyList<ScoredChunk> ClosestMatches) : AnswerEvent;
```

### 2.2 SQLite schema (sketch)

```sql
CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT);     -- schema_version, embedding_model, embedding_dims

CREATE TABLE notebooks(id TEXT PRIMARY KEY, title TEXT NOT NULL, updated_at TEXT NOT NULL);

CREATE TABLE documents(
  id TEXT PRIMARY KEY,
  notebook_id TEXT NOT NULL REFERENCES notebooks(id) ON DELETE CASCADE,
  file_name TEXT NOT NULL, stored_path TEXT NOT NULL,
  content_hash TEXT NOT NULL, page_count INT, pages_without_text INT,
  status TEXT NOT NULL, error TEXT, imported_at TEXT NOT NULL,
  UNIQUE(notebook_id, content_hash));                     -- the same PDF may sit in two notebooks

CREATE TABLE chunks(
  id INTEGER PRIMARY KEY,                                 -- == vec_chunks.rowid
  document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
  page INT NOT NULL, ordinal INT NOT NULL, text TEXT NOT NULL);

CREATE VIRTUAL TABLE vec_chunks USING vec0(
  notebook_id TEXT PARTITION KEY,                         -- KNN pre-filters to one notebook (sqlite-vec ≥ 0.1.6)
  embedding float[768] distance_metric=cosine);

CREATE TABLE conversations(
  id TEXT PRIMARY KEY,
  notebook_id TEXT NOT NULL REFERENCES notebooks(id) ON DELETE CASCADE,
  title TEXT, updated_at TEXT);
CREATE TABLE messages(
  id TEXT PRIMARY KEY, conversation_id TEXT REFERENCES conversations(id) ON DELETE CASCADE,
  role TEXT, content TEXT, not_found INT, created_at TEXT,
  citations_json TEXT);                                   -- small, read-only after write: JSON is enough
```

Notes:
- `vec0` doesn't take part in cascades. Delete a document with
  `DELETE FROM vec_chunks WHERE rowid IN (SELECT id FROM chunks WHERE document_id = ?)` in the same transaction.
  Deleting a notebook does the same for all its documents, then removes its stored PDFs from disk.
- Search: `... WHERE embedding MATCH ? AND k = 6 AND notebook_id = ?`. The partition key keeps searches fast and
  stops sources from one notebook leaking into another notebook's answers.
- Citations store their own snippet, so old conversations still render after a document is deleted.
- If `meta.embedding_model` doesn't match the configured model, block search and show **Rebuild index**, which re-embeds every document.

---

## 3. Key interfaces (Core/Abstractions)

```csharp
public interface IPdfTextExtractor
{
    Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken ct);
}

public interface IChunker
{
    IReadOnlyList<ChunkDraft> Chunk(ExtractedDocument document);
}

/// Makes the query/document asymmetry explicit
/// (nomic-embed-text needs "search_query: " / "search_document: " prefixes).
public interface ITextEmbedder
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct);
    Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken ct);
}

public interface INotebookRepository { /* create, list, rename, delete (cascades to sources + chats) */ }

public interface IDocumentRepository
{
    Task<Document?> FindByHashAsync(Guid notebookId, string contentHash, CancellationToken ct);
    Task<IReadOnlyList<Document>> ListAsync(Guid notebookId, CancellationToken ct);
    Task SaveAsync(Document document, CancellationToken ct);
    Task<IReadOnlyList<long>> InsertChunksAsync(Guid documentId, IReadOnlyList<ChunkDraft> chunks, CancellationToken ct);
    Task<IReadOnlyList<Chunk>> GetChunksAsync(IReadOnlyList<long> ids, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IVectorStore
{
    Task AddAsync(Guid notebookId, IReadOnlyList<(long ChunkId, ReadOnlyMemory<float> Vector)> items, CancellationToken ct);
    Task<IReadOnlyList<(long ChunkId, double Similarity)>> SearchAsync(
        Guid notebookId, ReadOnlyMemory<float> query, int k, CancellationToken ct);
}

public interface IRetriever
{
    Task<RetrievalResult> RetrieveAsync(Guid notebookId, string query, CancellationToken ct);
}

public interface IRagAnswerService
{
    IAsyncEnumerable<AnswerEvent> AskAsync(Guid conversationId, string question, CancellationToken ct);
    // The conversation's NotebookId decides which sources are searched.
}

public interface IIndexingQueue
{
    event EventHandler<IndexingProgress> ProgressChanged;    // ViewModel marshals to the UI thread
    ValueTask EnqueueAsync(Guid notebookId, string sourcePath, CancellationToken ct);
    void Cancel(Guid documentId);
}

public interface IConversationRepository { /* create, list, messages, append, rename, delete */ }

public interface IPageRenderer
{
    Task<byte[]> RenderPngAsync(string filePath, int pageNumber, int widthPx, CancellationToken ct);
}

public interface IAiHealthCheck
{
    Task<AiHealth> CheckAsync(CancellationToken ct);         // Reachable, MissingModels[]
}
```

`IChatClient` (Microsoft.Extensions.AI) is used directly by `RagAnswerService`. It's already the abstraction, so it needs no wrapper.

### ViewModels (App)

| ViewModel | Responsibility |
|---|---|
| `MainWindowViewModel` | Navigates between the notebooks home and an open notebook; health banner |
| `NotebooksViewModel` | Home screen: a grid of notebook cards (title, source count, last used); new, rename, delete |
| `NotebookViewModel` | An open notebook, laid out like NotebookLM: **Sources** pane, **Chat**, and the source panel on demand |
| `SourcesViewModel` / `DocumentItemViewModel` | Import into this notebook (file picker or drag-and-drop), progress bar, scanned warning, cancel/delete |
| `ConversationListViewModel` | This notebook's chats: new, rename, delete |
| `ChatViewModel` / `MessageViewModel` | Send (⌘↩), Stop, streamed text, not-found state with "closest matches" |
| `CitationChipViewModel` | `"report.pdf · p. 12"`; `OpenCommand` → source panel |
| `SourcePanelViewModel` | Snippet, rendered page, "Open in Preview" button |
| `SettingsViewModel` | Ollama URL and model names (a small sheet, not a full screen) |

---

## 4. Chunking strategy and retrieval settings

### 4.1 Extraction

1. Use PdfPig's **`ContentOrderTextExtractor`** per page: one call that handles reading order well enough for most PDFs.
   Only reach for the full layout-analysis pipeline if the two-column fixture comes out garbled.
2. Light cleanup: collapse whitespace, rejoin hyphenated line breaks (`exam-\nple`), and normalise ligatures (ﬁ → fi).
3. **Scanned-page detection:** a page with fewer than ~20 non-whitespace characters → `HasText = false`. If more than half the pages
   have no text, show the badge "Looks scanned. Text search won't work."

### 4.2 Chunking: page-bounded recursive splitting

| Setting | Default | Why |
|---|---|---|
| Boundary | **One page per chunk set.** Chunks never cross pages. | Every citation points to exactly one page, and the schema and citation code stay simple. |
| Target size | **~400 tokens** (≈1,600 chars, estimated as chars ÷ 4) | One idea per chunk keeps embeddings specific. A 3B model also does better with a few focused chunks than with long ones. Most pages become 1–2 chunks. |
| Overlap | **~60 tokens (15%)**, snapped to a sentence start | Keeps facts that straddle a split retrievable, at about 15% extra index size. |
| Separators | `\n\n` → `\n` → sentence end → space | Recursive: split on the biggest natural break that fits. |
| Tiny tails | Under ~50 tokens, merge into the previous chunk on the same page | Avoids junk chunks like a lone heading. |
| Embedding input | `"search_document: " + fileName + ", p." + page + "\n" + text` | The nomic prefix is **required**. The header helps queries that name a document. |

### 4.3 Retrieval pipeline

```
question ──(if follow-up) prepend previous user question──▶ "search_query: <text>"
   ▼
embed (nomic-embed-text) ─▶ vec0 KNN, k = 6
   ▼
gate: best cosine ≥ τ ? ──no──▶ "Not found in your documents" + closest matches (no LLM call)
   ▼ yes
prompt with numbered sources [1]..[6] + last 4 turns ─▶ llama3.2 (stream)
   ▼
on completion: parse [n] → validate → citation chips
```

| Setting | Default | Reasoning |
|---|---|---|
| k | **6** (~2.4k tokens of context) | Fits easily in an 8k context with the prompt, history and answer. More chunks tend to hurt small models. |
| Follow-up retrieval | Embed `previousQuestion + " " + question` | Fixes "what about the second one?" with **no extra LLM call**, which would roughly double latency on a 3B model. |
| Relevance gate τ | Start at cosine **0.55**, then **calibrate on the eval set** | The main defence against made-up answers. nomic scores cluster high, so τ has to be measured. |
| `num_ctx` | **8192**, set per request | Ollama's default (currently 4096, and it has changed between versions) **silently truncates** prompts. |
| Temperature | 0.1 | Grounded Q&A should be near-deterministic. |
| History | Last 4 turns, answers only | Leaves the context budget for the current sources. |
| Embedding batch | 16–32 chunks per call | Ollama's `/api/embed` accepts batches, which makes indexing much faster. |
| `keep_alive` | 30 min | Avoids reloading the model between questions. |

### 4.4 Grounding and "not found"

1. **Gate (deterministic):** below τ, show the fixed text *"I couldn't find this in your documents."* plus the 3 closest
   matches as chips, so the user can check.
2. **Prompt contract:** answer only from the numbered sources, cite claims as `[n]`, and if the sources don't answer the question,
   reply exactly `NOT_FOUND`, which the app turns into the same UI.
3. **Validation:** drop markers that don't map to a source. An answer with no valid citations is labelled
   "unverified".

---

## 5. Milestones

Each milestone ends with something runnable and a tagged commit (`m0` … `m7`). Do one PR per milestone, even solo,
with a screenshot in the description.

| # | Milestone | Runnable result | Tests |
|---|---|---|---|
| **M0** | **Scaffold + spikes.** Solution and 4 projects, central package versions, DI + config, light theme, CI. Spikes: load sqlite-vec (choose the package) and run `vec_version()`; OllamaSharp as `IChatClient` and embedder; render one page with PDFtoImage; **repeat all three after `dotnet publish -r osx-arm64`**. | Status bar: `Ollama ✓ llama3.2 ✓ nomic-embed-text ✓ sqlite-vec 0.1.9 ✓` | vec0 smoke test; health-check test |
| **M1** | **Notebooks + import + extract.** Notebooks home (create, rename, delete), open a notebook, import into it: copy into app storage, SHA-256 dedupe per notebook, extraction + cleanup, scanned detection. | Create "Taxes 2026", drop a PDF in, and see its pages with extracted text; scanned PDFs get a badge | Extractor tests on 3 fixtures (single column, two column, scanned); notebook repository tests |
| **M2** | **Chunk, embed, store.** `PageChunker`, `NomicTextEmbedder`, migrations, repositories, `SqliteVecVectorStore`; background `IndexingQueue` with per-document progress and cancel. | Drop 5 PDFs and watch progress bars while the UI stays responsive; notebooks survive a restart | Thorough chunker tests; repository and vector-store integration tests, **including that search never returns another notebook's chunks**; cancellation test |
| **M3** | **Search + eval.** Debug "Search" box showing top chunks with similarity and page; golden set + `RetrievalEvalTests` (Hit@6, MRR, gate precision). Compare embedding models (D2b), then calibrate τ for the winner. | Type a query and the right pages come back | Eval runs locally (needs Ollama) |
| **M4** | **Grounded streaming answers.** `PromptBuilder`, `RagAnswerService`, streamed text, Stop, gate, `NOT_FOUND`, model warm-up at launch. Compare chat models (D2) on citation accuracy, not-found behaviour and time to first word. | Ask and watch the answer stream; off-topic questions get "not found" | PromptBuilder snapshot tests; gate tests; ChatViewModel tests with a fake `IChatClient` |
| **M5** | **Citations + source panel.** Parser + validation, chips, side panel with snippet and lazily rendered page (cached), "Open in Preview". | Click `handbook.pdf · p. 7` and see the page | CitationParser tests (invalid, duplicate and adjacent markers, `NOT_FOUND`) |
| **M6** | **Saved multi-turn conversations.** Per-notebook chat list, history window, follow-up retrieval, title from the first question. | Follow-up questions work; chats survive a restart | Conversation repository tests; follow-up query tests |
| **M7** | **Polish + release.** Error states (Ollama down, model missing), shortcuts (⌘O, ⌘N, ⌘↩, ⌘.), empty states with example questions, app icon, `.app` via `dotnet publish -r osx-arm64` + ad-hoc codesign, README, demo GIF, private v1.0 release. | Downloadable `.app` | Full test pass on a clean checkout |

**Stretch, in order of value:** hybrid search (FTS5 + RRF) if M3 shows exact-term misses → CI eval with cached embeddings →
header/footer stripping → Azure OpenAI provider (proves the abstraction) → NotebookLM-style source checkboxes (ask only the ticked
sources in a notebook) → Markdown rendering → OCR.

**Out of scope, on purpose:** NotebookLM's audio overviews, generated study guides/FAQs/mind maps, sharing, and nested notebooks.
Zenote is meant to be the simple, clean core: grounded chat with citations over a chosen set of sources.

### Retrieval eval set (M3)

- **Corpus:** 4–6 small PDFs you're allowed to redistribute: public-domain government guides, a CC-BY arXiv paper (two-column),
  and one document you write yourself so you control the facts. Keep it under ~10 MB.
- **`Fixtures/eval.json`:** ~25 answerable questions with `expectedFile` + `expectedPages[]` (mixing paraphrase,
  exact-term and numeric questions), plus ~8 unanswerable ones.
- **Metrics:** Hit@6, MRR, gate precision/recall. Assert floors (e.g. Hit@6 ≥ 0.85) so regressions fail locally.

### Repository and commit policy

- **Private repo** while milestones are in progress. It goes **public only after v1.0 polish** (§9) and the owner signs off.
- Before going public, check the history: no personal PDFs, no absolute paths with usernames, no large binaries,
  and only redistributable fixtures. Squash spike/WIP branches if you like; keep the milestone tags.
- Commits written with Claude's help end with:
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- Add the licence file and the README's AI-assistance note in the commit that makes the repo public.

---

## 6. Simplicity, smoothness and usefulness

### What was cut from rev 1, and why

| Cut | Why it's safe | Add back when |
|---|---|---|
| Hybrid FTS5 + RRF | Vector search handles most natural-language questions. One retrieval path is easier to debug and explain. | The eval shows misses on codes, names or numbers |
| LLM query rewriting | An extra model call adds 1–3 s per follow-up on a 3B model. Concatenating the previous question covers most cases. | Follow-up eval cases fail |
| Streaming citation parsing | Parsing once at the end removes the split-token edge cases (`[`, `1`, `]` arriving separately). | Never really needed |
| Neighbour merge, near-duplicate removal, MMR | With page-bounded 400-token chunks and k = 6, duplicates are rare. | Answers show repeated context |
| Referencing PDFs by original path | Copying removes "file moved" errors, and page rendering always works. | Disk use becomes a complaint |
| Layout-analysis extraction, header/footer stripping | The content-order extractor is good enough for most PDFs. | A fixture comes out garbled |
| Generic Host, Markdown renderer, full settings screen, headless UI tests | Fewer dependencies and faster startup. ViewModels are tested directly. | You need background services or rich formatting |
| CI eval with cached embeddings | Running the eval locally is enough for v1. | Before going public: a green eval badge is a nice touch |

### Smoothness checklist

- Warm up both models in the background at launch, and show "Loading model…" until they're ready.
- Buffer streamed tokens and flush to the UI every ~40 ms. Never raise one `PropertyChanged` per token.
- Index on a single background worker with batched embeddings, so the UI thread never waits on Ollama or SQLite.
- Use SQLite in WAL mode with one shared connection factory, and do writes in transactions per document.
- Render pages lazily when a chip is clicked, at panel width, and cache the last ~10.
- Use compiled bindings (`AvaloniaUseCompiledBindingsByDefault`) and a virtualising list for long chats.
- Publish with ReadyToRun for faster cold start. Skip Native AOT for v1 (native libs and reflection make it fragile).

### Footprint (16 GB Mac)

llama3.2 3B Q4 (~2 GB) + 8k KV cache (<1 GB) + nomic-embed-text (~0.3 GB) + app (~150 MB) ≈ **3.5 GB**, leaving plenty of headroom.

### Small features that add real usefulness

Drag-and-drop import · "closest matches" on not-found · "Open in Preview" from the source panel · copy answer with citations ·
example questions on the empty state · Stop button (⌘.).

---

## 7. Risks and early decisions

### Decide now

| ID | Decision | Recommendation |
|---|---|---|
| D1 | Runtime | **.NET 10 LTS.** .NET 9 support ends 10 Nov 2026. |
| D2 | Chat model | Start with `llama3.2` (3B) as the baseline, with the model name configurable. In M4, compare it against newer small models (`qwen3.5:4b`, `gemma4:e4b`, `phi4-mini`; confirm tags at ollama.com/library) on the same questions, and make the winner the default. Turn off "thinking" output on models that have it, because it delays the first visible word. |
| D2b | Embedding model | Start with `nomic-embed-text`. In M3, compare it against `qwen3-embedding:0.6b` (1024-dim) on the eval set, and switch only if retrieval clearly improves. Changing later means re-embedding everything, so settle this before M4. Keep the vector size and the query/document prefixes per model in config, not hard-coded (`float[768]` in §2.2 is the nomic value). |
| D3 | sqlite-vec packaging | **Settled in M0:** raw SQL against `vec0` behind `IVectorStore`, using `LightStudio.sqlite-vec` 0.1.9.6. |
| D4 | Licence | Name is settled (**Zenote**, `com.zenwyn.zenote`). MIT is suggested; add the licence when the repo goes public. |

### Technical risks

| Risk | Mitigation |
|---|---|
| **sqlite-vec on macOS:** the system SQLite blocks extensions; SQLite doesn't follow NuGet's probing rules; downloaded dylibs get quarantined; wrong architecture gives a cryptic error | Use Microsoft.Data.Sqlite's bundled SQLite, `EnableExtensions(true)`, and `LoadExtension(<resolved path to vec0.dylib>, "sqlite3_vec_init")`. Ship the osx-arm64 binary from NuGet (not Homebrew). Ad-hoc codesign it inside the `.app`. Pin the version (still pre-1.0). Prove it in M0 **including after publish**. Fallback: `InMemoryVectorStore` (brute-force cosine is milliseconds for <20k chunks). |
| **Ollama context truncation** | Set `num_ctx` on every request. Log the estimated prompt size and warn if it's over 80% of the budget. |
| Missing nomic prefixes | Keep them inside `NomicTextEmbedder` and unit-test that they're applied. |
| 3B grounding quality (ignores instructions, invents `[n]`) | Gate, low temperature, k = 6, citation validation, "unverified" label, eval floors. |
| Cold start (5–15 s first answer) | Warm-up at launch + `keep_alive`. |
| Ollama not running or a model not pulled | Health banner with copy-paste fixes (`ollama serve`, `ollama pull …`). Never crash. |
| PdfPig text quality on odd PDFs | Fixture tests, including two-column. Upgrade to layout analysis only if needed. |
| PDFium native binary missing from the published `.app` | Covered by the post-publish check in M0. |
| Pre-1.0 libraries (PdfPig, sqlite-vec) | Pin versions; upgrade deliberately with tests. |
| Licensing | Credit the Llama 3.2 Community Licence; use Shouldly rather than FluentAssertions v8. |

---

## 8. README outline (portfolio audience)

1. **Zenote + tagline** (e.g. "Let your documents talk. A calm, local take on NotebookLM, with page-level citations.")
2. **Demo GIF:** create notebook → import → progress → question → streamed answer → click citation → page view.
3. **Why:** a learning project that rebuilds the core of NotebookLM simply and locally. Documents never leave the Mac,
   it costs nothing to run, and it's a native app. Include the line "Inspired by Google's NotebookLM; not affiliated with Google."
4. **Features.**
5. **Architecture:** Mermaid diagrams of the layers and of the indexing and query flow.
6. **How the RAG works:** page-bounded chunks, nomic prefixes, relevance gate, citation validation.
7. **Evaluation:** Hit@6 / MRR / not-found precision on the fixture corpus, and how to reproduce it.
8. **Design decisions: what I deliberately didn't build** (the §6 table). Showing restraint reads well to reviewers.
9. **Tech stack** with links.
10. **Getting started:** Apple Silicon, .NET 10 SDK, Ollama, `ollama pull llama3.2`, `ollama pull nomic-embed-text`,
    `dotnet run --project src/Zenote.App`, or the release `.app` (plus the Gatekeeper "Open anyway" note).
11. **Configuration:** `appsettings.json` keys and switching models.
12. **Tests:** categories and filter commands.
13. **Performance:** indexing speed, time to first token, RAM use.
14. **Limitations:** no OCR, text only, small-model reasoning.
15. **Roadmap:** the stretch list.
16. **Licence, credits, and an honest note on AI-assisted development.**

---

## 9. Definition of done (v1.0)

- [ ] All five core features work end to end on a fresh Mac by following the README
- [ ] CI is green on `macos-latest` (build, unit, integration)
- [ ] Local eval floors are met and the results are in the README
- [ ] No crashes when Ollama is off, a model is missing, or a PDF is scanned
- [ ] Private v1.0 release with an arm64 `.app`, demo GIF and milestone tags
- [ ] History checked, owner signs off, repo switched to public

---

## 10. Name

**Chosen: Zenote** (zen + note). Namespaces `Zenote.*`, bundle ID `com.zenwyn.zenote`, repo `zenote`.

Collision check (1 Nov 2026): a small note-taking startup called "Zenote" is listed on BetaList, and there are similar names
(ZenNote on Android, Zenoti for salon software). That's fine for a private learning project. Before going public, check again and use
a repo suffix like `zenote-app` if needed.

<details><summary>Earlier shortlist (for reference)</summary>

### Name shortlist ("let your documents talk")

| Name | Meaning / tagline | Notes |
|---|---|---|
| **Quoth** ⭐ | Old English for "said", as in "quoth the raven". *"Your documents, quoted."* | Short and unusual. It fits answers that quote and cite their sources. |
| **Speakeasy** ⭐ | A private place to talk. *"A private place to talk with your documents."* | Points to the offline, local-only angle. Common word, so check for collisions. |
| **Parley** | A conversation, often between two sides. *"Parley with your PDFs."* | Alliterates with PDFs; distinctive. |
| **PageTalk** | Says exactly what it does. *"Let your pages talk."* | Clearest option; may already be taken. |
| **Viva Voce** | Latin for "by living voice", also the name of an oral exam. *"Put your documents to the viva."* | Academic, memorable; longer to type. |
| **Folio Speaks** / **Folio** | A folio is a page number, which is what each citation shows. | Very apt, but "Folio" alone is crowded. |
| **Chinwag** | Informal for a chat. *"Have a chinwag with your documents."* | Playful and friendly; less "serious tool". |
| **Rapport** | Easy two-way understanding. *"Build rapport with your reading."* | Polished; common word. |
| **Murmur** | Documents quietly answering. *"Your documents, quietly answering."* | Calm, matches the local/private feel. |
| **Talkback** | Your documents answer back. *"Your PDFs, talking back."* | Literal; may clash with Android's screen reader of the same name. |
| **DocVox** | Document + voice. *"Give your documents a voice."* | Product-y; easy namespace (`DocVox.Core`). |
| **Inkwell** | Where the words come from. *"Ask the ink."* | Warm; less clearly about conversation. |

Also considered: ChattyNote/ChattyNotebook (too close to NotebookLM and note-taking apps), zenbook/zenbookLM (clashes with the ASUS Zenbook trademark).

</details>

---

## 11. Sources (checked 1 Nov 2026)

- .NET STS support extended to 24 months; .NET 8 and 9 both end 10 Nov 2026: https://devblogs.microsoft.com/dotnet/dotnet-sts-releases-supported-for-24-months/
- Avalonia 12 stable (Apr 2026), 12.1.x since: https://avaloniaui.net/blog/authors/15
- sqlite-vec 0.1.9 stable: https://docs.rs/crate/sqlite-vec/=0.1.7 (crate version list); official NuGet prerelease: https://www.nuget.org/packages/sqlite-vec/
- LightStudio.sqlite-vec (osx-arm64 dylib, `sqlite3_vec_init`, resolved path): https://www.nuget.org/packages/LightStudio.sqlite-vec
- CommunityToolkit.VectorData.SqliteVec: https://www.nuget.org/packages/CommunityToolkit.VectorData.SqliteVec
- Semantic Kernel SQLite connector (preview, no hybrid): https://learn.microsoft.com/en-us/semantic-kernel/concepts/vector-store-connectors/out-of-the-box-connectors/sqlite-connector
- Ollama context length defaults and overrides: https://lumadock.com/tutorials/ollama-context-window
- OllamaSharp: https://packages.nuget.org/packages/OllamaSharp
- PdfPig releases: https://www.github.com/UglyToad/PdfPig/releases
- Ollama embedding models compared: https://www.morphllm.com/ollama-embedding-models
- sqlite-vec partition keys (v0.1.6+): https://https-alexgarcia_xyz.proxy.bugsmash.io/sqlite-vec/features/vec0.html
- "Zenote" name check: https://betalist.com/startups/zenote
- Small chat models (checked 4 Nov 2026): https://www.promptquorum.com/local-llms/small-local-llm-models and https://tech-insider.org/?p=47562
- Small embedding models (checked 4 Nov 2026): https://www.morphllm.com/ollama-embedding-models
