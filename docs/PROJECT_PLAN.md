# Project Plan — MRA Internal Policy Chatbot

This document describes the phased build plan for the MRA Internal Policy
Chatbot, Phases 0 through 8. Each phase is scoped to be independently
reviewable and buildable; later phases are not started until the current
phase has been approved and completed. See `CLAUDE.md` for the standing
rules and constraints that apply across all phases.

## Phase 0 — Project Foundation & Documentation

**Goal:** Establish a clean, buildable project skeleton with no application
logic.

- ASP.NET Core MVC solution (`MraPolicyChatbot`) with a standard folder
  structure: `Controllers`, `Models`, `Views`, `Services`, `Data`,
  `wwwroot`, `docs`.
- `README.md`, `CLAUDE.md`, and this `PROJECT_PLAN.md`.
- A single placeholder home page.
- A verified successful `dotnet build`.
- No database, authentication, upload, or chatbot logic.

## Phase 1 — Static UI Shell

**Goal:** Build out the visual shell of the application with no backend
logic behind it.

- Shared layout (header, footer, navigation) using Bootstrap.
- Placeholder pages/routes for future features (e.g. "Ask a Question",
  "Browse Policies", "Admin") that render static content only.
- No forms that submit data, no authentication, no database.

## Phase 2 — SQL Server Data Layer

**Goal:** Introduce the data layer, schema only — no authentication yet.

- Add SQL Server + Entity Framework Core (or equivalent) to the `Data`
  project folder.
- Define initial models/entities: Users, Roles, Policies (metadata only,
  no document storage yet).
- Local database configuration only (connection strings for local/dev SQL
  Server instances — no cloud-hosted database).
- Migrations to create the schema; no seed data beyond what's needed for
  development/testing.

## Phase 3 — Username/Password Login & Roles

**Goal:** Implement the simple internal authentication model.

- Username and password login only — no JWT, no API tokens, no SSO.
- Session-based authentication (e.g. ASP.NET Core cookie authentication).
- Two roles: **Admin** and **Employee**.
- No self-service user registration — accounts provisioned internally
  (e.g. by an admin or a seeding process).
- No password reset flow and no password complexity validation in this
  phase, per standing project constraints, unless explicitly revisited.

## Phase 4 — Policy Document Management (Admin)

**Goal:** Allow Admins to manage the approved policy documents that the
chatbot will later reference.

- Admin-only screens to add, list, and edit policy document metadata.
- Document storage approach to be decided within local/free tooling
  constraints (no cloud storage services).
- No chatbot logic yet — this phase is about getting approved content into
  the system.

## Phase 5 — Document Text Extraction & Chunking Pipeline

**Goal:** Turn uploaded PDF/DOCX policy documents into searchable text
chunks, without any AI/embeddings involved yet.

- PDF text extraction (PdfPig) and DOCX text extraction (OpenXML SDK), both
  degrading gracefully (logged error, empty result) rather than crashing.
- Word-count-based text chunking with overlap, run after upload (and
  on-demand via "Re-process") without blocking the admin's request.
- Processing status (`IsProcessed`/`ProcessingError`) and chunk counts
  surfaced in the admin policy list.

> **Note:** this phase's actual scope (extraction/chunking) differs from
> this file's original Phase 5 description ("Employee Policy Browsing &
> Search" — a keyword-search UI over policy content). That original goal
> hasn't been built yet and needs a home in a later phase — likely folded
> into Phase 6 alongside chatbot Q&A, or inserted as its own phase. Revisit
> and decide explicitly before starting Phase 6.

## Phase 6 — Chatbot Q&A Integration (Complete)

**Goal:** Add the conversational question-answering feature, scoped
strictly to approved MRA policy documents.

- Q&A is powered by a locally-run [Ollama](https://ollama.com) server — no
  paid or cloud AI API dependency, consistent with the standing constraint
  in `CLAUDE.md`.
- Policy chunk embeddings are generated during Phase 5's processing
  pipeline (`nomic-embed-text` by default) and stored in the existing
  `PolicyChunks.Embedding` column; retrieval is brute-force in-memory
  cosine similarity (no vector DB) over chunks belonging to active,
  processed policies.
- Chatbot answers are grounded only in the retrieved policy excerpts via a
  strict prompt (`llama3.1` by default) — no general-purpose/open-domain
  answering. Below a hardcoded similarity threshold, or if Ollama can't be
  reached, the chatbot says so plainly instead of guessing or erroring.
- Answers show which policy title(s) they were drawn from.
- Not in scope for this phase (deferred to Phase 7): persisting/logging
  queries, feedback (thumbs up/down), analytics, a formalized/configurable
  similarity threshold, and admin-facing monitoring.

## Phase 7 — Admin Oversight

**Goal:** Give Admins visibility and control over system usage.

- Audit logs (e.g. who asked what, when documents were changed).
- Usage statistics/reporting.
- Content moderation / review tools for policy documents and chatbot
  responses.

## Phase 8 — Hardening & Deployment

**Goal:** Prepare the application for real internal use.

- Production-appropriate configuration (secrets handling, environment
  settings) within local/free/internal tooling constraints.
- Backup strategy for the SQL Server database.
- Internal deployment documentation (how the app is deployed and run in the
  MRA environment).
- Final security and constraint review against the rules in `CLAUDE.md`.

---

**Note:** Phase content beyond Phase 0 is a proposed plan and may be
refined as each phase is reached. Any change to the phase scope, order, or
the standing constraints in `CLAUDE.md` requires explicit approval from the
project owner before implementation begins.
