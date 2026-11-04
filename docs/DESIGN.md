# Design notes

Why Zenote is built the way it is. For how to install and use it, see the [README](../README.md).

## Goals

- Free to build and run, with no paid services
- Runs entirely on an Apple Silicon Mac
- Every answer can be checked against the page it came from
- Simple enough to understand, and to use without technical knowledge

## Structure

| Project | Responsibility |
|---|---|
| `Zenote.Core` | Domain, chunking, retrieval and answering. Knows nothing about SQLite, PDFs, Ollama or the UI. |
| `Zenote.Infrastructure` | Ollama, SQLite with sqlite-vec, PdfPig and PDFium |
| `Zenote.App` | Avalonia UI with MVVM |
| `Zenote.Tests` | Unit, integration and UI tests, plus the eval sets |

Core depends only on abstractions, including `IChatClient` from Microsoft.Extensions.AI, so another AI provider would only change Infrastructure.

## Key decisions

| Decision | Why |
|---|---|
| Chunks never cross pages | Every citation can point to exactly one page |
| About 400 tokens per chunk, 15% overlap | Focused enough for a small model, with overlap so facts aren't lost at a split |
| Vector search only | The right page was always in the top 3 in the eval, so keyword search wasn't worth the complexity |
| A relevance gate at 0.55 | Skips the model when nothing is close enough, which is the main defence against made-up answers |
| Previous question joined to follow-ups | Resolves "what about the second one?" without a slow extra model call |
| PDFs copied into app storage | Sources keep working if the original moves |
| Citations validated after streaming | Invented numbers are removed, and uncited answers are flagged |
| One background indexing worker | Keeps the UI responsive, with simple cancel and resume |
| Search model fixed, chat model switchable | Changing the search model means re-embedding everything, and the eval showed no gain |

## What was left out

| Left out | Why |
|---|---|
| Keyword and hybrid search | Worth adding if exact codes or names start being missed |
| LLM query rewriting | Roughly doubles the wait on a small model |
| Reranking and result merging | Little left to gain with page-sized chunks and 6 results |
| OCR | Scanned PDFs are flagged instead |
| Audio overviews, study guides, mind maps | Zenote is the simple core: grounded chat with citations |

## Evaluation

The eval set has 26 answerable and 8 unanswerable questions over 4 documents written for this project. It's small and synthetic, so it's a regression check rather than a benchmark. Results are in the [README](../README.md#evaluation).

## Lessons from testing

- **Don't close a flyout while its list is updating.** Doing so during a selection change crashed the app. The menu now closes only after a real click, on a posted callback.
- **Small models need guardrails around the prompt.** They invent citation numbers, write `[1-6]`, echo source headings and decline in their own words, so answers are cleaned up after they arrive.
- **Measure before optimising.** Database, search and PDF reading were all fast already. The real gains were keeping the model loaded and removing a network timeout.
- **Clicking through the real UI finds different bugs than unit tests.** Hover areas, clipped controls and damaged settings files were all found that way.
