# Project Rules and Constraints — MRA Internal Policy Chatbot

These rules govern all work on this repository, in every phase, until they
are explicitly revised by the project owner. They exist to keep development
scoped, incremental, and reviewable.

## Working Discipline

- **We are building phase by phase.** Work only on the current, explicitly
  approved phase. Do not implement functionality belonging to a future phase
  ahead of schedule, even if it seems convenient or related.
- Do not modify or create files for a new phase until that phase's plan has
  been presented and explicitly approved.
- See `docs/PROJECT_PLAN.md` for the full phase breakdown (Phases 0–8).

## Project Purpose

Build an internal web application where MRA employees will eventually log in
using a simple username and password and ask questions about approved MRA
policy documents only.

## Technology Constraints

- **Language:** C#
- **Framework:** ASP.NET Core MVC
- **Views:** Razor Views
- **UI:** Bootstrap
- **Database:** SQL Server (introduced in a later phase — not Phase 0)
- **Tooling:** Local/free tooling only where possible
- **No cloud AI API dependencies**

## Explicitly Out of Scope (Until Their Designated Phase)

The following must **not** be added until the project owner explicitly
approves the phase that calls for them:

- Login functionality
- Database code
- AI integration
- Document upload
- Authentication libraries
- APIs, tokens, or JWT
- Password reset flows
- Password complexity validation
- SSO (single sign-on)
- Cloud services
- Paid services
- User registration
- A password management module

## Authentication Model (Future)

- Login will eventually use **username and password only** — no SSO, no
  JWT/API tokens, no third-party identity providers.
- No user self-registration — accounts are provisioned internally.
- Two roles are planned: **Admin** and **Employee**.

## Scope of the Chatbot

- The chatbot must only answer questions about **approved MRA policy
  documents**. It is not a general-purpose assistant.
- No AI integration exists yet as of Phase 0; the approach for later phases
  is still to be decided and must remain free of paid or unapproved cloud AI
  API dependencies unless this rule is explicitly revised by the project
  owner.

## Phase 0 Scope (Current)

Phase 0 delivers only the clean project foundation and documentation:

1. An ASP.NET Core MVC solution named `MraPolicyChatbot`.
2. A clean MVC folder structure (`Controllers`, `Models`, `Views`,
   `Services`, `Data`, `wwwroot`, `docs`).
3. `README.md`, `CLAUDE.md`, and `docs/PROJECT_PLAN.md`.
4. A placeholder home page with no application logic.
5. A successful `dotnet build`.

No database migrations, tables, authentication, upload pages, chatbot logic,
or external integrations are part of Phase 0.
